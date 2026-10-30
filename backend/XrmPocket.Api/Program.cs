using XrmPocket.Api.Configuration;
using XrmPocket.Api.Environments;
using XrmPocket.Api.Mock;
using XrmPocket.Api.Query;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<XrmPocketOptions>(builder.Configuration.GetSection(XrmPocketOptions.SectionName));
builder.Services.AddSingleton<EnvironmentCatalog>();
builder.Services.AddSingleton<MockDataset>();
builder.Services.AddSingleton<QueryService>();

// De web-UI draait in ontwikkeling op de Vite-server (5173) en praat met deze API.
const string DevCors = "dev-web";
builder.Services.AddCors(options => options.AddPolicy(DevCors, policy => policy
    .WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.UseCors(DevCors);

// De gebouwde frontend (web/dist → wwwroot) wordt mee geserveerd, zodat de PWA
// achter dezelfde origin draait als de API.
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    engine = typeof(MarkMpn.Sql4Cds.Engine.Sql4CdsConnection).Assembly.GetName().Version?.ToString(),
    time = DateTimeOffset.UtcNow
}));

app.MapGet("/api/environments", (EnvironmentCatalog catalog)
    => Results.Ok(new { environments = catalog.List() }));

app.MapPost("/api/query", async (QueryRequest request, QueryService service, CancellationToken cancellationToken)
    => Results.Ok(await service.ExecuteAsync(request, cancellationToken)));

// Alles wat geen API-pad is, gaat naar de PWA.
app.MapFallbackToFile("index.html");

app.Run();
