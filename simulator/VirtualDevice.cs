using System.Buffers;
using System.Globalization;
using MQTTnet;
using MQTTnet.Protocol;
using NhatVuong.Contracts;
using NhatVuong.Contracts.Lan;
using NhatVuong.Contracts.Mqtt;

namespace NhatVuong.Simulator;

public sealed record BrokerEndpoint(string Host, int Port, bool UseTls, bool AllowUntrusted);

/// <summary>
/// Software twin of a device module (docs/06-testing-strategy.md §2). Speaks the same MQTT envelope and topics as
/// the firmware contract, keeps an idempotency window (AD-9), reports board state (AD-4), executes signed LAN
/// commands (AD-3) and its cached schedule when the server is unreachable (AD-6), and replays both as facts (AD-5).
/// </summary>
public sealed class VirtualDevice : IAsyncDisposable
{
    private const int RecentCommandWindow = 256;
    private const double AmbientTemperature = 31.0;

    private readonly object _gate = new();
    private readonly LinkedList<Guid> _recentOrder = new();
    private readonly HashSet<Guid> _recentIds = [];
    private readonly HashSet<Guid> _executedSchedule = [];
    private readonly List<OfflineFactDto> _pendingFacts = [];
    private readonly BrokerEndpoint _broker;
    private readonly string _password;
    private readonly TimeProvider _time;
    private readonly Action<string> _log;
    private readonly CancellationTokenSource _stop = new();
    private IMqttClient? _client;
    private Task? _loop;
    private DateTimeOffset _suppressReconnectUntil;
    private DateTimeOffset _lastServerContact;
    private DateTimeOffset _lastReport;
    private TimeSpan? _clockOffset;
    private ConfigMessage? _config;

    public VirtualDevice(string hardwareId, string password, BrokerEndpoint broker, TimeProvider? time = null, Action<string>? log = null)
    {
        HardwareId = hardwareId;
        _password = password;
        _broker = broker;
        _time = time ?? TimeProvider.System;
        _log = log ?? (_ => { });
        _lastServerContact = _time.GetUtcNow();
    }

    public string HardwareId { get; }

    // ---- Board (what the control board would report) ----
    public PowerState Power { get; private set; } = PowerState.Off;
    public double Setpoint { get; private set; } = 26;
    public AcMode Mode { get; private set; } = AcMode.Cool;
    public FanSpeed Fan { get; private set; } = FanSpeed.Auto;
    public double RoomTemperature { get; private set; } = AmbientTemperature;
    public bool CompressorRunning { get; private set; }
    public string? ErrorCode { get; private set; }

    // ---- Fault injection ----
    public bool NoReply { get; set; }
    public TimeSpan ReplyDelay { get; set; }
    public TimeSpan ReportInterval { get; set; } = TimeSpan.FromSeconds(5);

    // ---- LAN endpoint advertised in state reports ----
    public string? LanEndpoint { get; set; }
    public string? LanCertThumbprint { get; set; }

    public bool IsConnected => _client?.IsConnected == true;
    public bool HasConfig => _config is not null;
    public int PendingFactCount { get { lock (_gate) { return _pendingFacts.Count; } } }
    public int CommandsExecuted { get; private set; }

    public Task StartAsync()
    {
        _client = new MqttClientFactory().CreateMqttClient();
        _client.ApplicationMessageReceivedAsync += OnMessageAsync;
        _loop = Task.Run(() => RunAsync(_stop.Token));
        return Task.CompletedTask;
    }

    public async Task<bool> WaitUntilConnectedAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (IsConnected && HasConfig)
            {
                return true;
            }

