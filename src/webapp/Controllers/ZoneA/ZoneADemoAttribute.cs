using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using WebApp.Models.ZoneA;

namespace WebApp.Controllers.ZoneA;
[AttributeUsage(AttributeTargets.Class)]
public sealed class ZoneADemoAttribute : ActionFilterAttribute
{
    private static readonly HashSet<string> ValidationMessages = typeof(CreateGalleryRequest)
        .GetProperties().SelectMany(p => p.GetCustomAttributes<ValidationAttribute>())
        .Select(a => a.ErrorMessage).OfType<string>().ToHashSet(StringComparer.Ordinal);

    public ZoneADemoAttribute() => Order = -4000;
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
                .ToDictionary(g => g.Key, g => g.SelectMany(p => p.Value!.Errors)
                    .Select(e => ValidationMessage(g.Key, e)).Distinct().ToArray());
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
        return name.ToLowerInvariant() switch
        {
            "name" => "name", "expiresat" => "expiresAt", "id" => "id",
            "demoerror" => "demoError", _ => "body"
        };
    }

    private static string ValidationMessage(string field, ModelError error)
    {
        if (ValidationMessages.Contains(error.ErrorMessage)) return error.ErrorMessage;
        return field switch
        {
            "name" => "Передайте назву галереї як текст довжиною 1–100 символів.",
            "expiresAt" => "Укажіть дату ISO 8601 з часовим поясом, наприклад 2026-12-01T12:00:00Z.",
            "id" => "Укажіть ідентифікатор галереї у форматі UUID.",
            _ => "Передайте коректний JSON із полями name та expiresAt."
        };
    }

    private static Microsoft.AspNetCore.Mvc.ObjectResult RateLimited(HttpContext http)
    {
        http.Response.Headers.RetryAfter = "30";
        return GalleryProblems.Create(http, 429, "RATE_LIMIT_EXCEEDED", "Перевищено ліміт запитів. Повторіть через 30 секунд.");
    }
}
