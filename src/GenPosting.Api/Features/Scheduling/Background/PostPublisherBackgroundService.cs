using GenPosting.Api.Features.Instagram.Services;
using GenPosting.Api.Features.Scheduling.Models;
using GenPosting.Api.Features.Scheduling.Services;
using GenPosting.Api.Features.LinkedIn.Services; // Keep for ILinkedInService
using GenPosting.Shared.DTOs;
using GenPosting.Shared.Enums;

namespace GenPosting.Api.Features.Scheduling.Background;

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
        var instagramService = scope.ServiceProvider.GetRequiredService<IInstagramService>();

        var duePosts = await scheduledService.GetDuePostsAsync();

        foreach (var post in duePosts)
        {
            if (stoppingToken.IsCancellationRequested) break;

            _logger.LogInformation($"Publishing scheduled post {post.Id} ({post.Platform}) scheduled for {post.ScheduledTime}");

            try
            {
                bool success = false;
                string? error = null;
                string? publishedId = null;

                if (post.Platform == SocialPlatform.Instagram)
                {
                    // Instagram Publishing Logic
                    var mediaUrl = post.MediaUrns?.FirstOrDefault();
                    if (string.IsNullOrEmpty(mediaUrl))
                    {
                        success = false;
                        error = "No media URL found for Instagram post.";
                    }
                    else
                    {
                        // Default to Post if not specified, though it should be set
                        var igType = post.IgPostType ?? InstagramPostType.Post;
                        
                        var result = await instagramService.PublishPostWithUrlAsync(
                            post.AccessToken,
                            post.PlatformUserId,
                            post.Content,
                            igType,
                            mediaUrl
                        );
                        success = result.Success;
                        error = result.Error;
                    }
                }
                else
                {
                    // LinkedIn Publishing Logic
                    var (liSuccess, liError, liData) = await linkedInService.CreatePostAsync(
                        post.AccessToken,
                        post.Content,
                        post.MediaUrns,
                        post.MediaType
                    );
                    success = liSuccess;
                    error = liError;
                    publishedId = liData?.Id; // Only LinkedIn returns ID immediately in this flow currently
                }

                if (!success)
                {
                    _logger.LogError($"Failed to publish post {post.Id}: {error}");
                    await scheduledService.MarkAsFailedAsync(post.Id, error ?? "Unknown error");
                    continue; 
                }

                // Handle Comments (Currently LinkedIn only)
                if (post.Platform == SocialPlatform.LinkedIn && !string.IsNullOrEmpty(publishedId) && post.Comments != null && post.Comments.Any())
                {
                    foreach (var comment in post.Comments)
                    {
                        if (!string.IsNullOrWhiteSpace(comment))
                        {
                            await linkedInService.AddCommentAsync(post.AccessToken, publishedId, comment);
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
