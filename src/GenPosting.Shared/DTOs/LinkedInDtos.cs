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

public record CreateLinkedInPostRequest(string Content);
public record LinkedInPostCreatedResponse(string Id);

public record LinkedInProfileDto(string Name, string? PictureUrl);
