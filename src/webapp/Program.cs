using WebApp.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options => options.IncludeXmlComments(
    Path.Combine(AppContext.BaseDirectory, "WebApp.xml"), includeControllerXmlComments: true));
builder.Services.AddApiErrorHandling();
builder.Services.AddApiRateLimiting();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
// Для сторінок (не /api) коди помилок, зокрема 404 ще не створених сторінок, показує дружня сторінка /Status/{code}.
app.UseWhen(context => !context.Request.Path.StartsWithSegments("/api"),
    pages => pages.UseStatusCodePagesWithReExecute("/Status/{0}"));
app.UseStaticFiles();
app.UseRouting();
app.UseApiErrorHandling();
app.UseRateLimiter();
app.UseAuthorization();

app.MapRazorPages();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();
