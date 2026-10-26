using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using WebApp.Infrastructure;
using WebApp.Models.ZoneC;
using ProblemDetails = WebApp.Infrastructure.ApiProblemDetails;

namespace WebApp.Controllers;

/// <summary>API зони C: посилання для клієнта, прев'ю, лайки й відправка вибору.</summary>
[ApiController]
[EnableRateLimiting(RateLimitPolicies.Default)]
public class ShareController : ControllerBase
{
    private enum DemoLink { Open, Empty, Submitted, Finalized, Expired, Revoked }

    private static Guid Id(int n) => new($"00000000-0000-0000-0000-{n:D12}");

    private static readonly Dictionary<string, DemoLink> DemoTokens = new()
    {
        ["demo-open"] = DemoLink.Open,
        ["demo-empty"] = DemoLink.Empty,
        ["demo-submitted"] = DemoLink.Submitted,
        ["demo-finalized"] = DemoLink.Finalized,
        ["demo-expired"] = DemoLink.Expired,
        ["demo-revoked"] = DemoLink.Revoked,
    };

    private static readonly Dictionary<Guid, string> DemoGalleries = new()
    {
        [Id(101)] = "Draft",
        [Id(102)] = "Processing",
        [Id(103)] = "Ready",
        [Id(104)] = "Shared",
        [Id(105)] = "Finalized",
        [Id(106)] = "Expired",
    };

    private static readonly Dictionary<Guid, string> DemoShareLinks = new()
    {
        [Id(401)] = "Active",
        [Id(402)] = "Revoked",
        [Id(403)] = "Expired",
    };

    private static readonly Guid[] ReadyPhotos = { Id(301), Id(302), Id(303) };

