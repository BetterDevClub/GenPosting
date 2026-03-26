using GenPosting.Shared.DTOs;

namespace GenPosting.Api.Features.Instagram.Services;

public interface IInstagramService
{
    (string Url, string State) GetAuthorizationUrl();
    Task<InstagramTokenResponse?> ExchangeTokenAsync(string code, CancellationToken cancellationToken = default);
    Task<InstagramUserDto?> GetProfileAsync(string accessToken, string userId, CancellationToken cancellationToken = default);
    Task<(bool Success, string Error, string? PublishedId)> PublishPostAsync(string accessToken, string userId, CreateInstagramPostRequest request, Stream? fileStream, string? fileName, CancellationToken cancellationToken = default);
    Task<(bool Success, string Error, string? PublishedId)> PublishPostWithUrlAsync(string accessToken, string userId, string caption, InstagramPostType type, string mediaUrl, CancellationToken cancellationToken = default);
    Task<bool> AddCommentAsync(string accessToken, string mediaId, string message, CancellationToken cancellationToken = default);
    Task<List<InstagramMediaDto>> GetUserMediaAsync(string accessToken, string userId, CancellationToken cancellationToken = default);
    Task<List<InstagramInsightMetric>> GetMediaInsightsAsync(string accessToken, string mediaId, CancellationToken cancellationToken = default);
    Task<List<InstagramCommentDto>> GetRecentCommentsAsync(string accessToken, string userId, CancellationToken cancellationToken = default);
    Task<bool> ReplyToCommentAsync(string accessToken, string commentId, string message, CancellationToken cancellationToken = default);
    Task<InstagramAccountInsightsResponse?> GetAccountInsightsAsync(string accessToken, string userId, DateTime? from, DateTime? to, CancellationToken cancellationToken = default);
    Task<List<InstagramUserSearchResultDto>> SearchUsersAsync(string accessToken, string userId, string query, CancellationToken cancellationToken = default);
}