            await Task.Delay(50);
        }

        return IsConnected;
    }

    /// <summary>Simulates losing the network (last will fires) and staying away for <paramref name="duration"/>.</summary>
    public async Task DropConnectionAsync(TimeSpan duration)
    {
        _suppressReconnectUntil = _time.GetUtcNow() + duration;
        if (_client is { IsConnected: true })
        {
            await _client.DisconnectAsync(new MqttClientDisconnectOptionsBuilder()
                .WithReason(MqttClientDisconnectOptionsReason.DisconnectWithWillMessage).Build());
        }

        _log($"{HardwareId}: network dropped for {duration.TotalSeconds:0}s");
    }

    public void ResumeConnection() => _suppressReconnectUntil = DateTimeOffset.MinValue;

    /// <summary>US-09: a change made with the physical remote, reported without any command.</summary>
    public async Task UseRemoteAsync(CommandAction action, string? value)
    {
        lock (_gate)
        {
            Apply(action, value);
        }

        _log($"{HardwareId}: remote {action} {value}");
        await PublishStateAsync();
    }

    public async Task InjectErrorAsync(string code, string? message)
    {
        ErrorCode = code;
        await PublishAsync(MqttTopics.EventChannel, new EventMessage(
            EnvelopeVersion.Current, Guid.CreateVersion7(), HardwareId, Now(), EventKinds.Error, code, message, null));
        await PublishStateAsync();
        _log($"{HardwareId}: error {code}");
    }

    public async Task ClearErrorAsync()
    {
        ErrorCode = null;
        await PublishStateAsync();
    }

    /// <summary>US-24: a command arriving straight from an app on the LAN, authorised only by a server-signed grant.</summary>
    public async Task<LanCommandResponse> HandleLanCommandAsync(LanCommandRequest request)
    {
        var config = _config;
        var now = DeviceClock();
        if (config is null || now is null)
        {
            // F-3: without a server-synchronised clock, expiry cannot be checked, so nothing is accepted.
            return new LanCommandResponse(false, LanErrors.ClockUntrusted, null);
        }

        if (!LanSigning.TryVerifyGrant(request.Grant, config.ServerPublicKey, out var claims) || claims is null)
        {
            return new LanCommandResponse(false, LanErrors.BadGrant, null);
        }

        if (claims.DeviceId != HardwareId)
        {
            return new LanCommandResponse(false, LanErrors.WrongDevice, null);
        }

        if (now < claims.ValidFrom || now >= claims.ValidTo)
        {
            return new LanCommandResponse(false, LanErrors.GrantExpired, null);
        }

        if (config.RevokedGrantIds.Contains(claims.GrantId))
        {
            return new LanCommandResponse(false, LanErrors.GrantRevoked, null);
        }

        if (!LanSigning.VerifyCommand(request, claims))
        {
            return new LanCommandResponse(false, LanErrors.BadSignature, null);
        }

        if ((now.Value - request.IssuedAt).Duration() > LanSigning.MaxSkew)
        {
            return new LanCommandResponse(false, LanErrors.Stale, null);
        }

        bool fresh;
        bool success;
        string? error;
        lock (_gate)
        {
            fresh = Remember(request.CommandId);
            (success, error) = fresh ? Apply(request.Action, request.Value) : (true, null);
            if (fresh)
            {
                _pendingFacts.Add(new OfflineFactDto(
                    request.CommandId, now.Value, request.Action, request.Value, success, FactSources.Lan,
                    claims.GrantId, claims.SubjectUserId, claims.SubjectName, null));
            }
        }

        _log($"{HardwareId}: LAN {request.Action} {request.Value} by {claims.SubjectName} -> {(success ? "ok" : error)}");
        if (IsConnected)
        {
            await FlushFactsAsync();
            await PublishStateAsync();
        }

        return new LanCommandResponse(success, error, Board());
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        if (_loop is not null)
        {
            await _loop.ContinueWith(_ => { }, TaskScheduler.Default);
        }

        if (_client is { IsConnected: true })
        {
            await PublishStatusAsync(false);
            await _client.DisconnectAsync(new MqttClientDisconnectOptionsBuilder().Build());
        }

        _client?.Dispose();
        _stop.Dispose();
    }

    // ---- Internals ----

    private async Task RunAsync(CancellationToken ct)
    {
        var backoff = TimeSpan.FromSeconds(1);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!IsConnected && _time.GetUtcNow() >= _suppressReconnectUntil)
                {
                    await ConnectAsync(ct);
                    backoff = TimeSpan.FromSeconds(1);
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _log($"{HardwareId}: connect failed ({ex.Message})");
                await Delay(backoff, ct);
                backoff = TimeSpan.FromSeconds(Math.Min(30, backoff.TotalSeconds * 2));
                continue;
            }

            Tick();
            if (IsConnected)
            {
                _lastServerContact = _time.GetUtcNow();
                if (_time.GetUtcNow() - _lastReport >= ReportInterval)
                {
                    await SafeAsync(PublishStateAsync);
                }
            }
            else
            {
                RunCachedSchedule();
            }

            await Delay(TimeSpan.FromMilliseconds(250), ct);
        }
    }

    private async Task ConnectAsync(CancellationToken ct)
    {
        var offline = NvcJson.Serialize(new StatusMessage(EnvelopeVersion.Current, HardwareId, Now(), false));
        var builder = new MqttClientOptionsBuilder()
            .WithClientId(HardwareId)
            .WithCredentials(HardwareId, _password)
            .WithTcpServer(_broker.Host, _broker.Port)
            .WithCleanSession()
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(10))
            .WithTimeout(TimeSpan.FromSeconds(5))
            .WithWillTopic(MqttTopics.For(HardwareId, MqttTopics.StatusChannel))
            .WithWillPayload(offline)
            .WithWillRetain()
            .WithWillQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce);
        if (_broker.UseTls)
        {
            builder.WithTlsOptions(tls =>
            {
                tls.UseTls();
                if (_broker.AllowUntrusted)
                {
                    tls.WithAllowUntrustedCertificates(true).WithIgnoreCertificateChainErrors(true).WithCertificateValidationHandler(_ => true);
                }
            });
        }

        var result = await _client!.ConnectAsync(builder.Build(), ct);
        if (result.ResultCode != MqttClientConnectResultCode.Success)
        {
            throw new InvalidOperationException(result.ResultCode.ToString());
        }

        await _client.SubscribeAsync(new MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter(MqttTopics.For(HardwareId, MqttTopics.CommandChannel), MqttQualityOfServiceLevel.AtLeastOnce)
            .WithTopicFilter(MqttTopics.For(HardwareId, MqttTopics.ConfigChannel), MqttQualityOfServiceLevel.AtLeastOnce)
            .Build(), ct);

        await PublishStatusAsync(true);
        await FlushFactsAsync();
        await PublishStateAsync();
        _log($"{HardwareId}: connected");
    }

    private async Task OnMessageAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        if (!MqttTopics.TryParse(e.ApplicationMessage.Topic, out _, out var channel))
        {
            return;
        }

        var payload = e.ApplicationMessage.Payload.ToArray();
        _lastServerContact = _time.GetUtcNow();
        if (channel == MqttTopics.ConfigChannel)
        {
            var config = NvcJson.Deserialize<ConfigMessage>(payload);
            if (config is not null && EnvelopeVersion.IsSupported(config.Version))
            {
                _config = config;
                _clockOffset = config.ServerTime - _time.GetUtcNow();
            }

            return;
        }

        if (channel != MqttTopics.CommandChannel)
        {
            return;
        }

        var command = NvcJson.Deserialize<CommandMessage>(payload);
        if (command is null || !EnvelopeVersion.IsSupported(command.Version) || NoReply)
        {
            return;
        }

        if (ReplyDelay > TimeSpan.Zero)
        {
            await Task.Delay(ReplyDelay);
        }

        bool success;
        string? error;
        lock (_gate)
        {
            // AD-9: a duplicate is acknowledged without re-actuating.
            (success, error) = Remember(command.CommandId) ? Apply(command.Action, command.Value) : (true, null);
        }

        await PublishAsync(MqttTopics.AckChannel, new AckMessage(
            EnvelopeVersion.Current, command.CommandId, HardwareId, Now(), success, error, Board()));
        await PublishStateAsync();
    }

    private (bool Success, string? Error) Apply(CommandAction action, string? value)
    {
        switch (action)
        {
            case CommandAction.PowerOn:
                Power = PowerState.On;
                break;
            case CommandAction.PowerOff:
                Power = PowerState.Off;
                CompressorRunning = false;
                break;
            case CommandAction.SetTemperature:
                if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var celsius) || celsius is < 16 or > 32)
                {
                    return (false, "board-rejected-setpoint");
                }

                Setpoint = celsius;
                break;
            case CommandAction.SetMode:
                if (!Enum.TryParse<AcMode>(value, true, out var mode))
                {
                    return (false, "board-rejected-mode");
                }

                Mode = mode;
                break;
            case CommandAction.SetFanSpeed:
                if (!Enum.TryParse<FanSpeed>(value, true, out var fan))
                {
                    return (false, "board-rejected-fan");
                }

                Fan = fan;
                break;
        }

        CommandsExecuted++;
        return (true, null);
    }

    /// <summary>Very small thermal model so reports carry believable room temperature and compressor cycling.</summary>
    private void Tick()
    {
        lock (_gate)
        {
            if (Power == PowerState.On && Mode != AcMode.Fan)
            {
                if (RoomTemperature > Setpoint + 0.5)
                {
                    CompressorRunning = true;
                }
                else if (RoomTemperature < Setpoint - 0.5)
                {
                    CompressorRunning = false;
                }

                RoomTemperature += CompressorRunning ? -0.05 : 0.01;
            }
            else
            {
                CompressorRunning = false;
                RoomTemperature = Math.Min(AmbientTemperature, RoomTemperature + 0.01);
            }

            RoomTemperature = Math.Round(RoomTemperature, 2);
        }
    }

    /// <summary>US-25 / AD-6: only after the server has been unreachable longer than the threshold.</summary>
    private void RunCachedSchedule()
    {
        var config = _config;
        var now = DeviceClock();
        if (config is null || now is null || _time.GetUtcNow() - _lastServerContact < TimeSpan.FromSeconds(config.OfflineThresholdSeconds))
        {
            return;
        }

        foreach (var action in config.Schedule)
        {
            if (action.At > now || now.Value - action.At > TimeSpan.FromMinutes(15))
            {
                continue;
            }

            lock (_gate)
            {
                if (!_executedSchedule.Add(action.ActionId))
                {
                    continue;
                }

                var (success, _) = Apply(action.Action, null);
                _pendingFacts.Add(new OfflineFactDto(
                    Guid.CreateVersion7(), now.Value, action.Action, null, success, FactSources.Schedule, null, null, null, action.ActionId));
            }

            _log($"{HardwareId}: cached schedule {action.Reason} -> {action.Action}");
        }
    }

    private async Task FlushFactsAsync()
    {
        List<OfflineFactDto> facts;
        lock (_gate)
        {
            if (_pendingFacts.Count == 0)
            {
                return;
            }

            facts = [.. _pendingFacts];
        }

        await PublishAsync(MqttTopics.EventChannel, new EventMessage(
            EnvelopeVersion.Current, Guid.CreateVersion7(), HardwareId, Now(), EventKinds.OfflineFacts, null, null, facts));
        lock (_gate)
        {
            _pendingFacts.RemoveAll(f => facts.Contains(f));
        }

        _log($"{HardwareId}: replayed {facts.Count} offline fact(s)");
    }

    private Task PublishStateAsync()
    {
        _lastReport = _time.GetUtcNow();
        return PublishAsync(MqttTopics.StateChannel, new StateMessage(
            EnvelopeVersion.Current, Guid.CreateVersion7(), HardwareId, Now(), Board(), LanEndpoint, LanCertThumbprint));
    }

    private Task PublishStatusAsync(bool online) =>
        PublishAsync(MqttTopics.StatusChannel, new StatusMessage(EnvelopeVersion.Current, HardwareId, Now(), online), retain: true);

    private async Task PublishAsync<T>(string channel, T message, bool retain = false)
    {
        var client = _client;
        if (client is not { IsConnected: true })
        {
            return;
        }

        await client.PublishAsync(new MqttApplicationMessageBuilder()
            .WithTopic(MqttTopics.For(HardwareId, channel))
            .WithPayload(NvcJson.Serialize(message))
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .WithRetainFlag(retain)
            .Build());
    }

    private BoardStateDto Board()
    {
        lock (_gate)
        {
            return new BoardStateDto(Power, Setpoint, Mode, Fan, RoomTemperature, CompressorRunning, ErrorCode);
        }
    }

    private bool Remember(Guid commandId)
    {
        if (!_recentIds.Add(commandId))
        {
            return false;
        }

        _recentOrder.AddLast(commandId);
        if (_recentOrder.Count > RecentCommandWindow)
        {
            _recentIds.Remove(_recentOrder.First!.Value);
            _recentOrder.RemoveFirst();
        }

        return true;
    }

    private DateTimeOffset Now() => DeviceClock() ?? _time.GetUtcNow();

    /// <summary>Device clock synchronised from the server's config message; null until the first sync.</summary>
    private DateTimeOffset? DeviceClock() => _clockOffset is { } offset ? _time.GetUtcNow() + offset : null;

    private async Task SafeAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            _log($"{HardwareId}: publish failed ({ex.Message})");
        }
    }

    private static Task Delay(TimeSpan delay, CancellationToken ct) =>
        Task.Delay(delay, ct).ContinueWith(_ => { }, TaskScheduler.Default);
}
