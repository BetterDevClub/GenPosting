using Carter;
using FluentValidation;
using GenPosting.Api.Features.Scheduling.Services;
using GenPosting.Shared.DTOs;
using GenPosting.Shared.Enums;
using Microsoft.AspNetCore.Mvc;

namespace GenPosting.Api.Features.Scheduling;

public class SchedulingModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/scheduling").WithTags("Scheduling");

        group.MapGet("/scheduled", async (IScheduledPostService scheduler) =>
        {
            var posts = await scheduler.GetAllScheduledPostsAsync();
            var dtos = posts.Select(p => new ScheduledPostDto(p.Id, p.Platform, p.Content, p.MediaUrns, p.MediaType, p.Comments, p.ScheduledTime, p.Status, p.ThumbnailUrl, p.FailureReason, p.RetryCount, p.MaxRetries, p.NextRetryAt));
            return Results.Ok(dtos);
        });

        group.MapGet("/scheduled/{id}", async (Guid id, IScheduledPostService scheduler) =>
        {
            var post = await scheduler.GetScheduledPostByIdAsync(id);
            if (post == null) return Results.NotFound();
            return Results.Ok(new ScheduledPostDto(post.Id, post.Platform, post.Content, post.MediaUrns, post.MediaType, post.Comments, post.ScheduledTime, post.Status, post.ThumbnailUrl, post.FailureReason, post.RetryCount, post.MaxRetries, post.NextRetryAt));
        });

        group.MapDelete("/scheduled/{id}", async (Guid id, IScheduledPostService scheduler) =>
        {
            await scheduler.DeleteScheduledPostAsync(id);
            return Results.NoContent();
        });

        group.MapPut("/scheduled/{id}", async (Guid id, [FromBody] UpdateScheduledPostRequest request, IScheduledPostService scheduler, IValidator<UpdateScheduledPostRequest> validator) =>
        {
            var validation = await validator.ValidateAsync(request);
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());

            var post = await scheduler.GetScheduledPostByIdAsync(id);
            if (post == null) return Results.NotFound();

            post.Content = request.Content;
            post.MediaUrns = request.MediaUrns;
            post.MediaType = request.MediaType;
            post.Comments = request.Comments;
            post.ScheduledTime = request.ScheduledTime;
            if (request.ThumbnailUrl != null) post.ThumbnailUrl = request.ThumbnailUrl;

            await scheduler.UpdateScheduledPostAsync(post);
            return Results.Ok();
        });
        group.MapPost("/scheduled/{id}/retry", async (Guid id, IScheduledPostService scheduler) =>
        {
            var post = await scheduler.GetScheduledPostByIdAsync(id);
            if (post == null) return Results.NotFound();
            if (post.Status != ScheduledPostStatus.Failed)
                return Results.BadRequest("Only failed posts can be manually retried.");

            await scheduler.ResetForRetryAsync(id);
            return Results.Ok();
        });
    }
}
