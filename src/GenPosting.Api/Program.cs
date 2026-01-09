using Carter;
using GenPosting.Api.Features.LinkedIn;
using GenPosting.Api.Features.LinkedIn.Background;
using GenPosting.Api.Features.LinkedIn.Services;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddOpenApi();
builder.Services.AddCarter();

// Register LinkedIn Feature Services
builder.Services.Configure<LinkedInSettings>(builder.Configuration.GetSection("LinkedIn"));
builder.Services.AddHttpClient<ILinkedInService, LinkedInService>();
builder.Services.AddSingleton<IScheduledPostService, InMemoryScheduledPostService>();
builder.Services.AddHostedService<PostPublisherBackgroundService>();

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
