using GenPosting.Shared.DTOs;

namespace GenPosting.Api.Features.LinkedIn.Services;

public interface ILinkedInService
{
    (string Url, string State) GetAuthorizationUrl();
    Task<LinkedInTokenResponse?> ExchangeTokenAsync(string code, CancellationToken cancellationToken = default);
    Task<List<LinkedInPostDto>> GetPostsAsync(string accessToken, CancellationToken cancellationToken = default);
    Task<LinkedInProfileDto?> GetProfileAsync(string accessToken, CancellationToken cancellationToken = default);
    Task<LinkedInUploadResponse?> UploadMediaAsync(string accessToken, Stream fileStream, string contentType, bool isVideo, CancellationToken cancellationToken = default);
    Task<bool> AddCommentAsync(string accessToken, string postUrn, string commentText, CancellationToken cancellationToken = default);
    Task<(bool Success, string? Error, LinkedInPostCreatedResponse? Data)> CreatePostAsync(string accessToken, string content, List<string>? mediaUrns = null, string mediaType = "NONE", CancellationToken cancellationToken = default);
}
