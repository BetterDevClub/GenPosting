using System.Collections.Concurrent;
using GenPosting.Shared.DTOs;

namespace GenPosting.Api.Features.Friends.Services;

public class InMemoryFriendService : IFriendService
{
    private readonly ConcurrentDictionary<Guid, FriendDto> _friends = new();

    public Task<List<FriendDto>> GetAllAsync()
    {
        return Task.FromResult(_friends.Values.ToList());
    }

    public Task<FriendDto> AddAsync(FriendDto friend)
    {
        if (friend.Id == Guid.Empty)
            friend.Id = Guid.NewGuid();

        _friends.TryAdd(friend.Id, friend);
        return Task.FromResult(friend);
    }

    public Task<bool> DeleteAsync(Guid id)
    {
        return Task.FromResult(_friends.TryRemove(id, out _));
    }
}