    /// <summary>Створити посилання для клієнта (FR-301).</summary>
    /// <param name="galleryId">UUID галереї фотографа.</param>
    /// <param name="request">Необов'язковий термін дії посилання в UTC.</param>
    /// <response code="201">Посилання створено, стан галереї Ready змінено на Shared.</response>
    /// <response code="400">VALIDATION_FAILED: некоректний UUID або JSON.</response>
    /// <response code="401">UNAUTHENTICATED: фотограф не ввійшов.</response>
    /// <response code="404">GALLERY_NOT_FOUND: галерея відсутня або чужа.</response>
    /// <response code="409">GALLERY_NOT_SHAREABLE або GALLERY_EXPIRED: поточний стан галереї не дозволяє створити посилання.</response>
    /// <response code="422">SHARE_LINK_EXPIRES_AT_INVALID: термін дії не відповідає правилам галереї.</response>
    /// <response code="429">RATE_LIMIT_EXCEEDED: перевищено ліміт запитів, Retry-After містить секунди.</response>
    /// <response code="500">INTERNAL_ERROR: загальне повідомлення й traceId.</response>
    [HttpPost("api/galleries/{galleryId}/share-links")]
    [ProducesResponseType(typeof(CreateShareLinkResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError, "application/problem+json")]
    public IActionResult CreateShareLink(Guid galleryId, [FromBody] CreateShareLinkRequest? request)
    {
        var status = EnsureGallery(galleryId);
        if (status is "Draft" or "Processing" or "Finalized")
        {
            throw new ConflictException("GALLERY_NOT_SHAREABLE",
                "Посилання можна створити лише для галереї зі станом Ready або Shared.");
        }

        if (status == "Expired")
        {
            throw new ConflictException("GALLERY_EXPIRED",
                "Термін дії галереї минув, посилання створити не можна.");
        }

        var now = DateTimeOffset.UtcNow;
        var galleryExpiresAt = now.AddDays(30);
        var expiresAt = request?.ExpiresAt ?? Min(now.AddDays(14), galleryExpiresAt);
        if (expiresAt <= now || expiresAt > galleryExpiresAt)
        {
            throw new DomainValidationException(
                "SHARE_LINK_EXPIRES_AT_INVALID",
                "Термін дії посилання має бути пізніше поточного часу й не пізніше терміну дії галереї.",
                new Dictionary<string, string[]>
                {
                    ["expiresAt"] = new[] { "Укажіть майбутню дату не пізніше терміну дії галереї." }
                });
        }

        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var shareLinkId = Id(401);
        if (status == "Shared")
        {
            DemoShareLinks[Id(401)] = "Revoked";
            shareLinkId = Id(404);
            DemoShareLinks[shareLinkId] = "Active";
        }

        return StatusCode(StatusCodes.Status201Created, new CreateShareLinkResponse
        {
            ShareLinkId = shareLinkId,
            GalleryId = galleryId,
            Token = token,
            Url = $"/share/{token}",
            State = "Active",
            ExpiresAt = expiresAt.ToUniversalTime(),
        });
    }

    /// <summary>Відкликати посилання для клієнта (FR-302).</summary>
    /// <param name="galleryId">UUID власної галереї фотографа.</param>
    /// <param name="shareLinkId">UUID посилання в цій галереї.</param>
    /// <response code="200">Посилання має стан Revoked; повторне відкликання є успішним.</response>
    /// <response code="400">VALIDATION_FAILED: некоректний UUID.</response>
    /// <response code="401">UNAUTHENTICATED: фотограф не ввійшов.</response>
    /// <response code="404">GALLERY_NOT_FOUND або SHARE_LINK_NOT_FOUND: галерею чи посилання не знайдено.</response>
    /// <response code="409">SHARE_LINK_NOT_ACTIVE: посилання прострочене й уже не може бути відкликане.</response>
    /// <response code="429">RATE_LIMIT_EXCEEDED: перевищено ліміт запитів, Retry-After містить секунди.</response>
    /// <response code="500">INTERNAL_ERROR: загальне повідомлення й traceId.</response>
    [HttpPost("api/galleries/{galleryId}/share-links/{shareLinkId}/revoke")]
    [ProducesResponseType(typeof(RevokeShareLinkResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError, "application/problem+json")]
    public IActionResult RevokeShareLink(Guid galleryId, Guid shareLinkId)
    {
        EnsureGallery(galleryId);
        if (galleryId != Id(104))
        {
            throw new NotFoundException("SHARE_LINK_NOT_FOUND",
                "Посилання не знайдено в цій галереї.");
        }

        if (!DemoShareLinks.TryGetValue(shareLinkId, out var state))
        {
            throw new NotFoundException("SHARE_LINK_NOT_FOUND",
                "Посилання не знайдено в цій галереї.");
        }

        if (state == "Expired")
        {
            throw new ConflictException("SHARE_LINK_NOT_ACTIVE",
                "Прострочене посилання не можна відкликати.");
        }

        return Ok(new RevokeShareLinkResponse
        {
            ShareLinkId = shareLinkId,
            GalleryId = galleryId,
            State = "Revoked",
        });
    }

    /// <summary>Отримати прев'ю галереї за посиланням (FR-303).</summary>
    /// <param name="token">Секретний токен клієнтського посилання.</param>
    /// <response code="200">Назва галереї, нікнейм фотографа, статус вибору та прев'ю зі статусом Ready.</response>
    /// <response code="404">SHARE_LINK_INVALID: токен невідомий, прострочений або відкликаний.</response>
    /// <response code="429">RATE_LIMIT_EXCEEDED: перевищено ліміт запитів, Retry-After містить секунди.</response>
    /// <response code="500">INTERNAL_ERROR: загальне повідомлення й traceId.</response>
    [HttpGet("api/share-links/{token}/previews")]
    [ProducesResponseType(typeof(SharePreviewsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError, "application/problem+json")]
    public IActionResult GetPreviews(string token)
    {
        var link = FindLink(token);
        EnsureValidLink(link);
        var liked = InitialLikes(link);

        return Ok(new SharePreviewsResponse
        {
            GalleryName = "Весілля Олени та Андрія",
            Nickname = "Олена Фото",
            GalleryStatus = link == DemoLink.Finalized ? "Finalized" : "Shared",
            SelectionStatus = link == DemoLink.Submitted ? "Submitted" : "Open",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(14),
            LikedCount = liked.Count,
            Previews = ReadyPhotos.Select(id => new SharePreviewItem
            {
                PhotoId = id,
                PreviewUrl = $"https://storage.example.com/previews/{id}.jpg?signature=demo",
                Liked = liked.Contains(id),
            }).ToList(),
        });
    }

    /// <summary>Поставити лайк на прев'ю (FR-304).</summary>
    /// <param name="token">Секретний токен клієнтського посилання.</param>
    /// <param name="photoId">UUID фото зі статусом Ready.</param>
    /// <response code="200">Лайк встановлено; повторний запит є ідемпотентним.</response>
    /// <response code="400">VALIDATION_FAILED: photoId має неправильний формат.</response>
    /// <response code="404">SHARE_LINK_INVALID або PHOTO_NOT_FOUND.</response>
    /// <response code="409">SELECTION_ALREADY_SUBMITTED або GALLERY_FINALIZED: лайк більше не можна змінити.</response>
    /// <response code="429">RATE_LIMIT_EXCEEDED: перевищено ліміт запитів, Retry-After містить секунди.</response>
    /// <response code="500">INTERNAL_ERROR: загальне повідомлення й traceId.</response>
    [HttpPut("api/share-links/{token}/likes/{photoId}")]
    [ProducesResponseType(typeof(SetLikeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError, "application/problem+json")]
    public IActionResult SetLike(string token, Guid photoId)
    {
        return ChangeLike(token, photoId, true);
    }

    /// <summary>Зняти лайк із прев'ю (FR-304).</summary>
    /// <param name="token">Секретний токен клієнтського посилання.</param>
    /// <param name="photoId">UUID фото зі статусом Ready.</param>
    /// <response code="200">Лайк знято; зняття відсутнього лайка також є успішним.</response>
    /// <response code="400">VALIDATION_FAILED: photoId має неправильний формат.</response>
    /// <response code="404">SHARE_LINK_INVALID або PHOTO_NOT_FOUND.</response>
    /// <response code="409">SELECTION_ALREADY_SUBMITTED або GALLERY_FINALIZED: лайк більше не можна змінити.</response>
    /// <response code="429">RATE_LIMIT_EXCEEDED: перевищено ліміт запитів, Retry-After містить секунди.</response>
    /// <response code="500">INTERNAL_ERROR: загальне повідомлення й traceId.</response>
    [HttpDelete("api/share-links/{token}/likes/{photoId}")]
    [ProducesResponseType(typeof(SetLikeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError, "application/problem+json")]
    public IActionResult RemoveLike(string token, Guid photoId)
    {
        return ChangeLike(token, photoId, false);
    }

    /// <summary>Отримати зміни лайків після курсора для синхронізації пристроїв (FR-304).</summary>
    /// <param name="token">Секретний токен клієнтського посилання.</param>
    /// <param name="since">Необов'язковий невід'ємний номер останньої отриманої операції.</param>
    /// <response code="200">Зміни лайків після since і поточний номер операції.</response>
    /// <response code="400">VALIDATION_FAILED: since має неправильний формат.</response>
    /// <response code="404">SHARE_LINK_INVALID: токен недійсний.</response>
    /// <response code="429">RATE_LIMIT_EXCEEDED: перевищено ліміт запитів, Retry-After містить секунди.</response>
    /// <response code="500">INTERNAL_ERROR: загальне повідомлення й traceId.</response>
    [HttpGet("api/share-links/{token}/likes")]
    [ProducesResponseType(typeof(LikeChangesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError, "application/problem+json")]
    public IActionResult GetLikeChanges(string token, long since = 0)
    {
        var link = FindLink(token);
        EnsureValidLink(link);
        if (since < 0)
        {
            throw new DomainValidationException(
                "VALIDATION_FAILED",
                "Курсор since не може бути від'ємним.",
                new Dictionary<string, string[]>
                {
                    ["since"] = new[] { "Укажіть невід'ємний номер операції." }
                });
        }

        var changes = InitialLikes(link)
            .Select((photoId, index) => new LikeChange
            {
                PhotoId = photoId,
                Liked = true,
                OpSeq = index + 1,
            })
            .Where(change => change.OpSeq > since)
            .ToList();

        return Ok(new LikeChangesResponse
        {
            OpSeq = InitialLikes(link).Count,
            Changes = changes,
        });
    }

    /// <summary>Відправити вибір клієнта фотографу (FR-305).</summary>
    /// <param name="token">Секретний токен клієнтського посилання.</param>
    /// <response code="200">Вибір має статус Submitted; повторна відправка повертає той самий результат.</response>
    /// <response code="404">SHARE_LINK_INVALID: токен недійсний.</response>
    /// <response code="409">SELECTION_EMPTY або GALLERY_FINALIZED.</response>
    /// <response code="429">RATE_LIMIT_EXCEEDED: перевищено ліміт запитів, Retry-After містить секунди.</response>
    /// <response code="500">INTERNAL_ERROR: загальне повідомлення й traceId.</response>
    [HttpPost("api/share-links/{token}/selection/submit")]
    [ProducesResponseType(typeof(SubmitSelectionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError, "application/problem+json")]
    public IActionResult SubmitSelection(string token)
    {
        var link = FindLink(token);
        EnsureValidLink(link);
        var liked = InitialLikes(link);

        if (link == DemoLink.Finalized)
        {
            throw new ConflictException("GALLERY_FINALIZED",
                "Фотограф завершив відбір у цій галереї, вибір більше не приймається.");
        }

        if (link == DemoLink.Submitted)
        {
            return Ok(new SubmitSelectionResponse
            {
                Status = "Submitted",
                SubmittedAt = SubmittedAt,
                LikedCount = liked.Count,
                LikedPhotoIds = liked.OrderBy(id => id).ToList(),
            });
        }

        if (liked.Count == 0)
        {
            throw new ConflictException("SELECTION_EMPTY", "Спочатку оберіть хоча б один кадр.");
        }

        return Ok(new SubmitSelectionResponse
        {
            Status = "Submitted",
            SubmittedAt = SubmittedAt,
            LikedCount = liked.Count,
            LikedPhotoIds = liked.OrderBy(id => id).ToList(),
        });
    }

    /// <summary>Переглянути список обраних кадрів для фотографа (FR-306).</summary>
    /// <param name="galleryId">UUID власної галереї фотографа.</param>
    /// <response code="200">Статус вибору та список обраних кадрів.</response>
    /// <response code="400">VALIDATION_FAILED: некоректний UUID.</response>
    /// <response code="401">UNAUTHENTICATED: фотограф не ввійшов.</response>
    /// <response code="404">GALLERY_NOT_FOUND або SELECTION_NOT_FOUND.</response>
    /// <response code="409">GALLERY_EXPIRED: галерея прострочена.</response>
    /// <response code="429">RATE_LIMIT_EXCEEDED: перевищено ліміт запитів, Retry-After містить секунди.</response>
    /// <response code="500">INTERNAL_ERROR: загальне повідомлення й traceId.</response>
    [HttpGet("api/galleries/{galleryId}/selection")]
    [ProducesResponseType(typeof(SelectionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError, "application/problem+json")]
    public IActionResult GetSelection(Guid galleryId)
    {
        var status = EnsureGallery(galleryId);
        if (status == "Expired")
        {
            throw new ConflictException("GALLERY_EXPIRED", "Термін дії галереї минув.");
        }

        if (status is "Draft" or "Processing" or "Ready")
        {
            throw new NotFoundException("SELECTION_NOT_FOUND",
                "Для цієї галереї ще немає вибору: посилання для клієнта не створено.");
        }

        var submitted = status == "Finalized";
        var items = new[] { Id(301), Id(302) }.Select(id => new SelectionItem
        {
            PhotoId = id,
            FileName = $"IMG_{id.ToString()[^3..]}.jpg",
            PreviewUrl = $"https://storage.example.com/previews/{id}.jpg?signature=demo",
        }).ToList();

        return Ok(new SelectionResponse
        {
            GalleryId = galleryId,
            Status = submitted ? "Submitted" : "Open",
            SubmittedAt = submitted ? SubmittedAt : null,
            Count = items.Count,
            Items = items,
        });
    }

    private IActionResult ChangeLike(string token, Guid photoId, bool likedValue)
    {
        var link = FindLink(token);
        EnsureValidLink(link);
        if (!ReadyPhotos.Contains(photoId))
        {
            throw new NotFoundException("PHOTO_NOT_FOUND", "Фото не знайдено.");
        }

        if (link == DemoLink.Submitted)
        {
            throw new ConflictException("SELECTION_ALREADY_SUBMITTED",
                "Вибір уже відправлено, змінювати його не можна.");
        }

        if (link == DemoLink.Finalized)
        {
            throw new ConflictException("GALLERY_FINALIZED",
                "Фотограф завершив відбір у цій галереї, лайки більше не приймаються.");
        }

        var liked = InitialLikes(link);
        if (likedValue)
        {
            liked.Add(photoId);
        }
        else
        {
            liked.Remove(photoId);
        }

        return Ok(new SetLikeResponse
        {
            PhotoId = photoId,
            Liked = likedValue,
            LikedCount = liked.Count,
        });
    }

    private static readonly DateTimeOffset SubmittedAt =
        new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private static string EnsureGallery(Guid galleryId)
    {
        if (!DemoGalleries.TryGetValue(galleryId, out var status))
        {
            throw new NotFoundException("GALLERY_NOT_FOUND",
                "Галерею не знайдено або вона вам недоступна.");
        }

        return status;
    }

    private static DemoLink FindLink(string token)
    {
        if (!DemoTokens.TryGetValue(token, out var link))
        {
            throw new NotFoundException("SHARE_LINK_INVALID",
                "Це посилання більше не діє. Зверніться до вашого фотографа по нове.");
        }

        return link;
    }

    private static void EnsureValidLink(DemoLink link)
    {
        if (link is DemoLink.Expired or DemoLink.Revoked)
        {
            throw new NotFoundException("SHARE_LINK_INVALID",
                "Це посилання більше не діє. Зверніться до вашого фотографа по нове.");
        }
    }

    private static HashSet<Guid> InitialLikes(DemoLink link) =>
        link == DemoLink.Empty
            ? new HashSet<Guid>()
            : new HashSet<Guid> { Id(301), Id(302) };

    private static DateTimeOffset Min(DateTimeOffset first, DateTimeOffset second) =>
        first <= second ? first : second;
}
