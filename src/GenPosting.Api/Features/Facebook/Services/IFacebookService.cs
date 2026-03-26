using GenPosting.Shared.DTOs;

namespace GenPosting.Api.Features.Facebook.Services;

public interface IFacebookService
{
    string GetAuthorizationUrl(string redirectUri);
    Task<FacebookTokenResponse?> ExchangeTokenAsync(string code, string redirectUri, CancellationToken cancellationToken = default);
    Task<FacebookUserDto?> GetProfileAsync(string accessToken, string userId, CancellationToken cancellationToken = default);
    Task<List<FacebookPageDto>> GetUserPagesAsync(string accessToken, string userId, CancellationToken cancellationToken = default);
    Task<(bool Success, string Error, string? PublishedId)> PublishPostAsync(string accessToken, CreateFacebookPostRequest request, Stream? fileStream, string? fileName, CancellationToken cancellationToken = default);
    Task<(bool Success, string Error, string? PublishedId)> PublishPostWithUrlAsync(string accessToken, string content, FacebookPostType type, string mediaUrl, FacebookPostTarget target, string? targetId, CancellationToken cancellationToken = default);
    Task<List<FacebookPostDto>> GetPostsAsync(string accessToken, string targetId, bool isPage, CancellationToken cancellationToken = default);
    Task<FacebookPostDto?> GetPostAsync(string accessToken, string postId, CancellationToken cancellationToken = default);
    Task<FacebookPostInsightsResponse?> GetPostInsightsAsync(string accessToken, string postId, CancellationToken cancellationToken = default);
    Task<FacebookPageInsightsResponse?> GetPageInsightsAsync(string accessToken, string pageId, DateTime? from, DateTime? to, CancellationToken cancellationToken = default);
    Task<List<FacebookCommentDto>> GetRecentCommentsAsync(string accessToken, string targetId, bool isPage, CancellationToken cancellationToken = default);
    Task<bool> ReplyToCommentAsync(string accessToken, string commentId, string message, CancellationToken cancellationToken = default);
    Task<bool> DeleteCommentAsync(string accessToken, string commentId, CancellationToken cancellationToken = default);
    Task<string> CreateAlbumAsync(string accessToken, string targetId, string name, string? description, CancellationToken cancellationToken = default);
    Task<bool> AddPhotoToAlbumAsync(string accessToken, string albumId, string photoUrl, string? caption, CancellationToken cancellationToken = default);
}
