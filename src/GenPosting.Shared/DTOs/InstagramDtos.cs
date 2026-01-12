namespace GenPosting.Shared.DTOs;

public record InstagramAuthUrlResponse(string AuthUrl);
public record InstagramExchangeTokenRequest(string Code, string RedirectUri);
public record InstagramTokenResponse(string AccessToken, int ExpiresInSeconds, string UserId);
public record InstagramUserDto(string Id, string Username, string AccountType, int MediaCount);

public enum InstagramPostType { Post, Reel, Story }

public record CreateInstagramPostRequest(
    string Caption,
    InstagramPostType PostType,
    List<string> MediaUrls,
    DateTimeOffset? ScheduledFor = null
);
public record InstagramMediaUploadResponse(string Url);
