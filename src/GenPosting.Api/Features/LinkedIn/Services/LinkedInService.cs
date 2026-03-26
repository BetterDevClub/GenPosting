using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using GenPosting.Shared.DTOs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GenPosting.Api.Features.LinkedIn.Services;

public interface ILinkedInService
{
    (string Url, string State) GetAuthorizationUrl();
    Task<LinkedInTokenResponse?> ExchangeTokenAsync(string code);
    Task<List<LinkedInPostDto>> GetPostsAsync(string accessToken);
    Task<LinkedInProfileDto?> GetProfileAsync(string accessToken);
    Task<LinkedInUploadResponse?> UploadMediaAsync(string accessToken, Stream fileStream, string contentType, bool isVideo);
    Task<bool> AddCommentAsync(string accessToken, string postUrn, string commentText);
    Task<(bool Success, string? Error, LinkedInPostCreatedResponse? Data)> CreatePostAsync(string accessToken, string content, List<string>? mediaUrns = null, string mediaType = "NONE");
}

public class LinkedInService : ILinkedInService
{
    private readonly HttpClient _httpClient;
    private readonly LinkedInSettings _settings;
    private readonly ILogger<LinkedInService> _logger;

    public LinkedInService(HttpClient httpClient, IOptions<LinkedInSettings> settings, ILogger<LinkedInService> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public (string Url, string State) GetAuthorizationUrl()
    {
        var state = Guid.NewGuid().ToString();
        var paramsDict = new Dictionary<string, string>
        {
            { "response_type", "code" },
            { "client_id", _settings.ClientId },
            { "redirect_uri", _settings.CallbackUrl },
            { "scope", _settings.Scope },
            { "state", state }
        };

        var queryString = string.Join("&", paramsDict.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
        return ($"{_settings.AuthUrl}?{queryString}", state);
    }

    public async Task<LinkedInTokenResponse?> ExchangeTokenAsync(string code)
    {
        var paramsDict = new Dictionary<string, string>
        {
            { "grant_type", "authorization_code" },
            { "code", code },
            { "redirect_uri", _settings.CallbackUrl },
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

    private HttpRequestMessage CreateRequest(HttpMethod method, string url, string accessToken, bool withLinkedInHeaders = false)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (withLinkedInHeaders)
        {
            request.Headers.Add("X-Restli-Protocol-Version", "2.0.0");
            request.Headers.Add("LinkedIn-Version", "202401");
        }
        return request;
    }

    public async Task<List<LinkedInPostDto>> GetPostsAsync(string accessToken)
    {
        // NOTE: fetching posts (UGC) usually requires querying 'ugcPosts' or 'shares' with specific author URN.
        // First we need the user's URN (profile ID).

        // 1. Get Profile to get URN
        string authorUrn;
        try 
        {
            var userInfoRequest = CreateRequest(HttpMethod.Get, $"{_settings.ApiUrl}/userinfo", accessToken, withLinkedInHeaders: true);
            var userInfoResp = await _httpClient.SendAsync(userInfoRequest);
            var userInfoResponse = await userInfoResp.Content.ReadFromJsonAsync<LinkedInUserInfoResponse>();
            if (userInfoResponse == null) return new List<LinkedInPostDto>();
            authorUrn = $"urn:li:person:{userInfoResponse.sub}";
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError("LinkedIn Profile Fetch Error: {Message}", ex.Message);
            return new List<LinkedInPostDto>();
        }

        // 2. Fetch Posts (Simplified - in real world this query is more complex and depends on API version)
        // Using sample URN request for ugcPosts
        var requestUrl = $"{_settings.ApiUrl}/ugcPosts?q=authors&authors=List({Uri.EscapeDataString(authorUrn)})";
        
        // For this demo, we might mock if the API isn't accessible or returns 403 (common without partner program)
        try 
        {
            var postsRequest = CreateRequest(HttpMethod.Get, requestUrl, accessToken, withLinkedInHeaders: true);
            var postsResp = await _httpClient.SendAsync(postsRequest);
            var postsResponse = await postsResp.Content.ReadFromJsonAsync<LinkedInUgcPostsResponse>();
            
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
        try 
        {
            var request = CreateRequest(HttpMethod.Get, $"{_settings.ApiUrl}/userinfo", accessToken);
            var response = await _httpClient.SendAsync(request);
            var userInfo = await response.Content.ReadFromJsonAsync<LinkedInUserInfoResponse>();
            return userInfo != null ? new LinkedInProfileDto(userInfo.name, userInfo.picture) : null;
        }
        catch (Exception ex)
        {
            _logger.LogError("[LinkedInService] Error fetching profile: {Message}", ex.Message);
            return null;
        }
    }

    public async Task<LinkedInUploadResponse?> UploadMediaAsync(string accessToken, Stream fileStream, string contentType, bool isVideo)
    {
        // 1. Get Author URN
        string authorUrn;
        try 
        {
            var userInfoRequest = CreateRequest(HttpMethod.Get, $"{_settings.ApiUrl}/userinfo", accessToken, withLinkedInHeaders: true);
            var userInfoResp = await _httpClient.SendAsync(userInfoRequest);
            var userInfoResponse = await userInfoResp.Content.ReadFromJsonAsync<LinkedInUserInfoResponse>();
            if (userInfoResponse == null) return null;
            authorUrn = $"urn:li:person:{userInfoResponse.sub}";
        }
        catch { return null; }

        // 2. Initialize Upload (Images or Videos API)
        // Note: We use the /rest/ endpoint directly for modern APIs
        string initUrl;
        object initPayload;

        if (isVideo)
        {
            initUrl = "https://api.linkedin.com/rest/videos?action=initializeUpload";
            initPayload = new
            {
                initializeUploadRequest = new
                {
                    owner = authorUrn,
                    fileSizeBytes = fileStream.Length
                }
            };
        }
        else
        {
            initUrl = "https://api.linkedin.com/rest/images?action=initializeUpload";
            initPayload = new
            {
                initializeUploadRequest = new
                {
                    owner = authorUrn
                }
            };
        }

        var initRequest = CreateRequest(HttpMethod.Post, initUrl, accessToken, withLinkedInHeaders: true);
        initRequest.Content = new StringContent(
            System.Text.Json.JsonSerializer.Serialize(initPayload),
            System.Text.Encoding.UTF8,
            "application/json");
        var initResp = await _httpClient.SendAsync(initRequest);
        if (!initResp.IsSuccessStatusCode) 
        {
             var err = await initResp.Content.ReadAsStringAsync();
             _logger.LogError("[LinkedInService] Initialize Upload Failed: {Err}", err);
             return null;
        }

        var initResult = await initResp.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonNode>();
        if (initResult == null || initResult["value"] == null) return null;

        string uploadUrl;
        string assetUrn;

        if (isVideo)
        {
             var instructions = initResult["value"]?["uploadInstructions"]?.AsArray().FirstOrDefault();
             uploadUrl = instructions?["uploadUrl"]?.GetValue<string>() ?? "";
             assetUrn = initResult["value"]?["video"]?.GetValue<string>() ?? "";
        }
        else
        {
             uploadUrl = initResult["value"]?["uploadUrl"]?.GetValue<string>() ?? "";
             assetUrn = initResult["value"]?["image"]?.GetValue<string>() ?? "";
        }

        if (string.IsNullOrEmpty(uploadUrl) || string.IsNullOrEmpty(assetUrn)) return null;

        // 3. Upload File
        using var uploadClient = new HttpClient();
        // Upload binary directly to the provided URL
        var fileContent = new StreamContent(fileStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        
        // LinkedIn requires us to set Authorization header to empty or token is not needed for the upload URL as it is signed
        
        var uploadResp = await uploadClient.PutAsync(uploadUrl, fileContent);
        if (!uploadResp.IsSuccessStatusCode)
        {
             _logger.LogError("[LinkedInService] Media Upload Failed: {StatusCode}", uploadResp.StatusCode);
             return null;
        }

        return new LinkedInUploadResponse(assetUrn);
    }

    public async Task<bool> AddCommentAsync(string accessToken, string postUrn, string commentText)
    {
        var authorUrn = await GetAuthorUrnInternalAsync(accessToken);
        if (authorUrn == null) return false;

        var payload = new 
        {
            actor = authorUrn,
            @object = postUrn,
            message = new { text = commentText }
        };
        
        var serializerOptions = new System.Text.Json.JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        var jsonPayload = System.Text.Json.JsonSerializer.Serialize(payload, serializerOptions);
        var encodedUrn = Uri.EscapeDataString(postUrn);
        
        var request = CreateRequest(HttpMethod.Post, $"{_settings.ApiUrl}/socialActions/{encodedUrn}/comments", accessToken, withLinkedInHeaders: true);
        request.Content = new StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json");
        request.Headers.TransferEncodingChunked = false; // FIX: Prevent protocol violation
        
        var response = await _httpClient.SendAsync(request);
        
        if (!response.IsSuccessStatusCode)
        {
             var err = await response.Content.ReadAsStringAsync();
             _logger.LogError("[LinkedInService] Add Comment Failed: {StatusCode} - {Err}", response.StatusCode, err);
             return false;
        }
        
        _logger.LogInformation("[LinkedInService] Added comment to {PostUrn}", postUrn);
        return true;
    }

    private async Task<string?> GetAuthorUrnInternalAsync(string accessToken)
    {
        try 
        {
            var request = CreateRequest(HttpMethod.Get, $"{_settings.ApiUrl}/userinfo", accessToken);
            var response = await _httpClient.SendAsync(request);
            var userInfoResponse = await response.Content.ReadFromJsonAsync<LinkedInUserInfoResponse>();
            return userInfoResponse != null ? $"urn:li:person:{userInfoResponse.sub}" : null;
        }
        catch (Exception ex)
        {
            _logger.LogError("[LinkedInService] Error fetching profile: {Message}", ex.Message);
            return null;
        }
    }

    public async Task<(bool Success, string? Error, LinkedInPostCreatedResponse? Data)> CreatePostAsync(string accessToken, string content, List<string>? mediaUrns = null, string mediaType = "NONE")
    {
        _logger.LogDebug("[LinkedInService] Headers: X-Restli-Protocol-Version=2.0.0, LinkedIn-Version=202401");

        // 1. Get User URN
        string authorUrn;
        try 
        {
            var userInfoRequest = CreateRequest(HttpMethod.Get, $"{_settings.ApiUrl}/userinfo", accessToken, withLinkedInHeaders: true);
            var userInfoResp = await _httpClient.SendAsync(userInfoRequest);
            var userInfoResponse = await userInfoResp.Content.ReadFromJsonAsync<LinkedInUserInfoResponse>();
            if (userInfoResponse == null) return (false, "Failed to fetch user info", null);
            authorUrn = $"urn:li:person:{userInfoResponse.sub}";
        }
        catch (Exception ex)
        {
            return (false, $"Error fetching profile: {ex.Message}", null);
        }

        // 2. Create Post Payload (Posts API)
        // Doc: https://learn.microsoft.com/en-us/linkedin/marketing/community-management/shares/posts-api
        
        var postPayload = new Dictionary<string, object>
        {
            { "author", authorUrn },
            { "commentary", content }, // Directly supports Little Text Format @[Name](urn)
            { "visibility", "PUBLIC" },
            { "distribution", new 
                {
                    feedDistribution = "MAIN_FEED",
                    targetEntities = Array.Empty<object>(),
                    thirdPartyDistributionChannels = Array.Empty<object>()
                } 
            },
            { "lifecycleState", "PUBLISHED" },
            { "isReshareDisabledByAuthor", false }
        };

        if (mediaUrns != null && mediaUrns.Any() && mediaType != "NONE")
        {
            if (mediaType == "VIDEO")
            {
                 // Single video
                 postPayload["content"] = new 
                 {
                     media = new 
                     {
                         id = mediaUrns.First(),
                         title = "Video Content"
                     }
                 };
            }
            else if (mediaType == "IMAGE")
            {
                if (mediaUrns.Count == 1)
                {
                    // Single image
                    postPayload["content"] = new 
                    {
                        media = new 
                        {
                            id = mediaUrns.First(),
                            title = "Image Content"
                        }
                    };
                }
                else
                {
                    // Multi-image
                    postPayload["content"] = new 
                    {
                        multiImage = new 
                        {
                            images = mediaUrns.Select(urn => new { id = urn }).ToArray()
                        }
                    };
                }
            }
        }

        // Serialize manually to string to ensure Content-Length is computed exactly
        var serializerOptions = new System.Text.Json.JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        var jsonPayload = System.Text.Json.JsonSerializer.Serialize(postPayload, serializerOptions);
        
        // Use /rest/posts endpoint
        var requestMessage = CreateRequest(HttpMethod.Post, "https://api.linkedin.com/rest/posts", accessToken, withLinkedInHeaders: true);
        requestMessage.Content = new StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json");
        
        // Force disable chunked encoding to satisfy LinkedIn's strict server
        requestMessage.Headers.TransferEncodingChunked = false;
        
        // --- LOGGING ---
        _logger.LogInformation("[LinkedInService] Sending POST request to https://api.linkedin.com/rest/posts");
        _logger.LogDebug("[LinkedInService] Payload: {JsonPayload}", jsonPayload);
        
        // ----------------

        var response = await _httpClient.SendAsync(requestMessage);
        
        if (!response.IsSuccessStatusCode)
        {
             var errorBody = await response.Content.ReadAsStringAsync();
             _logger.LogError("[LinkedInService] ERROR: Status {StatusCode}", response.StatusCode);
             _logger.LogError("[LinkedInService] Response Body: {ErrorBody}", errorBody);
             return (false, $"LinkedIn Error ({response.StatusCode}): {errorBody}", null);
        }

        // Response header x-restli-id contains the ID
        if (response.Headers.TryGetValues("x-restli-id", out var ids))
        {
            var postId = ids.FirstOrDefault();
            return (true, null, new LinkedInPostCreatedResponse(postId ?? ""));
        }
        
        // Sometimes body contains ID, but posts API usually uses header
        return (true, null, new LinkedInPostCreatedResponse("CREATED"));
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
