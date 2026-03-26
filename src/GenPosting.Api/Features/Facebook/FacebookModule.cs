using Carter;
using GenPosting.Api.Features.Facebook.Services;
using GenPosting.Api.Features.Scheduling.Models;
using GenPosting.Api.Features.Scheduling.Services;
using GenPosting.Shared.DTOs;
using GenPosting.Shared.Enums;
using Microsoft.AspNetCore.Mvc;

namespace GenPosting.Api.Features.Facebook;

public class FacebookModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/facebook")
            .WithTags("Facebook");

        group.MapGet("/auth-url", (string redirectUri, IFacebookService service) =>
        {
            var url = service.GetAuthorizationUrl(redirectUri);
            return Results.Ok(new FacebookAuthUrlResponse(url));
        });

        group.MapPost("/exchange-token", async ([FromBody] FacebookExchangeTokenRequest request, IFacebookService service) =>
        {
            var token = await service.ExchangeTokenAsync(request.Code, request.RedirectUri);
            if (token == null) return Results.BadRequest("Failed to exchange token.");
            return Results.Ok(token);
        });

        group.MapGet("/profile", async ([FromQuery] string userId, [FromHeader(Name = "X-Facebook-Token")] string token, IFacebookService service) =>
        {
            if (string.IsNullOrEmpty(token)) return Results.Unauthorized();
            var profile = await service.GetProfileAsync(token, userId);
            return profile != null ? Results.Ok(profile) : Results.NotFound();
        });

        group.MapGet("/pages", async ([FromQuery] string userId, [FromHeader(Name = "X-Facebook-Token")] string token, IFacebookService service) =>
        {
            if (string.IsNullOrEmpty(token)) return Results.Unauthorized();
            var pages = await service.GetUserPagesAsync(token, userId);
            return Results.Ok(new FacebookPageListResponse(pages));
        });

        group.MapPost("/post", async (HttpRequest request, IFacebookService service, IScheduledPostService scheduledService) =>
        {
            if (!request.Headers.TryGetValue("X-Facebook-Token", out var token)) return Results.Unauthorized();
            if (!request.Headers.TryGetValue("X-Facebook-UserId", out var userId)) return Results.Unauthorized();

            if (!request.HasFormContentType) return Results.BadRequest("Expected form content type");

            var form = await request.ReadFormAsync();
            var content = form["content"];
            var postTypeStr = form["postType"];
            var targetStr = form["target"]; // "Profile" or "Page"
            var targetId = form["targetId"]; // Page ID if posting to page
            var scheduledForStr = form["scheduledFor"];
            var file = form.Files["file"];

            if (!Enum.TryParse<FacebookPostType>(postTypeStr, out var postType))
                postType = FacebookPostType.Text;

            if (!Enum.TryParse<FacebookPostTarget>(targetStr, out var target))
                target = FacebookPostTarget.Profile;

            Stream? stream = null;
            if (file != null) stream = file.OpenReadStream();

            DateTimeOffset? scheduledFor = null;
            if (!string.IsNullOrEmpty(scheduledForStr) && DateTimeOffset.TryParse(scheduledForStr, out var parsedDate))
            {
                scheduledFor = parsedDate;
            }

            if (scheduledFor.HasValue && scheduledFor.Value <= DateTimeOffset.UtcNow)
                return Results.BadRequest("Scheduled time must be in the future.");

            // Scheduling Logic
            if (scheduledFor.HasValue)
            {
                string? mediaUrl = null;
                if (stream != null && file != null)
                {
                    try
                    {
                        // UploadMediaAsync returns the blob name; background service generates fresh SAS at publish time
                        mediaUrl = await service.UploadMediaAsync(stream, file.FileName);
                    }
                    catch (Exception ex)
                    {
                        return Results.BadRequest($"Failed to upload media for scheduling: {ex.Message}");
                    }
                }

                var scheduledPost = new ScheduledPost
                {
                    Platform = SocialPlatform.Facebook,
                    PlatformUserId = userId,
                    AccessToken = token,
                    Content = content,
                    FbPostType = postType,
                    FbTarget = target,
                    FbTargetId = targetId,
                    MediaUrns = mediaUrl != null ? new List<string> { mediaUrl } : new List<string>(), // Blob name
                    ScheduledTime = scheduledFor.Value,
                    ThumbnailUrl = mediaUrl,
                    Status = "Pending"
                };

                await scheduledService.SchedulePostAsync(scheduledPost);
                return Results.Ok(new { Message = "Post scheduled successfully", ScheduledId = scheduledPost.Id });
            }

            // Immediate Publishing Logic
            var dto = new CreateFacebookPostRequest(
                content, 
                postType, 
                target, 
                targetId, 
                new List<string>()
            );

            var (success, error, publishedId) = await service.PublishPostAsync(token, dto, stream, file?.FileName);

            return success ? Results.Ok(new { PostId = publishedId }) : Results.BadRequest(error);
        });

        group.MapGet("/posts", async ([FromQuery] string targetId, [FromQuery] bool isPage, [FromHeader(Name = "X-Facebook-Token")] string token, IFacebookService service) =>
        {
            if (string.IsNullOrEmpty(token)) return Results.Unauthorized();
            var posts = await service.GetPostsAsync(token, targetId, isPage);
            return Results.Ok(new FacebookPostListResponse(posts, null));
        });

        group.MapGet("/posts/{postId}", async (string postId, [FromHeader(Name = "X-Facebook-Token")] string token, IFacebookService service) =>
        {
            if (string.IsNullOrEmpty(token)) return Results.Unauthorized();
            var post = await service.GetPostAsync(token, postId);
            return post != null ? Results.Ok(post) : Results.NotFound();
        });

        group.MapGet("/posts/{postId}/insights", async (string postId, [FromHeader(Name = "X-Facebook-Token")] string token, IFacebookService service) =>
        {
            if (string.IsNullOrEmpty(token)) return Results.Unauthorized();
            var insights = await service.GetPostInsightsAsync(token, postId);
            return insights != null ? Results.Ok(insights) : Results.NotFound();
        });

        group.MapGet("/insights/page", async ([FromQuery] string pageId, [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromHeader(Name = "X-Facebook-Token")] string token, IFacebookService service) =>
        {
            if (string.IsNullOrEmpty(token)) return Results.Unauthorized();
            var result = await service.GetPageInsightsAsync(token, pageId, from, to);
            return result != null ? Results.Ok(result) : Results.NotFound("Could not fetch page insights.");
        });

        group.MapGet("/comments/recent", async ([FromQuery] string targetId, [FromQuery] bool isPage, [FromHeader(Name = "X-Facebook-Token")] string token, IFacebookService service) =>
        {
            if (string.IsNullOrEmpty(token)) return Results.Unauthorized();
            var comments = await service.GetRecentCommentsAsync(token, targetId, isPage);
            return Results.Ok(new FacebookCommentListResponse(comments));
        });

        group.MapPost("/comments/{commentId}/reply", async (string commentId, [FromBody] ReplyToFacebookCommentRequest req, [FromHeader(Name = "X-Facebook-Token")] string token, IFacebookService service) =>
        {
            if (string.IsNullOrEmpty(token)) return Results.Unauthorized();
            var success = await service.ReplyToCommentAsync(token, commentId, req.Message);
            return success ? Results.Ok() : Results.BadRequest("Failed to reply");
        });

        group.MapDelete("/comments/{commentId}", async (string commentId, [FromHeader(Name = "X-Facebook-Token")] string token, IFacebookService service) =>
        {
            if (string.IsNullOrEmpty(token)) return Results.Unauthorized();
            var success = await service.DeleteCommentAsync(token, commentId);
            return success ? Results.Ok() : Results.BadRequest("Failed to delete comment");
        });

        group.MapPost("/albums", async ([FromBody] CreateFacebookAlbumRequest req, [FromHeader(Name = "X-Facebook-Token")] string token, IFacebookService service) =>
        {
            if (string.IsNullOrEmpty(token)) return Results.Unauthorized();
            
            var targetId = req.TargetPageId ?? "me";
            var albumId = await service.CreateAlbumAsync(token, targetId, req.Name, req.Description);
            
            if (string.IsNullOrEmpty(albumId))
                return Results.BadRequest("Failed to create album");

            // Add photos to album
            foreach (var photoUrl in req.PhotoUrls)
            {
                await service.AddPhotoToAlbumAsync(token, albumId, photoUrl, null);
            }

            return Results.Ok(new { AlbumId = albumId });
        });
    }
}
