using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace WebApp.Models.ZoneA;

/// <summary>Дані нової Gallery. Власник визначається сервером.</summary>
public sealed class CreateGalleryRequest
{
    private string? name;

    /// <summary>Назва: 1–100 символів після видалення крайніх пробілів.</summary>
    [Required(ErrorMessage = "Укажіть назву галереї.")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "Назва має містити 1–100 символів.")]
    public string? Name { get => name; set => name = value?.Trim(); }

    /// <summary>Момент завершення терміну з часовим поясом; зберігається в UTC.</summary>
    [Required(ErrorMessage = "Укажіть термін дії галереї.")]
    [JsonConverter(typeof(ExplicitOffsetDateTimeConverter))]
    public DateTimeOffset? ExpiresAt { get; set; }
}

// Не трактуємо дату без часового поясу як локальний час сервера.
internal sealed class ExplicitOffsetDateTimeConverter : JsonConverter<DateTimeOffset?>
{
    public override DateTimeOffset? Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        if (reader.TokenType != JsonTokenType.String) throw new JsonException("Очікується дата ISO 8601.");
        var text = reader.GetString()!;
        if (!Regex.IsMatch(text, @"T.*(?:Z|[+-]\d{2}:\d{2})$", RegexOptions.CultureInvariant)
            || !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw new JsonException("Очікується дата ISO 8601 з часовим поясом.");
        return date;
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue();
        else writer.WriteStringValue(value.Value.ToUniversalTime());
    }
}
