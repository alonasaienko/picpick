using System.Text;

namespace WebApp.Controllers.ZoneB;

// Перевірка одного файлу за FR-202: порожній файл, сигнатура вмісту (а не розширення) і розмір.
// Декодування тут не перевіряється: пошкоджений файл із правильною сигнатурою пройде перевірку,
// а помилку декодування згодом виявить Worker і поставить фото статус Failed (FR-205).
internal static class PhotoFileCheck
{
    private const long MaxImageBytes = 30L * 1024 * 1024;
    private const long MaxRawBytes = 150L * 1024 * 1024;
    private static readonly string[] TiffRawExtensions = { ".dng", ".cr2", ".nef", ".arw" };
    private static readonly string[] HeicBrands = { "heic", "heix", "heim", "heis", "mif1", "msf1" };

    /// <summary>Повертає null, якщо файл прийнято, або (errorCode, повідомлення), якщо відхилено.</summary>
    internal static (string Code, string Message)? Check(IFormFile file)
    {
        if (file.Length == 0)
            return ("PHOTO_FILE_EMPTY", "Файл порожній.");

        var header = new byte[16];
        using (var stream = file.OpenReadStream())
            stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);

        var kind = Detect(header, Path.GetExtension(file.FileName).ToLowerInvariant());
        if (kind is null)
            return ("PHOTO_FORMAT_NOT_SUPPORTED", "Формат не підтримується: дозволено JPEG, PNG, HEIC і RAW (DNG, CR2, CR3, NEF, ARW, RAF).");

        var limit = kind == "RAW" ? MaxRawBytes : MaxImageBytes;
        if (file.Length > limit)
            return ("PHOTO_FILE_TOO_LARGE", kind == "RAW" ? "RAW-файл більший за 150 МБ." : "Файл більший за 30 МБ.");

        return null;
    }

    private static string? Detect(byte[] h, string extension)
    {
        if (h[0] == 0xFF && h[1] == 0xD8 && h[2] == 0xFF) return "JPEG";
        if (h.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return "PNG";

        var ascii = Encoding.ASCII.GetString(h);
        if (ascii[4..8] == "ftyp")
        {
            var brand = ascii[8..12];
            if (HeicBrands.Contains(brand)) return "HEIC";
            if (brand == "crx ") return "RAW"; // Canon CR3
        }
        var isTiff = ascii.StartsWith("II*\0", StringComparison.Ordinal) || ascii.StartsWith("MM\0*", StringComparison.Ordinal);
        if (isTiff && TiffRawExtensions.Contains(extension)) return "RAW"; // DNG, CR2, NEF, ARW мають TIFF-контейнер
        if (ascii.StartsWith("FUJIFILMCCD-RAW", StringComparison.Ordinal)) return "RAW"; // RAF
        return null;
    }
}
