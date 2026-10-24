using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc;
using WebApp.Controllers;
using WebApp.Infrastructure;

var checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    checks++;
}

foreach (var environment in new[] { "Development", "Production" })
{
    var logs = new TestLogs();
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
    builder.Logging.ClearProviders();
    builder.Logging.AddProvider(logs);
    builder.Services.AddControllers().AddApplicationPart(typeof(GalleriesController).Assembly);
    builder.Services.AddApiErrorHandling();
    await using var app = builder.Build();
    app.Urls.Add("http://127.0.0.1:0");
    app.UseExceptionHandler(page => page.Run(async http =>
    {
        http.Response.StatusCode = 500;
        http.Response.ContentType = "text/html";
        await http.Response.WriteAsync("<p>Помилка сторінки</p>");
    }));
    app.UseRouting();
    app.UseApiErrorHandling();
    app.MapControllers();
    app.MapGet("/page-error", (Func<IResult>)(() => throw new InvalidOperationException("page-only")));
    await app.StartAsync();
    var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
    using var client = new HttpClient { BaseAddress = new Uri(address) };

    async Task<JsonElement> Problem(HttpResponseMessage response, int status, string code)
    {
        var text = await response.Content.ReadAsStringAsync();
        Check((int)response.StatusCode == status, $"{environment}: expected {status}, got {response.StatusCode}: {text}");
        Check(response.Content.Headers.ContentType?.MediaType == "application/problem+json", "Wrong error content type");
        var problem = JsonDocument.Parse(text).RootElement.Clone();
        Check(problem.GetProperty("status").GetInt32() == status, "HTTP and body statuses differ");
        Check(problem.GetProperty("errorCode").GetString() == code, "Wrong errorCode");
        foreach (var field in new[] { "type", "title", "detail", "instance", "traceId" })
            Check(!string.IsNullOrWhiteSpace(problem.GetProperty(field).GetString()), $"Missing {field}");
        Check(problem.GetProperty("instance").GetString()!.StartsWith("urn:request:"), "Unsafe instance");
        Check(!text.Contains("private-marker") && !text.Contains("InvalidOperationException"), "Internal data leaked");
        return problem;
    }

    foreach (var item in new[] { ("missing", 404, "GALLERY_NOT_FOUND"), ("conflict", 409, "GALLERY_NOT_FINALIZABLE"),
        ("invalid", 422, "GALLERY_EXPIRES_AT_INVALID"), ("unexpected", 500, "INTERNAL_ERROR") })
    {
        var errorCount = logs.Messages.Count(message => message.Contains("|Error|"));
        using var response = await client.GetAsync($"/checks/error/{item.Item1}?token=private-marker");
        var problem = await Problem(response, item.Item2, item.Item3);
        Check(problem.TryGetProperty("errors", out _) == (item.Item2 == 422), "Unexpected errors member");
        Check(logs.Messages.Count(message => message.Contains("|Error|")) == errorCount + (item.Item2 == 500 ? 1 : 0),
            "Expected exceptions logged as failures or unexpected exception logged twice");
        if (item.Item2 == 404) Check(problem.GetProperty("title").GetString() == "Ресурс не знайдено", "404 title is zone-specific");
        if (item.Item2 == 500)
        {
            var traceId = problem.GetProperty("traceId").GetString()!;
            Check(logs.Messages.Any(message => message.Contains(traceId) && message.Contains("private-marker")), "Exception not logged with traceId");
            Check(problem.GetProperty("detail").GetString() == "Спробуйте пізніше. Якщо помилка повторюється, повідомте код traceId", "500 message differs from convention");
        }
    }
    foreach (var item in new[] { (400, "VALIDATION_FAILED"), (401, "UNAUTHENTICATED"), (403, "ACCESS_FORBIDDEN"),
        (404, "RESOURCE_NOT_FOUND"), (409, "RESOURCE_CONFLICT"), (415, "UNSUPPORTED_MEDIA_TYPE"),
        (422, "VALIDATION_FAILED"), (429, "RATE_LIMIT_EXCEEDED"), (500, "INTERNAL_ERROR") })
    {
        using var response = await client.GetAsync($"/checks/bare/{item.Item1}");
        await Problem(response, item.Item1, item.Item2);
        if (item.Item1 == 429) Check(response.Headers.GetValues("Retry-After").Single() == "7", "Retry-After was changed");
    }
    using (var response = await client.PostAsJsonAsync("/checks/validate", new { }))
    {
        var problem = await Problem(response, 400, "VALIDATION_FAILED");
        Check(problem.GetProperty("errors").GetProperty("title")[0].GetString() == "Укажіть заголовок.", "Required message lost on another controller");
    }
    using (var response = await client.PostAsJsonAsync("/checks/validate", new { title = "123456" }))
    {
        var problem = await Problem(response, 400, "VALIDATION_FAILED");
        Check(problem.GetProperty("errors").GetProperty("title")[0].GetString() == "Заголовок має містити до 5 символів.", "MaxLength message lost");
    }
    foreach (var body in new[] { "{", "{\"title\":123}", "{\"private-marker\": [" })
    {
        using var response = await client.PostAsync("/checks/validate", new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
        var problem = await Problem(response, 400, "VALIDATION_FAILED");
        Check(problem.GetProperty("errors").EnumerateObject().Any(), "Validation errors missing");
    }
    using (var response = await client.PostAsync("/checks/validate", new StringContent("private-marker")))
        await Problem(response, 415, "UNSUPPORTED_MEDIA_TYPE");
    using (var response = await client.PostAsJsonAsync("/checks/default-validation", new { code = "1234" }))
    {
        var problem = await Problem(response, 400, "VALIDATION_FAILED");
        Check(problem.GetProperty("errors").GetProperty("label")[0].GetString() == "The Label field is required.", "Default ModelState Required message changed");
        Check(problem.GetProperty("errors").GetProperty("code")[0].GetString() == "The field Code must be a string or array type with a maximum length of '3'.", "Default ModelState MaxLength message changed");
    }
    using (var request = new HttpRequestMessage(HttpMethod.Get, "/checks/error/unexpected"))
    {
        request.Headers.Accept.ParseAdd("text/html");
        using var response = await client.SendAsync(request);
        await Problem(response, 500, "INTERNAL_ERROR");
    }
    using (var response = await client.GetAsync("/checks/unsafe-problem"))
        await Problem(response, 500, "INTERNAL_ERROR");
    using (var response = await client.GetAsync("/api/share-links/private-marker?secret=private-marker"))
        await Problem(response, 404, "RESOURCE_NOT_FOUND");
    using (var response = await client.PutAsync("/api/galleries", null))
        await Problem(response, 405, "METHOD_NOT_ALLOWED");
    using (var response = await client.PostAsJsonAsync("/checks/validate", new { title = "ok" }))
        Check(response.IsSuccessStatusCode, "Valid request failed");
    using (var response = await client.GetAsync("/page-error"))
    {
        Check(response.Content.Headers.ContentType?.MediaType == "text/html", "Page error handler replaced by API handler");
        Check((await response.Content.ReadAsStringAsync()).Contains("Помилка сторінки"), "Page fallback missing");
        Check(logs.Messages.Count(message => message.Contains("page-only")) == 1, "Page exception missing or logged twice");
    }
    await app.StopAsync();
}
Console.WriteLine($"Error handling checks passed: {checks}");

[ApiController]
[Route("checks")]
public sealed class ErrorProbeController : ControllerBase
{
    [HttpGet("error/{kind}")]
    public IActionResult Error(string kind) => throw (kind switch
    {
        "missing" => new NotFoundException("GALLERY_NOT_FOUND", "Галерею не знайдено або вона вам недоступна."),
        "conflict" => new ConflictException("GALLERY_NOT_FINALIZABLE", "Фіналізація недоступна."),
        "invalid" => new DomainValidationException("GALLERY_EXPIRES_AT_INVALID", "Перевірте термін.", new() { ["expiresAt"] = ["Дата вже настала."] }),
        _ => (Exception)new InvalidOperationException("private-marker: internal database detail")
    });

    [HttpGet("bare/{status:int}")]
    public IActionResult Bare(int status)
    {
        if (status == 429) Response.Headers.RetryAfter = "7";
        return StatusCode(status);
    }

    [HttpGet("unsafe-problem")]
    public IActionResult UnsafeProblem() => Problem("private-marker", statusCode: 500);

    [HttpPost("validate")]
    public IActionResult Validate(ProbeRequest request) => Ok(request);

    [HttpPost("default-validation")]
    public IActionResult DefaultValidation(DefaultProbeRequest request) => Ok(request);
}

public sealed class ProbeRequest
{
    [Required(ErrorMessage = "Укажіть заголовок.")]
    [MaxLength(5, ErrorMessage = "Заголовок має містити до 5 символів.")]
    public string? Title { get; set; }
}

public sealed class DefaultProbeRequest
{
    [Required]
    public string? Label { get; set; }
    [MaxLength(3)]
    public string? Code { get; set; }
}

internal sealed class TestLogs : ILoggerProvider
{
    internal ConcurrentQueue<string> Messages { get; } = new();
    public ILogger CreateLogger(string categoryName) => new TestLogger(Messages, categoryName);
    public void Dispose() { }

    private sealed class TestLogger(ConcurrentQueue<string> messages, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => messages.Enqueue($"{category}|{logLevel}|" + formatter(state, exception) + exception);
    }
}
