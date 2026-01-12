using GenPosting.Shared.DTOs;

namespace GenPosting.Api.Features.Instagram.Services;

public interface IInstagramService
{
    string GetAuthorizationUrl(string redirectUri);
    Task<InstagramTokenResponse?> ExchangeTokenAsync(string code, string redirectUri);
    Task<InstagramUserDto?> GetProfileAsync(string accessToken, string userId);
    Task<(bool Success, string Error, string? PublishedId)> PublishPostAsync(string accessToken, string userId, CreateInstagramPostRequest request, Stream? fileStream, string? fileName);
    Task<(bool Success, string Error, string? PublishedId)> PublishPostWithUrlAsync(string accessToken, string userId, string caption, InstagramPostType type, string mediaUrl);
    Task<string> UploadMediaAsync(Stream fileStream, string fileName);
    Task<bool> AddCommentAsync(string accessToken, string mediaId, string message);
    Task<List<InstagramMediaDto>> GetUserMediaAsync(string accessToken, string userId);
    Task<List<InstagramInsightMetric>> GetMediaInsightsAsync(string accessToken, string mediaId);
    Task<List<InstagramCommentDto>> GetRecentCommentsAsync(string accessToken, string userId);
    Task<bool> ReplyToCommentAsync(string accessToken, string commentId, string message);
    Task<InstagramAccountInsightsResponse?> GetAccountInsightsAsync(string accessToken, string userId, DateTime? from, DateTime? to);
    Task<List<InstagramUserSearchResultDto>> SearchUsersAsync(string accessToken, string userId, string query);
}
