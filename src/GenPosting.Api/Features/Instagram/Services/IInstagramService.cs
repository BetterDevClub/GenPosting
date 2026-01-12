using GenPosting.Shared.DTOs;

namespace GenPosting.Api.Features.Instagram.Services;

public interface IInstagramService
{
    string GetAuthorizationUrl(string redirectUri);
    Task<InstagramTokenResponse?> ExchangeTokenAsync(string code, string redirectUri);
    Task<InstagramUserDto?> GetProfileAsync(string accessToken, string userId);
    Task<(bool Success, string Error)> PublishPostAsync(string accessToken, string userId, CreateInstagramPostRequest request, Stream? fileStream, string? fileName);
    Task<(bool Success, string Error)> PublishPostWithUrlAsync(string accessToken, string userId, string caption, InstagramPostType type, string mediaUrl);
    Task<string> UploadMediaAsync(Stream fileStream, string fileName);
}
