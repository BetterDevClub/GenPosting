using GenPosting.Api.Features.LinkedIn.Services;

namespace GenPosting.Api.Features.LinkedIn.Background;

public class PostPublisherBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PostPublisherBackgroundService> _logger;

    public PostPublisherBackgroundService(IServiceProvider serviceProvider, ILogger<PostPublisherBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Post Publisher Service started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessDuePostsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while processing scheduled posts.");
            }

            // Check every 30 seconds
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }

    private async Task ProcessDuePostsAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var scheduledService = scope.ServiceProvider.GetRequiredService<IScheduledPostService>();
        var linkedInService = scope.ServiceProvider.GetRequiredService<ILinkedInService>();

        var duePosts = await scheduledService.GetDuePostsAsync();

        foreach (var post in duePosts)
        {
            if (stoppingToken.IsCancellationRequested) break;

            _logger.LogInformation($"Publishing scheduled post {post.Id} scheduled for {post.ScheduledTime}");

            try
            {
                // 1. Create Post
                var (success, error, data) = await linkedInService.CreatePostAsync(
                    post.AccessToken,
                    post.Content,
                    post.MediaUrns,
                    post.MediaType
                );

                if (!success)
                {
                    _logger.LogError($"Failed to publish post {post.Id}: {error}");
                    await scheduledService.MarkAsFailedAsync(post.Id, error ?? "Unknown error");
                    continue; // Skip comments if post failed
                }

                // 2. Add Comments
                if (post.Comments != null && post.Comments.Any())
                {
                    foreach (var comment in post.Comments)
                    {
                        if (!string.IsNullOrWhiteSpace(comment))
                        {
                            await linkedInService.AddCommentAsync(post.AccessToken, data!.Id, comment);
                             // Small delay to ensure order and avoid rate limits
                            await Task.Delay(500, stoppingToken);
                        }
                    }
                }

                await scheduledService.MarkAsPublishedAsync(post.Id);
                _logger.LogInformation($"Successfully published post {post.Id}");

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Exception publishing post {post.Id}");
                await scheduledService.MarkAsFailedAsync(post.Id, ex.Message);
            }
        }
    }
}
