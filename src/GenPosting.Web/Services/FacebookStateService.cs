using Microsoft.JSInterop;

namespace GenPosting.Web.Services;

public interface IFacebookStateService
{
    string? AccessToken { get; }
    string? UserId { get; }
    bool IsAuthenticated { get; }
    Task InitializeAsync();
    Task SetCredentialsAsync(string? token, string? userId);
}

public class FacebookStateService : IFacebookStateService
{
    private readonly IJSRuntime _js;
    private const string TOKEN_KEY = "facebook_token";
    private const string USERID_KEY = "facebook_userid";

    public FacebookStateService(IJSRuntime js)
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
            AccessToken = await _js.InvokeAsync<string>("sessionStorage.getItem", TOKEN_KEY);
            UserId = await _js.InvokeAsync<string>("sessionStorage.getItem", USERID_KEY);
        }
        catch { }
    }

    public async Task SetCredentialsAsync(string? token, string? userId)
    {
        AccessToken = token;
        UserId = userId;

        if (string.IsNullOrEmpty(token))
        {
            await _js.InvokeVoidAsync("sessionStorage.removeItem", TOKEN_KEY);
            await _js.InvokeVoidAsync("sessionStorage.removeItem", USERID_KEY);
        }
        else
        {
            await _js.InvokeVoidAsync("sessionStorage.setItem", TOKEN_KEY, token);
            await _js.InvokeVoidAsync("sessionStorage.setItem", USERID_KEY, userId ?? string.Empty);
        }
    }
}
