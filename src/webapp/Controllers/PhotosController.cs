using Microsoft.AspNetCore.Mvc;
using WebApp.Controllers.ZoneA;
using WebApp.Controllers.ZoneB;
using WebApp.Infrastructure;
using WebApp.Models.ZoneB;

namespace WebApp.Controllers;

/// <summary>API зони B: завантаження фото, прогрес обробки, список фото і повтор обробки.</summary>
/// <remarks>Заглушки: файли перевіряються, але нікуди не зберігаються; обробки немає. Галереї — тестові дані зони A.</remarks>
[ApiController]
[Route("api/galleries/{galleryId}")]
[ProducesResponseType(typeof(ApiProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
[ProducesResponseType(typeof(ApiProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType(typeof(ApiProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
[ProducesResponseType(typeof(ApiProblemDetails), StatusCodes.Status429TooManyRequests, "application/problem+json")]
[ProducesResponseType(typeof(ApiProblemDetails), StatusCodes.Status500InternalServerError, "application/problem+json")]
public sealed class PhotosController : ControllerBase
{
    private const int MaxFilesPerRequest = 50;
    private const long MaxRequestBytes = 1024L * 1024 * 1024;
    private const int MaxPhotosPerGallery = 1000;

    /// <summary>Завантажити пакет фото в галерею (FR-201, FR-202).</summary>
    /// <param name="galleryId">UUID власної галереї. Демо: …101 Draft, …103 Ready, …104 Shared, …105 Finalized, …999 відсутня.</param>
    /// <param name="request">До 50 файлів і сумарно до 1 ГБ.</param>
    /// <response code="200">Прийнято хоча б один файл. Прийняті фото мають статус Uploaded; відхилені — індекс, ім'я, errorCode і причину.</response>
    /// <response code="400">VALIDATION_FAILED: у запиті немає файлів або неправильний UUID.</response>
    /// <response code="401">UNAUTHENTICATED: фотограф не ввійшов.</response>
    /// <response code="404">GALLERY_NOT_FOUND: галерея відсутня або чужа.</response>
    /// <response code="409">GALLERY_NOT_EDITABLE: галерея Shared, Finalized або Expired.</response>
    /// <response code="422">PHOTO_ALL_FILES_REJECTED: відхилено всі файли, причини в errors["files[i]"]; PHOTO_BATCH_LIMIT_EXCEEDED: понад 50 файлів або 1 ГБ; GALLERY_PHOTO_LIMIT_EXCEEDED: у галереї стало б понад 1000 фото.</response>
    /// <response code="429">RATE_LIMIT_EXCEEDED: перевищено ліміт запитів, Retry-After містить секунди.</response>
    /// <response code="500">INTERNAL_ERROR: загальне повідомлення й traceId.</response>
    [HttpPost("photos")]
    [RequestSizeLimit(MaxRequestBytes + 10 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes + 10 * 1024 * 1024)]
    [ProducesResponseType(typeof(UploadPhotosResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType(typeof(ApiProblemDetails), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public ActionResult<UploadPhotosResponse> Upload(Guid galleryId, [FromForm] UploadPhotosRequest request)
    {
        var files = request.Files;
        if (files.Count == 0)
            return HttpContext.ApiError(400, "VALIDATION_FAILED", "Додайте хоча б один файл.",
                new() { ["files"] = new[] { "Файли відсутні." } });

        lock (GalleryDemoStore.Sync)
        {
            var gallery = GalleryDemoStore.FindOwned(galleryId, DateTimeOffset.UtcNow);
            if (gallery is null) return GalleryNotFound();
            if (gallery.Status is not ("Draft" or "Processing" or "Ready"))
                return GalleryNotEditable();

            if (files.Count > MaxFilesPerRequest || files.Sum(f => f.Length) > MaxRequestBytes)
                return HttpContext.ApiError(422, "PHOTO_BATCH_LIMIT_EXCEEDED",
                    "Один запит може містити до 50 файлів сумарним обсягом до 1 ГБ.");

            var accepted = new List<AcceptedPhoto>();
            var rejected = new List<RejectedFile>();
            for (var i = 0; i < files.Count; i++)
            {
                var problem = PhotoFileCheck.Check(files[i]);
                if (problem is { } p) rejected.Add(new(i, files[i].FileName, p.Code, p.Message));
                else accepted.Add(new(Guid.NewGuid(), files[i].FileName, "Uploaded"));
            }

            if (accepted.Count == 0)
                return HttpContext.ApiError(422, "PHOTO_ALL_FILES_REJECTED",
                    "Жоден файл не прийнято.",
                    rejected.ToDictionary(r => $"files[{r.Index}]", r => new[] { $"{r.FileName}: {r.Message}" }));

            // Ліміт галереї рахується за прийнятими файлами, а не за всіма надісланими (FR-202).
            var current = PhotoDemoStore.ForGallery(galleryId).Count();
            if (current + accepted.Count > MaxPhotosPerGallery)
                return HttpContext.ApiError(422, "GALLERY_PHOTO_LIMIT_EXCEEDED",
                    $"У галереї може бути до 1000 фото. Можна додати ще {MaxPhotosPerGallery - current}.");

            // Сховище змінюється лише після всіх перевірок.
            foreach (var photo in accepted)
                PhotoDemoStore.Photos[photo.PhotoId] = new(galleryId, new(photo.PhotoId, photo.FileName, "Uploaded", null));
            GalleryDemoStore.Galleries[galleryId] = new(GalleryDemoStore.PhotographerId,
                gallery with { Status = "Processing", PhotoCount = gallery.PhotoCount + accepted.Count });

            return Ok(new UploadPhotosResponse(accepted.ToArray(), rejected.ToArray()));
        }
    }

    /// <summary>Отримати прогрес обробки галереї (FR-204).</summary>
    /// <param name="galleryId">UUID власної галереї. Демо: …102 Processing, …103 Ready, …106 Expired.</param>
    /// <response code="200">Кількість фото: усього, готових, з помилкою, в обробці.</response>
    /// <response code="400">VALIDATION_FAILED: неправильний UUID.</response>
    /// <response code="401">UNAUTHENTICATED: фотограф не ввійшов.</response>
    /// <response code="404">GALLERY_NOT_FOUND: галерея відсутня або чужа.</response>
    /// <response code="409">GALLERY_EXPIRED: термін дії настав, дані фото недоступні; опитування слід припинити.</response>
    /// <response code="429">RATE_LIMIT_EXCEEDED: перевищено ліміт запитів, Retry-After містить секунди.</response>
    /// <response code="500">INTERNAL_ERROR: загальне повідомлення й traceId.</response>
    [HttpGet("progress")]
    [ProducesResponseType(typeof(ProcessingProgressResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    public ActionResult<ProcessingProgressResponse> Progress(Guid galleryId)
    {
        lock (GalleryDemoStore.Sync)
        {
            var gallery = GalleryDemoStore.FindOwned(galleryId, DateTimeOffset.UtcNow);
            if (gallery is null) return GalleryNotFound();
            if (gallery.Status == "Expired") return GalleryExpired();

            var photos = PhotoDemoStore.ForGallery(galleryId).ToList();
            var ready = photos.Count(p => p.Status == "Ready");
            var failed = photos.Count(p => p.Status == "Failed");
            return Ok(new ProcessingProgressResponse(gallery.Status, photos.Count, ready, failed, photos.Count - ready - failed));
        }
    }

    /// <summary>Отримати список фото галереї з їхніми статусами (FR-204).</summary>
    /// <param name="galleryId">UUID власної галереї. Демо: …102 Processing, …103 Ready, …106 Expired.</param>
    /// <response code="200">Фото з ім'ям файлу, статусом і причиною для Failed.</response>
    /// <response code="400">VALIDATION_FAILED: неправильний UUID.</response>
    /// <response code="401">UNAUTHENTICATED: фотограф не ввійшов.</response>
    /// <response code="404">GALLERY_NOT_FOUND: галерея відсутня або чужа.</response>
    /// <response code="409">GALLERY_EXPIRED: термін дії настав, список фото недоступний.</response>
    /// <response code="429">RATE_LIMIT_EXCEEDED: перевищено ліміт запитів, Retry-After містить секунди.</response>
    /// <response code="500">INTERNAL_ERROR: загальне повідомлення й traceId.</response>
    [HttpGet("photos")]
    [ProducesResponseType(typeof(PhotoResponse[]), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    public ActionResult<PhotoResponse[]> List(Guid galleryId)
    {
        lock (GalleryDemoStore.Sync)
        {
            var gallery = GalleryDemoStore.FindOwned(galleryId, DateTimeOffset.UtcNow);
            if (gallery is null) return GalleryNotFound();
            if (gallery.Status == "Expired") return GalleryExpired();
            return Ok(PhotoDemoStore.ForGallery(galleryId).ToArray());
        }
    }

    /// <summary>Повторити обробку фото зі статусом Failed (FR-206).</summary>
    /// <param name="galleryId">UUID власної галереї. Демо: …103 Ready.</param>
    /// <param name="photoId">UUID фото. Демо: …202 Failed, …201 Ready.</param>
    /// <response code="200">Фото повернуто в статус Uploaded, задача знову в черзі.</response>
    /// <response code="400">VALIDATION_FAILED: неправильний UUID.</response>
    /// <response code="401">UNAUTHENTICATED: фотограф не ввійшов.</response>
    /// <response code="404">GALLERY_NOT_FOUND або PHOTO_NOT_FOUND: галерея чи фото відсутні або чужі.</response>
    /// <response code="409">PHOTO_NOT_FAILED: фото не у статусі Failed; GALLERY_NOT_EDITABLE: галерея Shared, Finalized або Expired.</response>
    /// <response code="429">RATE_LIMIT_EXCEEDED: перевищено ліміт запитів, Retry-After містить секунди.</response>
    /// <response code="500">INTERNAL_ERROR: загальне повідомлення й traceId.</response>
    [HttpPost("photos/{photoId}/retry")]
    [ProducesResponseType(typeof(PhotoResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    public ActionResult<PhotoResponse> Retry(Guid galleryId, Guid photoId)
    {
        lock (GalleryDemoStore.Sync)
        {
            var gallery = GalleryDemoStore.FindOwned(galleryId, DateTimeOffset.UtcNow);
            if (gallery is null) return GalleryNotFound();
            if (!PhotoDemoStore.Photos.TryGetValue(photoId, out var entry) || entry.GalleryId != galleryId)
                return HttpContext.ApiError(404, "PHOTO_NOT_FOUND",
                    "Фото не знайдено або воно вам недоступне.");
            if (gallery.Status is not ("Processing" or "Ready"))
                return GalleryNotEditable();
            if (entry.Photo.Status != "Failed")
                return HttpContext.ApiError(409, "PHOTO_NOT_FAILED",
                    "Повторити можна лише обробку фото зі статусом Failed.");

            var retried = entry.Photo with { Status = "Uploaded", FailureReason = null };
            PhotoDemoStore.Photos[photoId] = entry with { Photo = retried };
            GalleryDemoStore.Galleries[galleryId] = new(GalleryDemoStore.PhotographerId, gallery with { Status = "Processing" });
            return Ok(retried);
        }
    }

    private ObjectResult GalleryNotFound() => HttpContext.ApiError(404, "GALLERY_NOT_FOUND",
        "Галерею не знайдено або вона вам недоступна.");

    private ObjectResult GalleryNotEditable() => HttpContext.ApiError(409, "GALLERY_NOT_EDITABLE",
        "Галерея зі станом Shared, Finalized або Expired не дозволяє змінювати фото.");

    private ObjectResult GalleryExpired() => HttpContext.ApiError(409, "GALLERY_EXPIRED",
        "Термін дії галереї завершився, дані фото недоступні.");
}
