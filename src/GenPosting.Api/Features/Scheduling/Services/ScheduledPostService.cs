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
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SqliteScheduledPostStore _store;
    private readonly Dictionary<Guid, ScheduledPost> _posts = new();
    private bool _isLoaded;

    public FileScheduledPostService(string? storagePath = null)
    {
        _store = new SqliteScheduledPostStore(storagePath);
    }

    public async Task SchedulePostAsync(ScheduledPost post, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(post);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);
            _posts[post.Id] = post;
            await PersistChangesAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<List<ScheduledPost>> GetAllScheduledPostsAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);
            return _posts.Values.OrderBy(post => post.ScheduledTime).ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ScheduledPost?> GetScheduledPostByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);
            _posts.TryGetValue(id, out var post);
            return post;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateScheduledPostAsync(ScheduledPost post, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(post);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);
            _posts[post.Id] = post;
            await PersistChangesAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteScheduledPostAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);
            _posts.Remove(id);
            await PersistChangesAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<List<ScheduledPost>> GetDuePostsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);
            return _posts.Values
                .Where(post => !post.IsPublished
                    && post.Status == ScheduledPostStatus.Pending
                    && post.ScheduledTime <= now
                    && (post.NextRetryAt == null || post.NextRetryAt <= now))
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task MarkAsPublishedAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);
            if (_posts.TryGetValue(id, out var post))
            {
                post.IsPublished = true;
                post.Status = ScheduledPostStatus.Published;
                await PersistChangesAsync(cancellationToken);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task MarkAsFailedAsync(Guid id, string error, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(error);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);
            if (_posts.TryGetValue(id, out var post))
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

                await PersistChangesAsync(cancellationToken);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ResetForRetryAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);
            if (_posts.TryGetValue(id, out var post))
            {
                post.Status = ScheduledPostStatus.Pending;
                post.RetryCount = 0;
                post.NextRetryAt = null;
                post.FailureReason = null;
                await PersistChangesAsync(cancellationToken);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_isLoaded)
        {
            return;
        }

        var persistedPosts = await _store.LoadAsync(cancellationToken);
        _posts.Clear();
        foreach (var post in persistedPosts)
        {
            _posts[post.Key] = post.Value;
        }

        _isLoaded = true;
    }

    private async Task PersistChangesAsync(CancellationToken cancellationToken)
    {
        await _store.SaveAsync(_posts, cancellationToken);
    }
}
