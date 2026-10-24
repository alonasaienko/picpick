using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;

namespace WebApp.Infrastructure;

public static class ErrorHandlingExtensions
{
    public static IServiceCollection AddApiErrorHandling(this IServiceCollection services)
    {
        services.AddProblemDetails();
        services.AddExceptionHandler<ApiExceptionHandler>();
        services.AddLogging(logging => logging.AddFilter("Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware", LogLevel.None));
        services.Configure<JsonOptions>(options => options.AllowInputFormatterExceptionMessages = false);
        services.Configure<ApiBehaviorOptions>(options =>
            options.InvalidModelStateResponseFactory = ValidationResponse);
        services.Configure<MvcOptions>(options =>
        {
            options.Filters.Add<ApiErrorResultFilter>();
            options.ModelMetadataDetailsProviders.Add(new SystemTextJsonValidationMetadataProvider());
            options.ModelBindingMessageProvider.SetAttemptedValueIsInvalidAccessor((_, _) => "Перевірте формат значення поля.");
            options.ModelBindingMessageProvider.SetNonPropertyAttemptedValueIsInvalidAccessor(_ => "Перевірте формат значення.");
        });
        return services;
    }

    public static IApplicationBuilder UseApiErrorHandling(this IApplicationBuilder app)
    {
        return app.UseWhen(context =>
            {
                var isApi = context.Request.Path.StartsWithSegments("/api")
                    || context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>() is not null;
                if (isApi) context.Items[typeof(ApiExceptionHandler)] = true;
                return isApi;
            },
            api =>
            {
                api.UseExceptionHandler(new ExceptionHandlerOptions { AllowStatusCode404Response = true });
                api.UseStatusCodePages(async statusContext =>
                {
                    var http = statusContext.HttpContext;
                    var problem = CreateProblem(http, http.Response.StatusCode);
                    await http.Response.WriteAsJsonAsync(problem, options: (JsonSerializerOptions?)null, contentType: "application/problem+json",
                        cancellationToken: http.RequestAborted);
                });
            });
    }

    public static ObjectResult ApiError(this HttpContext http, int status, string code, string detail,
        Dictionary<string, string[]>? errors = null) => Result(CreateProblem(http, status, code, detail, errors));

    internal static ApiProblemDetails CreateProblem(HttpContext http, int status, string? code = null,
        string? detail = null, Dictionary<string, string[]>? errors = null)
    {
        var (section, title, defaultCode, defaultDetail) = status switch
        {
            400 => ("15.5.1", "Некоректний запит", "VALIDATION_FAILED", "Перевірте дані запиту."),
            401 => ("15.5.2", "Потрібен вхід", "UNAUTHENTICATED", "Увійдіть в акаунт фотографа."),
            403 => ("15.5.4", "Дію заборонено", "ACCESS_FORBIDDEN", "Ви не маєте дозволу на цю дію."),
            404 => ("15.5.5", "Ресурс не знайдено", "RESOURCE_NOT_FOUND", "Ресурс не знайдено або він вам недоступний."),
            405 => ("15.5.6", "Метод не дозволено", "METHOD_NOT_ALLOWED", "Цей метод не підтримується за вказаною адресою."),
            406 => ("15.5.7", "Формат відповіді не підтримується", "NOT_ACCEPTABLE", "Запитайте відповідь у форматі JSON."),
            409 => ("15.5.10", "Конфлікт стану ресурсу", "RESOURCE_CONFLICT", "Поточний стан ресурсу не дозволяє дію."),
            415 => ("15.5.16", "Непідтримуваний тип вмісту", "UNSUPPORTED_MEDIA_TYPE", "Передайте тіло запиту як application/json."),
            422 => ("15.5.21", "Дані не відповідають правилам", "VALIDATION_FAILED", "Перевірте значення полів."),
            429 => ("", "Забагато запитів", "RATE_LIMIT_EXCEEDED", "Перевищено ліміт запитів. Повторіть пізніше."),
            _ => ("15.6.1", "Внутрішня помилка сервера", "INTERNAL_ERROR", "Спробуйте пізніше. Якщо помилка повторюється, повідомте код traceId")
        };
        var traceId = Activity.Current?.Id ?? http.TraceIdentifier;
        return new ApiProblemDetails
        {
            Status = status,
            Type = status == 429 ? "https://tools.ietf.org/html/rfc6585#section-4"
                : $"https://tools.ietf.org/html/rfc9110#section-{section}",
            Title = title,
            Detail = status >= 500 ? defaultDetail : detail ?? defaultDetail,
            Instance = $"urn:request:{traceId}",
            ErrorCode = status >= 500 ? "INTERNAL_ERROR" : code ?? defaultCode,
            TraceId = traceId,
            Errors = status is 400 or 422 ? errors : null
        };
    }

