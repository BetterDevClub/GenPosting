using GenPosting.Api.Features.Friends.Services;
using GenPosting.Api.Features.Scheduling.Models;
using GenPosting.Shared.DTOs;
using Microsoft.EntityFrameworkCore;

namespace GenPosting.Api.Data;

public sealed class GenPostingDbContext : DbContext
{
    public GenPostingDbContext(DbContextOptions<GenPostingDbContext> options)
        : base(options)
    {
    }

    public DbSet<FriendEntity> Friends => Set<FriendEntity>();
    public DbSet<ScheduledPostEntity> ScheduledPosts => Set<ScheduledPostEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FriendEntity>(entity =>
        {
            entity.HasKey(friend => friend.Id);
            entity.Property(friend => friend.Name).IsRequired().HasMaxLength(200);
        });

        modelBuilder.Entity<ScheduledPostEntity>(entity =>
        {
            entity.HasKey(post => post.Id);
            entity.Property(post => post.Content).IsRequired().HasMaxLength(4000);
            entity.Property(post => post.Platform).HasConversion<string>();
            entity.Property(post => post.Status).HasConversion<string>();
            entity.Property(post => post.MediaType).HasMaxLength(200);
            entity.Property(post => post.FbTarget).HasConversion<string>();
            entity.Property(post => post.IgPostType).HasConversion<string>();
            entity.Property(post => post.FbPostType).HasConversion<string>();
            entity.Property(post => post.PlatformUserId).HasMaxLength(500);
            entity.Property(post => post.AccessToken).HasMaxLength(4000);
            entity.Property(post => post.ThumbnailUrl).HasMaxLength(2000);
            entity.Property(post => post.FailureReason).HasMaxLength(4000);
            entity.HasIndex(post => post.Status);
            entity.HasIndex(post => post.ScheduledTime);
        });
    }

    public sealed class FriendEntity
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public sealed class ScheduledPostEntity
    {
        public Guid Id { get; set; }
        public string Platform { get; set; } = "LinkedIn";
        public string PlatformUserId { get; set; } = string.Empty;
        public string AccessToken { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string? MediaUrnsJson { get; set; }
        public string MediaType { get; set; } = "NONE";
        public string? IgPostType { get; set; }
        public string? FbPostType { get; set; }
        public string? FbTarget { get; set; }
        public string? FbTargetId { get; set; }
        public string? CommentsJson { get; set; }
        public DateTimeOffset ScheduledTime { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public string? ThumbnailUrl { get; set; }
        public bool IsPublished { get; set; }
        public string Status { get; set; } = "Pending";
        public string? FailureReason { get; set; }
        public int RetryCount { get; set; }
        public int MaxRetries { get; set; }
        public DateTimeOffset? NextRetryAt { get; set; }
    }
}
