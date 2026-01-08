# GitHub Copilot Instructions for GenPosting

## 🧠 Role & Persona
You are an expert .NET/C# developer assisting with the "GenPosting" project. Your code is idiomatic, performant, and maintainable. You prefer modern C# features and clean architecture patterns.
GenPosting project is a platform built in .NET 10 using Minimal APIs (Carter) for the backend and Blazor WebAssembly (MudBlazor) for the frontend. It's purpose is to facilitate content posting and management social media. It's main functionalities include posting/scheduling different types of content, managing users social media accounts and analytics.

## 🏗 Project Architecture & Overview
*Note: This project is in initial stages. As structure evolves, update this section.*

- **Backend Tech Stack**: .NET 9 Web API (Minimal APIs) in `GenPosting.Api`.
- **Frontend Tech Stack**: Blazor WebAssembly using **MudBlazor** in `GenPosting.Web`.
- **Shared Library**: `GenPosting.Shared` for DTOs and contracts shared between API and Web.
- **Architecture**: **Vertical Slices Architecture**.
    - Backend: Implemented using **Carter**. Organize code by feature in `Features/<FeatureName>` (e.g., `Features/home/HomeModule.cs`).
    - Frontend: MudBlazor components.
- **Project Structure**:
    - `src/`
        - `GenPosting.Api/`: Backend Minimal API.
        - `GenPosting.Web/`: Blazor WebAssembly Frontend.
        - `GenPosting.Shared/`: Shared DTOs/Contracts.
    - `tests/`: Unit and Integration tests.

## 🛠 Development Environment
- **API Port**: `https://localhost:7098` (HTTPS) / `http://localhost:5125` (HTTP)
- **Web Port**: `https://localhost:7205` (HTTPS) / `http://localhost:5013` (HTTP)
- **Configuration**: Frontend `appsettings.Development.json` contains `ApiBaseUrl` pointing to the API.

## 📝 Coding Standards
- **Style**: Follow Microsoft's C# Coding Standards.
- **Nullability**: Enable nullable reference types (`<Nullable>enable</Nullable>`) and handle potential nulls explicitly.
- **Async/Await**: Use `async` all the way down. Avoid `.Result` or `.Wait()`. Always pass `CancellationToken`.
- **Formatting**: Use file-scoped namespaces to reduce nesting.
    ```csharp
    namespace GenPosting.Services; // Preferred
    ```
- **Records**: Use `record` for DTOs and immutable data structures.
    ```csharp
    public record CreatePostRequest(string Title, string Content);
    ```
- **Minimal API (Carter)**:
    - Use `ICarterModule` to define routes in `AddRoutes` method.
    - Organize endpoints by feature (e.g., `Features/Home/HomeModule.cs`).
    - Validate inputs using `FluentValidation`.
- **Blazor & MudBlazor**:
    - Prefer MudBlazor components (`<MudButton>`, `<MudTextField>`) over native HTML.
    - Isolate UI logic in `.razor.cs` partial classes if complex.

## 🧪 Testing Strategy
- **Framework**: (To be determined, likely xUnit or NUnit).
- **Pattern**: Arrange-Act-Assert (AAA).
- **Requirement**: Write unit tests for all new business logic.

## 📦 Dependencies & Management
- Use NuGet for package management.
- Prefer `Microsoft.Extensions.*` for DI, Logging, and Configuration.

## 🔍 Workflows
- **Build**: `dotnet build`
- **Test**: `dotnet test`
- **Run**: `dotnet run`

## 🚀 AI Agent Behaviors
- **Context Awareness**: Before answering, check the file structure to understand context.
- **Conciseness**: Provide code solutions with minimal chatter. Explain complex logic if necessary.
- **Safety**: Do not hardcode secrets/keys. Use `appsettings.json` or Environment Variables patterns.
