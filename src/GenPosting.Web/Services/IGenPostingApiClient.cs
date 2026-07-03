namespace GenPosting.Web.Services;

public interface IGenPostingApiClient
{
    Task<T?> GetFromJsonAsync<T>(string requestUri, IReadOnlyDictionary<string, string>? headers = null, CancellationToken cancellationToken = default);
    Task<HttpResponseMessage> SendAsync(HttpMethod method, string requestUri, HttpContent? content = null, IReadOnlyDictionary<string, string>? headers = null, CancellationToken cancellationToken = default);
    Task<HttpResponseMessage> PostAsJsonAsync<TValue>(string requestUri, TValue value, IReadOnlyDictionary<string, string>? headers = null, CancellationToken cancellationToken = default);
    Task<HttpResponseMessage> PutAsJsonAsync<TValue>(string requestUri, TValue value, IReadOnlyDictionary<string, string>? headers = null, CancellationToken cancellationToken = default);
    Task<HttpResponseMessage> DeleteAsync(string requestUri, IReadOnlyDictionary<string, string>? headers = null, CancellationToken cancellationToken = default);
}
