using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NhatVuong.Application.Abstractions;
using NhatVuong.Domain.Entities;

namespace NhatVuong.Adapters.Notification;

public sealed class NotificationOptions
{
    public const string Section = "Notifications";

    /// <summary>SMTP host for e-mail delivery; leave empty to deliver in-app (and to the log) only.</summary>
    public string? SmtpHost { get; set; }

    public int SmtpPort { get; set; } = 587;

    public bool SmtpUseSsl { get; set; } = true;

    public string? SmtpUser { get; set; }

    public string? SmtpPassword { get; set; }

    public string From { get; set; } = "no-reply@nhatvuong.edu.vn";
}

/// <summary>Writes every outbound notification to the structured log.</summary>
public sealed class LogNotificationSender(ILogger<LogNotificationSender> logger) : INotificationPort
{
    public Task DeliverAsync(OutboundNotification notification, IReadOnlyList<User> recipients, CancellationToken ct)
    {
        logger.LogInformation(
            "Notification [{Category}] {Title} -> {Recipients}",
            notification.Category,
            notification.Title,
            string.Join(", ", recipients.Select(r => r.Email)));
        return Task.CompletedTask;
    }
}

/// <summary>E-mail delivery via SMTP (STARTTLS by default, NFR-04).</summary>
public sealed class SmtpNotificationSender(IOptions<NotificationOptions> options) : INotificationPort
{
    public async Task DeliverAsync(OutboundNotification notification, IReadOnlyList<User> recipients, CancellationToken ct)
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.SmtpHost) || recipients.Count == 0)
        {
            return;
        }

        using var client = new SmtpClient(o.SmtpHost, o.SmtpPort) { EnableSsl = o.SmtpUseSsl };
        if (!string.IsNullOrEmpty(o.SmtpUser))
        {
            client.Credentials = new NetworkCredential(o.SmtpUser, o.SmtpPassword);
        }

        using var message = new MailMessage { From = new MailAddress(o.From), Subject = notification.Title, Body = notification.Body };
        foreach (var recipient in recipients)
        {
            message.Bcc.Add(recipient.Email);
        }

        await client.SendMailAsync(message, ct);
    }
}

public sealed class CompositeNotificationSender(IEnumerable<INotificationChannel> channels) : INotificationPort
{
    public async Task DeliverAsync(OutboundNotification notification, IReadOnlyList<User> recipients, CancellationToken ct)
    {
        foreach (var channel in channels)
        {
            await channel.Sender.DeliverAsync(notification, recipients, ct);
        }
    }
}

public interface INotificationChannel
{
    INotificationPort Sender { get; }
}

internal sealed class NotificationChannel<T>(T sender) : INotificationChannel
    where T : INotificationPort
{
    public INotificationPort Sender => sender;
}

public static class NotificationExtensions
{
    public static IServiceCollection AddNvcNotifications(this IServiceCollection services, NotificationOptions options)
    {
        services.AddOptions<NotificationOptions>().Configure(o =>
        {
            o.SmtpHost = options.SmtpHost;
            o.SmtpPort = options.SmtpPort;
            o.SmtpUseSsl = options.SmtpUseSsl;
            o.SmtpUser = options.SmtpUser;
            o.SmtpPassword = options.SmtpPassword;
            o.From = options.From;
        });
        services.AddSingleton<LogNotificationSender>();
        services.AddSingleton<INotificationChannel, NotificationChannel<LogNotificationSender>>();
        if (!string.IsNullOrWhiteSpace(options.SmtpHost))
        {
            services.AddSingleton<SmtpNotificationSender>();
            services.AddSingleton<INotificationChannel, NotificationChannel<SmtpNotificationSender>>();
        }

        services.AddSingleton<INotificationPort, CompositeNotificationSender>();
        return services;
    }
}
