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

    // По 10 фото на кожну тестову галерею, крім Draft, — як PhotoCount у GalleryDemoStore зони A.
    // Тому photoCount у деталях галереї і total у прогресі збігаються.
    private static Dictionary<Guid, Entry> Seed()
    {
        var statuses = new (int Gallery, string[] Photos)[]
        {
            (103, new[] { "Ready", "Failed", "Ready", "Ready", "Ready", "Ready", "Ready", "Ready", "Ready", "Ready" }),
            (102, new[] { "Ready", "Ready", "Ready", "Ready", "Processing", "Processing", "Processing", "Uploaded", "Uploaded", "Uploaded" }),
            (104, Enumerable.Repeat("Ready", 10).ToArray()),
            (105, Enumerable.Repeat("Ready", 10).ToArray()),
            (106, Enumerable.Repeat("Ready", 10).ToArray()),
        };
        var result = new Dictionary<Guid, Entry>();
        var next = 201; // 201 — Ready і 202 — Failed у галереї 103 (див. zone-b.md)
        foreach (var (gallery, photos) in statuses)
        {
            var galleryId = Guid.Parse($"00000000-0000-0000-0000-{gallery:D12}");
            foreach (var status in photos)
            {
                var id = Guid.Parse($"00000000-0000-0000-0000-{next:D12}");
                var name = status == "Failed" ? $"IMG_{next - 200:D4}.NEF" : $"IMG_{next - 200:D4}.jpg";
                result[id] = new(galleryId, new(id, name, status, status == "Failed" ? "Файл пошкоджено" : null));
                next++;
            }
        }
        return result;
    }
}
