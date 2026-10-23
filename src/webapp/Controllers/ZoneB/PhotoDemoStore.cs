using WebApp.Models.ZoneB;

namespace WebApp.Controllers.ZoneB;

// Тестові фото в пам'яті процесу; MinIO, RabbitMQ і Worker тут немає.
// Використовує той самий lock, що й тестові галереї зони A (GalleryDemoStore.Sync).
internal static class PhotoDemoStore
{
    internal sealed record Entry(Guid GalleryId, PhotoResponse Photo);

    internal static readonly Dictionary<Guid, Entry> Photos = Seed();

    internal static IEnumerable<PhotoResponse> ForGallery(Guid galleryId) =>
        Photos.Values.Where(e => e.GalleryId == galleryId).Select(e => e.Photo);

    private static Dictionary<Guid, Entry> Seed()
    {
        var processing = Guid.Parse("00000000-0000-0000-0000-000000000102");
        var ready = Guid.Parse("00000000-0000-0000-0000-000000000103");
        var seed = new (Guid Gallery, string Name, string Status, string? Reason)[]
        {
            (ready, "IMG_0001.jpg", "Ready", null),
            (ready, "IMG_0002.NEF", "Failed", "Файл пошкоджено"),
            (processing, "IMG_0003.jpg", "Processing", null),
            (processing, "IMG_0004.jpg", "Uploaded", null),
        };
        var result = new Dictionary<Guid, Entry>();
        for (var i = 0; i < seed.Length; i++)
        {
            var id = Guid.Parse($"00000000-0000-0000-0000-{201 + i:D12}");
            result[id] = new(seed[i].Gallery, new(id, seed[i].Name, seed[i].Status, seed[i].Reason));
        }
        return result;
    }
}
