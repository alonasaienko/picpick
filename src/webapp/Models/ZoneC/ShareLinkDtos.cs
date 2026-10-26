namespace WebApp.Models.ZoneC;

/// <summary>Параметри створення посилання для клієнта.</summary>
public class CreateShareLinkRequest
{
    /// <summary>Необов'язковий термін дії посилання в UTC.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }
}

/// <summary>Створене посилання для клієнта (ShareLink).</summary>
public class CreateShareLinkResponse
{
    /// <summary>Ідентифікатор посилання.</summary>
    public Guid ShareLinkId { get; set; }

    /// <summary>Ідентифікатор галереї.</summary>
    public Guid GalleryId { get; set; }

    /// <summary>Секретний токен. Передається фотографу лише тут і ніколи не потрапляє в помилки.</summary>
    public string Token { get; set; } = "";

    /// <summary>Відносна адреса сторінки клієнта.</summary>
    public string Url { get; set; } = "";

    /// <summary>Стан посилання: <c>Active</c>.</summary>
    public string State { get; set; } = "Active";

    /// <summary>Дата, після якої посилання перестає діяти.</summary>
    public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>Результат відкликання посилання.</summary>
public class RevokeShareLinkResponse
{
    /// <summary>Ідентифікатор посилання.</summary>
    public Guid ShareLinkId { get; set; }

    /// <summary>Ідентифікатор галереї.</summary>
    public Guid GalleryId { get; set; }

    /// <summary>Стан посилання: <c>Revoked</c>.</summary>
    public string State { get; set; } = "Revoked";
}