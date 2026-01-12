namespace GenPosting.Api.Features.Instagram.Models;

public class InstagramSettings
{
    public const string SectionName = "Instagram";
    
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Scope { get; set; } = "instagram_basic,instagram_content_publish,instagram_manage_comments,instagram_manage_insights,pages_show_list,pages_read_engagement"; 
}
