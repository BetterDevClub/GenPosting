namespace GenPosting.Web.Services;

public interface IInstagramStateService
{
    string? AccessToken { get; set; }
    string? UserId { get; set; }
    bool IsAuthenticated => !string.IsNullOrEmpty(AccessToken);
}

public class InstagramStateService : IInstagramStateService
{
    public string? AccessToken { get; set; }
    public string? UserId { get; set; }
}
