using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NhatVuong.Application.Access;
using NhatVuong.Application.Devices;
using NhatVuong.Application.Maintenance;
using NhatVuong.Application.Scheduling;

namespace NhatVuong.Adapters.Scheduler;

public sealed class SchedulerOptions
{
    public const string Section = "Scheduler";

    /// <summary>
    /// Exactly one scheduler per deployment (F-19). Disable it on additional API replicas if the API is ever scaled out.
    /// </summary>
    public bool Enabled { get; set; } = true;

    public int TickSeconds { get; set; } = 15;

    /// <summary>How often cached schedules are re-pushed to every module (they cover a rolling 24-hour horizon).</summary>
    public int ConfigRefreshMinutes { get; set; } = 10;
}

/// <summary>Liveness of the scheduler loop, read by the readiness health check (CAP-23).</summary>
public sealed class SchedulerHeartbeat(TimeProvider time)
{
    private long _lastTickTicks;

    public DateTimeOffset? LastTick => Interlocked.Read(ref _lastTickTicks) is var t and > 0 ? new DateTimeOffset(t, TimeSpan.Zero) : null;

    public void Beat() => Interlocked.Exchange(ref _lastTickTicks, time.GetUtcNow().UtcTicks);
}

/// <summary>
/// Scheduler driving adapter: a <see cref="PeriodicTimer"/> loop. Each tick opens a scope and runs the time-based
/// jobs. All state is in the database, so a restart resumes where it left off (missed actions within the catch-up
/// window still fire).
/// </summary>
public sealed class SchedulerHostedService(
    IServiceScopeFactory scopes,
    IOptions<SchedulerOptions> options,
    SchedulerHeartbeat heartbeat,
    TimeProvider time,
    ILogger<SchedulerHostedService> logger) : BackgroundService
{
    private DateTimeOffset _lastConfigPush = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("Scheduler disabled on this instance");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, options.Value.TickSeconds)), time);
        do
        {
            await TickAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).AsTask().ContinueWith(t => !t.IsCanceled && t.Result, TaskScheduler.Default));
    }

    public async Task TickAsync(CancellationToken ct)
    {
        heartbeat.Beat();
        await RunAsync<AccessService>("grant-expiry", (s, c) => s.SweepExpiredAsync(time.GetUtcNow(), c), ct);
        await RunAsync<MonitoringService>("silent-devices", (s, c) => s.DetectSilentDevicesAsync(c), ct);
        await RunAsync<SchedulingService>("auto-off", (s, c) => s.RunAutoOffAsync(c), ct);
        await RunAsync<SchedulingService>("closing", (s, c) => s.RunClosingAsync(c), ct);
        await RunAsync<SchedulingService>("pre-cool", (s, c) => s.RunPreCoolAsync(c), ct);
        await RunAsync<MonitoringService>("disconnect-alerts", (s, c) => s.RaiseDisconnectAlertsAsync(c), ct);
        await RunAsync<MonitoringService>("long-run-alerts", (s, c) => s.RaiseLongRunAlertsAsync(c), ct);

        var now = time.GetUtcNow();
        if (now - _lastConfigPush >= TimeSpan.FromMinutes(Math.Max(1, options.Value.ConfigRefreshMinutes)))
        {
            _lastConfigPush = now;
            await RunAsync<DeviceConfigService>("config-refresh", async (s, c) => { await s.PublishAllAsync(c); return 0; }, ct);
        }
    }

    private async Task RunAsync<TService>(string job, Func<TService, CancellationToken, Task<int>> work, CancellationToken ct)
        where TService : notnull
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var affected = await work(scope.ServiceProvider.GetRequiredService<TService>(), ct);
            if (affected > 0)
            {
                logger.LogInformation("Scheduler job {Job} affected {Count} item(s)", job, affected);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            // One failing job must not stop the others or the loop.
            logger.LogError(ex, "Scheduler job {Job} failed", job);
        }
    }
}

public static class SchedulerExtensions
{
    public static IServiceCollection AddNvcScheduler(this IServiceCollection services, SchedulerOptions options)
    {
        services.AddOptions<SchedulerOptions>().Configure(o =>
        {
            o.Enabled = options.Enabled;
            o.TickSeconds = options.TickSeconds;
            o.ConfigRefreshMinutes = options.ConfigRefreshMinutes;
        });
        services.AddSingleton<SchedulerHeartbeat>();
        services.AddSingleton<SchedulerHostedService>();
        services.AddHostedService(sp => sp.GetRequiredService<SchedulerHostedService>());
        return services;
    }
}
