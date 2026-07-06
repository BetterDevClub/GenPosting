using System.Text.Json;
using GenPosting.Api.Features.Scheduling.Models;
using GenPosting.Api.Features.Scheduling.Services;
using GenPosting.Shared.Enums;
using Xunit;

namespace GenPosting.Api.Tests;

public class ScheduledPostServiceTests
{
    [Fact]
    public async Task PersistedScheduledPosts_AreAvailableAfterServiceRecreation()
    {
        var tempFile = CreateTempFilePath();

        try
        {
            var firstService = new FileScheduledPostService(tempFile);
            var post = CreatePost();

            await firstService.SchedulePostAsync(post);

            var reloadedService = new FileScheduledPostService(tempFile);
            var persistedPost = await reloadedService.GetScheduledPostByIdAsync(post.Id);

            Assert.NotNull(persistedPost);
            Assert.Equal(post.Id, persistedPost!.Id);
            Assert.Equal(post.Content, persistedPost.Content);
        }
        finally
        {
            DeleteTempFile(tempFile);
        }
    }

    [Fact]
    public async Task PublishedPosts_PersistStatusAcrossServiceRecreation()
    {
        var tempFile = CreateTempFilePath();

        try
        {
            var firstService = new FileScheduledPostService(tempFile);
            var post = CreatePost();

            await firstService.SchedulePostAsync(post);
            await firstService.MarkAsPublishedAsync(post.Id);

            var reloadedService = new FileScheduledPostService(tempFile);
            var persistedPost = await reloadedService.GetScheduledPostByIdAsync(post.Id);

            Assert.NotNull(persistedPost);
            Assert.True(persistedPost!.IsPublished);
            Assert.Equal(ScheduledPostStatus.Published, persistedPost.Status);
        }
        finally
        {
            DeleteTempFile(tempFile);
        }
    }

    [Fact]
    public async Task FailedPosts_PersistRetryStateAndResetForRetry()
    {
        var tempFile = CreateTempFilePath();

        try
        {
            var firstService = new FileScheduledPostService(tempFile);
            var post = CreatePost();

            await firstService.SchedulePostAsync(post);
            await firstService.MarkAsFailedAsync(post.Id, "Temporary failure");

            var firstState = await firstService.GetScheduledPostByIdAsync(post.Id);
            Assert.NotNull(firstState);
            Assert.Equal(1, firstState!.RetryCount);
            Assert.Equal(ScheduledPostStatus.Pending, firstState.Status);
            Assert.True(firstState.NextRetryAt.HasValue);
            Assert.True(firstState.NextRetryAt > DateTimeOffset.UtcNow);

            var reloadedService = new FileScheduledPostService(tempFile);
            var persistedState = await reloadedService.GetScheduledPostByIdAsync(post.Id);

            Assert.NotNull(persistedState);
            Assert.Equal(1, persistedState!.RetryCount);
            Assert.True(persistedState.NextRetryAt.HasValue);

            await reloadedService.ResetForRetryAsync(post.Id);
            var resetState = await reloadedService.GetScheduledPostByIdAsync(post.Id);

            Assert.NotNull(resetState);
            Assert.Equal(ScheduledPostStatus.Pending, resetState!.Status);
            Assert.Equal(0, resetState.RetryCount);
            Assert.Null(resetState.NextRetryAt);
            Assert.Null(resetState.FailureReason);
        }
        finally
        {
            DeleteTempFile(tempFile);
        }
    }

    [Fact]
    public async Task VersionedStoragePayload_IsLoadedAfterServiceRecreation()
    {
        var tempFile = CreateTempFilePath();

        try
        {
            var post = CreatePost();
            var payload = JsonSerializer.Serialize(new
            {
                Version = 1,
                Posts = new[] { post }
            });

            await File.WriteAllTextAsync(tempFile, payload);

            var service = new FileScheduledPostService(tempFile);
            var persistedPost = await service.GetScheduledPostByIdAsync(post.Id);

            Assert.NotNull(persistedPost);
            Assert.Equal(post.Content, persistedPost!.Content);
        }
        finally
        {
            DeleteTempFile(tempFile);
        }
    }

    [Fact]
    public async Task DuePosts_AreReturnedAfterServiceRecreation()
    {
        var tempFile = CreateTempFilePath();

        try
        {
            var firstService = new FileScheduledPostService(tempFile);
            var duePost = CreatePost("past due", DateTimeOffset.UtcNow.AddMinutes(-5));
            var futurePost = CreatePost("future", DateTimeOffset.UtcNow.AddMinutes(5));

            await firstService.SchedulePostAsync(duePost);
            await firstService.SchedulePostAsync(futurePost);

            var reloadedService = new FileScheduledPostService(tempFile);
            var duePosts = await reloadedService.GetDuePostsAsync();

            Assert.Contains(duePosts, post => post.Id == duePost.Id);
            Assert.DoesNotContain(duePosts, post => post.Id == futurePost.Id);
        }
        finally
        {
            DeleteTempFile(tempFile);
        }
    }

    [Fact]
    public async Task SqliteScheduledPosts_PersistAcrossServiceRecreation()
    {
        var tempFile = CreateTempFilePath("scheduled-posts");

        try
        {
            var firstService = new FileScheduledPostService(tempFile);
            var post = CreatePost("sqlite-backed post");

            await firstService.SchedulePostAsync(post);

            var reloadedService = new FileScheduledPostService(tempFile);
            var persistedPost = await reloadedService.GetScheduledPostByIdAsync(post.Id);

            Assert.NotNull(persistedPost);
            Assert.Equal(post.Content, persistedPost!.Content);
        }
        finally
        {
            DeleteTempFile(tempFile);
        }
    }

    [Fact]
    public async Task ConcurrentSchedules_PersistConsistentState()
    {
        var tempFile = CreateTempFilePath();

        try
        {
            var service = new FileScheduledPostService(tempFile);
            var posts = Enumerable.Range(0, 20)
                .Select(index => CreatePost($"post-{index}"))
                .ToList();

            await Task.WhenAll(posts.Select(post => service.SchedulePostAsync(post)));

            var persisted = await service.GetAllScheduledPostsAsync();
            Assert.Equal(posts.Count, persisted.Count);
            Assert.Equal(posts.Count, persisted.Select(post => post.Id).Distinct().Count());

            var reloadedService = new FileScheduledPostService(tempFile);
            var reloaded = await reloadedService.GetAllScheduledPostsAsync();
            Assert.Equal(posts.Count, reloaded.Count);
        }
        finally
        {
            DeleteTempFile(tempFile);
        }
    }

    private static string CreateTempFilePath(string? prefix = null)
    {
        return Path.Combine(Path.GetTempPath(), $"genposting-{prefix ?? "scheduled-posts"}-{Guid.NewGuid():N}.db");
    }

    private static void DeleteTempFile(string filePath)
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }

    private static ScheduledPost CreatePost(string content = "Hello world", DateTimeOffset? scheduledTime = null)
    {
        return new ScheduledPost
        {
            Id = Guid.NewGuid(),
            Platform = SocialPlatform.LinkedIn,
            Content = content,
            ScheduledTime = scheduledTime ?? DateTimeOffset.UtcNow.AddHours(1),
            Status = ScheduledPostStatus.Pending
        };
    }
}
