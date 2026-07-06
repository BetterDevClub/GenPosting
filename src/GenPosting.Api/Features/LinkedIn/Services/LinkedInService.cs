using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using GenPosting.Shared.DTOs;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GenPosting.Api.Features.LinkedIn.Services;

public class LinkedInService : ILinkedInService
{
    private static readonly string[] LinkedInApiVersions = ["202506", "202505", "202504", "202503", "202502"];
    private static readonly TimeSpan AuthorUrnCacheTtl = TimeSpan.FromHours(1);

    private readonly HttpClient _httpClient;
    private readonly LinkedInSettings _settings;
    private readonly IMemoryCache _cache;
    private readonly ILogger<LinkedInService> _logger;

    public LinkedInService(HttpClient httpClient, IOptions<LinkedInSettings> settings, IMemoryCache cache, ILogger<LinkedInService> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _cache = cache;
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

    public async Task<LinkedInTokenResponse?> ExchangeTokenAsync(string code, CancellationToken cancellationToken = default)
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
        var response = await _httpClient.PostAsync(_settings.TokenUrl, content, cancellationToken);

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
            request.Headers.Add("LinkedIn-Version", LinkedInApiVersions[0]);
        }
        return request;
    }

    public async Task<List<LinkedInPostDto>> GetPostsAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        var authorUrn = await GetAuthorUrnInternalAsync(accessToken, cancellationToken);
        if (authorUrn == null)
        {
            _logger.LogError("LinkedIn Profile Fetch Error: unable to resolve author URN");
            return new List<LinkedInPostDto>();
        }

        // Fetch Posts (Simplified - in real world this query is more complex and depends on API version)
        var requestUrl = $"{_settings.ApiUrl}/ugcPosts?q=authors&authors=List({Uri.EscapeDataString(authorUrn)})";
        
        // For this demo, we might mock if the API isn't accessible or returns 403 (common without partner program)
        try 
        {
            var postsRequest = CreateRequest(HttpMethod.Get, requestUrl, accessToken, withLinkedInHeaders: true);
            var postsResp = await _httpClient.SendAsync(postsRequest, cancellationToken);
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

    public async Task<LinkedInProfileDto?> GetProfileAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        try 
        {
            var request = CreateRequest(HttpMethod.Get, $"{_settings.ApiUrl}/userinfo", accessToken);
            var response = await _httpClient.SendAsync(request, cancellationToken);
            var userInfo = await response.Content.ReadFromJsonAsync<LinkedInUserInfoResponse>();
            return userInfo != null ? new LinkedInProfileDto(userInfo.name, userInfo.picture) : null;
        }
        catch (Exception ex)
        {
            _logger.LogError("[LinkedInService] Error fetching profile: {Message}", ex.Message);
            return null;
        }
    }

    public async Task<LinkedInUploadResponse?> UploadMediaAsync(string accessToken, Stream fileStream, string contentType, bool isVideo, CancellationToken cancellationToken = default)
    {
        var authorUrn = await GetAuthorUrnInternalAsync(accessToken, cancellationToken);
        if (authorUrn == null) return null;

        // Initialize Upload (Images or Videos API)
        string initUrl;
        object initPayload;

        if (isVideo)
        {
            initUrl = $"{_settings.RestApiUrl}/videos?action=initializeUpload";
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
            initUrl = $"{_settings.RestApiUrl}/images?action=initializeUpload";
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
        var initResp = await _httpClient.SendAsync(initRequest, cancellationToken);
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
        
        var uploadResp = await uploadClient.PutAsync(uploadUrl, fileContent, cancellationToken);
        if (!uploadResp.IsSuccessStatusCode)
        {
             _logger.LogError("[LinkedInService] Media Upload Failed: {StatusCode}", uploadResp.StatusCode);
             return null;
        }

        return new LinkedInUploadResponse(assetUrn);
    }

    public async Task<bool> AddCommentAsync(string accessToken, string postUrn, string commentText, CancellationToken cancellationToken = default)
    {
        var authorUrn = await GetAuthorUrnInternalAsync(accessToken, cancellationToken);
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
        
        var response = await _httpClient.SendAsync(request, cancellationToken);
        
        if (!response.IsSuccessStatusCode)
        {
             var err = await response.Content.ReadAsStringAsync();
             _logger.LogError("[LinkedInService] Add Comment Failed: {StatusCode} - {Err}", response.StatusCode, err);
             return false;
        }
        
        _logger.LogInformation("[LinkedInService] Added comment to {PostUrn}", postUrn);
        return true;
    }

    private async Task<string?> GetAuthorUrnInternalAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"linkedin:author-urn:{accessToken.GetHashCode()}";
        if (_cache.TryGetValue(cacheKey, out string? cached))
            return cached;

        try
        {
            var request = CreateRequest(HttpMethod.Get, $"{_settings.ApiUrl}/userinfo", accessToken);
            var response = await _httpClient.SendAsync(request, cancellationToken);
            var userInfoResponse = await response.Content.ReadFromJsonAsync<LinkedInUserInfoResponse>();
            if (userInfoResponse == null) return null;

            var urn = $"urn:li:person:{userInfoResponse.sub}";
            _cache.Set(cacheKey, urn, AuthorUrnCacheTtl);
            return urn;
        }
        catch (Exception ex)
        {
            _logger.LogError("[LinkedInService] Error fetching profile: {Message}", ex.Message);
            return null;
        }
    }

    public async Task<(bool Success, string? Error, LinkedInPostCreatedResponse? Data)> CreatePostAsync(string accessToken, string content, List<string>? mediaUrns = null, string mediaType = "NONE", CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[LinkedInService] Headers: X-Restli-Protocol-Version=2.0.0, LinkedIn-Version={LinkedInApiVersion}", LinkedInApiVersions[0]);

        var authorUrn = await GetAuthorUrnInternalAsync(accessToken, cancellationToken);
        if (authorUrn == null) return (false, "Failed to fetch user info", null);

        // Create Post Payload (Posts API)
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
        
        HttpResponseMessage? response = null;

        for (var index = 0; index < LinkedInApiVersions.Length; index++)
        {
            var version = LinkedInApiVersions[index];
            var requestMessage = CreateRequest(HttpMethod.Post, $"{_settings.RestApiUrl}/posts", accessToken, withLinkedInHeaders: true);
            requestMessage.Content = new StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json");
            requestMessage.Headers.TransferEncodingChunked = false;
            requestMessage.Headers.Remove("LinkedIn-Version");
            requestMessage.Headers.Add("LinkedIn-Version", version);

            _logger.LogInformation("[LinkedInService] Sending POST request to {RestApiUrl}/posts", _settings.RestApiUrl);
            _logger.LogDebug("[LinkedInService] Payload: {JsonPayload}", jsonPayload);

            response = await _httpClient.SendAsync(requestMessage, cancellationToken);
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                if (response.Headers.TryGetValues("x-restli-id", out var ids))
                {
                    var postId = ids.FirstOrDefault();
                    return (true, null, new LinkedInPostCreatedResponse(postId ?? ""));
                }

                return (true, null, new LinkedInPostCreatedResponse("CREATED"));
            }

            var nextVersion = index < LinkedInApiVersions.Length - 1 ? LinkedInApiVersions[index + 1] : null;
            if (!IsVersionMismatch(response.StatusCode, errorBody) || nextVersion is null)
            {
                _logger.LogError("[LinkedInService] ERROR: Status {StatusCode}", response.StatusCode);
                _logger.LogError("[LinkedInService] Response Body: {ErrorBody}", errorBody);
                return (false, $"LinkedIn Error ({response.StatusCode}): {errorBody}", null);
            }

            _logger.LogWarning("[LinkedInService] LinkedIn API version {Version} was rejected; retrying with {NextVersion}", version, nextVersion);
        }

        var finalErrorBody = await response!.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogError("[LinkedInService] ERROR: Status {StatusCode}", response.StatusCode);
        _logger.LogError("[LinkedInService] Response Body: {ErrorBody}", finalErrorBody);
        return (false, $"LinkedIn Error ({response.StatusCode}): {finalErrorBody}", null);
    }

    private static bool IsVersionMismatch(HttpStatusCode statusCode, string errorBody)
        => statusCode == HttpStatusCode.UpgradeRequired ||
           statusCode == HttpStatusCode.BadRequest &&
           (errorBody.Contains("version", StringComparison.OrdinalIgnoreCase) ||
            errorBody.Contains("unsupported", StringComparison.OrdinalIgnoreCase));

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
