using Microsoft.Extensions.DependencyInjection;
using NhatVuong.Application.Abstractions;

namespace NhatVuong.Adapters.Mqtt;

public static class MqttExtensions
{
    public static IServiceCollection AddNvcMqtt(this IServiceCollection services, MqttOptions options)
    {
        services.AddOptions<MqttOptions>().Configure(o =>
        {
            o.EmbeddedBroker = options.EmbeddedBroker;
            o.Host = options.Host;
            o.Port = options.Port;
            o.AllowPlaintext = options.AllowPlaintext;
            o.TlsPort = options.TlsPort;
            o.UseTls = options.UseTls;
            o.CertificatePath = options.CertificatePath;
            o.CertificatePassword = options.CertificatePassword;
            o.AllowUntrustedCertificates = options.AllowUntrustedCertificates;
            o.ServerUsername = options.ServerUsername;
            o.ServerPassword = options.ServerPassword;
            o.IngestWorkers = options.IngestWorkers;
        });

        // The broker is registered first so it is listening before the gateway connects.
        if (options.EmbeddedBroker)
        {
            services.AddSingleton<EmbeddedMqttBroker>();
            services.AddHostedService(sp => sp.GetRequiredService<EmbeddedMqttBroker>());
        }

        services.AddSingleton<MqttDeviceGateway>();
        services.AddSingleton<IDevicePort>(sp => sp.GetRequiredService<MqttDeviceGateway>());
        services.AddHostedService(sp => sp.GetRequiredService<MqttDeviceGateway>());
        return services;
    }
}
