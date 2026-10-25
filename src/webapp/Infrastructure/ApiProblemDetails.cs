using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace WebApp.Infrastructure;

public sealed class ApiProblemDetails : ProblemDetails
{
    public required string ErrorCode { get; init; }
    public required string TraceId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string[]>? Errors { get; init; }
}
