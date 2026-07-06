using GenPosting.Api.Data;
using GenPosting.Shared.DTOs;
using Microsoft.EntityFrameworkCore;

namespace GenPosting.Api.Features.Friends.Services;

public sealed class EfFriendStore : IFriendStore
{
    private readonly string _databasePath;

    public EfFriendStore(string? databasePath = null)
    {
        _databasePath = string.IsNullOrWhiteSpace(databasePath)
            ? Path.Combine(Directory.GetCurrentDirectory(), "genposting.db")
            : databasePath;
    }

    public async Task<IReadOnlyDictionary<Guid, FriendDto>> LoadAsync(CancellationToken cancellationToken = default)
    {
        await using var context = CreateContext();
        var friends = await context.Friends.AsNoTracking().ToListAsync(cancellationToken);
        return friends.ToDictionary(friend => friend.Id, friend => new FriendDto { Id = friend.Id, Name = friend.Name });
    }

    public async Task SaveAsync(IReadOnlyDictionary<Guid, FriendDto> friends, CancellationToken cancellationToken = default)
    {
        await using var context = CreateContext();
        var existing = await context.Friends.ToListAsync(cancellationToken);
        context.Friends.RemoveRange(existing);

        foreach (var friend in friends.Values.OrderBy(friend => friend.Name))
        {
            context.Friends.Add(new GenPostingDbContext.FriendEntity { Id = friend.Id == Guid.Empty ? Guid.NewGuid() : friend.Id, Name = friend.Name });
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private GenPostingDbContext CreateContext()
    {
        var directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var optionsBuilder = new DbContextOptionsBuilder<GenPostingDbContext>();
        optionsBuilder.UseSqlite($"Data Source={_databasePath}");
        var context = new GenPostingDbContext(optionsBuilder.Options);
        context.Database.EnsureCreated();
        return context;
    }
}
