namespace WebApp.Models.ZoneC;

/// <summary>Стан лайка після запиту.</summary>
public class SetLikeResponse
{
    /// <summary>Ідентифікатор фото.</summary>
    public Guid PhotoId { get; set; }

    /// <summary>Чи кадр тепер обраний.</summary>
    public bool Liked { get; set; }

    /// <summary>Загальна кількість лайків у виборі.</summary>
    public int LikedCount { get; set; }
}

/// <summary>Одна зміна лайка для синхронізації пристроїв.</summary>
public class LikeChange
{
    public Guid PhotoId { get; set; }
    public bool Liked { get; set; }
    public long OpSeq { get; set; }
}

/// <summary>Зміни лайків після курсора.</summary>
public class LikeChangesResponse
{
    public long OpSeq { get; set; }
    public List<LikeChange> Changes { get; set; } = new();
}

/// <summary>Результат відправки вибору.</summary>
public class SubmitSelectionResponse
{
    /// <summary>Статус вибору: <c>Submitted</c>.</summary>
    public string Status { get; set; } = "Submitted";

    /// <summary>Момент відправки.</summary>
    public DateTimeOffset SubmittedAt { get; set; }

    /// <summary>Кількість обраних кадрів.</summary>
    public int LikedCount { get; set; }

    /// <summary>Ідентифікатори обраних фото.</summary>
    public List<Guid> LikedPhotoIds { get; set; } = new();
}

/// <summary>Вибір клієнта для фотографа.</summary>
public class SelectionResponse
{
    /// <summary>Ідентифікатор галереї.</summary>
    public Guid GalleryId { get; set; }

    /// <summary>Статус вибору: <c>Open</c> або <c>Submitted</c>.</summary>
    public string Status { get; set; } = "";

    /// <summary>Момент відправки; <c>null</c>, поки вибір <c>Open</c>.</summary>
    public DateTimeOffset? SubmittedAt { get; set; }

    /// <summary>Кількість обраних кадрів.</summary>
    public int Count { get; set; }

    /// <summary>Обрані кадри.</summary>
    public List<SelectionItem> Items { get; set; } = new();
}

/// <summary>Один обраний кадр.</summary>
public class SelectionItem
{
    /// <summary>Ідентифікатор фото.</summary>
    public Guid PhotoId { get; set; }

    /// <summary>Ім'я файлу оригіналу.</summary>
    public string FileName { get; set; } = "";

    /// <summary>Тимчасова адреса прев'ю.</summary>
    public string PreviewUrl { get; set; } = "";
}