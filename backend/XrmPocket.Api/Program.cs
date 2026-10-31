using XrmPocket.Api.Configuration;
using XrmPocket.Api.Connections;
using XrmPocket.Api.Environments;
using XrmPocket.Api.Mock;
using XrmPocket.Api.Query;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<XrmPocketOptions>(builder.Configuration.GetSection(XrmPocketOptions.SectionName));
builder.Services.AddSingleton<EnvironmentTemplateCatalog>();
builder.Services.AddSingleton<MockDataset>();
builder.Services.AddSingleton<ConnectionStore>();
builder.Services.AddSingleton<ConnectionFactory>();
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

// Sjablonen om een verbinding mee aan te maken (dev/test/acc/productie).
app.MapGet("/api/environments", (EnvironmentTemplateCatalog catalog)
    => Results.Ok(new { environments = catalog.List() }));

// Verbindingen — het equivalent van de Connection Manager in XrmToolBox.
app.MapGet("/api/connections", (ConnectionStore store, ConnectionFactory factory) =>
{
    var defaultConnection = store.GetDefault();

    return Results.Ok(new
    {
        defaultConnectionId = defaultConnection?.Id,
        connections = store.List().Select(profile => ToInfo(profile, store, factory)).ToList()
    });
});

app.MapPost("/api/connections", (SaveConnectionRequest request, ConnectionStore store, ConnectionFactory factory) =>
{
    var profile = store.Upsert(request, out var error);

    if (profile == null)
        return Results.BadRequest(new { error });

    factory.Invalidate(profile.Id);

    return Results.Ok(ToInfo(profile, store, factory));
});

app.MapDelete("/api/connections/{id}", (string id, ConnectionStore store, ConnectionFactory factory) =>
{
    if (!store.Delete(id))
        return Results.NotFound(new { error = "Verbinding niet gevonden." });

    factory.Invalidate(id);
    return Results.Ok(new { deleted = id });
});

app.MapPost("/api/connections/{id}/default", (string id, ConnectionStore store, ConnectionFactory factory) =>
{
    if (!store.SetDefault(id))
        return Results.NotFound(new { error = "Verbinding niet gevonden." });

    var profile = store.Get(id)!;
    return Results.Ok(ToInfo(profile, store, factory));
});

app.MapPost("/api/connections/{id}/test", (string id, ConnectionStore store, ConnectionFactory factory) =>
{
    var profile = store.Get(id);

    if (profile == null)
        return Results.NotFound(new { error = "Verbinding niet gevonden." });

    return Results.Ok(factory.Test(profile));
});

// Secret instellen: de waarde komt binnen via de body, wordt nooit teruggegeven of
// gelogd en belandt in een bestand met chmod 600 (analoog aan XrmToolBox' RequestPassword).
app.MapPost("/api/connections/{id}/secret", (string id, SaveSecretRequest request, ConnectionStore store, ConnectionFactory factory) =>
{
    var profile = store.Get(id);

    if (profile == null)
        return Results.NotFound(new { error = "Verbinding niet gevonden." });

    if (profile.IsMock)
        return Results.BadRequest(new { error = "De mock-verbinding heeft geen secret nodig." });

    try
    {
        var path = factory.SaveSecret(profile, request.Secret);

        return Results.Ok(new
        {
            ok = true,
            secretFile = path,
            message = "Secret bewaard. Test de verbinding om te bevestigen dat hij werkt."
        });
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/api/query", async (QueryRequest request, QueryService service, CancellationToken cancellationToken)
    => Results.Ok(await service.ExecuteAsync(request, cancellationToken)));

// Alles wat geen API-pad is, gaat naar de PWA.
app.MapFallbackToFile("index.html");

app.Run();

static SavedConnection ToInfo(ConnectionProfile profile, ConnectionStore store, ConnectionFactory factory)
{
    var missing = factory.DescribeMissingConfiguration(profile);
    var isDefault = store.GetDefault()?.Id == profile.Id;

    return new SavedConnection(
        profile.Id,
        profile.Name,
        profile.Kind,
        profile.AuthType,
        profile.Environment,
        profile.Url,
        profile.TenantId,
        profile.ClientId,
        profile.Highlight,
        profile.TimeoutSeconds,
        isDefault,
        missing == null,
        factory.HasSecret(profile),
        missing,
        profile.LastUsedOn);
}
