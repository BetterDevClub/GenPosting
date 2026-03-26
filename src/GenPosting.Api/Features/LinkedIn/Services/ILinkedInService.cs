using GenPosting.Shared.DTOs;

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
