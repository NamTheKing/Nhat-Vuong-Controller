using System.Buffers;
using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Protocol;
using NhatVuong.Application.Abstractions;
using NhatVuong.Application.Devices;
using NhatVuong.Contracts;
using NhatVuong.Contracts.Mqtt;

namespace NhatVuong.Adapters.Mqtt;

/// <summary>
/// The server's MQTT client: implements <see cref="IDevicePort"/> and feeds device reports into the application
/// ingest. Reconnect with exponential backoff lives here and nowhere else (MQTTnet 5 has no managed client).
/// </summary>
public sealed class MqttDeviceGateway(
    IOptions<MqttOptions> options,
    IServiceScopeFactory scopes,
    TimeProvider time,
    ILogger<MqttDeviceGateway> logger) : BackgroundService, IDevicePort
{
    private readonly MqttOptions _options = options.Value;
    private readonly ConcurrentDictionary<Guid, (string HardwareId, TaskCompletionSource<DeviceAck> Completion)> _pending = new();
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private Channel<(string HardwareId, string Channel, byte[] Payload)>[] _workers = [];
    private IMqttClient? _client;

    public bool IsConnected => _client?.IsConnected == true;

    public async Task<DeviceAck> SendCommandAsync(DeviceCommand command, TimeSpan timeout, CancellationToken ct)
    {
        var client = _client;
        if (client is not { IsConnected: true })
        {
            throw new DeviceUnavailableException("MQTT gateway is not connected.");
        }

        var completion = new TaskCompletionSource<DeviceAck>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[command.CommandId] = (command.HardwareId, completion);
        try
        {
            var message = new MqttApplicationMessageBuilder()
                .WithTopic(MqttTopics.For(command.HardwareId, MqttTopics.CommandChannel))
                .WithPayload(NvcJson.Serialize(command.ToMessage()))
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .WithMessageExpiryInterval((uint)Math.Max(1, timeout.TotalSeconds))
                .Build();

            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutSource.CancelAfter(timeout);
            try
            {
                await client.PublishAsync(message, timeoutSource.Token);
                return await completion.Task.WaitAsync(timeoutSource.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new DeviceTimeoutException($"No reply to {command.CommandId} within {timeout.TotalSeconds:0} s.");
            }
        }
        finally
        {
            _pending.TryRemove(command.CommandId, out _);
        }
    }

    public async Task PublishConfigAsync(string hardwareId, DeviceConfig config, CancellationToken ct)
    {
        var client = _client;
        if (client is not { IsConnected: true })
        {
            return;
        }

        var message = new MqttApplicationMessageBuilder()
            .WithTopic(MqttTopics.For(hardwareId, MqttTopics.ConfigChannel))
            .WithPayload(NvcJson.Serialize(config.ToMessage()))
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .WithRetainFlag()
            .Build();
        await client.PublishAsync(message, ct);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var workerCount = Math.Max(1, _options.IngestWorkers);
        _workers = Enumerable.Range(0, workerCount)
            .Select(_ => Channel.CreateBounded<(string, string, byte[])>(new BoundedChannelOptions(5_000) { SingleReader = true }))
            .ToArray();
        var consumers = _workers.Select(w => Task.Run(() => ConsumeAsync(w.Reader, stoppingToken), stoppingToken)).ToArray();

        _client = new MqttClientFactory().CreateMqttClient();
        _client.ApplicationMessageReceivedAsync += OnMessageAsync;

        var backoff = TimeSpan.FromSeconds(1);
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!_client.IsConnected)
            {
                try
                {
                    await ConnectAsync(stoppingToken);
                    backoff = TimeSpan.FromSeconds(1);
                    logger.LogInformation("MQTT gateway connected to {Host}", _options.Host);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogWarning("MQTT gateway connection failed ({Message}); retrying in {Delay}s", ex.Message, backoff.TotalSeconds);
                    await Task.Delay(backoff, time, stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
                    backoff = TimeSpan.FromSeconds(Math.Min(30, backoff.TotalSeconds * 2));
                    continue;
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(1), time, stoppingToken).ContinueWith(_ => { }, TaskScheduler.Default);
        }

        foreach (var worker in _workers)
        {
            worker.Writer.TryComplete();
        }

        await Task.WhenAll(consumers).ContinueWith(_ => { }, TaskScheduler.Default);
        if (_client.IsConnected)
        {
            await _client.DisconnectAsync(new MqttClientDisconnectOptionsBuilder().Build(), CancellationToken.None);
        }
    }

    private async Task ConnectAsync(CancellationToken ct)
    {
        await _connectGate.WaitAsync(ct);
        try
        {
            var builder = new MqttClientOptionsBuilder()
                .WithClientId($"{_options.ServerUsername}-{Guid.NewGuid():N}")
                .WithTcpServer(_options.Host, _options.UseTls ? _options.TlsPort : _options.Port)
                .WithCredentials(_options.ServerUsername, _options.ServerPassword)
                .WithCleanSession()
                .WithKeepAlivePeriod(TimeSpan.FromSeconds(15))
                .WithTimeout(TimeSpan.FromSeconds(10));
            if (_options.UseTls)
            {
                builder.WithTlsOptions(tls =>
                {
                    tls.UseTls();
                    if (_options.AllowUntrustedCertificates)
                    {
                        tls.WithAllowUntrustedCertificates(true).WithIgnoreCertificateChainErrors(true).WithCertificateValidationHandler(_ => true);
                    }
                });
            }

            await _client!.ConnectAsync(builder.Build(), ct);
            var subscribe = new MqttClientSubscribeOptionsBuilder();
            foreach (var channel in MqttTopics.DevicePublishChannels)
            {
                subscribe.WithTopicFilter(MqttTopics.AllDevices(channel), MqttQualityOfServiceLevel.AtLeastOnce);
            }

            await _client.SubscribeAsync(subscribe.Build(), ct);
        }
        finally
        {
            _connectGate.Release();
        }
    }

    private async Task OnMessageAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        if (!MqttTopics.TryParse(e.ApplicationMessage.Topic, out var hardwareId, out var channel))
        {
            return;
        }

        var payload = e.ApplicationMessage.Payload.ToArray();
        if (channel == MqttTopics.AckChannel)
        {
            CompleteAck(hardwareId, payload);
            return;
        }

        // Same device → same worker, so a device's reports are applied in the order they arrived.
        var worker = _workers[(int)((uint)StringComparer.Ordinal.GetHashCode(hardwareId) % (uint)_workers.Length)];
        await worker.Writer.WriteAsync((hardwareId, channel, payload));
    }

    private void CompleteAck(string hardwareId, byte[] payload)
    {
        AckMessage? ack;
        try
        {
            ack = NvcJson.Deserialize<AckMessage>(payload);
        }
        catch (System.Text.Json.JsonException ex)
        {
            logger.LogWarning(ex, "Malformed ack from {HardwareId}", hardwareId);
            return;
        }

        if (ack is null || !EnvelopeVersion.IsSupported(ack.Version))
        {
            return;
        }

        // Only the device the command was addressed to can complete it.
        if (_pending.TryGetValue(ack.CommandId, out var pending) && pending.HardwareId == hardwareId)
        {
            pending.Completion.TrySetResult(new DeviceAck(ack.CommandId, ack.Success, ack.Error, ack.State?.ToObserved(), ack.OccurredAt.ToUniversalTime()));
        }
    }

    private async Task ConsumeAsync(ChannelReader<(string HardwareId, string Channel, byte[] Payload)> reader, CancellationToken ct)
    {
        await foreach (var (hardwareId, channel, payload) in reader.ReadAllAsync(CancellationToken.None))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var reports = scope.ServiceProvider.GetRequiredService<DeviceReportService>();
                await HandleAsync(reports, hardwareId, channel, payload, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogError(ex, "Failed to ingest {Channel} report from {HardwareId}", channel, hardwareId);
            }
        }
    }

    private async Task HandleAsync(DeviceReportService reports, string hardwareId, string channel, byte[] payload, CancellationToken ct)
    {
        switch (channel)
        {
            case MqttTopics.StateChannel:
                var state = NvcJson.Deserialize<StateMessage>(payload);
                if (state is not null && EnvelopeVersion.IsSupported(state.Version))
                {
                    await reports.HandleStateReportAsync(
                        hardwareId, state.State.ToObserved(), state.OccurredAt.ToUniversalTime(), state.LanEndpoint, state.LanCertThumbprint, ct);
                }

                break;

            case MqttTopics.StatusChannel:
                var status = NvcJson.Deserialize<StatusMessage>(payload);
                if (status is not null && EnvelopeVersion.IsSupported(status.Version))
                {
                    await reports.HandleConnectivityAsync(hardwareId, status.Online, status.OccurredAt.ToUniversalTime(), ct);
                }

                break;

            case MqttTopics.EventChannel:
                var evt = NvcJson.Deserialize<EventMessage>(payload);
                if (evt is null || !EnvelopeVersion.IsSupported(evt.Version))
                {
                    break;
                }

                if (evt.Kind == EventKinds.Error && evt.ErrorCode is { } code)
                {
                    await reports.HandleErrorAsync(hardwareId, code, evt.ErrorMessage, evt.OccurredAt.ToUniversalTime(), ct);
                }
                else if (evt.Kind == EventKinds.OfflineFacts && evt.Facts is { Count: > 0 } facts)
                {
                    await reports.HandleOfflineFactsAsync(hardwareId, facts.Select(f => f.ToFact()).ToList(), ct);
                }

                break;
        }
    }
}
