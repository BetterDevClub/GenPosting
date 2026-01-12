using Carter;
using GenPosting.Api.Features.Instagram.Services;
using GenPosting.Api.Features.Scheduling.Models; // For ScheduledPost
using GenPosting.Api.Features.Scheduling.Services; // For IScheduledPostService
using GenPosting.Shared.DTOs;
using GenPosting.Shared.Enums; // For SocialPlatform
using Microsoft.AspNetCore.Mvc;

namespace GenPosting.Api.Features.Instagram;

public class InstagramModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/instagram")
            .WithTags("Instagram");

        group.MapGet("/auth-url", (string redirectUri, IInstagramService service) =>
        {
            var url = service.GetAuthorizationUrl(redirectUri);
            return Results.Ok(new InstagramAuthUrlResponse(url));
        });

        group.MapPost("/exchange-token", async ([FromBody] InstagramExchangeTokenRequest request, IInstagramService service) =>
        {
            var token = await service.ExchangeTokenAsync(request.Code, request.RedirectUri);
            if (token == null) return Results.BadRequest("Failed to exchange token.");
            return Results.Ok(token);
        });

        group.MapGet("/profile", async ([FromQuery] string userId, [FromHeader(Name = "X-Instagram-Token")] string token, IInstagramService service) =>
        {
            if (string.IsNullOrEmpty(token)) return Results.Unauthorized();
            var profile = await service.GetProfileAsync(token, userId);
            return profile != null ? Results.Ok(profile) : Results.NotFound();
        });

        group.MapPost("/post", async (HttpRequest request, IInstagramService service, IScheduledPostService scheduledService) =>
        {
            if (!request.Headers.TryGetValue("X-Instagram-Token", out var token)) return Results.Unauthorized();
            if (!request.Headers.TryGetValue("X-Instagram-UserId", out var userId)) return Results.Unauthorized();

            if (!request.HasFormContentType) return Results.BadRequest("Expected form content type");

            var form = await request.ReadFormAsync();
            var caption = form["caption"];
            var postTypeStr = form["postType"];
            var scheduledForStr = form["scheduledFor"];
            var file = form.Files["file"];

            if (!Enum.TryParse<InstagramPostType>(postTypeStr, out var postType))
                postType = InstagramPostType.Post;

            Stream? stream = null;
            if (file != null) stream = file.OpenReadStream();

            DateTimeOffset? scheduledFor = null;
            if (!string.IsNullOrEmpty(scheduledForStr) && DateTimeOffset.TryParse(scheduledForStr, out var parsedDate))
            {
                scheduledFor = parsedDate;
            }

            // Scheduling Logic
            if (scheduledFor.HasValue)
            {
                 if (stream == null || file == null) 
                    return Results.BadRequest("File is required for scheduling.");

                 // 1. Upload Media First
                 string mediaUrl;
                 try 
                 {
                     // Use the exposed UploadMediaAsync from IInstagramService
                     mediaUrl = await service.UploadMediaAsync(stream, file.FileName);
                 }
                 catch (Exception ex)
                 {
                     return Results.BadRequest($"Failed to upload media for scheduling: {ex.Message}");
                 }

                 // 2. Create Scheduled Post
                 var scheduledPost = new ScheduledPost
                 {
                     Platform = SocialPlatform.Instagram,
                     PlatformUserId = userId,
                     AccessToken = token,
                     Content = caption,
                     IgPostType = postType,
                     MediaUrns = new List<string> { mediaUrl }, // Storing URL in MediaUrns mechanism
                     ScheduledTime = scheduledFor.Value,
                     ThumbnailUrl = mediaUrl, // Using same URL for thumbnail for now
                     Status = "Pending"
                 };

                 await scheduledService.SchedulePostAsync(scheduledPost);
                 return Results.Ok(new { Message = "Post scheduled successfully", ScheduledId = scheduledPost.Id });
            }

            // Immediate Publishing Logic
            var dto = new CreateInstagramPostRequest(caption, postType, new List<string>());

            var (success, error) = await service.PublishPostAsync(token, userId, dto, stream, file?.FileName);
            return success ? Results.Ok() : Results.BadRequest(error);
        });
    }
}
