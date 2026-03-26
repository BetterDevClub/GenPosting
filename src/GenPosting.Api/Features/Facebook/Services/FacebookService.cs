using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GenPosting.Api.Features.Facebook.Models;
using GenPosting.Api.Services;
using GenPosting.Shared.DTOs;
using Microsoft.Extensions.Options;

namespace GenPosting.Api.Features.Facebook.Services;

public class FacebookService : IFacebookService
{
    private readonly HttpClient _httpClient;
    private readonly FacebookSettings _settings;
    private readonly IBlobStorageService _blobService;

    public FacebookService(HttpClient httpClient, IOptions<FacebookSettings> settings, IBlobStorageService blobService)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _blobService = blobService;
    }

    public string GetAuthorizationUrl(string redirectUri)
    {
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

    public async Task<FacebookTokenResponse?> ExchangeTokenAsync(string code, string redirectUri)
    {
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

        var tokenData = await response.Content.ReadFromJsonAsync<FbTokenDto>();
        if (tokenData == null) return null;

        // Get User ID
        var meUrl = $"https://graph.facebook.com/me?access_token={tokenData.AccessToken}";
        var meResponse = await _httpClient.GetAsync(meUrl);
        var meData = await meResponse.Content.ReadFromJsonAsync<FbUserDto>();

        return new FacebookTokenResponse(
            tokenData.AccessToken, 
            tokenData.ExpiresIn, 
            meData?.Id ?? string.Empty
        );
    }

    public async Task<FacebookUserDto?> GetProfileAsync(string accessToken, string userId)
    {
        var url = $"https://graph.facebook.com/v19.0/{userId}?fields=id,name,email,picture{{url}}&access_token={accessToken}";
        var response = await _httpClient.GetAsync(url);
        
        if (!response.IsSuccessStatusCode) return null;

        var data = await response.Content.ReadFromJsonAsync<FbProfileDto>();
        if (data == null) return null;

        return new FacebookUserDto(data.Id, data.Name, data.Email, data.Picture?.Data?.Url);
    }

    public async Task<List<FacebookPageDto>> GetUserPagesAsync(string accessToken, string userId)
    {
        var url = $"https://graph.facebook.com/v19.0/{userId}/accounts?fields=id,name,category,access_token,followers_count,fan_count,picture{{url}},cover{{source}}&access_token={accessToken}";
        var response = await _httpClient.GetAsync(url);
        
        if (!response.IsSuccessStatusCode) 
        {
            Console.WriteLine($"[GetUserPagesAsync] Failed: {await response.Content.ReadAsStringAsync()}");
            return new List<FacebookPageDto>();
        }

        var result = await response.Content.ReadFromJsonAsync<FbPagesResponse>();
        
        return result?.Data.Select(p => new FacebookPageDto(
            p.Id,
            p.Name,
            p.Category,
            p.AccessToken,
            p.FollowersCount,
            p.FanCount,
            p.Picture?.Data?.Url,
            p.Cover?.Source
        )).ToList() ?? new List<FacebookPageDto>();
    }

    public async Task<string> UploadMediaAsync(Stream fileStream, string fileName)
    {
        var contentType = fileName.EndsWith(".mp4") ? "video/mp4" : "image/jpeg";
        return await _blobService.UploadFileAsync(fileStream, fileName, contentType);
    }

    public async Task<(bool Success, string Error, string? PublishedId)> PublishPostWithUrlAsync(
        string accessToken, string content, FacebookPostType type, string mediaUrl, FacebookPostTarget target, string? targetId)
    {
        try
        {
            // Determine target endpoint
            string endpoint = target == FacebookPostTarget.Page && !string.IsNullOrEmpty(targetId) 
                ? targetId 
                : "me";

            switch (type)
            {
                case FacebookPostType.Text:
                    return await PublishTextPostAsync(accessToken, endpoint, content);
                    
                case FacebookPostType.Photo:
                    return await PublishPhotoPostAsync(accessToken, endpoint, content, mediaUrl);
                    
                case FacebookPostType.Video:
                    return await PublishVideoPostAsync(accessToken, endpoint, content, mediaUrl);
                    
                case FacebookPostType.Story:
                    return await PublishStoryAsync(accessToken, endpoint, mediaUrl);
                    
                default:
                    return (false, "Unsupported post type", null);
            }
        }
        catch (Exception ex)
        {
            return (false, $"Publishing failed: {ex.Message}", null);
        }
    }

    public async Task<(bool Success, string Error, string? PublishedId)> PublishPostAsync(
        string accessToken, CreateFacebookPostRequest request, Stream? fileStream, string? fileName)
    {
        // Handle album creation separately
        if (request.PostType == FacebookPostType.Album)
        {
            return await PublishAlbumAsync(accessToken, request, fileStream, fileName);
        }

        // Upload media if provided
        string? mediaUrl = null;
        if (fileStream != null && !string.IsNullOrEmpty(fileName))
        {
            try
            {
                var blobName = await UploadMediaAsync(fileStream, fileName);
                mediaUrl = await _blobService.GetSasUrlAsync(blobName, TimeSpan.FromHours(1));
                Console.WriteLine($"[PublishPostAsync] Uploaded media to: {mediaUrl}");
            }
            catch (Exception ex)
            {
                return (false, $"Media upload failed: {ex.Message}", null);
            }
        }
        else if (request.MediaUrls.Any())
        {
            mediaUrl = request.MediaUrls.First();
        }

        // For text-only posts, mediaUrl can be null
        if (request.PostType != FacebookPostType.Text && string.IsNullOrEmpty(mediaUrl))
        {
            return (false, "Media URL required for this post type", null);
        }

        return await PublishPostWithUrlAsync(
            accessToken, 
            request.Content, 
            request.PostType, 
            mediaUrl ?? string.Empty, 
            request.Target, 
            request.TargetId
        );
    }

    private async Task<(bool Success, string Error, string? PublishedId)> PublishTextPostAsync(
        string accessToken, string endpoint, string message)
    {
        var url = $"https://graph.facebook.com/v19.0/{endpoint}/feed";
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "message", message },
            { "access_token", accessToken }
        });

        var response = await _httpClient.PostAsync(url, content);
        
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"[PublishTextPost] Failed: {error}");
            return (false, "Failed to publish text post", null);
        }

        var result = await response.Content.ReadFromJsonAsync<FbPostResponse>();
        return (true, string.Empty, result?.Id);
    }

    private async Task<(bool Success, string Error, string? PublishedId)> PublishPhotoPostAsync(
        string accessToken, string endpoint, string message, string photoUrl)
    {
        var url = $"https://graph.facebook.com/v19.0/{endpoint}/photos";
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "url", photoUrl },
            { "caption", message },
            { "access_token", accessToken }
        });

        var response = await _httpClient.PostAsync(url, content);
        
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"[PublishPhotoPost] Failed: {error}");
            return (false, "Failed to publish photo post", null);
        }

        var result = await response.Content.ReadFromJsonAsync<FbPostResponse>();
        return (true, string.Empty, result?.Id);
    }

    private async Task<(bool Success, string Error, string? PublishedId)> PublishVideoPostAsync(
        string accessToken, string endpoint, string description, string videoUrl)
    {
        var url = $"https://graph.facebook.com/v19.0/{endpoint}/videos";
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "file_url", videoUrl },
            { "description", description },
            { "access_token", accessToken }
        });

        var response = await _httpClient.PostAsync(url, content);
        
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"[PublishVideoPost] Failed: {error}");
            return (false, "Failed to publish video post", null);
        }

        var result = await response.Content.ReadFromJsonAsync<FbPostResponse>();
        return (true, string.Empty, result?.Id);
    }

    private async Task<(bool Success, string Error, string? PublishedId)> PublishStoryAsync(
        string accessToken, string endpoint, string mediaUrl)
    {
        var url = $"https://graph.facebook.com/v19.0/{endpoint}/stories";
        var mediaType = mediaUrl.EndsWith(".mp4") ? "VIDEO" : "PHOTO";
        
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "url", mediaUrl },
            { "access_token", accessToken }
        });

        var response = await _httpClient.PostAsync(url, content);
        
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"[PublishStory] Failed: {error}");
            return (false, "Failed to publish story", null);
        }

        var result = await response.Content.ReadFromJsonAsync<FbPostResponse>();
        return (true, string.Empty, result?.Id);
    }

    private async Task<(bool Success, string Error, string? PublishedId)> PublishAlbumAsync(
        string accessToken, CreateFacebookPostRequest request, Stream? fileStream, string? fileName)
    {
        var endpoint = request.Target == FacebookPostTarget.Page && !string.IsNullOrEmpty(request.TargetId) 
            ? request.TargetId 
            : "me";

        // Create album
        var albumId = await CreateAlbumAsync(accessToken, endpoint, "Album", request.Content);
        if (string.IsNullOrEmpty(albumId))
        {
            return (false, "Failed to create album", null);
        }

        // Add photos to album
        foreach (var photoUrl in request.MediaUrls)
        {
            await AddPhotoToAlbumAsync(accessToken, albumId, photoUrl, null);
        }

        return (true, string.Empty, albumId);
    }

    public async Task<string> CreateAlbumAsync(string accessToken, string targetId, string name, string? description)
    {
        var url = $"https://graph.facebook.com/v19.0/{targetId}/albums";
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "name", name },
            { "message", description ?? string.Empty },
            { "access_token", accessToken }
        });

        var response = await _httpClient.PostAsync(url, content);
        
        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"[CreateAlbum] Failed: {await response.Content.ReadAsStringAsync()}");
            return string.Empty;
        }

        var result = await response.Content.ReadFromJsonAsync<FbPostResponse>();
        return result?.Id ?? string.Empty;
    }

    public async Task<bool> AddPhotoToAlbumAsync(string accessToken, string albumId, string photoUrl, string? caption)
    {
        var url = $"https://graph.facebook.com/v19.0/{albumId}/photos";
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "url", photoUrl },
            { "message", caption ?? string.Empty },
            { "access_token", accessToken }
        });

        var response = await _httpClient.PostAsync(url, content);
        return response.IsSuccessStatusCode;
    }

    public async Task<List<FacebookPostDto>> GetPostsAsync(string accessToken, string targetId, bool isPage)
    {
        // For personal profile, use "me/feed" instead of "me/posts" as it works with basic permissions
        var endpoint = isPage ? $"{targetId}/feed" : "me/feed";
        
        // Simplified fields - only request data that doesn't require special permissions
        // Removed: reactions.summary(true), comments.summary(true), shares (require pages_read_engagement)
        var url = $"https://graph.facebook.com/v19.0/{endpoint}?fields=id,message,story,full_picture,type,created_time,updated_time,permalink_url&access_token={accessToken}";
        
        var response = await _httpClient.GetAsync(url);
        
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"[GetPosts] Failed: {errorContent}");
            return new List<FacebookPostDto>();
        }

        var result = await response.Content.ReadFromJsonAsync<FbPostsResponse>();
        
        return result?.Data.Select(p => new FacebookPostDto(
            p.Id,
            p.Message ?? string.Empty,
            p.Story,
            p.FullPicture,
            p.Type,
            p.CreatedTime,
            p.UpdatedTime,
            p.PermalinkUrl,
            null, // Reactions not available without pages_read_engagement
            0, // Comments count not available
            0  // Shares count not available
        )).ToList() ?? new List<FacebookPostDto>();
    }

    public async Task<FacebookPostDto?> GetPostAsync(string accessToken, string postId)
    {
        var url = $"https://graph.facebook.com/v19.0/{postId}?fields=id,message,story,full_picture,type,created_time,updated_time,permalink_url,reactions.summary(true),comments.summary(true),shares&access_token={accessToken}";
        
        var response = await _httpClient.GetAsync(url);
        
        if (!response.IsSuccessStatusCode) return null;

        var post = await response.Content.ReadFromJsonAsync<FbPost>();
        if (post == null) return null;

        return new FacebookPostDto(
            post.Id,
            post.Message ?? string.Empty,
            post.Story,
            post.FullPicture,
            post.Type,
            post.CreatedTime,
            post.UpdatedTime,
            post.PermalinkUrl,
            MapReactions(post.Reactions),
            post.Comments?.Summary?.TotalCount ?? 0,
            post.Shares?.Count ?? 0
        );
    }

    public async Task<FacebookPostInsightsResponse?> GetPostInsightsAsync(string accessToken, string postId)
    {
        var url = $"https://graph.facebook.com/v19.0/{postId}/insights?metric=post_impressions,post_engaged_users,post_reactions_by_type_total,post_clicks&access_token={accessToken}";
        
        var response = await _httpClient.GetAsync(url);
        
        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"[GetPostInsights] Failed: {await response.Content.ReadAsStringAsync()}");
            return null;
        }

        var result = await response.Content.ReadFromJsonAsync<FbInsightsResponse>();
        if (result?.Data == null) return null;

        var impressions = GetMetricValue(result.Data, "post_impressions");
        var engagement = GetMetricValue(result.Data, "post_engaged_users");
        var clicks = GetMetricValue(result.Data, "post_clicks");

        // Get detailed post info for reactions/comments/shares
        var post = await GetPostAsync(accessToken, postId);

        return new FacebookPostInsightsResponse(
            postId,
            0, // Reach not available in post insights
            impressions,
            engagement,
            post?.Reactions ?? new FacebookReactionsDto(0, 0, 0, 0, 0, 0, 0),
            post?.CommentsCount ?? 0,
            post?.SharesCount ?? 0,
            clicks
        );
    }

    public async Task<FacebookPageInsightsResponse?> GetPageInsightsAsync(string accessToken, string pageId, DateTime? from, DateTime? to)
    {
        var since = (from ?? DateTime.Now.AddDays(-30)).ToUniversalTime();
        var until = (to ?? DateTime.Now).ToUniversalTime();
        
        var sinceUnix = new DateTimeOffset(since).ToUnixTimeSeconds();
        var untilUnix = new DateTimeOffset(until).ToUnixTimeSeconds();

        var url = $"https://graph.facebook.com/v19.0/{pageId}/insights?metric=page_impressions,page_impressions_unique,page_engaged_users,page_views_total,page_fans&period=day&since={sinceUnix}&until={untilUnix}&access_token={accessToken}";
        
        var response = await _httpClient.GetAsync(url);
        
        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"[GetPageInsights] Failed: {await response.Content.ReadAsStringAsync()}");
            return null;
        }

        var result = await response.Content.ReadFromJsonAsync<FbInsightsResponse>();
        if (result?.Data == null) return null;

        var impressionsMetric = ProcessMetric(result.Data, "page_impressions");
        var reachMetric = ProcessMetric(result.Data, "page_impressions_unique");
        var engagementMetric = ProcessMetric(result.Data, "page_engaged_users");
        var viewsMetric = ProcessMetric(result.Data, "page_views_total");

        // Get current fan count
        var pageUrl = $"https://graph.facebook.com/v19.0/{pageId}?fields=fan_count,followers_count&access_token={accessToken}";
        var pageResponse = await _httpClient.GetAsync(pageUrl);
        var pageData = await pageResponse.Content.ReadFromJsonAsync<FbPageStatsDto>();

        return new FacebookPageInsightsResponse(
            pageData?.FollowersCount ?? 0,
            pageData?.FanCount ?? 0,
            impressionsMetric,
            reachMetric,
            engagementMetric,
            viewsMetric
        );
    }

    public async Task<List<FacebookCommentDto>> GetRecentCommentsAsync(string accessToken, string targetId, bool isPage)
    {
        var posts = await GetPostsAsync(accessToken, targetId, isPage);
        var allComments = new List<FacebookCommentDto>();

        foreach (var post in posts.Take(10))
        {
            var url = $"https://graph.facebook.com/v19.0/{post.Id}/comments?fields=id,message,from,created_time,like_count,attachment&access_token={accessToken}";
            
            try
            {
                var response = await _httpClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<FbCommentsResponse>();
                    if (result?.Data != null)
                    {
                        var mapped = result.Data.Select(c => new FacebookCommentDto(
                            c.Id,
                            c.Message,
                            c.From?.Name ?? "Unknown",
                            c.From?.Id ?? string.Empty,
                            c.CreatedTime,
                            c.LikeCount,
                            post.Id,
                            c.Attachment?.Media?.Image?.Url
                        ));
                        allComments.AddRange(mapped);
                    }
                }
            }
            catch { /* Ignore errors */ }
        }

        return allComments.OrderByDescending(c => 
            DateTimeOffset.TryParse(c.CreatedTime, out var dt) ? dt : DateTimeOffset.MinValue
        ).ToList();
    }

    public async Task<bool> ReplyToCommentAsync(string accessToken, string commentId, string message)
    {
        var url = $"https://graph.facebook.com/v19.0/{commentId}/comments";
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "message", message },
            { "access_token", accessToken }
        });

        var response = await _httpClient.PostAsync(url, content);
        
        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"[ReplyToComment] Failed: {await response.Content.ReadAsStringAsync()}");
            return false;
        }
        
        return true;
    }

    public async Task<bool> DeleteCommentAsync(string accessToken, string commentId)
    {
        var url = $"https://graph.facebook.com/v19.0/{commentId}?access_token={accessToken}";
        var response = await _httpClient.DeleteAsync(url);
        
        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"[DeleteComment] Failed: {await response.Content.ReadAsStringAsync()}");
            return false;
        }
        
        return true;
    }

    private FacebookReactionsDto? MapReactions(FbReactionsData? reactionsData)
    {
        if (reactionsData == null) return null;

        var total = reactionsData.Summary?.TotalCount ?? 0;
        
        // Count reactions by type (simplified - would need detailed breakdown from API)
        return new FacebookReactionsDto(total, 0, 0, 0, 0, 0, 0);
    }

    private FacebookInsightMetricDto ProcessMetric(List<FbInsightMetric> data, string name)
    {
        var metric = data.FirstOrDefault(x => x.Name == name);
        if (metric == null) return new FacebookInsightMetricDto(name, 0, new List<FacebookDailyValue>());

        var values = new List<FacebookDailyValue>();
        int total = 0;

        foreach (var v in metric.Values)
        {
            int val = 0;
            if (v.Value is JsonElement je && je.ValueKind == JsonValueKind.Number)
            {
                val = je.GetInt32();
            }
            else if (int.TryParse(v.Value?.ToString(), out int i))
            {
                val = i;
            }
            
            total += val;
            
            if (!string.IsNullOrEmpty(v.EndTime) && DateTime.TryParse(v.EndTime, out var date))
            {
                values.Add(new FacebookDailyValue(date, val));
            }
        }
        
        return new FacebookInsightMetricDto(name, total, values);
    }

    private int GetMetricValue(List<FbInsightMetric> data, string name)
    {
        var metric = data.FirstOrDefault(x => x.Name == name);
        if (metric?.Values == null || !metric.Values.Any()) return 0;

        var value = metric.Values.First().Value;
        if (value is JsonElement je && je.ValueKind == JsonValueKind.Number)
        {
            return je.GetInt32();
        }
        if (int.TryParse(value?.ToString(), out int i))
        {
            return i;
        }
        return 0;
    }

    // DTOs for Graph API responses
    private class FbTokenDto
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }

    private class FbUserDto
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }

    private class FbProfileDto
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("picture")]
        public FbPictureData? Picture { get; set; }
    }

    private class FbPictureData
    {
        [JsonPropertyName("data")]
        public FbImageData? Data { get; set; }
    }

    private class FbImageData
    {
        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;
    }

    private class FbPagesResponse
    {
        [JsonPropertyName("data")]
        public List<FbPageData> Data { get; set; } = new();
    }

    private class FbPageData
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("category")]
        public string Category { get; set; } = string.Empty;

        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("followers_count")]
        public int FollowersCount { get; set; }

        [JsonPropertyName("fan_count")]
        public int FanCount { get; set; }

        [JsonPropertyName("picture")]
        public FbPictureData? Picture { get; set; }

        [JsonPropertyName("cover")]
        public FbCoverData? Cover { get; set; }
    }

    private class FbCoverData
    {
        [JsonPropertyName("source")]
        public string Source { get; set; } = string.Empty;
    }

    private class FbPostResponse
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;
    }

    private class FbPostsResponse
    {
        [JsonPropertyName("data")]
        public List<FbPost> Data { get; set; } = new();
    }

    private class FbPost
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("story")]
        public string? Story { get; set; }

        [JsonPropertyName("full_picture")]
        public string? FullPicture { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("created_time")]
        public string CreatedTime { get; set; } = string.Empty;

        [JsonPropertyName("updated_time")]
        public string UpdatedTime { get; set; } = string.Empty;

        [JsonPropertyName("permalink_url")]
        public string PermalinkUrl { get; set; } = string.Empty;

        [JsonPropertyName("reactions")]
        public FbReactionsData? Reactions { get; set; }

        [JsonPropertyName("comments")]
        public FbCommentsData? Comments { get; set; }

        [JsonPropertyName("shares")]
        public FbSharesData? Shares { get; set; }
    }

    private class FbReactionsData
    {
        [JsonPropertyName("summary")]
        public FbSummaryData? Summary { get; set; }
    }

    private class FbCommentsData
    {
        [JsonPropertyName("summary")]
        public FbSummaryData? Summary { get; set; }
    }

    private class FbSharesData
    {
        [JsonPropertyName("count")]
        public int Count { get; set; }
    }

    private class FbSummaryData
    {
        [JsonPropertyName("total_count")]
        public int TotalCount { get; set; }
    }

    private class FbInsightsResponse
    {
        [JsonPropertyName("data")]
        public List<FbInsightMetric> Data { get; set; } = new();
    }

    private class FbInsightMetric
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("values")]
        public List<FbInsightValue> Values { get; set; } = new();
    }

    private class FbInsightValue
    {
        [JsonPropertyName("value")]
        public object Value { get; set; } = new();

        [JsonPropertyName("end_time")]
        public string EndTime { get; set; } = string.Empty;
    }

    private class FbCommentsResponse
    {
        [JsonPropertyName("data")]
        public List<FbComment> Data { get; set; } = new();
    }

    private class FbComment
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        [JsonPropertyName("from")]
        public FbFromData? From { get; set; }

        [JsonPropertyName("created_time")]
        public string CreatedTime { get; set; } = string.Empty;

        [JsonPropertyName("like_count")]
        public int LikeCount { get; set; }

        [JsonPropertyName("attachment")]
        public FbAttachmentData? Attachment { get; set; }
    }

    private class FbFromData
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;
    }

    private class FbAttachmentData
    {
        [JsonPropertyName("media")]
        public FbMediaData? Media { get; set; }
    }

    private class FbMediaData
    {
        [JsonPropertyName("image")]
        public FbImageData? Image { get; set; }
    }

    private class FbPageStatsDto
    {
        [JsonPropertyName("fan_count")]
        public int FanCount { get; set; }

        [JsonPropertyName("followers_count")]
        public int FollowersCount { get; set; }
    }
}
