using GenPosting.Api.Features.Friends.Services;
using GenPosting.Shared.DTOs;
using Xunit;

namespace GenPosting.Api.Tests;

public class FriendServiceTests
{
    [Fact]
    public async Task Friends_ArePersistedAcrossServiceRecreation()
    {
        var tempFile = CreateTempFilePath();

        try
        {
            var firstService = new FileFriendService(new JsonFileFriendStore(tempFile));
            var friend = new FriendDto { Name = "Ada Lovelace" };

            await firstService.AddAsync(friend);

            var reloadedService = new FileFriendService(new JsonFileFriendStore(tempFile));
            var persistedFriends = await reloadedService.GetAllAsync();

            Assert.Single(persistedFriends);
            Assert.Equal(friend.Name, persistedFriends[0].Name);
            Assert.Equal(friend.Id, persistedFriends[0].Id);
        }
        finally
        {
            DeleteTempFile(tempFile);
        }
    }

    [Fact]
    public async Task AddAsync_DoesNotMutateCallerProvidedFriendRecord()
    {
        var tempFile = CreateTempFilePath();

        try
        {
            var service = new FileFriendService(new JsonFileFriendStore(tempFile));
            var friend = new FriendDto { Id = Guid.Empty, Name = "Margaret Hamilton" };

            var persistedFriend = await service.AddAsync(friend);

            Assert.Equal(Guid.Empty, friend.Id);
            Assert.NotEqual(Guid.Empty, persistedFriend.Id);
            Assert.Equal("Margaret Hamilton", persistedFriend.Name);
        }
        finally
        {
            DeleteTempFile(tempFile);
        }
    }

    [Fact]
    public async Task Delete_RemovesFriendFromPersistedStore()
    {
        var tempFile = CreateTempFilePath();

        try
        {
            var firstService = new FileFriendService(new JsonFileFriendStore(tempFile));
            var friend = new FriendDto { Name = "Grace Hopper" };

            await firstService.AddAsync(friend);
            var deleted = await firstService.DeleteAsync(friend.Id);

            Assert.True(deleted);

            var reloadedService = new FileFriendService(new JsonFileFriendStore(tempFile));
            var persistedFriends = await reloadedService.GetAllAsync();

            Assert.Empty(persistedFriends);
        }
        finally
        {
            DeleteTempFile(tempFile);
        }
    }

    [Fact]
    public async Task EfFriendStore_PersistsFriendsAcrossServiceRecreation()
    {
        var tempFile = CreateTempFilePath("friends");

        try
        {
            var firstService = new FileFriendService(new EfFriendStore(tempFile));
            var friend = new FriendDto { Name = "Katherine Johnson" };

            await firstService.AddAsync(friend);

            var reloadedService = new FileFriendService(new EfFriendStore(tempFile));
            var persistedFriends = await reloadedService.GetAllAsync();

            Assert.Single(persistedFriends);
            Assert.Equal(friend.Name, persistedFriends[0].Name);
        }
        finally
        {
            DeleteTempFile(tempFile);
        }
    }

    private static string CreateTempFilePath(string? prefix = null)
    {
        return Path.Combine(Path.GetTempPath(), $"genposting-{prefix ?? "friends"}-{Guid.NewGuid():N}.db");
    }

    private static void DeleteTempFile(string filePath)
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }
}
