using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GenPosting.Api.Features.Instagram.Models;
using GenPosting.Api.Services;
using GenPosting.Shared.DTOs;
using Microsoft.Extensions.Options;

namespace GenPosting.Api.Features.Instagram.Services;

public class InstagramService : IInstagramService
{
    private readonly HttpClient _httpClient;
    private readonly InstagramSettings _settings;
    private readonly IBlobStorageService _blobService;

    public InstagramService(HttpClient httpClient, IOptions<InstagramSettings> settings, IBlobStorageService blobService)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _blobService = blobService;
    }

    public string GetAuthorizationUrl(string redirectUri)
    {
        // Using Facebook Login (Graph API) to support Instagram Content Publishing
        // https://developers.facebook.com/docs/facebook-login/manually-build-a-login-flow
        var paramsDict = new Dictionary<string, string>
        {
            { "client_id", _settings.ClientId },
            { "redirect_uri", redirectUri },
            { "scope", _settings.Scope },
            { "response_type", "code" },
            { "state", Guid.NewGuid().ToString() } 
        };

        var queryString = string.Join("&", paramsDict.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
        return $"https://www.facebook.com/v19.0/dialog/oauth?{queryString}";
    }

    public async Task<InstagramTokenResponse?> ExchangeTokenAsync(string code, string redirectUri)
    {
        // Exchange code for token via Graph API
        // https://developers.facebook.com/docs/facebook-login/manually-build-a-login-flow#exchange-code-for-token
        var tokenUrl = $"https://graph.facebook.com/v19.0/oauth/access_token?" +
                       $"client_id={_settings.ClientId}" +
                       $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                       $"&client_secret={_settings.ClientSecret}" +
                       $"&code={code}";

        var response = await _httpClient.GetAsync(tokenUrl);
        
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"Facebook Token Exchange Failed: {error}");
            return null;
        }

        var tokenData = await response.Content.ReadFromJsonAsync<FacebookTokenDto>();
        if (tokenData == null) return null;

        // Get User ID (Facebook User ID)
        var meUrl = $"https://graph.facebook.com/me?access_token={tokenData.AccessToken}";
        var meResponse = await _httpClient.GetAsync(meUrl);
        var meData = await meResponse.Content.ReadFromJsonAsync<FacebookUserDto>();

        return new InstagramTokenResponse(
            tokenData.AccessToken, 
            tokenData.ExpiresIn, 
            meData?.Id ?? string.Empty
        );
    }

    public async Task<InstagramUserDto?> GetProfileAsync(string accessToken, string userId)
    {
        // 1. Get Instagram Business Account ID
        var instagramBusinessId = await GetInstagramBusinessIdAsync(accessToken, userId);
        if (string.IsNullOrEmpty(instagramBusinessId)) return null;

        // 2. Get Profile Details
        // https://graph.facebook.com/v19.0/{ig-user-id}?fields=username,profile_picture_url,media_count,followers_count,name
        var url = $"https://graph.facebook.com/v19.0/{instagramBusinessId}?fields=username,name,profile_picture_url,media_count,followers_count,ig_id&access_token={accessToken}";
        var response = await _httpClient.GetAsync(url);
        
        if (!response.IsSuccessStatusCode) return null;

        var data = await response.Content.ReadFromJsonAsync<IgProfileDto>();
        if (data == null) return null;

        return new InstagramUserDto(data.Id, data.Username, "BUSINESS", data.MediaCount, data.FollowersCount, data.ProfilePictureUrl);
    }

    public async Task<string> UploadMediaAsync(Stream fileStream, string fileName)
    {
         var contentType = fileName.EndsWith(".mp4") ? "video/mp4" : "image/jpeg";
         return await _blobService.UploadFileAsync(fileStream, fileName, contentType);
    }

    public async Task<(bool Success, string Error, string? PublishedId)> PublishPostWithUrlAsync(string accessToken, string userId, string caption, InstagramPostType type, string mediaUrl)
    {
        // 1. Get Instagram Business Account ID
        var instagramBusinessId = await GetInstagramBusinessIdAsync(accessToken, userId);
        if (string.IsNullOrEmpty(instagramBusinessId))
        {
            return (false, "No connected Instagram Account found.", null);
        }

        // 2. Create Media Container
        Console.WriteLine($"[PublishPostAsync] Creating container for: {mediaUrl}");
        string? containerId = await CreateMediaContainerAsync(accessToken, instagramBusinessId, mediaUrl, caption, type);
        if (string.IsNullOrEmpty(containerId))
        {
             return (false, "Failed to create Instagram Media Container (API rejected media/caption).", null);
        }

        // 3. Publish Container
        try 
        {
            await WaitForContainerReadyAsync(accessToken, containerId);

            var publishedId = await PublishMediaContainerAsync(accessToken, instagramBusinessId, containerId);
            return !string.IsNullOrEmpty(publishedId) ? (true, string.Empty, publishedId) : (false, "Failed to publish media container.", null);
        }
        catch (Exception ex)
        {
            return (false, $"Publishing process failed: {ex.Message}", null);
        }
    }

    public async Task<(bool Success, string Error, string? PublishedId)> PublishPostAsync(string accessToken, string userId, CreateInstagramPostRequest request, Stream? fileStream, string? fileName)
    {
        if (fileStream == null || string.IsNullOrEmpty(fileName))
        {
            return (false, "No file provided for Instagram upload.", null);
        }

        // 1. Upload to Azure Blob Storage
        string publicUrl;
        try 
        {
             publicUrl = await UploadMediaAsync(fileStream, fileName);
             Console.WriteLine($"[PublishPostAsync] Uploaded media to: {publicUrl}");
        }
        catch (Exception ex)
        {
            return (false, $"Blob Storage Upload Failed: {ex.Message}", null);
        }

        return await PublishPostWithUrlAsync(accessToken, userId, request.Caption, request.PostType, publicUrl);
    }

    private async Task<string?> GetInstagramBusinessIdAsync(string accessToken, string fbUserId)
    {
        // GET /me/accounts?fields=instagram_business_account&access_token=...
        var url = $"https://graph.facebook.com/v19.0/{fbUserId}/accounts?fields=name,instagram_business_account&access_token={accessToken}";
        var response = await _httpClient.GetAsync(url);
        
        if (!response.IsSuccessStatusCode) 
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"[GetInstagramBusinessIdAsync] API Error: {errorContent}");
            return null;
        }

        var data = await response.Content.ReadFromJsonAsync<FbAccountsResponse>();
        
        if (data?.Data == null || !data.Data.Any())
        {
             Console.WriteLine("[GetInstagramBusinessIdAsync] No Facebook Pages found for this user. Ensure the user manages a Facebook Page.");
             return null;
        }

        var pageWithIg = data.Data.FirstOrDefault(x => x.InstagramBusinessAccount?.Id != null);
        
        if (pageWithIg == null)
        {
            Console.WriteLine($"[GetInstagramBusinessIdAsync] Found {data.Data.Count} pages ({string.Join(", ", data.Data.Select(x => x.Name))}), but NONE have an Instagram Business Account connected. Please connect your Instagram Business account to your Facebook Page in Page Settings.");
            return null;
        }

        return pageWithIg.InstagramBusinessAccount?.Id;
    }

    private async Task<string?> CreateMediaContainerAsync(string accessToken, string igUserId, string mediaUrl, string caption, InstagramPostType type)
    {
        var url = $"https://graph.facebook.com/v19.0/{igUserId}/media";
        var query = new List<string>
        {
            $"access_token={accessToken}",
            $"caption={Uri.EscapeDataString(caption)}"
        };

        if (type == InstagramPostType.Story)
        {
             query.Add("media_type=STORIES");
             // Stories accept image_url or video_url
             if (mediaUrl.EndsWith(".mp4")) query.Add($"video_url={Uri.EscapeDataString(mediaUrl)}");
             else query.Add($"image_url={Uri.EscapeDataString(mediaUrl)}");
        }
        else if (type == InstagramPostType.Reel || mediaUrl.EndsWith(".mp4"))
        {
            query.Add("media_type=REELS");
            query.Add($"video_url={Uri.EscapeDataString(mediaUrl)}");
        }
        else // Post (Image)
        {
            query.Add($"image_url={Uri.EscapeDataString(mediaUrl)}");
        }

        var fullUrl = $"{url}?{string.Join("&", query)}";
        var response = await _httpClient.PostAsync(fullUrl, null); // POST request
        
        if (!response.IsSuccessStatusCode) 
        {
             var error = await response.Content.ReadAsStringAsync();
             Console.WriteLine($"IG Create Container Error: {error}");
             return null;
        }

        var result = await response.Content.ReadFromJsonAsync<IgContainerResponse>();
        return result?.Id;
    }

    private async Task WaitForContainerReadyAsync(string accessToken, string containerId)
    {
        // Poll for up to 60 seconds (status check every 3s)
        int retries = 0;
        while (retries < 20)
        {
            var url = $"https://graph.facebook.com/v19.0/{containerId}?fields=status_code,status&access_token={accessToken}";
            var response = await _httpClient.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                var status = await response.Content.ReadFromJsonAsync<IgContainerStatus>();
                Console.WriteLine($"[WaitForContainerReadyAsync] Container {containerId} status: {status?.StatusCode}");
                
                if (status?.StatusCode == "FINISHED") return;
                if (status?.StatusCode == "ERROR") 
                {
                     throw new Exception($"IG Container {containerId} processing failed. Instagram Status: ERROR. Verify the file is H.264 MP4 (AAC Audio) and accessible.");
                }
            }
            await Task.Delay(3000); // Wait 3s
            retries++;
        }
        throw new Exception("Timed out waiting for IG Container to process");
    }

    private async Task<string?> PublishMediaContainerAsync(string accessToken, string igUserId, string containerId)
    {
        var url = $"https://graph.facebook.com/v19.0/{igUserId}/media_publish?creation_id={containerId}&access_token={accessToken}";
        var response = await _httpClient.PostAsync(url, null);
        
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"IG Publish Error: {error}");
            return null;
        }

        var data = await response.Content.ReadFromJsonAsync<IgContainerResponse>();
        return data?.Id;
    }

    public async Task<bool> AddCommentAsync(string accessToken, string mediaId, string message)
    {
        // https://developers.facebook.com/docs/instagram-api/reference/ig-media/comments
        var url = $"https://graph.facebook.com/v19.0/{mediaId}/comments?message={Uri.EscapeDataString(message)}&access_token={accessToken}";
        var response = await _httpClient.PostAsync(url, null);
        
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"IG Add Comment Error: {error}");
            return false;
        }
        return true;
    }

    public async Task<List<InstagramMediaDto>> GetUserMediaAsync(string accessToken, string userId)
    {
        var instagramBusinessId = await GetInstagramBusinessIdAsync(accessToken, userId);
        if (string.IsNullOrEmpty(instagramBusinessId)) return new List<InstagramMediaDto>();

        var url = $"https://graph.facebook.com/v19.0/{instagramBusinessId}/media?fields=id,caption,media_type,media_url,permalink,thumbnail_url,timestamp,like_count,comments_count&access_token={accessToken}";
        var response = await _httpClient.GetAsync(url);
        
        if (!response.IsSuccessStatusCode)
        {
             var error = await response.Content.ReadAsStringAsync();
             Console.WriteLine($"[GetUserMediaAsync] Error: {error}");
             return new List<InstagramMediaDto>();
        }
        
        var result = await response.Content.ReadFromJsonAsync<IgMediaListResponse>();
        
        return result?.Data.Select(x => new InstagramMediaDto(
            x.Id, x.Caption, x.MediaType, x.MediaUrl, x.Permalink, x.ThumbnailUrl, x.Timestamp, x.LikeCount, x.CommentsCount
        )).ToList() ?? new List<InstagramMediaDto>();
    }

    public async Task<List<InstagramInsightMetric>> GetMediaInsightsAsync(string accessToken, string mediaId)
    {
        // 1. Determine Media Type to select correct metrics
        var typeUrl = $"https://graph.facebook.com/v19.0/{mediaId}?fields=media_type,media_product_type&access_token={accessToken}";
        var typeResp = await _httpClient.GetAsync(typeUrl);
        
        string metrics;
        if (typeResp.IsSuccessStatusCode)
        {
             var details = await typeResp.Content.ReadFromJsonAsync<IgMediaDetails>();
             // Logic: https://developers.facebook.com/docs/instagram-api/reference/ig-media/insights
             if (details?.MediaProductType == "REELS" || details?.MediaType == "VIDEO")
             {
                 // Consolidate Video & Reels logic. 
                 // 'impressions' and 'plays' are often tricky/deprecated for these types in v22+.
                 // We rely on 'reach' and engagement stats.
                 metrics = "reach,total_interactions,saved,likes,comments,shares"; 
             }
             else 
             {
                 // Image/Carousel
                 // 'impressions' removed due to v22 deprecation/error for some media.
                 metrics = "reach,saved,total_interactions,likes,comments";
             }
        }
        else
        {
             // Fallback if detail fetch fails
             metrics = "reach,saved"; 
        }

        var url = $"https://graph.facebook.com/v19.0/{mediaId}/insights?metric={metrics}&access_token={accessToken}";
        var response = await _httpClient.GetAsync(url);
        
        if (!response.IsSuccessStatusCode) 
        {
             // Debugging: Log error
             try {
                var err = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[GetMediaInsightsAsync] Failed: {err}");
             } catch {}
             return new List<InstagramInsightMetric>();
        }

        var result = await response.Content.ReadFromJsonAsync<IgInsightsResponse>();
        return result?.Data.Select(x => new InstagramInsightMetric(
            x.Name, x.Title, x.Description, 
            x.Values.Select(v => new InstagramInsightValue(v.Value.ToString() ?? "")).ToList()
        )).ToList() ?? new List<InstagramInsightMetric>();
    }

    public async Task<List<InstagramCommentDto>> GetRecentCommentsAsync(string accessToken, string userId)
    {
        // 1. Get recent media (limit to 10 to check for comments)
        var mediaList = await GetUserMediaAsync(accessToken, userId);
        if (!mediaList.Any()) return new List<InstagramCommentDto>();

        var recentMedia = mediaList.Take(10).ToList();
        var allComments = new List<InstagramCommentDto>();

        foreach (var media in recentMedia)
        {
            var url = $"https://graph.facebook.com/v19.0/{media.Id}/comments?fields=id,text,username,timestamp,like_count&access_token={accessToken}";
            try 
            {
                var response = await _httpClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<IgCommentListResponse>();
                    if (result?.Data != null)
                    {
                        var mapped = result.Data.Select(c => new InstagramCommentDto(
                            c.Id, c.Text, c.Username, c.Timestamp, c.LikeCount, 
                            media.Id, string.IsNullOrEmpty(media.ThumbnailUrl) ? media.MediaUrl : media.ThumbnailUrl // Use thumb/url identifying post
                        ));
                        allComments.AddRange(mapped);
                    }
                }
            } 
            catch { /* Ignore errors for individual media */ }
        }

        // Return sorted by newest first
        return allComments.OrderByDescending(c => DateTimeOffset.TryParse(c.Timestamp, out var dt) ? dt : DateTimeOffset.MinValue).ToList();
    }

    public async Task<bool> ReplyToCommentAsync(string accessToken, string commentId, string message)
    {
        // POST /{comment-id}/replies
        var url = $"https://graph.facebook.com/v19.0/{commentId}/replies?message={Uri.EscapeDataString(message)}&access_token={accessToken}";
        var response = await _httpClient.PostAsync(url, null);
        
        if (!response.IsSuccessStatusCode)
        {
             var err = await response.Content.ReadAsStringAsync();
             Console.WriteLine($"[ReplyToCommentAsync] Failed: {err}");
             return false;
        }
        return true;
    }
    public async Task<InstagramAccountInsightsResponse?> GetAccountInsightsAsync(string accessToken, string userId, DateTime? from, DateTime? to)
    {
        var instagramBusinessId = await GetInstagramBusinessIdAsync(accessToken, userId);
        if (string.IsNullOrEmpty(instagramBusinessId)) return null;

        var profile = await GetProfileAsync(accessToken, userId);
        if (profile == null) return null;

        var since = (from.HasValue ? new DateTimeOffset(from.Value) : DateTimeOffset.Now.AddDays(-30)).ToUnixTimeSeconds();
        var until = (to.HasValue ? new DateTimeOffset(to.Value) : DateTimeOffset.Now).ToUnixTimeSeconds();
        
        // --- Request 1: Reach (Time Series) ---
        // Reach works best with time_series to get the daily breakdown
        var url1 = $"https://graph.facebook.com/v19.0/{instagramBusinessId}/insights?metric=reach&period=day&since={since}&until={until}&access_token={accessToken}";
        
        var reachMetric = new InstagramAccountInsightMetricDto("reach", 0, new List<InstagramDailyValue>());
        
        var response1 = await _httpClient.GetAsync(url1);
        if (response1.IsSuccessStatusCode)
        {
             var result1 = await response1.Content.ReadFromJsonAsync<IgInsightsResponse>();
             if (result1 != null) reachMetric = ProcessMetric(result1.Data, "reach");
        }
        else 
        {
             Console.WriteLine($"[GetAccountInsightsAsync] Request 1 Failed: {await response1.Content.ReadAsStringAsync()}");
        }
        
        // --- Request 2: Accounts Engaged & Profile Views (Total Value) ---
        // These often require metric_type=total_value. Also "period=day" is seemingly required by API validation even for total_value?
        var url2 = $"https://graph.facebook.com/v19.0/{instagramBusinessId}/insights?metric=accounts_engaged,profile_views&metric_type=total_value&period=day&since={since}&until={until}&access_token={accessToken}";
        
        var accountsEngagedMetric = new InstagramAccountInsightMetricDto("accounts_engaged", 0, new List<InstagramDailyValue>());
        var profileViewsMetric = new InstagramAccountInsightMetricDto("profile_views", 0, new List<InstagramDailyValue>());

        var response2 = await _httpClient.GetAsync(url2);
        if (response2.IsSuccessStatusCode)
        {
             var json = await response2.Content.ReadAsStringAsync();
             // Console.WriteLine($"[GetAccountInsightsAsync] Request 2 RAW: {json}"); // Debug
             var result2 = JsonSerializer.Deserialize<IgInsightsResponse>(json);
             
             if (result2 != null) 
             {
                 accountsEngagedMetric = ProcessMetric(result2.Data, "accounts_engaged");
                 profileViewsMetric = ProcessMetric(result2.Data, "profile_views");
             }
        }
        else
        {
             Console.WriteLine($"[GetAccountInsightsAsync] Request 2 Failed: {await response2.Content.ReadAsStringAsync()}");
        }

        return new InstagramAccountInsightsResponse(
            profile.FollowersCount,
            reachMetric,
            accountsEngagedMetric,
            profileViewsMetric
        );
    }

    private InstagramAccountInsightMetricDto ProcessMetric(List<IgInsightMetric> data, string name)
    {
        var metric = data.FirstOrDefault(x => x.Name == name);
        if (metric == null) return new InstagramAccountInsightMetricDto(name, 0, new List<InstagramDailyValue>());

        var values = new List<InstagramDailyValue>();
        int total = 0;

        foreach (var v in metric.Values)
        {
             // For metric_type=total_value, EndTime might be present representing the whole period, or not.
             // We prioritize 'value' directly.
             
             int val = 0;
             if (v.Value is JsonElement je && je.ValueKind == JsonValueKind.Number)
             {
                 val = je.GetInt32();
             }
             else if (v.Value is JsonElement jeObject && jeObject.ValueKind == JsonValueKind.Object)
             {
                 // Handle error or special object structure
             } 
             else if (int.TryParse(v.Value?.ToString(), out int i))
             {
                 val = i;
             }
             
             total += val;
             
             // Try to parse date if available for completeness, though for total_value it's less critical for charts
             if (!string.IsNullOrEmpty(v.EndTime) && DateTime.TryParse(v.EndTime, out var date))
             {
                values.Add(new InstagramDailyValue(date, val));
             }
        }
        
        return new InstagramAccountInsightMetricDto(name, total, values);
    }
    private class IgMediaDetails {
        [JsonPropertyName("media_type")] public string MediaType { get; set; } = "";
        [JsonPropertyName("media_product_type")] public string MediaProductType { get; set; } = "";
    }

    // DTOs for Graph API responses
    private class IgMediaListResponse
    {
        [JsonPropertyName("data")]
        public List<IgMediaItem> Data { get; set; } = new();
    }
    
    private class IgMediaItem
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("caption")] public string Caption { get; set; } = "";
        [JsonPropertyName("media_type")] public string MediaType { get; set; } = "";
        [JsonPropertyName("media_url")] public string MediaUrl { get; set; } = "";
        [JsonPropertyName("permalink")] public string Permalink { get; set; } = "";
        [JsonPropertyName("thumbnail_url")] public string ThumbnailUrl { get; set; } = "";
        [JsonPropertyName("timestamp")] public string Timestamp { get; set; } = "";
        [JsonPropertyName("like_count")] public int LikeCount { get; set; }
        [JsonPropertyName("comments_count")] public int CommentsCount { get; set; }
    }

    private class IgInsightsResponse { [JsonPropertyName("data")] public List<IgInsightMetric> Data { get; set; } = new(); }
    
    private class IgInsightMetric {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("title")] public string Title { get; set; } = "";
        [JsonPropertyName("description")] public string Description { get; set; } = "";
        [JsonPropertyName("values")] public List<IgInsightValue> Values { get; set; } = new();
    }
    
    private class IgInsightValue { 
        [JsonPropertyName("value")] public object Value { get; set; } = new(); 
        [JsonPropertyName("end_time")] public string EndTime { get; set; } = "";
    }

    private class IgCommentListResponse { [JsonPropertyName("data")] public List<IgCommentItem> Data { get; set; } = new(); }
    private class IgCommentItem {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("text")] public string Text { get; set; } = "";
        [JsonPropertyName("username")] public string Username { get; set; } = "";
        [JsonPropertyName("timestamp")] public string Timestamp { get; set; } = "";
        [JsonPropertyName("like_count")] public int LikeCount { get; set; }
    }

    private class FbAccountsResponse { 
        [JsonPropertyName("data")]
        public List<FbPageDto> Data { get; set; } = new(); 
    }
    private class FbPageDto { 
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";
        
        [JsonPropertyName("instagram_business_account")]
        public IgBusinessAccountDto? InstagramBusinessAccount { get; set; } 
    }
    private class IgBusinessAccountDto { 
        [JsonPropertyName("id")]
        public string Id { get; set; } = ""; 
    }
    private class IgProfileDto 
    { 
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";
        
        [JsonPropertyName("username")]
        public string Username { get; set; } = "";
        
        [JsonPropertyName("media_count")] 
        public int MediaCount { get; set; } 

        [JsonPropertyName("followers_count")] 
        public int FollowersCount { get; set; }
        
        [JsonPropertyName("profile_picture_url")]
        public string ProfilePictureUrl { get; set; } = "";
        
        [JsonPropertyName("ig_id")]
        public long IgId { get; set; }
    }
    private class IgContainerResponse { 
        [JsonPropertyName("id")]
        public string Id { get; set; } = ""; 
    }
    private class IgContainerStatus { [JsonPropertyName("status_code")] public string StatusCode { get; set; } = ""; }


    private class FacebookTokenDto
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("token_type")]
        public string TokenType { get; set; } = string.Empty;
    }

    private class FacebookUserDto
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }
}
