namespace GenPosting.Shared.DTOs;

public record InstagramAuthUrlResponse(string AuthUrl);
public record InstagramExchangeTokenRequest(string Code, string RedirectUri);
public record InstagramTokenResponse(string AccessToken, int ExpiresInSeconds, string UserId);
public record InstagramUserDto(string Id, string Username, string AccountType, int MediaCount, string? ProfilePictureUrl);

public enum InstagramPostType { Post, Reel, Story }

public record CreateInstagramPostRequest(
    string Caption,
    InstagramPostType PostType,
    List<string> MediaUrls,
    DateTimeOffset? ScheduledFor = null
);
public record InstagramMediaUploadResponse(string Url);

public record InstagramMediaDto(
    string Id,
    string Caption,
    string MediaType,
    string MediaUrl,
    string Permalink,
    string ThumbnailUrl,
    string Timestamp,
    int LikeCount,
    int CommentsCount
);

public record InstagramMediaListResponse(List<InstagramMediaDto> Data);

public record InstagramInsightValue(string Value);
public record InstagramInsightMetric(string Name, string Title, string Description, List<InstagramInsightValue> Values);
public record InstagramInsightsResponse(List<InstagramInsightMetric> Data);

public record InstagramCommentDto(
    string Id,
    string Text,
    string Username,
    string Timestamp,
    int LikeCount,
    string MediaId,
    string? MediaUrl // Optional: URL of the post this comment is on
);

public record InstagramCommentListResponse(List<InstagramCommentDto> Data);
public record ReplyToCommentRequest(string Message);

