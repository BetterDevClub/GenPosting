using GenPosting.Shared.DTOs;

namespace GenPosting.Api.Features.Friends.Services;

public interface IFriendService
{
    Task<List<FriendDto>> GetAllAsync();
    Task<FriendDto> AddAsync(FriendDto friend);
    Task<bool> DeleteAsync(Guid id);
}
