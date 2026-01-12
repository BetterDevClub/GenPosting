using Carter;
using GenPosting.Api.Features.LinkedIn;
using GenPosting.Api.Features.Scheduling.Background;
using GenPosting.Api.Features.Scheduling.Services;
using GenPosting.Api.Features.LinkedIn.Services;
using GenPosting.Api.Features.Instagram.Services;
using GenPosting.Api.Features.Instagram.Models;
using GenPosting.Api.Services;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddOpenApi();
builder.Services.AddCarter();

// Shared Infrastructure
builder.Services.AddSingleton<IBlobStorageService, AzureBlobStorageService>();

// Register LinkedIn Feature Services
builder.Services.Configure<LinkedInSettings>(builder.Configuration.GetSection("LinkedIn"));
builder.Services.AddHttpClient<ILinkedInService, LinkedInService>();

// Register Scheduling Services
builder.Services.AddSingleton<IScheduledPostService, InMemoryScheduledPostService>();
builder.Services.AddHostedService<PostPublisherBackgroundService>();

// Register Instagram Feature Services
builder.Services.Configure<InstagramSettings>(builder.Configuration.GetSection(InstagramSettings.SectionName));
builder.Services.AddHttpClient<IInstagramService, InstagramService>();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(b =>
    {
        b.AllowAnyOrigin()
         .AllowAnyHeader()
         .AllowAnyMethod();
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();
app.UseCors();

app.MapCarter();

app.Run();
