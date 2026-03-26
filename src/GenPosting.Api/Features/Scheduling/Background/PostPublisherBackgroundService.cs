using GenPosting.Api.Features.Instagram.Services;
using GenPosting.Api.Features.Scheduling.Models;
using GenPosting.Api.Features.Scheduling.Services;
using GenPosting.Api.Features.LinkedIn.Services;
using GenPosting.Api.Services;
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
        var facebookService = scope.ServiceProvider.GetRequiredService<GenPosting.Api.Features.Facebook.Services.IFacebookService>();
        var blobService = scope.ServiceProvider.GetRequiredService<IBlobStorageService>();

        var duePosts = await scheduledService.GetDuePostsAsync(stoppingToken);

        foreach (var post in duePosts)
        {
            if (stoppingToken.IsCancellationRequested) break;

            _logger.LogInformation("Publishing scheduled post {PostId} ({Platform}) scheduled for {ScheduledTime}", post.Id, post.Platform, post.ScheduledTime);

            try
            {
                bool success = false;
                string? error = null;
                string? publishedId = null;

                if (post.Platform == SocialPlatform.Instagram)
                {
                    // Instagram Publishing Logic
                    var blobName = post.MediaUrns?.FirstOrDefault();
                    if (string.IsNullOrEmpty(blobName))
                    {
                        success = false;
                        error = "No media found for Instagram post.";
                    }
                    else
                    {
                        // Regenerate a fresh SAS URL — the stored blob name never expires
                        var mediaUrl = await blobService.GetSasUrlAsync(blobName, TimeSpan.FromHours(1));
                        var igType = post.IgPostType ?? InstagramPostType.Post;
                        
                        var result = await instagramService.PublishPostWithUrlAsync(
                            post.AccessToken,
                            post.PlatformUserId,
                            post.Content,
                            igType,
                            mediaUrl,
                            stoppingToken
                        );
                        success = result.Success;
                        error = result.Error;
                        publishedId = result.PublishedId;
                    }
                }
                else if (post.Platform == SocialPlatform.Facebook)
                {
                    // Facebook Publishing Logic
                    var fbType = post.FbPostType ?? FacebookPostType.Text;
                    var fbTarget = post.FbTarget ?? FacebookPostTarget.Profile;

                    // Regenerate a fresh SAS URL if there's a stored blob name
                    var blobName = post.MediaUrns?.FirstOrDefault();
                    var mediaUrl = !string.IsNullOrEmpty(blobName)
                        ? await blobService.GetSasUrlAsync(blobName, TimeSpan.FromHours(1))
                        : string.Empty;

                    var result = await facebookService.PublishPostWithUrlAsync(
                        post.AccessToken,
                        post.Content,
                        fbType,
                        mediaUrl,
                        fbTarget,
                        post.FbTargetId,
                        stoppingToken
                    );
                    success = result.Success;
                    error = result.Error;
                    publishedId = result.PublishedId;
                }
                else
                {
                    // LinkedIn Publishing Logic
                    var (liSuccess, liError, liData) = await linkedInService.CreatePostAsync(
                        post.AccessToken,
                        post.Content,
                        post.MediaUrns,
                        post.MediaType,
                        stoppingToken
                    );
                    success = liSuccess;
                    error = liError;
                    publishedId = liData?.Id; 
                }

                if (!success)
                {
                    _logger.LogError("Failed to publish post {PostId}: {Error}", post.Id, error);
                    await scheduledService.MarkAsFailedAsync(post.Id, error ?? "Unknown error", stoppingToken);
                    continue; 
                }

                // Handle Comments (Unified for all platforms)
                if (!string.IsNullOrEmpty(publishedId) && post.Comments != null && post.Comments.Any())
                {
                    foreach (var comment in post.Comments)
                    {
                        if (!string.IsNullOrWhiteSpace(comment))
                        {
                            if (post.Platform == SocialPlatform.Instagram)
                            {
                                await instagramService.AddCommentAsync(post.AccessToken, publishedId, comment, stoppingToken);
                            }
                            else if (post.Platform == SocialPlatform.LinkedIn)
                            {
                                await linkedInService.AddCommentAsync(post.AccessToken, publishedId, comment, stoppingToken);
                            }
                            // Note: Facebook comments on posts require different API - not implementing here
                            
                            await Task.Delay(500, stoppingToken);
                        }
                    }
                }

                await scheduledService.MarkAsPublishedAsync(post.Id, stoppingToken);
                _logger.LogInformation("Successfully published post {PostId}", post.Id);

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception publishing post {PostId}", post.Id);
                await scheduledService.MarkAsFailedAsync(post.Id, ex.Message, stoppingToken);
            }
        }
    }
}
