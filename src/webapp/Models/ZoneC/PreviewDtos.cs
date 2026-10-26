namespace WebApp.Models.ZoneC;

/// <summary>Галерея для клієнта: прев'ю з позначками лайків.</summary>
public class SharePreviewsResponse
{
    /// <summary>Назва галереї.</summary>
    public string GalleryName { get; set; } = "";

    /// <summary>Нікнейм фотографа (email не розкривається).</summary>
    public string Nickname { get; set; } = "";

    /// <summary>Статус галереї: <c>Shared</c> або <c>Finalized</c>.</summary>
    public string GalleryStatus { get; set; } = "";

    /// <summary>Статус вибору: <c>Open</c> або <c>Submitted</c>.</summary>
    public string SelectionStatus { get; set; } = "";

    /// <summary>Дата завершення терміну дії посилання.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Кількість кадрів із лайком.</summary>
    public int LikedCount { get; set; }

    /// <summary>Прев'ю з вотермаркою; оригінали клієнту не показуються.</summary>
    public List<SharePreviewItem> Previews { get; set; } = new();
}

/// <summary>Одне прев'ю в галереї клієнта.</summary>
public class SharePreviewItem
{
    /// <summary>Ідентифікатор фото.</summary>
    public Guid PhotoId { get; set; }

    /// <summary>Тимчасова адреса прев'ю (без внутрішніх шляхів сховища).</summary>
    public string PreviewUrl { get; set; } = "";

    /// <summary>Чи поставив клієнт лайк.</summary>
    public bool Liked { get; set; }
}