using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using RoomBooking.Agent;
using RoomBooking.Core.Bookings;
using RoomBooking.Core.Data;
using RoomBooking.Web.Auth;
using RoomBooking.Web.Components;

var builder = WebApplication.CreateBuilder(args);

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

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();

app.MapPost("/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
