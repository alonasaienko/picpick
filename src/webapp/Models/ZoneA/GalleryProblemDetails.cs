using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace WebApp.Models.ZoneA;
public sealed class GalleryProblemDetails : ProblemDetails
{
    public required string ErrorCode { get; init; }
    public required string TraceId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string[]>? Errors { get; init; }
}
