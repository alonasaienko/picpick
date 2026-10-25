using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WebApp.Controllers.ZoneA;
using WebApp.Models.ZoneA;
using WebApp.Infrastructure;

namespace WebApp.Controllers;

/// <summary>API зони A: створення, перегляд, фіналізація та видалення власних галерей.</summary>
/// <remarks>Заглушки для одного Photographer без авторизації, PostgreSQL і MinIO; дані в пам'яті до перезапуску.</remarks>
[ApiController]
[Route("api/galleries")]
[ZoneADemo]
[EnableRateLimiting(RateLimitPolicies.Default)]
[ProducesResponseType(typeof(ApiProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
[ProducesResponseType(typeof(ApiProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType(typeof(ApiProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
[ProducesResponseType(typeof(ApiProblemDetails), StatusCodes.Status429TooManyRequests, "application/problem+json")]
[ProducesResponseType(typeof(ApiProblemDetails), StatusCodes.Status500InternalServerError, "application/problem+json")]
public sealed class GalleriesController : ControllerBase
{
    /// <summary>Створити власну галерею у статусі Draft без фото.</summary>
    /// <param name="request">Назва й майбутній термін дії не далі ніж через 30 діб. Власника визначає сервер.</param>
    /// <param name="demoError">Лише Development: 401, 403, 415, 500. Для звичайного запиту залишити порожнім.</param>
    /// <returns>Метадані нової Gallery і заголовок Location з адресою деталей.</returns>
    /// <response code="201">Створено Draft, photoCount = 0. Location містить адресу деталей.</response>
    /// <response code="400">VALIDATION_FAILED: некоректний JSON або тип; відсутні поля, порожня назва чи понад 100 символів. errors пояснює помилки полів.</response>
    /// <response code="401">UNAUTHENTICATED: фотограф не ввійшов або сесія нечинна. Заглушка: demoError=401.</response>
    /// <response code="403">GALLERY_ACTION_FORBIDDEN: користувач не має дозволу на дію. Заглушка: demoError=403.</response>
    /// <response code="415">UNSUPPORTED_MEDIA_TYPE: тіло запиту не JSON. Демонстрація у Swagger: demoError=415.</response>
    /// <response code="422">GALLERY_EXPIRES_AT_INVALID: expiresAt уже настав або перевищує 30 діб. Причина в errors.expiresAt.</response>
    /// <response code="429">RATE_LIMIT_EXCEEDED: перевищено ліміт частоти, Retry-After містить секунди. Реальна політика default: 120 запитів за хвилину на IP.</response>
    /// <response code="500">INTERNAL_ERROR: загальне повідомлення й traceId без внутрішніх даних. Заглушка: demoError=500.</response>
    [HttpPost]
    [ProducesResponseType(typeof(GalleryResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiProblemDetails), StatusCodes.Status415UnsupportedMediaType, "application/problem+json")]
    [ProducesResponseType(typeof(ApiProblemDetails), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public ActionResult<GalleryResponse> Create([FromBody] CreateGalleryRequest request, [FromQuery] string? demoError = null)
    {
        var now = DateTimeOffset.UtcNow;
        var expiresAt = request.ExpiresAt!.Value.ToUniversalTime();
        if (expiresAt <= now || expiresAt > now.AddDays(30))
            throw new DomainValidationException("GALLERY_EXPIRES_AT_INVALID",
                "Термін дії має бути в майбутньому й не далі ніж через 30 діб.",
                new() { ["expiresAt"] = new[] { "Укажіть допустимий майбутній момент завершення терміну." } });

        var gallery = new GalleryResponse(Guid.NewGuid(), request.Name!, "Draft", expiresAt, now, 0);
        lock (GalleryDemoStore.Sync)
            GalleryDemoStore.Galleries.Add(gallery.GalleryId, new(GalleryDemoStore.PhotographerId, gallery));
        return CreatedAtAction(nameof(Get), new { id = gallery.GalleryId }, gallery);
    }
    /// <summary>Отримати список власних галерей, новіші першими.</summary>
    /// <param name="demoError">Лише Development: 401, 403, 500. Для звичайного запиту залишити порожнім.</param>
    /// <returns>Масив метаданих власних галерей, зокрема порожній.</returns>
    /// <response code="200">Метадані власних галерей за createdAt від новіших до старіших; при однаковому часі — galleryId за зростанням.</response>
    /// <response code="400">VALIDATION_FAILED: невідомий demoError або демонстраційний параметр поза Development.</response>
    /// <response code="401">UNAUTHENTICATED: фотограф не ввійшов або сесія нечинна. Заглушка: demoError=401.</response>
    /// <response code="403">GALLERY_ACTION_FORBIDDEN: користувач не має дозволу на дію. Заглушка: demoError=403.</response>
    /// <response code="429">RATE_LIMIT_EXCEEDED: перевищено ліміт частоти, Retry-After містить секунди. Реальна політика default: 120 запитів за хвилину на IP.</response>
    /// <response code="500">INTERNAL_ERROR: загальне повідомлення й traceId без внутрішніх даних. Заглушка: demoError=500.</response>
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
    /// <summary>Отримати метадані власної галереї.</summary>
    /// <param name="id">UUID Gallery. Демо: 00000000-0000-0000-0000-000000000103 — Ready; 199 замість 103 — чужа, 999 — відсутня.</param>
    /// <param name="demoError">Лише Development: 401, 403, 500. Для звичайного запиту залишити порожнім.</param>
    /// <returns>Метадані власної Gallery без фото й вибору.</returns>
    /// <response code="200">Метадані власної Gallery; після expiresAt — Expired до очищення, не довше 24 годин.</response>
    /// <response code="400">VALIDATION_FAILED: неправильний UUID або demoError. errors пояснює помилку.</response>
    /// <response code="401">UNAUTHENTICATED: фотограф не ввійшов або сесія нечинна. Заглушка: demoError=401.</response>
    /// <response code="403">GALLERY_ACTION_FORBIDDEN: користувач не має дозволу на дію. Заглушка: demoError=403. Чужа Gallery повертає 404.</response>
    /// <response code="404">GALLERY_NOT_FOUND: галерея відсутня, видалена або чужа; однакове повідомлення без чужих даних.</response>
    /// <response code="429">RATE_LIMIT_EXCEEDED: перевищено ліміт частоти, Retry-After містить секунди. Реальна політика default: 120 запитів за хвилину на IP.</response>
    /// <response code="500">INTERNAL_ERROR: загальне повідомлення й traceId без внутрішніх даних. Заглушка: demoError=500.</response>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(GalleryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    public ActionResult<GalleryResponse> Get(Guid id, [FromQuery] string? demoError = null)
    {
        lock (GalleryDemoStore.Sync)
        {
            var gallery = GalleryDemoStore.FindOwned(id, DateTimeOffset.UtcNow);
            if (gallery is null) throw new NotFoundException("GALLERY_NOT_FOUND", "Галерею не знайдено або вона вам недоступна.");
            return Ok(gallery);
        }
    }
    /// <summary>Фіналізувати чинну Ready або Shared; повтор Finalized також успішний.</summary>
    /// <param name="id">UUID власної Gallery.</param>
    /// <param name="demoError">Лише Development: 401, 403, 500. Для звичайного запиту залишити порожнім.</param>
    /// <returns>Gallery зі статусом Finalized і повідомлення про результат.</returns>
    /// <response code="200">Finalized у gallery та повідомлення message. Повтор повертає «Галерею вже фіналізовано» без зміни даних.</response>
    /// <response code="400">VALIDATION_FAILED: неправильний UUID або demoError. errors пояснює помилку.</response>
    /// <response code="401">UNAUTHENTICATED: фотограф не ввійшов або сесія нечинна. Заглушка: demoError=401.</response>
    /// <response code="403">GALLERY_ACTION_FORBIDDEN: користувач не має дозволу на дію. Заглушка: demoError=403. Чужа Gallery повертає 404.</response>
    /// <response code="404">GALLERY_NOT_FOUND: галерея відсутня, видалена або чужа; однакове загальне повідомлення.</response>
    /// <response code="409">GALLERY_NOT_FINALIZABLE: Draft, Processing або Expired не дозволяють фіналізацію. Повтор чинної Finalized повертає 200.</response>
    /// <response code="429">RATE_LIMIT_EXCEEDED: перевищено ліміт частоти, Retry-After містить секунди. Реальна політика default: 120 запитів за хвилину на IP.</response>
    /// <response code="500">INTERNAL_ERROR: загальне повідомлення й traceId без внутрішніх даних. Заглушка: demoError=500.</response>
    [HttpPost("{id}/finalize")]
    [ProducesResponseType(typeof(FinalizeGalleryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType(typeof(ApiProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    public ActionResult<FinalizeGalleryResponse> FinalizeGallery(Guid id, [FromQuery] string? demoError = null)
    {
        lock (GalleryDemoStore.Sync)
        {
            var gallery = GalleryDemoStore.FindOwned(id, DateTimeOffset.UtcNow);
            if (gallery is null) throw new NotFoundException("GALLERY_NOT_FOUND", "Галерею не знайдено або вона вам недоступна.");
            if (gallery.Status is not ("Ready" or "Shared" or "Finalized"))
                throw new ConflictException("GALLERY_NOT_FINALIZABLE",
                    "Фіналізація доступна лише для чинної галереї Ready або Shared.");
            var repeated = gallery.Status == "Finalized";
            var finalized = gallery with { Status = "Finalized" };
            GalleryDemoStore.Galleries[id] = new(GalleryDemoStore.PhotographerId, finalized);
            return Ok(new FinalizeGalleryResponse(finalized,
                repeated ? "Галерею вже фіналізовано" : "Галерею фіналізовано"));
        }
    }
    /// <summary>Видалити власну галерею в будь-якому статусі.</summary>
    /// <param name="id">UUID власної Gallery.</param>
    /// <param name="demoError">Лише Development: 401, 403, 500. Для звичайного запиту залишити порожнім.</param>
    /// <returns>204 без тіла після видалення тестових метаданих.</returns>
    /// <response code="204">Тестові метадані видалено; тіла відповіді немає.</response>
    /// <response code="400">VALIDATION_FAILED: неправильний UUID або demoError. errors пояснює помилку.</response>
    /// <response code="401">UNAUTHENTICATED: фотограф не ввійшов або сесія нечинна. Заглушка: demoError=401.</response>
    /// <response code="403">GALLERY_ACTION_FORBIDDEN: користувач не має дозволу на дію. Заглушка: demoError=403. Чужа Gallery повертає 404.</response>
    /// <response code="404">GALLERY_NOT_FOUND: галерея відсутня, уже видалена або чужа; однакове загальне повідомлення.</response>
    /// <response code="429">RATE_LIMIT_EXCEEDED: перевищено ліміт частоти, Retry-After містить секунди. Реальна політика default: 120 запитів за хвилину на IP.</response>
    /// <response code="500">INTERNAL_ERROR: загальне повідомлення й traceId без внутрішніх даних. Заглушка: demoError=500.</response>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    public IActionResult Delete(Guid id, [FromQuery] string? demoError = null)
    {
        lock (GalleryDemoStore.Sync)
        {
            if (GalleryDemoStore.FindOwned(id, DateTimeOffset.UtcNow) is null)
                throw new NotFoundException("GALLERY_NOT_FOUND", "Галерею не знайдено або вона вам недоступна.");
            GalleryDemoStore.Galleries.Remove(id);
            return NoContent();
        }
    }
}
