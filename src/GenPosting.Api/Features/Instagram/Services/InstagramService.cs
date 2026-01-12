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
        // https://graph.facebook.com/v19.0/{ig-user-id}?fields=username,profile_picture_url,media_count,name
        var url = $"https://graph.facebook.com/v19.0/{instagramBusinessId}?fields=username,name,profile_picture_url,media_count,ig_id&access_token={accessToken}";
        var response = await _httpClient.GetAsync(url);
        
        if (!response.IsSuccessStatusCode) return null;

        var data = await response.Content.ReadFromJsonAsync<IgProfileDto>();
        if (data == null) return null;

        return new InstagramUserDto(data.Id, data.Username, "BUSINESS", data.MediaCount);
    }

    public async Task<string> UploadMediaAsync(Stream fileStream, string fileName)
    {
         var contentType = fileName.EndsWith(".mp4") ? "video/mp4" : "image/jpeg";
         return await _blobService.UploadFileAsync(fileStream, fileName, contentType);
    }

    public async Task<(bool Success, string Error)> PublishPostWithUrlAsync(string accessToken, string userId, string caption, InstagramPostType type, string mediaUrl)
    {
        // 1. Get Instagram Business Account ID
        var instagramBusinessId = await GetInstagramBusinessIdAsync(accessToken, userId);
        if (string.IsNullOrEmpty(instagramBusinessId))
        {
            return (false, "No connected Instagram Account found.");
        }

        // 2. Create Media Container
        Console.WriteLine($"[PublishPostAsync] Creating container for: {mediaUrl}");
        string? containerId = await CreateMediaContainerAsync(accessToken, instagramBusinessId, mediaUrl, caption, type);
        if (string.IsNullOrEmpty(containerId))
        {
             return (false, "Failed to create Instagram Media Container (API rejected media/caption).");
        }

        // 3. Publish Container
        try 
        {
            await WaitForContainerReadyAsync(accessToken, containerId);

            var published = await PublishMediaContainerAsync(accessToken, instagramBusinessId, containerId);
            return published ? (true, string.Empty) : (false, "Failed to publish media container.");
        }
        catch (Exception ex)
        {
            return (false, $"Publishing process failed: {ex.Message}");
        }
    }

    public async Task<(bool Success, string Error)> PublishPostAsync(string accessToken, string userId, CreateInstagramPostRequest request, Stream? fileStream, string? fileName)
    {
        if (fileStream == null || string.IsNullOrEmpty(fileName))
        {
            return (false, "No file provided for Instagram upload.");
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
            return (false, $"Blob Storage Upload Failed: {ex.Message}");
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

        if (type == InstagramPostType.Reel || mediaUrl.EndsWith(".mp4"))
        {
            query.Add("media_type=REELS");
            query.Add($"video_url={Uri.EscapeDataString(mediaUrl)}");
        }
        else if (type == InstagramPostType.Story)
        {
             query.Add("media_type=STORIES");
             // Stories accept image_url or video_url
             if (mediaUrl.EndsWith(".mp4")) query.Add($"video_url={Uri.EscapeDataString(mediaUrl)}");
             else query.Add($"image_url={Uri.EscapeDataString(mediaUrl)}");
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

    private async Task<bool> PublishMediaContainerAsync(string accessToken, string igUserId, string containerId)
    {
        var url = $"https://graph.facebook.com/v19.0/{igUserId}/media_publish?creation_id={containerId}&access_token={accessToken}";
        var response = await _httpClient.PostAsync(url, null);
        
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"IG Publish Error: {error}");
            return false;
        }
        return true;
    }

    // DTOs for Graph API responses
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
