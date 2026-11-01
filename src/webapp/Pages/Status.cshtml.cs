using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace WebApp.Pages;

// Дружня сторінка для кодів помилок сторінок (не API). Найчастіше — 404 для ще не створених сторінок.
public class StatusModel : PageModel
{
    public int Code { get; private set; }
    public string? OriginalPath { get; private set; }
    public bool IsNotFound => Code == StatusCodes.Status404NotFound;

    public void OnGet(int code)
    {
        Code = code;
        OriginalPath = HttpContext.Features.Get<IStatusCodeReExecuteFeature>()?.OriginalPath;
    }
}
