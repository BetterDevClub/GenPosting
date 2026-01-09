using System.Net.Http.Headers;
using GenPosting.Shared.DTOs;
using Microsoft.Extensions.Options;

namespace GenPosting.Api.Features.LinkedIn.Services;

public interface ILinkedInService
{
    string GetAuthorizationUrl(string redirectUri);
    Task<LinkedInTokenResponse?> ExchangeTokenAsync(string code, string redirectUri);
    Task<List<LinkedInPostDto>> GetPostsAsync(string accessToken);
    Task<LinkedInProfileDto?> GetProfileAsync(string accessToken);
    Task<LinkedInUploadResponse?> UploadMediaAsync(string accessToken, Stream fileStream, string contentType, bool isVideo);
    Task<(bool Success, string? Error, LinkedInPostCreatedResponse? Data)> CreatePostAsync(string accessToken, string content, List<string>? mediaUrns = null, string mediaType = "NONE");
}

public class LinkedInService : ILinkedInService
{
    private readonly HttpClient _httpClient;
    private readonly LinkedInSettings _settings;

    public LinkedInService(HttpClient httpClient, IOptions<LinkedInSettings> settings)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
    }

    public string GetAuthorizationUrl(string redirectUri)
    {
        var paramsDict = new Dictionary<string, string>
        {
            { "response_type", "code" },
            { "client_id", _settings.ClientId },
            { "redirect_uri", redirectUri },
            { "scope", _settings.Scope },
            { "state", Guid.NewGuid().ToString() } // In prod, manage state properly
        };

        var queryString = string.Join("&", paramsDict.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
        return $"{_settings.AuthUrl}?{queryString}";
    }

    public async Task<LinkedInTokenResponse?> ExchangeTokenAsync(string code, string redirectUri)
    {
        var paramsDict = new Dictionary<string, string>
        {
            { "grant_type", "authorization_code" },
            { "code", code },
            { "redirect_uri", redirectUri },
            { "client_id", _settings.ClientId },
            { "client_secret", _settings.ClientSecret }
        };

        var content = new FormUrlEncodedContent(paramsDict);
        var response = await _httpClient.PostAsync(_settings.TokenUrl, content);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var result = await response.Content.ReadFromJsonAsync<LinkedInTokenResponseInternal>();
        if (result == null) return null;

        return new LinkedInTokenResponse(result.access_token, result.expires_in);
    }

    public async Task<List<LinkedInPostDto>> GetPostsAsync(string accessToken)
    {
        // NOTE: fetching posts (UGC) usually requires querying 'ugcPosts' or 'shares' with specific author URN.
        // First we need the user's URN (profile ID).
        
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        _httpClient.DefaultRequestHeaders.Add("X-Restli-Protocol-Version", "2.0.0"); // Important for V2 API
        _httpClient.DefaultRequestHeaders.Add("LinkedIn-Version", "202306"); // Recommended versioning header, check docs for latest

        // 1. Get Profile to get URN
        string authorUrn;
        try 
        {
            var userInfoResponse = await _httpClient.GetFromJsonAsync<LinkedInUserInfoResponse>($"{_settings.ApiUrl}/userinfo");
            if (userInfoResponse == null) return new List<LinkedInPostDto>();
            authorUrn = $"urn:li:person:{userInfoResponse.sub}";
        }
        catch (HttpRequestException ex)
        {
            Console.WriteLine($"LinkedIn Profile Fetch Error: {ex.Message}");
            return new List<LinkedInPostDto>();
        }

        // 2. Fetch Posts (Simplified - in real world this query is more complex and depends on API version)
        // Using sample URN request for ugcPosts
        var requestUrl = $"{_settings.ApiUrl}/ugcPosts?q=authors&authors=List({Uri.EscapeDataString(authorUrn)})";
        
        // For this demo, we might mock if the API isn't accessible or returns 403 (common without partner program)
        try 
        {
            var postsResponse = await _httpClient.GetFromJsonAsync<LinkedInUgcPostsResponse>(requestUrl);
            
            // Map to DTO
            return postsResponse?.elements?.Select(e => new LinkedInPostDto(
                e.id,
                e.specificContent?.shareContent?.shareCommentary?.text ?? "No Content",
                DateTimeOffset.FromUnixTimeMilliseconds(e.created?.time ?? 0).UtcDateTime,
                new LinkedInPostMetricsDto(0,0,0,0) // Metrics usually require separate call or different field
            )).ToList() ?? new List<LinkedInPostDto>();
        }
        catch (HttpRequestException)
        {
            // Fallback for demo/empty state
            return new List<LinkedInPostDto>();
        }
    }

    public async Task<LinkedInProfileDto?> GetProfileAsync(string accessToken)
    {
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        try 
        {
            var userInfo = await _httpClient.GetFromJsonAsync<LinkedInUserInfoResponse>($"{_settings.ApiUrl}/userinfo");
            return userInfo != null ? new LinkedInProfileDto(userInfo.name, userInfo.picture) : null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LinkedInService] Error fetching profile: {ex.Message}");
            return null;
        }
    }

    public async Task<LinkedInUploadResponse?> UploadMediaAsync(string accessToken, Stream fileStream, string contentType, bool isVideo)
    {
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        _httpClient.DefaultRequestHeaders.Remove("X-Restli-Protocol-Version");
        _httpClient.DefaultRequestHeaders.Remove("LinkedIn-Version");
        _httpClient.DefaultRequestHeaders.Add("X-Restli-Protocol-Version", "2.0.0");
        _httpClient.DefaultRequestHeaders.Add("LinkedIn-Version", "202306");

        // 1. Get Author URN
        string authorUrn;
        try 
        {
            var userInfoResponse = await _httpClient.GetFromJsonAsync<LinkedInUserInfoResponse>($"{_settings.ApiUrl}/userinfo");
            if (userInfoResponse == null) return null;
            authorUrn = $"urn:li:person:{userInfoResponse.sub}";
        }
        catch { return null; }

        // 2. Register Upload
        var recipe = isVideo ? "urn:li:digitalmediaRecipe:feedshare-video" : "urn:li:digitalmediaRecipe:feedshare-image";
        
        var registerPayload = new
        {
            registerUploadRequest = new
            {
                recipes = new[] { recipe },
                owner = authorUrn,
                serviceRelationships = new[]
                {
                    new { relationshipType = "OWNER", identifier = "urn:li:userGeneratedContent" }
                }
            }
        };

        var registerResp = await _httpClient.PostAsJsonAsync($"{_settings.ApiUrl}/assets?action=registerUpload", registerPayload);
        if (!registerResp.IsSuccessStatusCode) 
        {
             var err = await registerResp.Content.ReadAsStringAsync();
             Console.WriteLine($"[LinkedInService] Register Upload Failed: {err}");
             return null;
        }

        var registerResult = await registerResp.Content.ReadFromJsonAsync<RegisterUploadResponse>();
        if (registerResult?.value == null) return null;

        var uploadUrl = registerResult.value.uploadMechanism.UploadHttpRequest.uploadUrl;
        var assetUrn = registerResult.value.asset; 

        // 3. Upload File
        using var uploadClient = new HttpClient();
        // Upload binary directly to the provided URL
        var fileContent = new StreamContent(fileStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        
        // LinkedIn requires us to set Authorization header to empty or token is not needed for the upload URL as it is signed
        // However, we must NOT send the Bearer token to the upload URL usually.
        
        var uploadResp = await uploadClient.PutAsync(uploadUrl, fileContent);
        if (!uploadResp.IsSuccessStatusCode)
        {
             Console.WriteLine($"[LinkedInService] Media Upload Failed: {uploadResp.StatusCode}");
             return null;
        }

        return new LinkedInUploadResponse(assetUrn);
    }

    public async Task<(bool Success, string? Error, LinkedInPostCreatedResponse? Data)> CreatePostAsync(string accessToken, string content, List<string>? mediaUrns = null, string mediaType = "NONE")
    {
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        _httpClient.DefaultRequestHeaders.Remove("X-Restli-Protocol-Version"); // Clear potential duplicates if reused
        _httpClient.DefaultRequestHeaders.Remove("LinkedIn-Version");
        
        _httpClient.DefaultRequestHeaders.Add("X-Restli-Protocol-Version", "2.0.0");
        _httpClient.DefaultRequestHeaders.Add("LinkedIn-Version", "202306");
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json")); // Explicit Accept
        
        Console.WriteLine($"[LinkedInService] Headers: X-Restli-Protocol-Version=2.0.0, LinkedIn-Version=202306");

        // 1. Get User URN
        string authorUrn;
        try 
        {
            var userInfoResponse = await _httpClient.GetFromJsonAsync<LinkedInUserInfoResponse>($"{_settings.ApiUrl}/userinfo");
            if (userInfoResponse == null) return (false, "Failed to fetch user info", null);
            authorUrn = $"urn:li:person:{userInfoResponse.sub}";
        }
        catch (Exception ex)
        {
            return (false, $"Error fetching profile: {ex.Message}", null);
        }

        // 2. Create Post Payload (UGC Post)
        // keys containing dots must be handled explicitly, so we use Dictionary for the nested objects
        
        var specificContent = new Dictionary<string, object>
        {
            { "shareCommentary", new { text = content } },
            { "shareMediaCategory", mediaType }
        };

        if (mediaUrns != null && mediaUrns.Any() && mediaType != "NONE")
        {
            var mediaList = mediaUrns.Select(urn => newDictionaryEntry(urn)).ToList();
            specificContent.Add("media", mediaList);
        }

        var postPayload = new Dictionary<string, object>
        {
            { "author", authorUrn },
            { "lifecycleState", "PUBLISHED" },
            { "specificContent", new Dictionary<string, object>
                {
                    { "com.linkedin.ugc.ShareContent", specificContent }
                }
            },
            { "visibility", new Dictionary<string, object>
                {
                    { "com.linkedin.ugc.MemberNetworkVisibility", "PUBLIC" }
                }
            }
        };

        // Serialize manually to string to ensure Content-Length is computed exactly and prevent chunking
        var jsonPayload = System.Text.Json.JsonSerializer.Serialize(postPayload);
        
        var requestMessage = new HttpRequestMessage(HttpMethod.Post, $"{_settings.ApiUrl}/ugcPosts");
        requestMessage.Content = new StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json");
        
        // Force disable chunked encoding to satisfy LinkedIn's strict server
        requestMessage.Headers.TransferEncodingChunked = false;
        
        // --- LOGGING ---
        Console.WriteLine($"[LinkedInService] Sending POST request to {_settings.ApiUrl}/ugcPosts");
        Console.WriteLine($"[LinkedInService] Payload: {jsonPayload}");
        
        // ----------------

        var response = await _httpClient.SendAsync(requestMessage);
        
        if (!response.IsSuccessStatusCode)
        {
             var errorBody = await response.Content.ReadAsStringAsync();
             Console.WriteLine($"[LinkedInService] ERROR: Status {response.StatusCode}");
             Console.WriteLine($"[LinkedInService] Response Body: {errorBody}");
             return (false, $"LinkedIn Error ({response.StatusCode}): {errorBody}", null);
        }

        var result = await response.Content.ReadFromJsonAsync<LinkedInUgcPostCreatedResponse>();
        return result != null 
            ? (true, null, new LinkedInPostCreatedResponse(result.id)) 
            : (false, "Empty response from LinkedIn", null);
    }

    private object newDictionaryEntry(string urn) 
    {
        return new 
        {
            status = "READY",
            description = new { text = "Media Content" },
            media = urn,
            title = new { text = "Media Content" }
        };
    }

    // Internal classes for JSON deserialization
    private record LinkedInTokenResponseInternal(string access_token, int expires_in);
    private record LinkedInUserInfoResponse(string sub, string name, string picture);
    private record LinkedInUgcPostsResponse(List<LinkedInUgcPostElement> elements);
    private record LinkedInUgcPostCreatedResponse(string id);
    private record LinkedInUgcPostElement(string id, LinkedInSpecificContent specificContent, LinkedInCreationInfo created);
    private record LinkedInSpecificContent(LinkedInShareContent shareContent);
    private record LinkedInShareContent(LinkedInShareCommentary shareCommentary);
    private record LinkedInShareCommentary(string text);
    private record LinkedInCreationInfo(long time);

    private record RegisterUploadResponse(RegisterUploadValue value);
    private record RegisterUploadValue(
        [property: System.Text.Json.Serialization.JsonPropertyName("mediaArtifact")] string mediaArtifact, 
        [property: System.Text.Json.Serialization.JsonPropertyName("uploadMechanism")] RegisterUploadMechanism uploadMechanism, 
        [property: System.Text.Json.Serialization.JsonPropertyName("asset")] string asset
    );
    private record RegisterUploadMechanism(
        [property: System.Text.Json.Serialization.JsonPropertyName("com.linkedin.digitalmedia.uploading.MediaUploadHttpRequest")] 
        RegisterUploadMediaRequest UploadHttpRequest
    );
    private record RegisterUploadMediaRequest(string uploadUrl);
}
