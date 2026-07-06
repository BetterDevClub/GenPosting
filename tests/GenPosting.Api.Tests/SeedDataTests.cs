using GenPosting.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace GenPosting.Api.Tests;

public class SeedDataTests
{
    [Fact]
    public async Task InitializeAsync_DoesNotSeedWhenEnvironmentIsNotDevelopment()
    {
        var tempDbPath = CreateTempFilePath();

        try
        {
            var services = new ServiceCollection();
            services.AddSingleton<IHostEnvironment>(new FakeHostEnvironment { EnvironmentName = Environments.Production });
            services.AddDbContext<GenPostingDbContext>(options =>
                options.UseSqlite($"Data Source={tempDbPath}"));

            using var provider = services.BuildServiceProvider();

            await SeedData.InitializeAsync(provider);

            Assert.False(File.Exists(tempDbPath));
        }
        finally
        {
            DeleteTempFile(tempDbPath);
        }
    }

    private static string CreateTempFilePath(string? prefix = null)
    {
        return Path.Combine(Path.GetTempPath(), $"genposting-seed-{prefix ?? "tests"}-{Guid.NewGuid():N}.db");
    }

    private static void DeleteTempFile(string filePath)
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "GenPosting.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider FileProvider { get; set; } = new NullFileProvider();
    }
}
