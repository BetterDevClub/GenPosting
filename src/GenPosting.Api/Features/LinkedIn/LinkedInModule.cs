using Carter;
using GenPosting.Api.Features.LinkedIn.Services;
using GenPosting.Shared.DTOs;
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

        group.MapGet("/posts", async ([FromHeader(Name = "X-LinkedIn-Token")] string? accessToken, ILinkedInService service) =>
        {
            if (string.IsNullOrEmpty(accessToken)) return Results.Unauthorized();
            
            var posts = await service.GetPostsAsync(accessToken);
            return Results.Ok(posts);
        });

        group.MapPost("/post", async ([FromHeader(Name = "X-LinkedIn-Token")] string? accessToken, [FromBody] CreateLinkedInPostRequest request, ILinkedInService service) =>
        {
            if (string.IsNullOrEmpty(accessToken)) return Results.Unauthorized();
            if (string.IsNullOrWhiteSpace(request.Content)) return Results.BadRequest("Content cannot be empty");

            var (success, error, data) = await service.CreatePostAsync(accessToken, request.Content);
            if (!success) return Results.BadRequest($"Failed to create post on LinkedIn. Details: {error}");

            return Results.Ok(data);
        });
    }
}
