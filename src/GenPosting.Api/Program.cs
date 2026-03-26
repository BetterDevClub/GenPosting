using Carter;
using FluentValidation;
using GenPosting.Api.Features.LinkedIn;
using GenPosting.Api.Features.Scheduling.Background;
using GenPosting.Api.Features.Scheduling.Services;
using GenPosting.Api.Features.LinkedIn.Services;
using GenPosting.Api.Features.Instagram.Services;
using GenPosting.Api.Features.Instagram.Models;
using GenPosting.Api.Features.Facebook.Services;
using GenPosting.Api.Features.Facebook.Models;
using GenPosting.Api.Services;
using GenPosting.Api.Features.Friends.Services;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddOpenApi();
builder.Services.AddCarter();
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

// Shared Infrastructure
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IBlobStorageService, AzureBlobStorageService>();

// Register LinkedIn Feature Services
builder.Services.Configure<LinkedInSettings>(builder.Configuration.GetSection(LinkedInSettings.SectionName));
builder.Services.AddHttpClient<ILinkedInService, LinkedInService>();

// Register Scheduling Services
builder.Services.AddSingleton<IScheduledPostService, InMemoryScheduledPostService>();
builder.Services.AddHostedService<PostPublisherBackgroundService>();

// Register Friends Services
builder.Services.AddSingleton<IFriendService, InMemoryFriendService>();

// Register Instagram Feature Services
builder.Services.Configure<InstagramSettings>(builder.Configuration.GetSection(InstagramSettings.SectionName));
builder.Services.AddHttpClient<IInstagramService, InstagramService>();

// Register Facebook Feature Services
builder.Services.Configure<FacebookSettings>(builder.Configuration.GetSection(FacebookSettings.SectionName));
builder.Services.AddHttpClient<IFacebookService, FacebookService>();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(b =>
    {
        b.AllowAnyOrigin()
         .AllowAnyHeader()
         .AllowAnyMethod();
    });
});

builder.Services.AddHealthChecks();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();
app.UseCors();

app.MapHealthChecks("/health");
app.MapCarter();

app.Run();
