using Carter;

namespace GenPosting.Api.Features.Home;

public class HomeModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/test", () =>
        {
            return Results.Ok(new { Message = "API is working!", Timestamp = DateTime.UtcNow });
        })
        .WithTags("Home")
        .WithName("TestEndpoint");
    }
}