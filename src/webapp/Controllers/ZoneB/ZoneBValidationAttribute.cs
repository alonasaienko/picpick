using Microsoft.AspNetCore.Mvc.Filters;
using WebApp.Controllers.ZoneA;

namespace WebApp.Controllers.ZoneB;

// Перетворює помилки прив'язки (неправильний UUID тощо) на 400 VALIDATION_FAILED за конвенцією.
// Запускається раніше за вбудовану перевірку [ApiController], як ZoneADemoAttribute.
[AttributeUsage(AttributeTargets.Class)]
public sealed class ZoneBValidationAttribute : ActionFilterAttribute
{
    public ZoneBValidationAttribute() => Order = -4000;

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.ModelState.IsValid) return;
        var errors = context.ModelState.Where(p => p.Value?.Errors.Count > 0)
            .ToDictionary(p => p.Key, p => new[] { "Некоректне значення." });
        context.Result = GalleryProblems.Create(context.HttpContext, 400, "VALIDATION_FAILED",
            "Перевірте параметри запиту.", errors);
    }
}
