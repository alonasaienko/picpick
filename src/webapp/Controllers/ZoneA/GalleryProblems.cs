using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using WebApp.Models.ZoneA;

namespace WebApp.Controllers.ZoneA;

// Локально для заглушок #21. Спільна інфраструктура винятків належить #23.
internal static class GalleryProblems
{
    internal static ObjectResult Create(HttpContext context, int status, string code,
        string detail, Dictionary<string, string[]>? errors = null)
    {
        var section = status switch
        {
            400 => "15.5.1", 401 => "15.5.2", 403 => "15.5.4", 404 => "15.5.5",
            409 => "15.5.10", 415 => "15.5.16", 422 => "15.5.21", _ => "15.6.1"
        };
        var title = status switch
        {
            400 => "Некоректний запит", 401 => "Потрібен вхід", 403 => "Дію заборонено",
            404 => "Галерею не знайдено", 409 => "Конфлікт стану галереї",
            415 => "Непідтримуваний тип вмісту", 422 => "Дані не відповідають правилам",
            429 => "Забагато запитів", _ => "Внутрішня помилка сервера"
        };
        var problem = new GalleryProblemDetails
        {
            Type = status == 429 ? "https://tools.ietf.org/html/rfc6585#section-4"
                : $"https://tools.ietf.org/html/rfc9110#section-{section}",
            Title = title, Status = status, Detail = detail,
            Instance = context.Request.Path,
            ErrorCode = code,
            TraceId = Activity.Current?.Id ?? context.TraceIdentifier,
            Errors = errors
        };
        var result = new ObjectResult(problem) { StatusCode = status };
        result.ContentTypes.Add("application/problem+json");
        return result;
    }

    internal static ObjectResult NotFound(HttpContext context) =>
        Create(context, 404, "GALLERY_NOT_FOUND", "Галерею не знайдено або вона вам недоступна.");
}
