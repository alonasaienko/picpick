using Microsoft.AspNetCore.Mvc;
using WebApp.Controllers.ZoneA;
using WebApp.Models.ZoneA;

namespace WebApp.Controllers;

/// <summary>Демонстраційний API зони A для одного фіксованого Photographer.</summary>
/// <remarks>Заглушки без авторизації, PostgreSQL і MinIO. Дані скидаються після перезапуску.</remarks>
[ApiController]
[Route("api/galleries")]
[ZoneADemo]
[ProducesResponseType(typeof(GalleryProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
[ProducesResponseType(typeof(GalleryProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType(typeof(GalleryProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
[ProducesResponseType(typeof(GalleryProblemDetails), StatusCodes.Status429TooManyRequests, "application/problem+json")]
[ProducesResponseType(typeof(GalleryProblemDetails), StatusCodes.Status500InternalServerError, "application/problem+json")]
public sealed class GalleriesController : ControllerBase
{
    /// <summary>A-101: створити власну Gallery у статусі Draft без фото.</summary>
    /// <param name="request">Назва та майбутній термін дії не далі ніж через 30 діб.</param>
    /// <param name="demoError">Лише Development: 401, 403, 415, 429 або 500; інакше залишити порожнім.</param>
    /// <response code="201">Метадані створеної Gallery; Location веде до деталей.</response>
    /// <response code="400">Некоректний JSON, відсутні поля або назва поза межами 1–100 символів.</response>
    /// <response code="401">Потрібен вхід фотографа (демонстрація).</response>
    /// <response code="403">Поточному користувачу заборонено дію (демонстрація).</response>
    /// <response code="415">Тіло не application/json.</response>
    /// <response code="422">Термін уже настав або перевищує 30 діб.</response>
    /// <response code="429">Ліміт частоти перевищено, Retry-After у секундах (демонстрація).</response>
    /// <response code="500">Загальна помилка сервера (демонстрація).</response>
    [HttpPost]
    [ProducesResponseType(typeof(GalleryResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(GalleryProblemDetails), StatusCodes.Status415UnsupportedMediaType, "application/problem+json")]
    [ProducesResponseType(typeof(GalleryProblemDetails), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public ActionResult<GalleryResponse> Create([FromBody] CreateGalleryRequest request, [FromQuery] string? demoError = null)
    {
        var now = DateTimeOffset.UtcNow;
        var expiresAt = request.ExpiresAt!.Value.ToUniversalTime();
        if (expiresAt <= now || expiresAt > now.AddDays(30))
            return GalleryProblems.Create(HttpContext, 422, "GALLERY_EXPIRES_AT_INVALID",
                "Термін дії має бути в майбутньому й не далі ніж через 30 діб.",
                new() { ["expiresAt"] = new[] { "Укажіть допустимий майбутній момент завершення терміну." } });

        var gallery = new GalleryResponse(Guid.NewGuid(), request.Name!, "Draft", expiresAt, now, 0);
        lock (GalleryDemoStore.Sync)
            GalleryDemoStore.Galleries.Add(gallery.GalleryId, new(GalleryDemoStore.PhotographerId, gallery));
        return CreatedAtAction(nameof(Get), new { id = gallery.GalleryId }, gallery);
    }

    /// <summary>A-102: список лише власних галерей, від новіших до старіших.</summary>
    /// <param name="demoError">Лише Development: 401, 403, 429 або 500.</param>
    /// <response code="200">Масив метаданих, зокрема порожній.</response>
    [HttpGet]
    [ProducesResponseType(typeof(GalleryResponse[]), StatusCodes.Status200OK)]
    public ActionResult<GalleryResponse[]> List([FromQuery] string? demoError = null)
    {
        lock (GalleryDemoStore.Sync)
        {
            var now = DateTimeOffset.UtcNow;
            return Ok(GalleryDemoStore.Galleries.Keys.ToArray()
                .Select(id => GalleryDemoStore.FindOwned(id, now)).OfType<GalleryResponse>()
                .OrderByDescending(g => g.CreatedAt)
                .ThenBy(g => g.GalleryId.ToString("D"), StringComparer.Ordinal).ToArray());
        }
    }

    /// <summary>A-103: метадані власної Gallery, включно з Expired до очищення.</summary>
    /// <param name="id">UUID галереї; тестові ідентифікатори наведено в zone-a.md.</param>
    /// <param name="demoError">Лише Development: 401, 403, 429 або 500.</param>
    /// <response code="200">Метадані без фото й вибору.</response>
    /// <response code="404">Галереї немає або вона чужа; відповідь однакова.</response>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(GalleryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(GalleryProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    public ActionResult<GalleryResponse> Get(Guid id, [FromQuery] string? demoError = null)
    {
        lock (GalleryDemoStore.Sync)
        {
            var gallery = GalleryDemoStore.FindOwned(id, DateTimeOffset.UtcNow);
            return gallery is null ? GalleryProblems.NotFound(HttpContext) : Ok(gallery);
        }
    }

    /// <summary>A-104: фіналізувати Ready/Shared; повтор Finalized також успішний.</summary>
    /// <param name="id">UUID власної галереї.</param>
    /// <param name="demoError">Лише Development: 401, 403, 429 або 500.</param>
    /// <response code="200">Finalized і повідомлення про першу або повторну фіналізацію.</response>
    /// <response code="404">Галереї немає або вона чужа.</response>
    /// <response code="409">Draft, Processing або Expired не дозволяють фіналізацію.</response>
    [HttpPost("{id}/finalize")]
    [ProducesResponseType(typeof(FinalizeGalleryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(GalleryProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType(typeof(GalleryProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    public ActionResult<FinalizeGalleryResponse> FinalizeGallery(Guid id, [FromQuery] string? demoError = null)
    {
        lock (GalleryDemoStore.Sync)
        {
            var gallery = GalleryDemoStore.FindOwned(id, DateTimeOffset.UtcNow);
            if (gallery is null) return GalleryProblems.NotFound(HttpContext);
            if (gallery.Status is not ("Ready" or "Shared" or "Finalized"))
                return GalleryProblems.Create(HttpContext, 409, "GALLERY_NOT_FINALIZABLE",
                    "Фіналізація доступна лише для чинної галереї Ready або Shared.");
            var repeated = gallery.Status == "Finalized";
            var finalized = gallery with { Status = "Finalized" };
            GalleryDemoStore.Galleries[id] = new(GalleryDemoStore.PhotographerId, finalized);
            return Ok(new FinalizeGalleryResponse(finalized,
                repeated ? "Галерею вже фіналізовано" : "Галерею фіналізовано"));
        }
    }

    /// <summary>A-105: видалити власну тестову Gallery в будь-якому статусі.</summary>
    /// <param name="id">UUID власної галереї.</param>
    /// <param name="demoError">Лише Development: 401, 403, 429 або 500.</param>
    /// <response code="204">Тестові метадані видалено; тіла немає.</response>
    /// <response code="404">Галереї немає, вже видалена або чужа.</response>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(GalleryProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    public IActionResult Delete(Guid id, [FromQuery] string? demoError = null)
    {
        lock (GalleryDemoStore.Sync)
        {
            if (GalleryDemoStore.FindOwned(id, DateTimeOffset.UtcNow) is null)
                return GalleryProblems.NotFound(HttpContext);
            GalleryDemoStore.Galleries.Remove(id);
            return NoContent();
        }
    }
}
