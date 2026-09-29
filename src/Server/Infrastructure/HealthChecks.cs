using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using NhatVuong.Adapters.Mqtt;
using NhatVuong.Adapters.Persistence;
using NhatVuong.Adapters.Scheduler;

namespace NhatVuong.Server.Infrastructure;

// Readiness contract (CAP-23, F-16): the supervisor restarts the process when any of these stays unhealthy.

public sealed class DatabaseHealthCheck(NvcDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        await db.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Database unreachable.");
}

public sealed class MqttHealthCheck(MqttDeviceGateway gateway, IServiceProvider services) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var broker = services.GetService<EmbeddedMqttBroker>();
        if (broker is { IsRunning: false })
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("Embedded MQTT broker is not running."));
        }

        return Task.FromResult(gateway.IsConnected
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("MQTT gateway is not connected to the broker."));
    }
}

public sealed class SchedulerHealthCheck(SchedulerHeartbeat heartbeat, IOptions<SchedulerOptions> options, TimeProvider time) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!options.Value.Enabled)
        {
            return Task.FromResult(HealthCheckResult.Healthy("Scheduler disabled on this instance."));
        }

        var allowed = TimeSpan.FromSeconds(Math.Max(1, options.Value.TickSeconds) * 4 + 30);
        return Task.FromResult(heartbeat.LastTick is { } last && time.GetUtcNow() - last <= allowed
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("Scheduler loop has not ticked recently."));
    }
}
