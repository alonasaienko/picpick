using Microsoft.AspNetCore.Mvc;
using WebApp.Controllers.ZoneA;
using WebApp.Models.ZoneA;

namespace WebApp.Controllers;

// Заглушки без авторизації, PostgreSQL і MinIO; тестові дані скидаються після перезапуску.
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
