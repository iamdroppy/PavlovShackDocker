using Hangfire;
using Hangfire.InMemory;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Pavlov.Manager.Components;
using Pavlov.Manager.Services;
using Pavlov.Manager.Services.EndOfGame;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.Configure<RconSettings>(builder.Configuration.GetSection(RconSettings.Section));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<RconSession>();
builder.Services.AddSingleton<LoginThrottle>();
builder.Services.AddSingleton<ServerMonitor>();
builder.Services.AddHostedService(services => services.GetRequiredService<ServerMonitor>());
builder.Services.AddSingleton<RuleStore>();
builder.Services.AddSingleton<MatchTracker>();
builder.Services.AddTransient<EndOfGameJob>();
builder.Services.AddHostedService<EndOfGameScheduler>();

// Hangfire runs the end-of-game rules. Storage is in memory: the rules file is the source of
// truth and the recurring jobs are rebuilt from it on startup. Job history is kept 30 minutes.
builder.Services.AddHangfire(configuration => configuration
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseInMemoryStorage(new InMemoryStorageOptions { MaxExpirationTime = TimeSpan.FromMinutes(30) }));
builder.Services.AddHangfireServer(options =>
{
    options.ServerName = "shack-control";
    options.WorkerCount = 2;
    options.SchedulePollingInterval = TimeSpan.FromSeconds(1);
});

builder.Services.AddScoped<ToastService>();
builder.Services.AddScoped<DialogService>();
builder.Services.AddScoped<RconActions>();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthenticationStateProvider, RconAuthenticationStateProvider>();
builder.Services.AddAuthorization();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "pavlov-manager";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.LoginPath = "/login";
        options.ExpireTimeSpan = TimeSpan.FromHours(12);
        options.SlidingExpiration = true;
        // Reject cookies issued for credentials the manager no longer holds (restart or password change).
        options.Events.OnValidatePrincipal = async context =>
        {
            if (!context.HttpContext.RequestServices.GetRequiredService<RconSession>().IsValid(context.Principal))
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
    });

// Behind a reverse proxy (TLS termination), trust X-Forwarded-* so the login throttle sees real client IPs.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

if (app.Configuration.GetValue<bool>("TrustForwardedHeaders"))
{
    app.UseForwardedHeaders();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();

app.MapPost("/logout", async (HttpContext context, [FromForm] string? returnUrl) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.LocalRedirect("~/login");
});

// Hangfire dashboard: same sign-in as the app (the RCON password). Signed-out visitors are
// sent to /login and come back here afterwards.
app.MapHangfireDashboard("/hangfire", new DashboardOptions
{
    DashboardTitle = "Shack Control · Jobs",
    AppPath = "/maps",
    Authorization = [new SignedInDashboardAuthorization()],
    DisplayStorageConnectionString = false,
}).RequireAuthorization();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
