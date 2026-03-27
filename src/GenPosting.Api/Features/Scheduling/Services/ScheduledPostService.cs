using System.Collections.Concurrent;
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

public class InMemoryScheduledPostService : IScheduledPostService
{
    // In a real app, use a Database (EF Core / Dapper)
    private readonly ConcurrentDictionary<Guid, ScheduledPost> _posts = new();

    public Task SchedulePostAsync(ScheduledPost post, CancellationToken cancellationToken = default)
    {
        _posts.TryAdd(post.Id, post);
        return Task.CompletedTask;
    }

    public Task<List<ScheduledPost>> GetAllScheduledPostsAsync(CancellationToken cancellationToken = default)
    {
        var all = _posts.Values.OrderBy(p => p.ScheduledTime).ToList();
        return Task.FromResult(all);
    }

    public Task<ScheduledPost?> GetScheduledPostByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _posts.TryGetValue(id, out var post);
        return Task.FromResult(post);
    }

    public Task UpdateScheduledPostAsync(ScheduledPost post, CancellationToken cancellationToken = default)
    {
        // For in-memory, we just overwrite
        _posts.AddOrUpdate(post.Id, post, (k, v) => post);
        return Task.CompletedTask;
    }

    public Task DeleteScheduledPostAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _posts.TryRemove(id, out _);
        return Task.CompletedTask;
    }

    public Task<List<ScheduledPost>> GetDuePostsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var due = _posts.Values
            .Where(p => !p.IsPublished
                && p.Status == ScheduledPostStatus.Pending
                && p.ScheduledTime <= now
                && (p.NextRetryAt == null || p.NextRetryAt <= now))
            .ToList();
        return Task.FromResult(due);
    }

    public Task MarkAsPublishedAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (_posts.TryGetValue(id, out var post))
        {
            post.IsPublished = true;
            post.Status = ScheduledPostStatus.Published;
        }
        return Task.CompletedTask;
    }

    public Task MarkAsFailedAsync(Guid id, string error, CancellationToken cancellationToken = default)
    {
        if (_posts.TryGetValue(id, out var post))
        {
            post.RetryCount++;
            post.FailureReason = error;

            if (post.RetryCount < post.MaxRetries)
            {
                // Exponential backoff: 1min, 4min, 16min, ...
                var delayMinutes = Math.Pow(4, post.RetryCount - 1);
                post.NextRetryAt = DateTimeOffset.UtcNow.AddMinutes(delayMinutes);
                post.Status = ScheduledPostStatus.Pending;
            }
            else
            {
                post.Status = ScheduledPostStatus.Failed;
            }
        }
        return Task.CompletedTask;
    }

    public Task ResetForRetryAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (_posts.TryGetValue(id, out var post))
        {
            post.Status = ScheduledPostStatus.Pending;
            post.RetryCount = 0;
            post.NextRetryAt = null;
            post.FailureReason = null;
        }
        return Task.CompletedTask;
    }
}
