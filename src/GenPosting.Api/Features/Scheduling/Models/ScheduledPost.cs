using GenPosting.Shared.DTOs;
using GenPosting.Shared.Enums;

namespace GenPosting.Api.Features.Scheduling.Models;

public class ScheduledPost
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public SocialPlatform Platform { get; set; } = SocialPlatform.LinkedIn;
    public string PlatformUserId { get; set; } = string.Empty; // e.g. LinkedIn URN or IG UserID
    public string AccessToken { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty; // Caption for IG
    public List<string>? MediaUrns { get; set; } // Stores URLs for IG/FB
    public string MediaType { get; set; } = "NONE"; // LinkedIn media type, or "InstagramImage"/"InstagramVideo"
    public InstagramPostType? IgPostType { get; set; } // Specific for IG
    public FacebookPostType? FbPostType { get; set; } // Specific for FB
    public FacebookPostTarget? FbTarget { get; set; } // FB: Profile or Page
    public string? FbTargetId { get; set; } // FB: Page ID if posting to page
    public List<string>? Comments { get; set; }
    public DateTimeOffset ScheduledTime { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? ThumbnailUrl { get; set; }
    public bool IsPublished { get; set; } = false;
    public ScheduledPostStatus Status { get; set; } = ScheduledPostStatus.Pending;
    public string? FailureReason { get; set; }
}
