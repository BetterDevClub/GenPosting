namespace GenPosting.Api.Features.Instagram.Models;

public class InstagramSettings
{
    public const string SectionName = "Instagram";
    
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Scope { get; set; } = "user_profile,user_media"; // Basic scopes
}
