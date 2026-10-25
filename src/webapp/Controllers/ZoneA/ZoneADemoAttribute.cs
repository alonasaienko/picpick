using Microsoft.AspNetCore.Mvc.Filters;
using WebApp.Infrastructure;

namespace WebApp.Controllers.ZoneA;
[AttributeUsage(AttributeTargets.Class)]
public sealed class ZoneADemoAttribute : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        var http = context.HttpContext;
        var demo = http.Request.Query["demoError"].ToString();
        if (string.IsNullOrEmpty(demo)) return;
        if (!http.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment())
        {
            context.Result = http.ApiError(400, "VALIDATION_FAILED",
                "Демонстраційний параметр доступний лише в Development.",
                new() { ["demoError"] = new[] { "Приберіть демонстраційний параметр." } });
            return;
        }
        context.Result = demo switch
        {
            "401" => http.ApiError(401, "UNAUTHENTICATED", "Увійдіть в акаунт фотографа."),
            "403" => http.ApiError(403, "GALLERY_ACTION_FORBIDDEN", "Ця дія недоступна поточному користувачу."),
            "415" when context.ActionDescriptor.RouteValues["action"] == "Create" =>
                http.ApiError(415, "UNSUPPORTED_MEDIA_TYPE", "Передайте тіло запиту як application/json."),
            "500" => throw new InvalidOperationException("Демонстраційний виняток зони A."),
            _ => http.ApiError(400, "VALIDATION_FAILED", "Невідомий демонстраційний сценарій.",
                new() { ["demoError"] = new[] { "Допустимі значення: 401, 403, 500; для створення також 415." } })
        };
    }

}
