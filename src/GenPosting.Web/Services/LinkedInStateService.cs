namespace GenPosting.Web.Services;

public interface ILinkedInStateService
{
    string? AccessToken { get; set; }
    bool IsAuthenticated => !string.IsNullOrEmpty(AccessToken);
}

public class LinkedInStateService : ILinkedInStateService
{
    public string? AccessToken { get; set; }
}
