using GenPosting.Shared.DTOs;

namespace GenPosting.Api.Features.Friends.Services;

/// <summary>
/// Friend service backed by a file-based persistence store.
/// </summary>
public sealed class FileFriendService : IFriendService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IFriendStore _store;
    private Dictionary<Guid, FriendDto> _friends = new();
    private bool _isLoaded;

    public FileFriendService(IFriendStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<List<FriendDto>> GetAllAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await EnsureLoadedAsync();
            return _friends.Values.OrderBy(friend => friend.Name).ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<FriendDto> AddAsync(FriendDto friend)
    {
        ArgumentNullException.ThrowIfNull(friend);

        await _gate.WaitAsync();
        try
        {
            await EnsureLoadedAsync();

            var persistedFriend = friend.Id == Guid.Empty
                ? friend with { Id = Guid.NewGuid() }
                : friend;

            _friends[persistedFriend.Id] = persistedFriend;
            await PersistChangesAsync();
            return persistedFriend;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        await _gate.WaitAsync();
        try
        {
            await EnsureLoadedAsync();

            var removed = _friends.Remove(id);
            if (removed)
            {
                await PersistChangesAsync();
            }

            return removed;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureLoadedAsync()
    {
        if (_isLoaded)
        {
            return;
        }

        var persistedFriends = await _store.LoadAsync();
        _friends = persistedFriends.ToDictionary(friend => friend.Key, friend => friend.Value);
        _isLoaded = true;
    }

    private async Task PersistChangesAsync()
    {
        await _store.SaveAsync(_friends);
    }
}
