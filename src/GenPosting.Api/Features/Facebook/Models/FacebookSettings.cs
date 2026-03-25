namespace GenPosting.Api.Features.Facebook.Models;

public class FacebookSettings
{
    public const string SectionName = "Facebook";
    
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Scope { get; set; } = "public_profile,email,pages_show_list,pages_read_engagement"; 
}
