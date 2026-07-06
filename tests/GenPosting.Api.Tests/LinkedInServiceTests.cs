using System.Net;
using System.Text;
using GenPosting.Api.Features.LinkedIn;
using GenPosting.Api.Features.LinkedIn.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace GenPosting.Api.Tests;

public class LinkedInServiceTests
{
    [Fact]
    public async Task CreatePostAsync_UsesSupportedLinkedInApiVersionHeader()
    {
        var handler = new RecordingHttpMessageHandler((request, cancellationToken) =>
        {
            if (request.RequestUri?.AbsolutePath.Contains("/userinfo", StringComparison.Ordinal) == true)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"sub\":\"123\",\"name\":\"Test User\",\"picture\":\"https://example.com/pic.jpg\"}", Encoding.UTF8, "application/json")
                });
            }

            if (request.RequestUri?.AbsolutePath.Contains("/posts", StringComparison.Ordinal) == true)
            {
                var version = request.Headers.GetValues("LinkedIn-Version").Single();
                Assert.Equal("202506", version);

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });

        using var httpClient = new HttpClient(handler);
        var service = new LinkedInService(
            httpClient,
            Options.Create(new LinkedInSettings
            {
                ClientId = "client-id",
                ClientSecret = "client-secret",
                CallbackUrl = "https://example.com/callback",
                ApiUrl = "https://api.linkedin.com/v2",
                RestApiUrl = "https://api.linkedin.com/rest"
            }),
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<LinkedInService>.Instance);

        var result = await service.CreatePostAsync("token", "Hello from tests");

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
    }


    [Fact]
    public async Task CreatePostAsync_TriesMultipleLinkedInApiVersionsUntilOneSucceeds()
    {
        var attemptCount = 0;
        var handler = new RecordingHttpMessageHandler((request, cancellationToken) =>
        {
            if (request.RequestUri?.AbsolutePath.Contains("/userinfo", StringComparison.Ordinal) == true)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"sub\":\"123\",\"name\":\"Test User\",\"picture\":\"https://example.com/pic.jpg\"}", Encoding.UTF8, "application/json")
                });
            }

            if (request.RequestUri?.AbsolutePath.Contains("/posts", StringComparison.Ordinal) == true)
            {
                attemptCount++;
                var version = request.Headers.GetValues("LinkedIn-Version").Single();

                if (attemptCount <= 2)
                {
                    Assert.Matches("^20\\d{4}$", version);
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
                    {
                        Content = new StringContent("{\"message\":\"Requested version 20240901 is not active\"}", Encoding.UTF8, "application/json")
                    });
                }

                Assert.Equal("202504", version);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });

        using var httpClient = new HttpClient(handler);
        var service = new LinkedInService(
            httpClient,
            Options.Create(new LinkedInSettings
            {
                ClientId = "client-id",
                ClientSecret = "client-secret",
                CallbackUrl = "https://example.com/callback",
                ApiUrl = "https://api.linkedin.com/v2",
                RestApiUrl = "https://api.linkedin.com/rest"
            }),
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<LinkedInService>.Instance);

        var result = await service.CreatePostAsync("token", "Hello from tests");

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(3, attemptCount);
    }

    private sealed class RecordingHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => handler(request, cancellationToken);
    }
}
