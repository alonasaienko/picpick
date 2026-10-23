using WebApp.Models.ZoneA;

namespace WebApp.Controllers.ZoneA;

// Тестові метадані одного процесу; PostgreSQL, файлів і фонового очищення тут немає.
internal static class GalleryDemoStore
{
    internal static readonly object Sync = new();
    internal static readonly Guid PhotographerId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    internal static readonly Dictionary<Guid, Entry> Galleries = Seed();

    internal sealed record Entry(Guid OwnerId, GalleryResponse Gallery);

    internal static GalleryResponse? FindOwned(Guid id, DateTimeOffset now)
    {
        if (!Galleries.TryGetValue(id, out var entry) || entry.OwnerId != PhotographerId) return null;
        if (now >= entry.Gallery.ExpiresAt.AddHours(24))
        {
            Galleries.Remove(id);
            return null;
        }
        return now >= entry.Gallery.ExpiresAt ? entry.Gallery with { Status = "Expired" } : entry.Gallery;
    }

    private static Dictionary<Guid, Entry> Seed()
    {
        var now = DateTimeOffset.UtcNow;
        var result = new Dictionary<Guid, Entry>();
        var states = new[] { "Draft", "Processing", "Ready", "Shared", "Finalized", "Expired" };
        for (var index = 0; index < states.Length; index++)
        {
            var id = Guid.Parse($"00000000-0000-0000-0000-{101 + index:D12}");
            result[id] = new(PhotographerId, new(id, $"Демо {states[index]}", states[index],
                index == 5 ? now.AddHours(-1) : now.AddDays(7), now.AddMinutes(-index), index == 0 ? 0 : 10));
        }
        var foreignId = Guid.Parse("00000000-0000-0000-0000-000000000199");
        result[foreignId] = new(Guid.NewGuid(), new(foreignId, "Чужа галерея", "Ready", now.AddDays(7), now, 10));
        return result;
    }
}
