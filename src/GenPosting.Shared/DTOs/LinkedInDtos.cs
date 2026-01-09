namespace GenPosting.Shared.DTOs;

public record LinkedInAuthUrlResponse(string AuthUrl);
public record LinkedInExchangeTokenRequest(string Code, string RedirectUri);
public record LinkedInTokenResponse(string AccessToken, int ExpiresInSeconds);

public record LinkedInPostDto(
    string Id,
    string Content,
    DateTime PublishedAt,
    LinkedInPostMetricsDto Metrics
);

public record LinkedInPostMetricsDto(
    int Views,
    int Likes,
    int Comments,
    int Shares
);

public record CreateLinkedInPostRequest(
    string Content, 
    List<string>? MediaUrns = null, 
    string MediaType = "NONE", 
    List<string>? Comments = null, 
    DateTimeOffset? ScheduledAt = null,
    string? ThumbnailUrl = null
);
public record LinkedInPostCreatedResponse(string Id, bool IsScheduled = false);

public record LinkedInUploadResponse(string AssetUrn);

public record LinkedInProfileDto(string Name, string? PictureUrl);
