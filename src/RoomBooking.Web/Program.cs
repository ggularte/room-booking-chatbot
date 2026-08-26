using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RoomBooking.Agent;
using RoomBooking.Core.Bookings;
using RoomBooking.Core.Data;
using RoomBooking.Web.Auth;
using RoomBooking.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Platforms like Railway, Render and Fly assign the port at runtime and pass it in PORT. Without
// this the container listens on the default port and the platform's health check never answers.
//
// It only applies when nothing more specific was configured. PORT is a common variable to have
// lying around locally, and letting it override an explicit ASPNETCORE_URLS would silently drop
// the launch profile's HTTPS endpoint.
var assignedPort = Environment.GetEnvironmentVariable("PORT");
var configuredUrls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");

if (!string.IsNullOrWhiteSpace(assignedPort) && string.IsNullOrWhiteSpace(configuredUrls))
    builder.WebHost.UseUrls($"http://+:{assignedPort}");

// TLS terminates at the platform's proxy, so the app sees plain HTTP. Without the forwarded
// headers it would consider the request insecure and refuse to issue the auth cookie.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Five seconds, not the thirty SQLite defaults to. A booking request that has to wait for another
// one's lock should give up quickly and tell the user to try again, rather than leaving a chat
// message spinning for half a minute.
const string DefaultConnectionString = "Data Source=bookings.db;Default Timeout=5";

// A factory rather than a scoped context: under Blazor Server a scoped service lives as long as
// the circuit, and a context that long-lived would serve stale rooms and bookings.
builder.Services.AddDbContextFactory<BookingDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Bookings") ?? DefaultConnectionString));

// Registered here rather than relying on the assistant's extension method to provide it:
// BookingService lives in Core and should not depend on the Agent package being wired up.
builder.Services.TryAddSingleton(TimeProvider.System);

builder.Services.AddScoped<BookingService>();

builder.Services.AddSingleton(
    builder.Configuration.GetSection(Credentials.SectionName).Get<Credentials>() ?? new Credentials());

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    });

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

builder.Services.AddScoped<CurrentUser>();
builder.Services.AddScoped<IUserContext>(services => services.GetRequiredService<CurrentUser>());

var groq = builder.Configuration.GetSection("Groq");
var apiKey = groq["ApiKey"];

if (string.IsNullOrWhiteSpace(apiKey))
{
    throw new InvalidOperationException(
        "No Groq API key is configured. Set it with:\n" +
        "  dotnet user-secrets set \"Groq:ApiKey\" \"<key>\"\n" +
        "or supply it as the Groq__ApiKey environment variable. Free keys: https://console.groq.com");
}

builder.Services.AddBookingAssistant(
    apiKey,
    groq["Model"] ?? "openai/gpt-oss-120b",
    new Uri(groq["Endpoint"] ?? "https://api.groq.com/openai/v1"),
    // The daily allowance is per model, so a second one keeps the assistant answering after the
    // first has spent its own. Leave it unset to disable the fallback.
    groq["FallbackModel"]);

var app = builder.Build();

EnsureTheDatabaseFolderIsWritable(
    builder.Configuration.GetConnectionString("Bookings") ?? DefaultConnectionString);

// The schema and the seeded office are created on start, so the app runs from a clean clone with
// no migration step. The data is fixed reference data, not something a migration history buys much.
await using (var scope = app.Services.CreateAsyncScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BookingDbContext>>();
    await using var db = await factory.CreateDbContextAsync();
    await db.Database.EnsureCreatedAsync();
}

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
else
{
    // Only in development, where the app terminates TLS itself. In a container the proxy already
    // did, and redirecting to a port the app does not serve produces a loop.
    app.UseHttpsRedirection();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// Both, deliberately. MapStaticAssets serves what the build's manifest lists, with fingerprinted
// names and precompressed variants. In the deployed container that manifest arrived carrying the
// application's own assets but not the framework's, so _framework/blazor.web.js was neither
// fingerprinted in the markup nor served at all — leaving a page with no Blazor runtime, which
// looks like an application that silently refuses to do anything.
//
// The cause of the truncated manifest is not established: the same publish, run from the same
// output on this machine, produces a complete one. UseStaticFiles serves wwwroot from disk
// regardless of what the manifest says, so a missing entry costs the fingerprint rather than the
// file. It is redundant when the manifest is whole, which is the point.
app.UseStaticFiles();
app.MapStaticAssets();

app.MapPost("/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
});

// Somewhere for the platform's health check to land that does not require a session.
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

/// <summary>
/// Fails with something worth reading when the database folder cannot be written to.
///
/// SQLite reports this as "SQLite Error 14: unable to open database file", which names neither the
/// folder nor the reason, and arrives wrapped in a stack trace through three layers of EF Core.
/// Mounted volumes are the usual cause: several platforms hand them to the container owned by root
/// while the image runs as somebody else.
/// </summary>
static void EnsureTheDatabaseFolderIsWritable(string connectionString)
{
    var dataSource = new SqliteConnectionStringBuilder(connectionString).DataSource;

    if (string.IsNullOrWhiteSpace(dataSource) || dataSource.Contains(":memory:"))
        return;

    var folder = Path.GetDirectoryName(Path.GetFullPath(dataSource));

    if (string.IsNullOrEmpty(folder))
        return;

    try
    {
        Directory.CreateDirectory(folder);

        var probe = Path.Combine(folder, $".write-probe-{Environment.ProcessId}");
        File.WriteAllText(probe, string.Empty);
        File.Delete(probe);
    }
    catch (Exception denied) when (denied is UnauthorizedAccessException or IOException)
    {
        throw new InvalidOperationException(
            $"The database folder '{folder}' cannot be written to, so the database cannot be created.\n" +
            $"Running as uid {Environment.UserName}.\n\n" +
            "A mounted volume is the usual cause: some platforms hand it to the container owned by " +
            "root while the image runs as a non-root user. On Railway, set RAILWAY_RUN_UID=0. " +
            "Elsewhere, give the mount to the container's user or point ConnectionStrings__Bookings " +
            "somewhere writable.",
            denied);
    }
}
