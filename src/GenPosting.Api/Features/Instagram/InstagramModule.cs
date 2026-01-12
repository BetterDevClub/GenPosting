using Carter;
using GenPosting.Api.Features.Instagram.Services;
using GenPosting.Shared.DTOs;
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

        group.MapPost("/post", async (HttpRequest request, IInstagramService service) =>
        {
            if (!request.Headers.TryGetValue("X-Instagram-Token", out var token)) return Results.Unauthorized();
            if (!request.Headers.TryGetValue("X-Instagram-UserId", out var userId)) return Results.Unauthorized();

            if (!request.HasFormContentType) return Results.BadRequest("Expected form content type");

            var form = await request.ReadFormAsync();
            var caption = form["caption"];
            var postTypeStr = form["postType"];
            var file = form.Files["file"];

            if (!Enum.TryParse<InstagramPostType>(postTypeStr, out var postType))
                postType = InstagramPostType.Post;

            Stream? stream = null;
            if (file != null) stream = file.OpenReadStream();

            var dto = new CreateInstagramPostRequest(caption, postType, new List<string>());

            var (success, error) = await service.PublishPostAsync(token, userId, dto, stream, file?.FileName);
            return success ? Results.Ok() : Results.BadRequest(error);
        });
    }
}