    internal static ObjectResult Result(ApiProblemDetails problem)
    {
        var result = new ObjectResult(problem) { StatusCode = problem.Status };
        result.ContentTypes.Add("application/problem+json");
        return result;
    }

    private static IActionResult ValidationResponse(ActionContext context)
    {
        var errors = new Dictionary<string, string[]>();
        foreach (var entry in context.ModelState.Where(p => p.Value!.Errors.Count > 0))
        {
            var name = string.IsNullOrEmpty(entry.Key) || entry.Key.StartsWith('$')
                ? "body" : JsonNamingPolicy.CamelCase.ConvertName(entry.Key);
            var messages = entry.Value!.Errors.Select(error => error.Exception is null && !string.IsNullOrWhiteSpace(error.ErrorMessage)
                ? error.ErrorMessage : "Передайте коректне тіло JSON з правильними типами значень.");
            errors[name] = errors.GetValueOrDefault(name, Array.Empty<string>()).Concat(messages).Distinct().ToArray();
        }
        return context.HttpContext.ApiError(400, "VALIDATION_FAILED", "Перевірте обов'язкові поля, типи та довжину значень.", errors);
    }

}

internal sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken cancellationToken)
    {
        if (!http.Items.ContainsKey(typeof(ApiExceptionHandler)))
        {
            logger.LogError(exception, "Помилка сторінки. TraceId: {TraceId}", Activity.Current?.Id ?? http.TraceIdentifier);
            return false;
        }
        var expected = exception as ApiException;
        var problem = ErrorHandlingExtensions.CreateProblem(http, expected?.StatusCode ?? 500,
            expected?.ErrorCode, expected?.Message, expected?.Errors);
        if (expected is null)
            logger.LogError(exception, "Помилка API. TraceId: {TraceId}", problem.TraceId);
        http.Response.StatusCode = problem.Status!.Value;
        await http.Response.WriteAsJsonAsync(problem, options: (JsonSerializerOptions?)null,
            contentType: "application/problem+json", cancellationToken: cancellationToken);
        return true;
    }
}

internal sealed class ApiErrorResultFilter : IAlwaysRunResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        if (context.ActionDescriptor is not ControllerActionDescriptor) return;
        var status = context.Result switch
        {
            ObjectResult result => result.StatusCode ?? (result.Value as ProblemDetails)?.Status,
            StatusCodeResult result => result.StatusCode,
            _ => null
        };
        if (status is null or < 400) return;
        var original = (context.Result as ObjectResult)?.Value as ProblemDetails;
        var typed = original as ApiProblemDetails;
        var code = typed?.ErrorCode;
        if (code is null && original?.Extensions.TryGetValue("errorCode", out var extensionCode) == true)
            code = extensionCode?.ToString();
        var errors = typed?.Errors ?? (original as ValidationProblemDetails)?.Errors;
        context.Result = ErrorHandlingExtensions.Result(ErrorHandlingExtensions.CreateProblem(context.HttpContext,
            status.Value, code, original?.Detail, errors is null ? null : new(errors)));
    }

    public void OnResultExecuted(ResultExecutedContext context) { }
}
