using GenPosting.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace GenPosting.Api.Data;

public static class SeedData
{
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var environment = services.GetService<IHostEnvironment>();
        if (environment is null || !environment.IsDevelopment())
        {
            return;
        }

        await using var scope = services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<GenPostingDbContext>();
        await context.Database.MigrateAsync(cancellationToken);

        if (!await context.Friends.AnyAsync(cancellationToken))
        {
            context.Friends.Add(new GenPostingDbContext.FriendEntity { Id = Guid.NewGuid(), Name = "Seed Friend" });
            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
