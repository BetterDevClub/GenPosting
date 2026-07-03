using System.Net;
using System.Net.Http.Json;
using GenPosting.Web.Services;
using Xunit;

namespace GenPosting.Web.Tests;

public class GenPostingApiClientTests
{
    [Fact]
    public async Task GetFromJsonAsync_AddsHeadersAndDeserializesPayload()
    {
        var handler = new RecordingHandler(async request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/api/scheduling/scheduled", request.RequestUri?.PathAndQuery);
            Assert.Equal("abc", request.Headers.GetValues("X-Test").Single());

            await Task.CompletedTask;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new TestPayload { Name = "hello" })
            };
        });

        var client = new GenPostingApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://example.test") });
        var result = await client.GetFromJsonAsync<TestPayload>("/api/scheduling/scheduled", new Dictionary<string, string>
        {
            ["X-Test"] = "abc"
        });

        Assert.NotNull(result);
        Assert.Equal("hello", result!.Name);
    }

    [Fact]
    public async Task SendAsync_ReturnsNullPayload_WhenResponseIsNotSuccessful()
    {
        var handler = new RecordingHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)));
        var client = new GenPostingApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://example.test") });

        var result = await client.GetFromJsonAsync<TestPayload>("/api/friends");

        Assert.Null(result);
    }

    [Fact]
    public async Task PostAsJsonAsync_UsesJsonContentAndHeaders()
    {
        var handler = new RecordingHandler(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/friends", request.RequestUri?.PathAndQuery);
            Assert.Equal("abc", request.Headers.GetValues("X-Test").Single());

            var body = await request.Content!.ReadAsStringAsync();
            Assert.Contains("hello", body);

            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var client = new GenPostingApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://example.test") });
        var response = await client.PostAsJsonAsync("/api/friends", new TestPayload { Name = "hello" }, new Dictionary<string, string>
        {
            ["X-Test"] = "abc"
        });

        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task DeleteAsync_UsesDeleteMethodAndHeaders()
    {
        var handler = new RecordingHandler(request =>
        {
            Assert.Equal(HttpMethod.Delete, request.Method);
            Assert.Equal("/api/friends/1", request.RequestUri?.PathAndQuery);
            Assert.Equal("abc", request.Headers.GetValues("X-Test").Single());

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        });

        var client = new GenPostingApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://example.test") });
        var response = await client.DeleteAsync("/api/friends/1", new Dictionary<string, string>
        {
            ["X-Test"] = "abc"
        });

        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task RetryScheduledPostAsync_UsesPostMethodForRetryEndpoint()
    {
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var handler = new RecordingHandler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal($"/api/scheduling/scheduled/{id}/retry", request.RequestUri?.PathAndQuery);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });

        var client = new GenPostingApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://example.test") });
        var response = await client.RetryScheduledPostAsync(id);

        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task PutAsJsonAsync_UsesJsonContentAndHeaders()
    {
        var handler = new RecordingHandler(async request =>
        {
            Assert.Equal(HttpMethod.Put, request.Method);
            Assert.Equal("/api/scheduling/scheduled/1", request.RequestUri?.PathAndQuery);
            Assert.Equal("abc", request.Headers.GetValues("X-Test").Single());

            var body = await request.Content!.ReadAsStringAsync();
            Assert.Contains("hello", body);

            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var client = new GenPostingApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://example.test") });
        var response = await client.PutAsJsonAsync("/api/scheduling/scheduled/1", new TestPayload { Name = "hello" }, new Dictionary<string, string>
        {
            ["X-Test"] = "abc"
        });

        Assert.True(response.IsSuccessStatusCode);
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => handler(request);
    }

    private sealed class TestPayload
    {
        public string Name { get; set; } = string.Empty;
    }
}
