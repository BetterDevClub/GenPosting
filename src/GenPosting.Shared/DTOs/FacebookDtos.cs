namespace GenPosting.Shared.DTOs;

public record FacebookAuthUrlResponse(string AuthUrl);
public record FacebookExchangeTokenRequest(string Code, string RedirectUri);
public record FacebookTokenResponse(string AccessToken, int ExpiresInSeconds, string UserId);

public record FacebookUserDto(string Id, string Name, string? Email, string? ProfilePictureUrl);

public record FacebookPageDto(
    string Id,
    string Name,
    string Category,
    string AccessToken,
    int FollowersCount,
    int FanCount,
    string? ProfilePictureUrl,
    string? CoverPhoto
);

public record FacebookPageListResponse(List<FacebookPageDto> Data);

public enum FacebookPostType { Text, Photo, Video, Album, Story }

public enum FacebookPostTarget { Profile, Page }

public record CreateFacebookPostRequest(
    string Content,
    FacebookPostType PostType,
    FacebookPostTarget Target,
    string? TargetId, // Page ID if posting to page, null for personal profile
    List<string> MediaUrls,
    DateTimeOffset? ScheduledFor = null,
    bool Published = true
);

public record FacebookMediaUploadResponse(string Url);

public record FacebookPostDto(
    string Id,
    string Message,
    string? Story,
    string? FullPicture,
    string Type,
    string CreatedTime,
    string UpdatedTime,
    string Permalink,
    FacebookReactionsDto? Reactions,
    int CommentsCount,
    int SharesCount
);

public record FacebookReactionsDto(
    int TotalCount,
    int Like,
    int Love,
    int Wow,
    int Haha,
    int Sad,
    int Angry
);

public record FacebookPostListResponse(List<FacebookPostDto> Data, string? NextPageCursor);

public record FacebookPageInsightsRequest(string PageId, DateTime? From = null, DateTime? To = null);

public record FacebookInsightMetricDto(string Metric, int TotalValue, List<FacebookDailyValue> DailyValues);
public record FacebookDailyValue(DateTime Date, int Value);

public record FacebookPageInsightsResponse(
    int FollowersCount,
    int FanCount,
    FacebookInsightMetricDto PageImpressions,
    FacebookInsightMetricDto PageReach,
    FacebookInsightMetricDto PageEngagement,
    FacebookInsightMetricDto PageViews
);

public record FacebookPostInsightsResponse(
    string PostId,
    int Reach,
    int Impressions,
    int Engagement,
    FacebookReactionsDto Reactions,
    int Comments,
    int Shares,
    int Clicks
);

public record FacebookCommentDto(
    string Id,
    string Message,
    string From,
    string FromId,
    string CreatedTime,
    int LikeCount,
    string PostId,
    string? AttachmentUrl
);

public record FacebookCommentListResponse(List<FacebookCommentDto> Data);

public record ReplyToFacebookCommentRequest(string Message);

public record FacebookStoryDto(
    string Id,
    string Type,
    string? MediaUrl,
    string CreatedTime,
    string ExpiresAt,
    int Views,
    int Exits,
    int TapsForward,
    int TapsBack
);

public record FacebookAlbumDto(
    string Id,
    string Name,
    string? Description,
    int PhotoCount,
    string CreatedTime,
    string? CoverPhotoUrl
);

public record CreateFacebookAlbumRequest(
    string Name,
    string? Description,
    List<string> PhotoUrls,
    string? TargetPageId
);
