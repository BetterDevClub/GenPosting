# GitHub Copilot Instructions for GenPosting

GenPosting is a .NET 10 social media content scheduling and publishing platform. The backend uses Minimal APIs (Carter) with vertical slices architecture; the frontend is Blazor WebAssembly with MudBlazor.

## Architecture

Three projects under `src/`:
- **`GenPosting.Api`** – ASP.NET Core Minimal API. Features live in `Features/<PlatformOrDomain>/`. Each feature has a `*Module.cs` (Carter), `Services/`, and `Models/`.
- **`GenPosting.Web`** – Blazor WebAssembly. Pages in `Pages/`, reusable UI in `Components/`, per-platform state in `Services/*StateService.cs`.
- **`GenPosting.Shared`** – DTOs (as `record` types) and enums shared between API and Web.

Supported social platforms: `LinkedIn`, `Instagram`, `Facebook` (see `SocialPlatform` enum).

## Key Patterns

### Carter Modules (API)
Each feature implements `ICarterModule` and groups its routes:
```csharp
public class FacebookModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/facebook").WithTags("Facebook");
        group.MapGet("/profile", async (...) => { ... });
    }
}
```
`app.MapCarter()` in `Program.cs` auto-discovers all modules.

### Auth Token Passing
Social platform tokens are **not** stored server-side. The frontend persists them in `localStorage` (via `IJSRuntime`) and passes them per-request using platform-specific headers:
- `X-Facebook-Token` / `X-Facebook-UserId`
- `X-Instagram-Token` / `X-Instagram-UserId`  
- LinkedIn uses Bearer tokens in standard `Authorization` header

Each platform has a corresponding `*StateService` in `GenPosting.Web/Services/` that manages this.

### Platform Configuration (API)
Platform OAuth credentials live in `appsettings.json` under named sections and are bound with `IOptions<T>`:
```json
{ "LinkedIn": { "ClientId": "", "ClientSecret": "", "Scope": "..." } }
```
```csharp
builder.Services.Configure<LinkedInSettings>(builder.Configuration.GetSection("LinkedIn"));
```
**Never hardcode credentials.** Use `appsettings.Development.json` or environment variables locally.

### Scheduling Flow
When a post has a `scheduledFor` value:
1. If media is attached, upload it to Azure Blob Storage first to get a URL.
2. Store a `ScheduledPost` via `IScheduledPostService` (currently in-memory).
3. `PostPublisherBackgroundService` polls every 30 seconds, publishes due posts, and marks them published/failed.

Immediate posts skip scheduling and call the platform service directly.

### In-Memory Implementations
`InMemoryScheduledPostService` and `InMemoryFriendService` are dev-only placeholders registered as `Singleton`. Replace with persistent implementations for production.

### Shared DTOs
All DTOs in `GenPosting.Shared` are positional `record` types:
```csharp
public record ScheduledPostDto(Guid Id, SocialPlatform Platform, string Content, ...);
```

### MudBlazor UI
Use MudBlazor components exclusively (`<MudButton>`, `<MudTextField>`, `<MudDialog>`, etc.). Complex dialog logic lives in `Components/` (e.g., `EditScheduledPostDialog.razor`).

## Development Environment

- **API**: `https://localhost:7098` / `http://localhost:5125`
- **Web**: `https://localhost:7205` / `http://localhost:5013`
- **API docs**: Scalar UI at `/scalar/v1` (dev only)
- Frontend reads `ApiBaseUrl` from `wwwroot/appsettings.Development.json`

## Commands

```bash
dotnet build          # Build entire solution
dotnet run            # Run from a project directory
dotnet test           # Run all tests
```

## Coding Standards

- Target **C# 14** / **.NET 10**; use file-scoped namespaces.
- Nullable reference types are enabled — use `is null` / `is not null`, not `== null`.
- `async`/`await` all the way down; always propagate `CancellationToken`.
- Use `nameof()` instead of string literals for member references.
- XML doc comments on all public APIs.
- Test pattern: Arrange-Act-Assert (no AAA comments in code). Use xUnit + Moq/NSubstitute.
