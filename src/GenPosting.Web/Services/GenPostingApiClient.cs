using System.Net.Http.Json;
using System.Text.Json;

namespace GenPosting.Web.Services;

public sealed class GenPostingApiClient : IGenPostingApiClient
{
    private readonly HttpClient _httpClient;

    public GenPostingApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<T?> GetFromJsonAsync<T>(string requestUri, IReadOnlyDictionary<string, string>? headers = null, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, requestUri, headers: headers, cancellationToken: cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return default;
        }

        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken);
    }

    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string requestUri, HttpContent? content = null, IReadOnlyDictionary<string, string>? headers = null, CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(method, requestUri)
        {
            Content = content
        };

        if (headers is not null)
        {
            foreach (var header in headers)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return await _httpClient.SendAsync(request, cancellationToken);
    }

    public Task<HttpResponseMessage> PostAsJsonAsync<TValue>(string requestUri, TValue value, IReadOnlyDictionary<string, string>? headers = null, CancellationToken cancellationToken = default)
        => SendAsync(HttpMethod.Post, requestUri, JsonContent.Create(value), headers, cancellationToken);

    public Task<HttpResponseMessage> PutAsJsonAsync<TValue>(string requestUri, TValue value, IReadOnlyDictionary<string, string>? headers = null, CancellationToken cancellationToken = default)
        => SendAsync(HttpMethod.Put, requestUri, JsonContent.Create(value), headers, cancellationToken);

    public Task<HttpResponseMessage> DeleteAsync(string requestUri, IReadOnlyDictionary<string, string>? headers = null, CancellationToken cancellationToken = default)
        => SendAsync(HttpMethod.Delete, requestUri, headers: headers, cancellationToken: cancellationToken);
}
