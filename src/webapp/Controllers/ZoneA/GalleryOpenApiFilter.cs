using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace WebApp.Controllers.ZoneA;

public sealed class GalleryOpenApiFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.MethodInfo.DeclaringType != typeof(GalleriesController)) return;

        operation.Summary = context.MethodInfo.Name switch
        {
            "Create" => "Створити власну галерею у статусі Draft без фото",
            "List" => "Отримати список власних галерей, новіші першими",
            "Get" => "Отримати метадані власної галереї",
            "FinalizeGallery" => "Фіналізувати Ready/Shared; повтор Finalized також успішний",
            "Delete" => "Видалити власну галерею в будь-якому статусі",
            _ => operation.Summary
        };
        operation.Description = "Заглушка для одного фіксованого Photographer; дані в пам’яті до перезапуску. "
            + "Авторизацію не реалізовано. Помилки — Problem Details за conventions.md.";

        var descriptions = new Dictionary<string, string>
        {
            ["200"] = "Успішно: метадані або список власних галерей.",
            ["201"] = "Створено Draft, photoCount = 0. Location містить адресу деталей.",
            ["204"] = "Тестові метадані видалено; тіла відповіді немає.",
            ["400"] = "VALIDATION_FAILED: некоректний JSON, тип або UUID; відсутні поля, порожня назва чи понад 100 символів. errors містить пояснення для кожного поля.",
            ["401"] = "UNAUTHENTICATED: фотограф не ввійшов або сесія нечинна. У заглушці: demoError=401.",
            ["403"] = "GALLERY_ACTION_FORBIDDEN: користувач бачить ресурс, але не має дозволу на дію. У заглушці: demoError=403. Чужа галерея повертає 404.",
            ["404"] = "GALLERY_NOT_FOUND: галерея не існує, видалена або чужа; однакове загальне повідомлення без чужих даних.",
            ["409"] = "GALLERY_NOT_FINALIZABLE: фіналізація Draft, Processing або Expired заборонена. Повтор чинної Finalized повертає 200.",
            ["415"] = "UNSUPPORTED_MEDIA_TYPE: тіло запиту не JSON. Для демонстрації у Swagger: demoError=415.",
            ["422"] = "GALLERY_EXPIRES_AT_INVALID: expiresAt уже настав або перевищує 30 діб від серверного часу. Причина — в errors.expiresAt.",
            ["429"] = "RATE_LIMIT_EXCEEDED: перевищено ліміт частоти; Retry-After містить секунди. Заглушка demoError=429 повертає Retry-After: 30.",
            ["500"] = "INTERNAL_ERROR: загальне повідомлення й traceId без внутрішніх даних. У заглушці: demoError=500; виняток не генерується."
        };
        if (context.MethodInfo.Name == "FinalizeGallery")
            descriptions["200"] = "Finalized у полі gallery та повідомлення message. Повтор повертає «Галерею вже фіналізовано» без зміни даних.";
        foreach (var response in operation.Responses)
            if (descriptions.TryGetValue(response.Key, out var description))
                response.Value.Description = description;

        foreach (var parameter in operation.Parameters)
            parameter.Description = parameter.Name switch
            {
                "demoError" => "Лише Development: 401, 403, 429, 500"
                    + (context.MethodInfo.Name == "Create" ? ", також 415" : "")
                    + ". Для звичайного запиту залиште порожнім.",
                "id" => "UUID власної Gallery. Демо: 00000000-0000-0000-0000-000000000103 — Ready; 199 замість 103 — чужа, 999 — відсутня.",
                _ => parameter.Description
            };
        if (operation.RequestBody is not null)
            operation.RequestBody.Description = "name: 1–100 символів після видалення крайніх пробілів; expiresAt: дата ISO 8601 з часовим поясом у майбутньому, не далі ніж через 30 діб. Власника задає сервер.";
    }
}
