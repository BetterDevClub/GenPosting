using Microsoft.JSInterop;

namespace GenPosting.Web.Services;

public interface IInstagramStateService
{
    string? AccessToken { get; }
    string? UserId { get; }
    bool IsAuthenticated { get; }
    Task InitializeAsync();
    Task SetCredentialsAsync(string? token, string? userId);
}

public class InstagramStateService : IInstagramStateService
{
    private readonly IJSRuntime _js;
    private const string TOKEN_KEY = "instagram_token";
    private const string USERID_KEY = "instagram_userid";

    public InstagramStateService(IJSRuntime js)
    {
        _js = js;
    }

    public string? AccessToken { get; private set; }
    public string? UserId { get; private set; }
    public bool IsAuthenticated => !string.IsNullOrEmpty(AccessToken);

    public async Task InitializeAsync()
    {
        try
        {
            AccessToken = await _js.InvokeAsync<string>("localStorage.getItem", TOKEN_KEY);
            UserId = await _js.InvokeAsync<string>("localStorage.getItem", USERID_KEY);
        }
        catch { }
    }

    public async Task SetCredentialsAsync(string? token, string? userId)
    {
        AccessToken = token;
        UserId = userId;

        if (string.IsNullOrEmpty(token))
        {
            await _js.InvokeVoidAsync("localStorage.removeItem", TOKEN_KEY);
            await _js.InvokeVoidAsync("localStorage.removeItem", USERID_KEY);
        }
        else
        {
            await _js.InvokeVoidAsync("localStorage.setItem", TOKEN_KEY, token);
            if (!string.IsNullOrEmpty(userId))
            {
                await _js.InvokeVoidAsync("localStorage.setItem", USERID_KEY, userId);
            }
        }
    }
}
