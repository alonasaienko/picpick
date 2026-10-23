namespace WebApp.Models.ZoneA;
public sealed record GalleryResponse(
    Guid GalleryId, string Name, string Status,
    DateTimeOffset ExpiresAt, DateTimeOffset CreatedAt, int PhotoCount);
public sealed record FinalizeGalleryResponse(GalleryResponse Gallery, string Message);
