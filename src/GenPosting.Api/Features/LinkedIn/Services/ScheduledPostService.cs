using System.Collections.Concurrent;
using GenPosting.Api.Features.LinkedIn.Models;

namespace GenPosting.Api.Features.LinkedIn.Services;

public interface IScheduledPostService
{
    Task SchedulePostAsync(ScheduledPost post);
    Task<List<ScheduledPost>> GetAllScheduledPostsAsync();
    Task<ScheduledPost?> GetScheduledPostByIdAsync(Guid id);
    Task UpdateScheduledPostAsync(ScheduledPost post);
    Task DeleteScheduledPostAsync(Guid id);
    Task<List<ScheduledPost>> GetDuePostsAsync();
    Task MarkAsPublishedAsync(Guid id);
    Task MarkAsFailedAsync(Guid id, string error);
}

public class InMemoryScheduledPostService : IScheduledPostService
{
    // In a real app, use a Database (EF Core / Dapper)
    private static readonly ConcurrentDictionary<Guid, ScheduledPost> _posts = new();

    public Task SchedulePostAsync(ScheduledPost post)
    {
        _posts.TryAdd(post.Id, post);
        return Task.CompletedTask;
    }

    public Task<List<ScheduledPost>> GetAllScheduledPostsAsync()
    {
        var all = _posts.Values.OrderBy(p => p.ScheduledTime).ToList();
        return Task.FromResult(all);
    }

    public Task<ScheduledPost?> GetScheduledPostByIdAsync(Guid id)
    {
        _posts.TryGetValue(id, out var post);
        return Task.FromResult(post);
    }

    public Task UpdateScheduledPostAsync(ScheduledPost post)
    {
        // For in-memory, we just overwrite
        _posts.AddOrUpdate(post.Id, post, (k, v) => post);
        return Task.CompletedTask;
    }

    public Task DeleteScheduledPostAsync(Guid id)
    {
        _posts.TryRemove(id, out _);
        return Task.CompletedTask;
    }

    public Task<List<ScheduledPost>> GetDuePostsAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var due = _posts.Values
            .Where(p => !p.IsPublished && p.Status == "Pending" && p.ScheduledTime <= now)
            .ToList();
        return Task.FromResult(due);
    }

    public Task MarkAsPublishedAsync(Guid id)
    {
        if (_posts.TryGetValue(id, out var post))
        {
            post.IsPublished = true;
            post.Status = "Published";
        }
        return Task.CompletedTask;
    }

    public Task MarkAsFailedAsync(Guid id, string error)
    {
        if (_posts.TryGetValue(id, out var post))
        {
            post.Status = $"Failed: {error}";
        }
        return Task.CompletedTask;
    }
}
