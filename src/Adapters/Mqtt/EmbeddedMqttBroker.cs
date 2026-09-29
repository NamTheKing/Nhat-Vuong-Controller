using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet.Protocol;
using MQTTnet.Server;
using NhatVuong.Application.Devices;
using NhatVuong.Contracts.Mqtt;

namespace NhatVuong.Adapters.Mqtt;

/// <summary>
/// In-process MQTT broker. Authenticates every module with its own credentials from the device registry and confines
/// each module to its own topics (NFR-04). TLS is enabled when a certificate is configured.
/// </summary>
public sealed class EmbeddedMqttBroker(
    IOptions<MqttOptions> options,
    IServiceScopeFactory scopes,
    ILogger<EmbeddedMqttBroker> logger) : IHostedService, IAsyncDisposable
{
    private readonly MqttOptions _options = options.Value;
    private MqttServer? _server;

    public bool IsRunning => _server?.IsStarted == true;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var builder = new MqttServerOptionsBuilder();
        if (_options.AllowPlaintext)
        {
            builder.WithDefaultEndpoint().WithDefaultEndpointPort(_options.Port);
        }
        else
        {
            builder.WithoutDefaultEndpoint();
        }

        if (!string.IsNullOrWhiteSpace(_options.CertificatePath))
        {
            var certificate = X509CertificateLoader.LoadPkcs12FromFile(_options.CertificatePath, _options.CertificatePassword);
            builder.WithEncryptedEndpoint().WithEncryptedEndpointPort(_options.TlsPort).WithEncryptionCertificate(certificate);
        }
        else if (!_options.AllowPlaintext)
        {
            throw new InvalidOperationException("Mqtt:AllowPlaintext is false but no Mqtt:CertificatePath is configured.");
        }

        _server = new MqttServerFactory().CreateMqttServer(builder.Build());
        _server.ValidatingConnectionAsync += ValidateConnectionAsync;
        _server.InterceptingPublishAsync += InterceptPublishAsync;
        _server.InterceptingSubscriptionAsync += InterceptSubscriptionAsync;
        await _server.StartAsync();
        logger.LogInformation(
            "Embedded MQTT broker started (plain: {Plain}, TLS port: {TlsPort})",
            _options.AllowPlaintext ? _options.Port : "disabled",
            string.IsNullOrWhiteSpace(_options.CertificatePath) ? "disabled" : _options.TlsPort);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_server is { IsStarted: true })
        {
            await _server.StopAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_server is not null)
        {
            await StopAsync(CancellationToken.None);
            _server.Dispose();
        }
    }

    private async Task ValidateConnectionAsync(ValidatingConnectionEventArgs e)
    {
        if (e.UserName == _options.ServerUsername)
        {
            var expected = Encoding.UTF8.GetBytes(_options.ServerPassword);
            var actual = Encoding.UTF8.GetBytes(e.Password ?? string.Empty);
            e.ReasonCode = expected.Length > 0 && CryptographicOperations.FixedTimeEquals(expected, actual)
                ? MqttConnectReasonCode.Success
                : MqttConnectReasonCode.BadUserNameOrPassword;
            return;
        }

        // A module's client id must be its hardware id, so its identity and its topics line up.
        if (string.IsNullOrEmpty(e.UserName) || e.ClientId != e.UserName)
        {
            e.ReasonCode = MqttConnectReasonCode.ClientIdentifierNotValid;
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        var registry = scope.ServiceProvider.GetRequiredService<DeviceRegistryService>();
        var valid = await registry.ValidateCredentialsAsync(e.UserName, e.Password ?? string.Empty, e.CancellationToken);
        e.ReasonCode = valid ? MqttConnectReasonCode.Success : MqttConnectReasonCode.BadUserNameOrPassword;
        if (!valid)
        {
            logger.LogWarning("Rejected MQTT connection for {ClientId}: bad credentials", e.ClientId);
        }
    }

    private Task InterceptPublishAsync(InterceptingPublishEventArgs e)
    {
        if (e.UserName == _options.ServerUsername)
        {
            return Task.CompletedTask;
        }

        var allowed = MqttTopics.TryParse(e.ApplicationMessage.Topic, out var hardwareId, out var channel)
                      && hardwareId == e.UserName
                      && MqttTopics.DevicePublishChannels.Contains(channel);
        if (!allowed)
        {
            e.ProcessPublish = false;
            logger.LogWarning("Dropped publish from {ClientId} to {Topic}", e.ClientId, e.ApplicationMessage.Topic);
        }

        return Task.CompletedTask;
    }

    private Task InterceptSubscriptionAsync(InterceptingSubscriptionEventArgs e)
    {
        if (e.UserName == _options.ServerUsername)
        {
            return Task.CompletedTask;
        }

        var allowed = MqttTopics.TryParse(e.TopicFilter.Topic, out var hardwareId, out var channel)
                      && hardwareId == e.UserName
                      && MqttTopics.DeviceSubscribeChannels.Contains(channel);
        if (!allowed)
        {
            e.ProcessSubscription = false;
            logger.LogWarning("Refused subscription from {ClientId} to {Topic}", e.ClientId, e.TopicFilter.Topic);
        }

        return Task.CompletedTask;
    }
}
