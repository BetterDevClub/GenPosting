using Carter;
using GenPosting.Api.Features.LinkedIn.Services;
using GenPosting.Api.Features.Scheduling.Models;
using GenPosting.Api.Features.Scheduling.Services;
using GenPosting.Shared.DTOs;
using GenPosting.Shared.Enums;
using Microsoft.AspNetCore.Mvc;

namespace GenPosting.Api.Features.LinkedIn;

public class LinkedInModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/linkedin").WithTags("LinkedIn");

        group.MapGet("/auth-url", (string redirectUri, ILinkedInService service) =>
        {
            var url = service.GetAuthorizationUrl(redirectUri);
            return Results.Ok(new LinkedInAuthUrlResponse(url));
        });

        group.MapPost("/exchange", async ([FromBody] LinkedInExchangeTokenRequest request, ILinkedInService service) =>
        {
            var token = await service.ExchangeTokenAsync(request.Code, request.RedirectUri);
            if (token == null) return Results.BadRequest("Failed to exchange token.");
            
            return Results.Ok(token);
        });

        group.MapPost("/upload", async (HttpRequest request, [FromHeader(Name = "X-LinkedIn-Token")] string? accessToken, ILinkedInService service) =>
        {
            if (string.IsNullOrEmpty(accessToken)) return Results.Unauthorized();
            
            if (!request.HasFormContentType)
                return Results.BadRequest("Expected multipart/form-data");

            var form = await request.ReadFormAsync();
            var file = form.Files.FirstOrDefault();
            
            if (file == null || file.Length == 0)
                return Results.BadRequest("No file uploaded");
            
            // Basic detection. LinkedIn supports images/video.
            // mime types: image/jpeg, image/png, image/gif, video/mp4, etc.
            var isVideo = file.ContentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase);
            
            using var stream = file.OpenReadStream();
            var result = await service.UploadMediaAsync(accessToken, stream, file.ContentType, isVideo);
            
            if (result == null) return Results.BadRequest("Failed to upload media to LinkedIn");
            
            return Results.Ok(result);
        }).DisableAntiforgery();

        group.MapGet("/profile", async ([FromHeader(Name = "X-LinkedIn-Token")] string? accessToken, ILinkedInService service) =>
        {
            if (string.IsNullOrEmpty(accessToken)) return Results.Unauthorized();
            var profile = await service.GetProfileAsync(accessToken);
            return profile != null ? Results.Ok(profile) : Results.NotFound();
        });

        group.MapGet("/posts", async ([FromHeader(Name = "X-LinkedIn-Token")] string? accessToken, ILinkedInService service) =>
        {
            if (string.IsNullOrEmpty(accessToken)) return Results.Unauthorized();
            
            var posts = await service.GetPostsAsync(accessToken);
            return Results.Ok(posts);
        });

        group.MapPost("/post", async ([FromHeader(Name = "X-LinkedIn-Token")] string? accessToken, [FromBody] CreateLinkedInPostRequest request, ILinkedInService service, IScheduledPostService scheduler) =>
        {
            if (string.IsNullOrEmpty(accessToken)) return Results.Unauthorized();

            // Handling Scheduling
            if (request.ScheduledAt.HasValue)
            {
                if (request.ScheduledAt.Value <= DateTimeOffset.UtcNow)
                {
                    return Results.BadRequest("Scheduled time must be in the future.");
                }

                var scheduledPost = new ScheduledPost
                {
                    Platform = SocialPlatform.LinkedIn, // Explicitly set platform
                    AccessToken = accessToken,
                    Content = request.Content,
                    MediaUrns = request.MediaUrns,
                    MediaType = request.MediaType,
                    Comments = request.Comments,
                    ScheduledTime = request.ScheduledAt.Value,
                    ThumbnailUrl = request.ThumbnailUrl
                };

                await scheduler.SchedulePostAsync(scheduledPost);
                return Results.Ok(new LinkedInPostCreatedResponse(scheduledPost.Id.ToString(), IsScheduled: true));
            }
            
            var (success, error, data) = await service.CreatePostAsync(accessToken, request.Content, request.MediaUrns, request.MediaType);
            if (!success) return Results.BadRequest($"Failed to create post on LinkedIn. Details: {error}");

            // Handle follow-up comments
            if (request.Comments != null && request.Comments.Any())
            {
                foreach (var comment in request.Comments)
                {
                    if (!string.IsNullOrWhiteSpace(comment))
                    {
                        await service.AddCommentAsync(accessToken, data!.Id, comment);
                    }
                }
            }

            return Results.Ok(data);
        });

        // Scheduled Posts Endpoints
        group.MapGet("/scheduled", async (IScheduledPostService scheduler) =>
        {
            var posts = await scheduler.GetAllScheduledPostsAsync();
            var dtos = posts.Select(p => new ScheduledPostDto(p.Id, p.Platform, p.Content, p.MediaUrns, p.MediaType, p.Comments, p.ScheduledTime, p.Status ?? "Pending", p.ThumbnailUrl));
            return Results.Ok(dtos);
        });
        
        group.MapGet("/scheduled/{id}", async (Guid id, IScheduledPostService scheduler) =>
        {
            var post = await scheduler.GetScheduledPostByIdAsync(id);
            if (post == null) return Results.NotFound();
            return Results.Ok(new ScheduledPostDto(post.Id, post.Platform, post.Content, post.MediaUrns, post.MediaType, post.Comments, post.ScheduledTime, post.Status ?? "Pending", post.ThumbnailUrl));
        });

        group.MapDelete("/scheduled/{id}", async (Guid id, IScheduledPostService scheduler) =>
        {
            await scheduler.DeleteScheduledPostAsync(id);
            return Results.NoContent();
        });

        group.MapPut("/scheduled/{id}", async (Guid id, [FromBody] UpdateScheduledPostRequest request, IScheduledPostService scheduler) =>
        {
            var post = await scheduler.GetScheduledPostByIdAsync(id);
            if (post == null) return Results.NotFound();

            post.Content = request.Content;
            post.MediaUrns = request.MediaUrns; // Keep old or update if provided
            post.MediaType = request.MediaType;
            post.Comments = request.Comments;
            post.ScheduledTime = request.ScheduledTime;
            if (request.ThumbnailUrl != null) post.ThumbnailUrl = request.ThumbnailUrl;

            await scheduler.UpdateScheduledPostAsync(post);
            return Results.Ok();
        });
    }
}
