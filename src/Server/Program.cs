using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NhatVuong.Adapters.Mqtt;
using NhatVuong.Adapters.Notification;
using NhatVuong.Adapters.Persistence;
using NhatVuong.Adapters.Rest;
using NhatVuong.Adapters.Rest.Security;
using NhatVuong.Adapters.Scheduler;
using NhatVuong.Application;
using NhatVuong.Application.Abstractions;
using NhatVuong.Application.Commands;
using NhatVuong.Server.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

T Bind<T>(string section)
    where T : new()
{
    var options = new T();
    configuration.GetSection(section).Bind(options);
    return options;
}

Text.Culture = CultureInfo.GetCultureInfo(configuration["Localization:ServerCulture"] ?? "vi");

// Composition root: the application core plus one adapter per port (hexagonal, AD-1).
builder.Services.TryAddSingleton(TimeProvider.System);
builder.Services.AddNvcApplication();
builder.Services.AddNvcPersistence(Bind<DatabaseOptions>(DatabaseOptions.Section));
builder.Services.AddNvcMqtt(Bind<MqttOptions>(MqttOptions.Section));
builder.Services.AddNvcScheduler(Bind<SchedulerOptions>(SchedulerOptions.Section));
builder.Services.AddNvcNotifications(Bind<NotificationOptions>(NotificationOptions.Section));
builder.Services.AddNvcRest(Bind<AuthOptions>(AuthOptions.Section));

var signingKeyPath = configuration["Signing:KeyPath"] ?? "keys/grant-signing.pem";
builder.Services.AddSingleton<IGrantSigner>(sp => new EcdsaGrantSigner(signingKeyPath, sp.GetRequiredService<ILogger<EcdsaGrantSigner>>()));

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"])
    .AddCheck<MqttHealthCheck>("mqtt", tags: ["ready"])
    .AddCheck<SchedulerHealthCheck>("scheduler", tags: ["ready"]);

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync(Bind<SeedOptions>(SeedOptions.Section));
    var interrupted = await scope.ServiceProvider.GetRequiredService<CommandService>().ResolveInterruptedAsync();
    if (interrupted > 0)
    {
        app.Logger.LogWarning("{Count} command(s) interrupted by the previous shutdown were marked failed", interrupted);
    }
}

app.UseExceptionHandler();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapNvcApi();
app.MapGet("/", () => Results.Ok(new { name = "Nhat Vuong Controller API", version = "v1" })).AllowAnonymous();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        await JsonSerializer.SerializeAsync(context.Response.Body, new
        {
            status = report.Status.ToString(),
            checks = report.Entries.ToDictionary(e => e.Key, e => new { status = e.Value.Status.ToString(), e.Value.Description }),
        });
    },
    ResultStatusCodes =
    {
        [HealthStatus.Healthy] = StatusCodes.Status200OK,
        [HealthStatus.Degraded] = StatusCodes.Status200OK,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
    },
}).AllowAnonymous();

await app.RunAsync();

public partial class Program;
