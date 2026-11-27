using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace XrmPocket.Api.Auth;

/// <summary>
/// Toegangsbeveiliging: één gedeeld wachtwoord uit de omgevingsvariabele
/// XRMPOCKET_PASSWORD. Zonder die variabele weigert de API alles (fail closed).
/// Na inloggen krijgt de browser een httpOnly-sessiecookie.
/// </summary>
public static class AccessGate
{
    public const string CookieName = "xp_session";

    private static readonly ConcurrentDictionary<string, DateTimeOffset> Sessions = new();
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(7);

    private static readonly HashSet<string> OpenPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/session",
        "/api/login",
        "/api/logout",
        "/api/health",
        "/api/auth/callback"
    };

    public static void MapAccessGate(this WebApplication app)
    {
        var password = ReadPassword(app.Environment.ContentRootPath);

        app.Use(async (context, next) =>
        {
            var path = context.Request.Path.Value ?? "";

            if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) || OpenPaths.Contains(path))
            {
                await next();
                return;
            }

            if (string.IsNullOrEmpty(password))
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await context.Response.WriteAsJsonAsync(new { error = "Er is geen wachtwoord ingesteld op de server (XRMPOCKET_PASSWORD)." });
                return;
            }

            if (IsValidSession(context.Request.Cookies[CookieName]))
            {
                await next();
                return;
            }

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Niet ingelogd." });
        });

        app.MapGet("/api/session", (HttpContext context) => Results.Ok(new
        {
            authenticated = IsValidSession(context.Request.Cookies[CookieName]),
            passwordConfigured = !string.IsNullOrEmpty(password)
        }));

        app.MapPost("/api/login", async (LoginRequest request, HttpContext context) =>
        {
            if (string.IsNullOrEmpty(password))
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

            var given = Encoding.UTF8.GetBytes(request.Password ?? "");
            var expected = Encoding.UTF8.GetBytes(password);

            if (!CryptographicOperations.FixedTimeEquals(given, expected))
            {
                // Kleine vertraging tegen snel raden.
                await Task.Delay(TimeSpan.FromMilliseconds(800));
                return Results.Json(new { error = "Onjuist wachtwoord." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            Sessions[token] = DateTimeOffset.UtcNow.Add(SessionLifetime);

            context.Response.Cookies.Append(CookieName, token, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Strict,
                Secure = context.Request.IsHttps,
                Expires = DateTimeOffset.UtcNow.Add(SessionLifetime),
                Path = "/"
            });

            return Results.Ok(new { authenticated = true });
        });

        app.MapPost("/api/logout", (HttpContext context) =>
        {
            var token = context.Request.Cookies[CookieName];

            if (token != null)
                Sessions.TryRemove(token, out _);

            context.Response.Cookies.Delete(CookieName, new CookieOptions { Path = "/" });
            return Results.Ok(new { authenticated = false });
        });
    }

    private static string? ReadPassword(string contentRoot)
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("XRMPOCKET_PASSWORD");

        if (!string.IsNullOrWhiteSpace(fromEnvironment))
            return fromEnvironment.Trim();

        // backend/XrmPocket.Api -> repo-root/secrets/wachtwoord.txt (map staat in .gitignore)
        var path = Path.GetFullPath(Path.Combine(contentRoot, "..", "..", "secrets", "wachtwoord.txt"));

        if (!File.Exists(path))
            return null;

        var value = File.ReadAllText(path).Trim();
        return value.Length == 0 || value == "VUL-HIER-JE-WACHTWOORD-IN" ? null : value;
    }

    private static bool IsValidSession(string? token)
    {
        if (string.IsNullOrEmpty(token))
            return false;

        if (!Sessions.TryGetValue(token, out var expires))
            return false;

        if (expires > DateTimeOffset.UtcNow)
            return true;

        Sessions.TryRemove(token, out _);
        return false;
    }

    public sealed record LoginRequest(string? Password);
}
