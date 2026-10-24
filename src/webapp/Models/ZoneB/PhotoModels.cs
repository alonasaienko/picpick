using Microsoft.AspNetCore.Mvc;

namespace WebApp.Models.ZoneB;

/// <summary>Пакет файлів для завантаження: до 50 файлів і сумарно до 1 ГБ.</summary>
public sealed class UploadPhotosRequest
{
    /// <summary>Файли JPEG, PNG, HEIC або RAW (DNG, CR2, CR3, NEF, ARW, RAF).</summary>
    [FromForm(Name = "files")]
    public List<IFormFile> Files { get; set; } = new();
}

/// <summary>Результат завантаження: прийняті фото й відхилені файли (FR-201, FR-202).</summary>
public sealed record UploadPhotosResponse(AcceptedPhoto[] Accepted, RejectedFile[] Rejected);

/// <summary>Прийняте фото зі статусом Uploaded.</summary>
public sealed record AcceptedPhoto(Guid PhotoId, string FileName, string Status);

/// <summary>Відхилений файл: індекс у запиті, ім'я, errorCode і причина.</summary>
public sealed record RejectedFile(int Index, string FileName, string ErrorCode, string Message);

/// <summary>Прогрес обробки галереї (FR-204).</summary>
public sealed record ProcessingProgressResponse(string GalleryStatus, int Total, int Ready, int Failed, int InProgress);

/// <summary>Фото галереї з його статусом; причина заповнена лише для Failed.</summary>
public sealed record PhotoResponse(Guid PhotoId, string FileName, string Status, string? FailureReason);
