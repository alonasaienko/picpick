using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace WebApp.Controllers.ZoneA;

/// <summary>Локальне оформлення помилок заглушок до появи інфраструктури #23.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ZoneADemoAttribute : ActionFilterAttribute
{
    /// <summary>Виконується перед автоматичною відповіддю ApiController (-2000).</summary>
    public ZoneADemoAttribute() => Order = -4000;

    /// <inheritdoc />
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        var http = context.HttpContext;
        if (!context.ModelState.IsValid)
        {
            if (context.ModelState.Values.SelectMany(v => v.Errors)
                .Any(e => e.Exception is UnsupportedContentTypeException))
            {
                context.Result = GalleryProblems.Create(http, 415, "UNSUPPORTED_MEDIA_TYPE",
                    "Передайте тіло запиту як application/json.");
                return;
            }

            var errors = context.ModelState.Where(p => p.Value?.Errors.Count > 0)
                .GroupBy(p => FieldName(p.Key))
                .ToDictionary(g => g.Key, g => new[] { "Поле відсутнє або не відповідає контракту запиту." });
            context.Result = GalleryProblems.Create(http, 400, "VALIDATION_FAILED",
                "Перевірте обов'язкові поля, типи та довжину значень.", errors);
            return;
        }

        var demo = http.Request.Query["demoError"].ToString();
        if (string.IsNullOrEmpty(demo)) return;
        if (!http.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment())
        {
            context.Result = GalleryProblems.Create(http, 400, "VALIDATION_FAILED",
                "Демонстраційний параметр доступний лише в Development.",
                new() { ["demoError"] = new[] { "Приберіть демонстраційний параметр." } });
            return;
        }
        context.Result = demo switch
        {
            "401" => GalleryProblems.Create(http, 401, "UNAUTHENTICATED", "Увійдіть в акаунт фотографа."),
            "403" => GalleryProblems.Create(http, 403, "GALLERY_ACTION_FORBIDDEN", "Ця дія недоступна поточному користувачу."),
            "415" when context.ActionDescriptor.RouteValues["action"] == "Create" =>
                GalleryProblems.Create(http, 415, "UNSUPPORTED_MEDIA_TYPE", "Передайте тіло запиту як application/json."),
            "429" => RateLimited(http),
            "500" => GalleryProblems.Create(http, 500, "INTERNAL_ERROR", "Не вдалося виконати запит. Повторіть пізніше."),
            _ => GalleryProblems.Create(http, 400, "VALIDATION_FAILED", "Невідомий демонстраційний сценарій.",
                new() { ["demoError"] = new[] { "Допустимі значення: 401, 403, 429, 500; для створення також 415." } })
        };
    }

    private static string FieldName(string key)
    {
        var name = key.StartsWith("$.", StringComparison.Ordinal) ? key[2..] : key;
        // Не повертаємо довільні імена властивостей із зламаного JSON.
        return name.ToLowerInvariant() switch
        {
            "name" => "name", "expiresat" => "expiresAt", "id" => "id",
            "demoerror" => "demoError", _ => "body"
        };
    }

    private static Microsoft.AspNetCore.Mvc.ObjectResult RateLimited(HttpContext http)
    {
        http.Response.Headers.RetryAfter = "30";
        return GalleryProblems.Create(http, 429, "RATE_LIMIT_EXCEEDED", "Перевищено ліміт запитів. Повторіть через 30 секунд.");
    }
}
