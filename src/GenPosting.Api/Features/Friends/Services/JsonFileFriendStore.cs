using System.Text.Json;
using GenPosting.Shared.DTOs;

namespace GenPosting.Api.Features.Friends.Services;

/// <summary>
/// File-based implementation of <see cref="IFriendStore"/>.
/// </summary>
public sealed class JsonFileFriendStore : IFriendStore
{
    private const int CurrentStorageVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _storagePath;

    public JsonFileFriendStore(string? storagePath = null)
    {
        _storagePath = string.IsNullOrWhiteSpace(storagePath)
            ? Path.Combine(Directory.GetCurrentDirectory(), "friends.json")
            : storagePath;
    }

    public async Task<IReadOnlyDictionary<Guid, FriendDto>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_storagePath))
        {
            return new Dictionary<Guid, FriendDto>();
        }

        try
        {
            var json = await File.ReadAllTextAsync(_storagePath, cancellationToken);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new Dictionary<Guid, FriendDto>();
            }

            var payload = JsonSerializer.Deserialize<FriendFilePayload>(json, SerializerOptions);
            return payload?.Friends?.ToDictionary(friend => friend.Id) ?? new Dictionary<Guid, FriendDto>();
        }
        catch (JsonException)
        {
            return new Dictionary<Guid, FriendDto>();
        }
    }

    public async Task SaveAsync(IReadOnlyDictionary<Guid, FriendDto> friends, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_storagePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var payload = new FriendFilePayload
        {
            Version = CurrentStorageVersion,
            Friends = friends.Values.OrderBy(friend => friend.Name).ToList()
        };

        var json = JsonSerializer.Serialize(payload, SerializerOptions);
        await WriteToStorageAtomicallyAsync(json, cancellationToken);
    }

    private async Task WriteToStorageAtomicallyAsync(string json, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_storagePath) ?? Directory.GetCurrentDirectory();
        var tempFilePath = Path.Combine(directory, $".{Path.GetFileName(_storagePath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await File.WriteAllTextAsync(tempFilePath, json, cancellationToken);

            if (File.Exists(_storagePath))
            {
                File.Move(tempFilePath, _storagePath, overwrite: true);
            }
            else
            {
                File.Move(tempFilePath, _storagePath);
            }
        }
        finally
        {
            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
            }
        }
    }

    private sealed class FriendFilePayload
    {
        public int Version { get; set; }
        public List<FriendDto>? Friends { get; set; }
    }
}
