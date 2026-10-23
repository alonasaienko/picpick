using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace WebApp.Models.ZoneA;

/// <summary>Контракт Problem Details з розширеннями конвенції помилок PicPick.</summary>
public sealed class GalleryProblemDetails : ProblemDetails
{
    /// <summary>Стабільний машинний код помилки.</summary>
    public required string ErrorCode { get; init; }

    /// <summary>Ідентифікатор запиту для пошуку в журналах.</summary>
    public required string TraceId { get; init; }

    /// <summary>Невалідні поля в camelCase; лише для помилок валідації.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string[]>? Errors { get; init; }
}
