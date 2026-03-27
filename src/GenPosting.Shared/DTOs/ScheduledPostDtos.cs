using GenPosting.Shared.Enums;

namespace GenPosting.Shared.DTOs;

public record ScheduledPostDto(
    Guid Id,
    SocialPlatform Platform,
    string Content,
    List<string>? MediaUrns,
    string MediaType,
    List<string>? Comments,
    DateTimeOffset ScheduledTime,
    ScheduledPostStatus Status,
    string? ThumbnailUrl = null,
    string? FailureReason = null,
    int RetryCount = 0,
    int MaxRetries = 3,
    DateTimeOffset? NextRetryAt = null
);

public record UpdateScheduledPostRequest(
    string Content,
    List<string>? MediaUrns,
    string MediaType,
    List<string>? Comments,
    DateTimeOffset ScheduledTime,
    string? ThumbnailUrl = null
);
