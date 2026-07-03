using System.Text.Json;
using GenPosting.Api.Features.Scheduling.Models;
using GenPosting.Shared.Enums;

namespace GenPosting.Api.Features.Scheduling.Services;

public interface IScheduledPostService
{
    Task SchedulePostAsync(ScheduledPost post, CancellationToken cancellationToken = default);
    Task<List<ScheduledPost>> GetAllScheduledPostsAsync(CancellationToken cancellationToken = default);
    Task<ScheduledPost?> GetScheduledPostByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task UpdateScheduledPostAsync(ScheduledPost post, CancellationToken cancellationToken = default);
    Task DeleteScheduledPostAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<ScheduledPost>> GetDuePostsAsync(CancellationToken cancellationToken = default);
    Task MarkAsPublishedAsync(Guid id, CancellationToken cancellationToken = default);
    Task MarkAsFailedAsync(Guid id, string error, CancellationToken cancellationToken = default);
    Task ResetForRetryAsync(Guid id, CancellationToken cancellationToken = default);
}

public class FileScheduledPostService : IScheduledPostService
{
    private const int CurrentStorageVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly object _syncRoot = new();
    private readonly string _storagePath;
    private readonly Dictionary<Guid, ScheduledPost> _posts;

    public FileScheduledPostService(string? storagePath = null)
    {
        _storagePath = string.IsNullOrWhiteSpace(storagePath)
            ? Path.Combine(Directory.GetCurrentDirectory(), "scheduled-posts.json")
            : storagePath;

        _posts = LoadPosts();
    }

    public Task SchedulePostAsync(ScheduledPost post, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(post);

        ApplyMutation(posts => posts[post.Id] = post);
        return Task.CompletedTask;
    }

    public Task<List<ScheduledPost>> GetAllScheduledPostsAsync(CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            return Task.FromResult(_posts.Values.OrderBy(p => p.ScheduledTime).ToList());
        }
    }

    public Task<ScheduledPost?> GetScheduledPostByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            _posts.TryGetValue(id, out var post);
            return Task.FromResult(post);
        }
    }

    public Task UpdateScheduledPostAsync(ScheduledPost post, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(post);

        ApplyMutation(posts => posts[post.Id] = post);
        return Task.CompletedTask;
    }

    public Task DeleteScheduledPostAsync(Guid id, CancellationToken cancellationToken = default)
    {
        ApplyMutation(posts => posts.Remove(id));
        return Task.CompletedTask;
    }

    public Task<List<ScheduledPost>> GetDuePostsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        lock (_syncRoot)
        {
            var due = _posts.Values
                .Where(p => !p.IsPublished
                    && p.Status == ScheduledPostStatus.Pending
                    && p.ScheduledTime <= now
                    && (p.NextRetryAt == null || p.NextRetryAt <= now))
                .ToList();

            return Task.FromResult(due);
        }
    }

    public Task MarkAsPublishedAsync(Guid id, CancellationToken cancellationToken = default)
    {
        ApplyMutation(posts =>
        {
            if (posts.TryGetValue(id, out var post))
            {
                post.IsPublished = true;
                post.Status = ScheduledPostStatus.Published;
            }
        });

        return Task.CompletedTask;
    }

    public Task MarkAsFailedAsync(Guid id, string error, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(error);

        ApplyMutation(posts =>
        {
            if (posts.TryGetValue(id, out var post))
            {
                post.RetryCount++;
                post.FailureReason = error;

                if (post.RetryCount < post.MaxRetries)
                {
                    var delayMinutes = Math.Pow(4, post.RetryCount - 1);
                    post.NextRetryAt = DateTimeOffset.UtcNow.AddMinutes(delayMinutes);
                    post.Status = ScheduledPostStatus.Pending;
                }
                else
                {
                    post.Status = ScheduledPostStatus.Failed;
                }
            }
        });

        return Task.CompletedTask;
    }

    public Task ResetForRetryAsync(Guid id, CancellationToken cancellationToken = default)
    {
        ApplyMutation(posts =>
        {
            if (posts.TryGetValue(id, out var post))
            {
                post.Status = ScheduledPostStatus.Pending;
                post.RetryCount = 0;
                post.NextRetryAt = null;
                post.FailureReason = null;
            }
        });

        return Task.CompletedTask;
    }

    private void ApplyMutation(Action<Dictionary<Guid, ScheduledPost>> mutation)
    {
        lock (_syncRoot)
        {
            var updatedPosts = new Dictionary<Guid, ScheduledPost>(_posts);
            mutation(updatedPosts);
            SavePosts(updatedPosts);

            _posts.Clear();
            foreach (var item in updatedPosts)
            {
                _posts[item.Key] = item.Value;
            }
        }
    }

    private Dictionary<Guid, ScheduledPost> LoadPosts()
    {
        if (!File.Exists(_storagePath))
        {
            return new Dictionary<Guid, ScheduledPost>();
        }

        try
        {
            var json = File.ReadAllText(_storagePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new Dictionary<Guid, ScheduledPost>();
            }

            var envelope = JsonSerializer.Deserialize<ScheduledPostFilePayload>(json, SerializerOptions);
            if (envelope?.Posts is { Count: > 0 } posts)
            {
                return posts.ToDictionary(post => post.Id);
            }

            var legacyPosts = JsonSerializer.Deserialize<List<ScheduledPost>>(json, SerializerOptions);
            return legacyPosts?.ToDictionary(post => post.Id) ?? new Dictionary<Guid, ScheduledPost>();
        }
        catch (JsonException)
        {
            return new Dictionary<Guid, ScheduledPost>();
        }
    }

    private void SavePosts(IReadOnlyDictionary<Guid, ScheduledPost> posts)
    {
        var directory = Path.GetDirectoryName(_storagePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var payload = new ScheduledPostFilePayload
        {
            Version = CurrentStorageVersion,
            Posts = posts.Values.OrderBy(post => post.ScheduledTime).ToList()
        };

        var json = JsonSerializer.Serialize(payload, SerializerOptions);
        WriteToStorageAtomically(json);
    }

    private void WriteToStorageAtomically(string json)
    {
        var directory = Path.GetDirectoryName(_storagePath) ?? Directory.GetCurrentDirectory();
        var tempFilePath = Path.Combine(directory, $".{Path.GetFileName(_storagePath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllText(tempFilePath, json);

            if (File.Exists(_storagePath))
            {
                File.Move(tempFilePath, _storagePath, overwrite: true);
            }
            else
            {
                File.Move(tempFilePath, _storagePath);
            }
        }
        finally
        {
            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
            }
        }
    }

    private sealed class ScheduledPostFilePayload
    {
        public int Version { get; set; }
        public List<ScheduledPost>? Posts { get; set; }
    }
}
