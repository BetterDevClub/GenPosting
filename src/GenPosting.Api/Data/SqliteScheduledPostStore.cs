using System.Text.Json;
using GenPosting.Api.Data;
using GenPosting.Api.Features.Scheduling.Models;
using GenPosting.Shared.DTOs;
using GenPosting.Shared.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GenPosting.Api.Features.Scheduling.Services;

public sealed class SqliteScheduledPostStore
{
    private readonly string _databasePath;

    public SqliteScheduledPostStore(string? databasePath = null)
    {
        _databasePath = string.IsNullOrWhiteSpace(databasePath)
            ? Path.Combine(Directory.GetCurrentDirectory(), "genposting.db")
            : databasePath;
    }

    public async Task<IReadOnlyDictionary<Guid, ScheduledPost>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (File.Exists(_databasePath))
        {
            var fileContents = await File.ReadAllTextAsync(_databasePath, cancellationToken);
            if (LooksLikeLegacyJson(fileContents))
            {
                return await LoadLegacyJsonAsync(fileContents, cancellationToken);
            }
        }

        try
        {
            await using var context = CreateContext();
            var posts = await context.ScheduledPosts.AsNoTracking().ToListAsync(cancellationToken);
            return posts.ToDictionary(post => post.Id, MapToDomain);
        }
        catch (SqliteException)
        {
            if (File.Exists(_databasePath))
            {
                var fileContents = await File.ReadAllTextAsync(_databasePath, cancellationToken);
                if (LooksLikeLegacyJson(fileContents))
                {
                    return await LoadLegacyJsonAsync(fileContents, cancellationToken);
                }
            }

            throw;
        }
    }

    public async Task SaveAsync(IReadOnlyDictionary<Guid, ScheduledPost> posts, CancellationToken cancellationToken = default)
    {
        await using var context = CreateContext();
        var existing = await context.ScheduledPosts.ToListAsync(cancellationToken);
        context.ScheduledPosts.RemoveRange(existing);

        foreach (var post in posts.Values.OrderBy(post => post.ScheduledTime))
        {
            context.ScheduledPosts.Add(MapToEntity(post));
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private GenPostingDbContext CreateContext()
    {
        var directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var optionsBuilder = new DbContextOptionsBuilder<GenPostingDbContext>();
        optionsBuilder.UseSqlite($"Data Source={_databasePath}");
        var context = new GenPostingDbContext(optionsBuilder.Options);
        context.Database.EnsureCreated();
        return context;
    }

    private static bool LooksLikeLegacyJson(string text)
    {
        return !string.IsNullOrWhiteSpace(text)
            && (text.TrimStart().StartsWith("{", StringComparison.Ordinal) || text.TrimStart().StartsWith("[", StringComparison.Ordinal));
    }

    private static async Task<IReadOnlyDictionary<Guid, ScheduledPost>> LoadLegacyJsonAsync(string fileContents, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<ScheduledPostFilePayload>(fileContents, SerializerOptions);
        var posts = payload?.Posts?.ToDictionary(post => post.Id) ?? new Dictionary<Guid, ScheduledPost>();
        return posts;
    }

    private static ScheduledPost MapToDomain(GenPostingDbContext.ScheduledPostEntity entity)
    {
        return new ScheduledPost
        {
            Id = entity.Id,
            Platform = Enum.Parse<SocialPlatform>(entity.Platform),
            PlatformUserId = entity.PlatformUserId,
            AccessToken = entity.AccessToken,
            Content = entity.Content,
            MediaUrns = string.IsNullOrWhiteSpace(entity.MediaUrnsJson) ? null : JsonSerializer.Deserialize<List<string>>(entity.MediaUrnsJson),
            MediaType = entity.MediaType,
            IgPostType = string.IsNullOrWhiteSpace(entity.IgPostType) ? null : Enum.Parse<InstagramPostType>(entity.IgPostType),
            FbPostType = string.IsNullOrWhiteSpace(entity.FbPostType) ? null : Enum.Parse<FacebookPostType>(entity.FbPostType),
            FbTarget = string.IsNullOrWhiteSpace(entity.FbTarget) ? null : Enum.Parse<FacebookPostTarget>(entity.FbTarget),
            FbTargetId = entity.FbTargetId,
            Comments = string.IsNullOrWhiteSpace(entity.CommentsJson) ? null : JsonSerializer.Deserialize<List<string>>(entity.CommentsJson),
            ScheduledTime = entity.ScheduledTime,
            CreatedAt = entity.CreatedAt,
            ThumbnailUrl = entity.ThumbnailUrl,
            IsPublished = entity.IsPublished,
            Status = Enum.Parse<ScheduledPostStatus>(entity.Status),
            FailureReason = entity.FailureReason,
            RetryCount = entity.RetryCount,
            MaxRetries = entity.MaxRetries,
            NextRetryAt = entity.NextRetryAt
        };
    }

    private static GenPostingDbContext.ScheduledPostEntity MapToEntity(ScheduledPost post)
    {
        return new GenPostingDbContext.ScheduledPostEntity
        {
            Id = post.Id,
            Platform = post.Platform.ToString(),
            PlatformUserId = post.PlatformUserId,
            AccessToken = post.AccessToken,
            Content = post.Content,
            MediaUrnsJson = post.MediaUrns is null ? null : JsonSerializer.Serialize(post.MediaUrns),
            MediaType = post.MediaType,
            IgPostType = post.IgPostType?.ToString(),
            FbPostType = post.FbPostType?.ToString(),
            FbTarget = post.FbTarget?.ToString(),
            FbTargetId = post.FbTargetId,
            CommentsJson = post.Comments is null ? null : JsonSerializer.Serialize(post.Comments),
            ScheduledTime = post.ScheduledTime,
            CreatedAt = post.CreatedAt,
            ThumbnailUrl = post.ThumbnailUrl,
            IsPublished = post.IsPublished,
            Status = post.Status.ToString(),
            FailureReason = post.FailureReason,
            RetryCount = post.RetryCount,
            MaxRetries = post.MaxRetries,
            NextRetryAt = post.NextRetryAt
        };
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed class ScheduledPostFilePayload
    {
        public List<ScheduledPost>? Posts { get; set; }
    }
}
