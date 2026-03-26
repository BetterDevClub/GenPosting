namespace GenPosting.Api.Features.LinkedIn;

public class LinkedInSettings
{
    public const string SectionName = "LinkedIn";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Scope { get; set; } = "openid profile email";
    public string AuthUrl { get; set; } = "https://www.linkedin.com/oauth/v2/authorization";
    public string TokenUrl { get; set; } = "https://www.linkedin.com/oauth/v2/accessToken";
    public string ApiUrl { get; set; } = "https://api.linkedin.com/v2";
    public string CallbackUrl { get; set; } = string.Empty;
}
