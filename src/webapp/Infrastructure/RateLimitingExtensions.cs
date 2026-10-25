using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace WebApp.Infrastructure;

/// <summary>Назви політик обмеження частоти для атрибута [EnableRateLimiting].</summary>
public static class RateLimitPolicies
{
    /// <summary>Завантаження фото: token bucket, до 20 запитів поспіль, далі 10 на хвилину.</summary>
    public const string Upload = "upload";

    /// <summary>Звичайні запити API: sliding window, 120 запитів за хвилину.</summary>
    public const string Default = "default";
}

public static class RateLimitingExtensions
{
    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Token bucket дозволяє короткий сплеск: браузер надсилає великий пакет кількома запитами поспіль (FR-201).
            // 20 токенів = 20 запитів по 50 файлів = галерея на 1000 фото за один раз; далі 10 запитів на хвилину.
            options.AddPolicy(RateLimitPolicies.Upload, http => RateLimitPartition.GetTokenBucketLimiter(
                PartitionKey(http), _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = 20,
                    TokensPerPeriod = 10,
                    ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true
                }));

            // Sliding window рівномірно обмежує звичайні запити без сплеску на межі вікон.
            // Опитування прогресу раз на 5 с — це 12 запитів на хвилину на вкладку; 120 дає запас на кілька вкладок і переходи.
            options.AddPolicy(RateLimitPolicies.Default, http => RateLimitPartition.GetSlidingWindowLimiter(
                PartitionKey(http), _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = 120,
                    Window = TimeSpan.FromMinutes(1),
                    SegmentsPerWindow = 6,
                    QueueLimit = 0
                }));

            options.OnRejected = async (context, cancellationToken) =>
            {
                var http = context.HttpContext;
                var seconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                    ? (int)Math.Ceiling(retryAfter.TotalSeconds) : 60;
                http.Response.Headers.RetryAfter = seconds.ToString();
                var problem = ErrorHandlingExtensions.CreateProblem(http, 429, "RATE_LIMIT_EXCEEDED",
                    $"Перевищено ліміт запитів. Повторіть через {seconds} с.");
                await http.Response.WriteAsJsonAsync(problem, options: (JsonSerializerOptions?)null,
                    contentType: "application/problem+json", cancellationToken: cancellationToken);
            };
        });
        return services;
    }

    // У прототипі входу ще немає, тому ліміт рахується за IP-адресою.
    // У цільовій системі для фотографа ключем буде його ідентифікатор, для клієнта — посилання.
    private static string PartitionKey(HttpContext http) =>
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
