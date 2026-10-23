namespace WebApp.Models.ZoneA;

/// <summary>Метадані власної Gallery, без фото, вибору й даних інших фотографів.</summary>
public sealed record GalleryResponse(
    Guid GalleryId, string Name, string Status,
    DateTimeOffset ExpiresAt, DateTimeOffset CreatedAt, int PhotoCount);

/// <summary>Результат першої або повторної фіналізації.</summary>
public sealed record FinalizeGalleryResponse(GalleryResponse Gallery, string Message);
