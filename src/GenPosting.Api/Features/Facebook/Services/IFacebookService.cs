using GenPosting.Shared.DTOs;

namespace GenPosting.Api.Features.Facebook.Services;

public interface IFacebookService
{
    string GetAuthorizationUrl(string redirectUri);
    Task<FacebookTokenResponse?> ExchangeTokenAsync(string code, string redirectUri);
    Task<FacebookUserDto?> GetProfileAsync(string accessToken, string userId);
    Task<List<FacebookPageDto>> GetUserPagesAsync(string accessToken, string userId);
    Task<(bool Success, string Error, string? PublishedId)> PublishPostAsync(string accessToken, CreateFacebookPostRequest request, Stream? fileStream, string? fileName);
    Task<(bool Success, string Error, string? PublishedId)> PublishPostWithUrlAsync(string accessToken, string content, FacebookPostType type, string mediaUrl, FacebookPostTarget target, string? targetId);
    Task<List<FacebookPostDto>> GetPostsAsync(string accessToken, string targetId, bool isPage);
    Task<FacebookPostDto?> GetPostAsync(string accessToken, string postId);
    Task<FacebookPostInsightsResponse?> GetPostInsightsAsync(string accessToken, string postId);
    Task<FacebookPageInsightsResponse?> GetPageInsightsAsync(string accessToken, string pageId, DateTime? from, DateTime? to);
    Task<List<FacebookCommentDto>> GetRecentCommentsAsync(string accessToken, string targetId, bool isPage);
    Task<bool> ReplyToCommentAsync(string accessToken, string commentId, string message);
    Task<bool> DeleteCommentAsync(string accessToken, string commentId);
    Task<string> CreateAlbumAsync(string accessToken, string targetId, string name, string? description);
    Task<bool> AddPhotoToAlbumAsync(string accessToken, string albumId, string photoUrl, string? caption);
}
