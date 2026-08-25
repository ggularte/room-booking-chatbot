using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
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

// A factory rather than a scoped context: under Blazor Server a scoped service lives as long as
// the circuit, and a context that long-lived would serve stale rooms and bookings.
builder.Services.AddDbContextFactory<BookingDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Bookings") ?? "Data Source=bookings.db"));

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
    new Uri(groq["Endpoint"] ?? "https://api.groq.com/openai/v1"));

var app = builder.Build();

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
