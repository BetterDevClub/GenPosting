using GenPosting.Shared.DTOs;

namespace GenPosting.Api.Features.LinkedIn.Models;

public class ScheduledPost
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string AccessToken { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public List<string>? MediaUrns { get; set; }
    public string MediaType { get; set; } = "NONE";
    public List<string>? Comments { get; set; }
    public DateTimeOffset ScheduledTime { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool IsPublished { get; set; } = false;
    public string? Status { get; set; } = "Pending";
}
