using GenPosting.Shared.DTOs;

namespace GenPosting.Api.Features.Friends.Services;

/// <summary>
/// Persists friends to a backing store.
/// </summary>
public interface IFriendStore
{
    Task<IReadOnlyDictionary<Guid, FriendDto>> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(IReadOnlyDictionary<Guid, FriendDto> friends, CancellationToken cancellationToken = default);
}
