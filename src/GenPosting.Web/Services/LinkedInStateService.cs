using Microsoft.JSInterop;

namespace GenPosting.Web.Services;

public interface ILinkedInStateService
{
    string? AccessToken { get; }
    bool IsAuthenticated { get; }
    Task InitializeAsync();
    Task SetAccessTokenAsync(string? token);
}

public class LinkedInStateService : ILinkedInStateService
{
    private readonly IJSRuntime _js;
    private const string STORAGE_KEY = "linkedin_token";

    public LinkedInStateService(IJSRuntime js)
    {
        _js = js;
    }

    public string? AccessToken { get; private set; }

    public bool IsAuthenticated => !string.IsNullOrEmpty(AccessToken);

    public async Task InitializeAsync()
    {
        try
        {
            AccessToken = await _js.InvokeAsync<string>("localStorage.getItem", STORAGE_KEY);
        }
        catch { }
    }

    public async Task SetAccessTokenAsync(string? token)
    {
        AccessToken = token;
        if (string.IsNullOrEmpty(token))
        {
            await _js.InvokeVoidAsync("localStorage.removeItem", STORAGE_KEY);
        }
        else
        {
            await _js.InvokeVoidAsync("localStorage.setItem", STORAGE_KEY, token);
        }
    }
}
