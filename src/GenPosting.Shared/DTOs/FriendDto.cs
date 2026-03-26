using System.ComponentModel.DataAnnotations;

namespace GenPosting.Shared.DTOs;

public record FriendDto
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public string Name { get; set; } = string.Empty;

    public string? LinkedInUrn { get; set; }

    public string? InstagramUsername { get; set; }
}
