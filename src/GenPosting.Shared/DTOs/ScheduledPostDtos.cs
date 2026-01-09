namespace GenPosting.Shared.DTOs;

public record ScheduledPostDto(
    Guid Id,
    string Content,
    List<string>? MediaUrns,
    string MediaType,
    List<string>? Comments,
    DateTimeOffset ScheduledTime,
    string Status,
    string? ThumbnailUrl = null
);

public record UpdateScheduledPostRequest(
    string Content,
    List<string>? MediaUrns,
    string MediaType,
    List<string>? Comments,
    DateTimeOffset ScheduledTime,
    string? ThumbnailUrl = null
);
