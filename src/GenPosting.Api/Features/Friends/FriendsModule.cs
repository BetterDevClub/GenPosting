using Carter;
using GenPosting.Api.Features.Friends.Services;
using GenPosting.Shared.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace GenPosting.Api.Features.Friends;

public class FriendsModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/friends")
            .WithTags("Friends");

        group.MapGet("/", async (IFriendService service) =>
        {
            var friends = await service.GetAllAsync();
            return Results.Ok(friends);
        });

        group.MapPost("/", async ([FromBody] FriendDto friend, IFriendService service) =>
        {
            if (string.IsNullOrWhiteSpace(friend.Name))
                return Results.BadRequest("Name is required");

            var created = await service.AddAsync(friend);
            return Results.Created($"/api/friends/{created.Id}", created);
        });

        group.MapDelete("/{id:guid}", async (Guid id, IFriendService service) =>
        {
            var deleted = await service.DeleteAsync(id);
            return deleted ? Results.NoContent() : Results.NotFound();
        });
    }
}
