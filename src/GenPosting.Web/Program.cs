using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using GenPosting.Web;
using MudBlazor.Services;

using GenPosting.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => 
    new HttpClient 
    { 
        BaseAddress = new Uri(builder.Configuration["ApiBaseUrl"] ?? builder.HostEnvironment.BaseAddress) 
    });
builder.Services.AddMudServices();
builder.Services.AddSingleton<ILinkedInStateService, LinkedInStateService>();

await builder.Build().RunAsync();
