using NhatVuong.Application.Abstractions;
using NhatVuong.Application.Notifications;
using NhatVuong.Domain;
using NhatVuong.Domain.Entities;

namespace NhatVuong.Application.Maintenance;

public sealed record IncidentView(
    Guid Id,
    Guid DeviceId,
    string DeviceName,
    string RoomCode,
    IncidentKind Kind,
    string Code,
    string? Message,
    DateTimeOffset OccurredAt,
    DateTimeOffset LastOccurredAt,
    int OccurrenceCount,
    IncidentStatus Status,
    string? ResolvedByName,
    DateTimeOffset? ResolvedAt,
    string? ResolutionNote);

/// <summary>Device errors and alerts become incidents (US-16, US-18, FR-D4); staff close them with a note (US-17).</summary>
public sealed class MaintenanceService(IDataStore store, NotificationService notifications, TimeProvider time)
{
    public const string DisconnectCode = "NVC-DISCONNECTED";
    public const string LongRunCode = "NVC-LONG-RUN";

    /// <summary>
    /// Records an occurrence. A repeat of an open incident (same device, kind and code) increments it instead of
    /// raising a second notification, so 300 modules repeating a code produce alerts, not a flood.
    /// The caller saves. Returns true when a new incident was opened.
    /// </summary>
    public async Task<bool> RecordAsync(
        Device device, IncidentKind kind, string code, string? message, DateTimeOffset occurredAt, CancellationToken ct = default)
    {
        var open = await store.Query<Incident>()
            .Where(i => i.DeviceId == device.Id && i.Kind == kind && i.Code == code && i.Status == IncidentStatus.Open)
            .FirstOrDefaultAsync(ct);

        if (open is not null)
        {
            open.OccurrenceCount++;
            if (occurredAt > open.LastOccurredAt)
            {
                open.LastOccurredAt = occurredAt;
            }

            return false;
        }

        var incident = new Incident
        {
            Id = Guid.CreateVersion7(),
            DeviceId = device.Id,
            Kind = kind,
            Code = code,
            Message = message,
            OccurredAt = occurredAt,
            LastOccurredAt = occurredAt,
            Status = IncidentStatus.Open,
        };
        store.Add(incident);

        var roomCode = device.Room?.Code ?? "?";
        var (category, title, body, roles) = kind switch
        {
            IncidentKind.DeviceError => (
                Text.Get("Notify_Category_Incident"),
                Text.Get("Notify_DeviceError_Title", code),
                Text.Get("Notify_DeviceError_Body", device.Name, roomCode, code, message ?? string.Empty),
                new[] { UserRole.MaintenanceStaff }),
            IncidentKind.ProlongedDisconnect => (
                Text.Get("Notify_Category_Alert"),
                Text.Get("Notify_Disconnect_Title"),
                message ?? Text.Get("Notify_Disconnect_Title"),
                new[] { UserRole.MaintenanceStaff }),
            _ => (
                Text.Get("Notify_Category_Alert"),
                Text.Get("Notify_LongRun_Title"),
                message ?? Text.Get("Notify_LongRun_Title"),
                new[] { UserRole.MaintenanceStaff, UserRole.Administrator }),
        };

        await notifications.NotifyRolesAsync(roles, category, title, body, incident.Id, ct);
        return true;
    }

    /// <summary>US-17: resolve with a note; the record keeps who resolved it and when.</summary>
    public async Task<Incident> ResolveAsync(Guid incidentId, Actor actor, string note, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            throw new ValidationException("NoteRequired", "A resolution note is required.");
        }

        if (note.Length > 2000)
        {
            throw new ValidationException("NoteTooLong", "The note is at most 2000 characters.");
        }

        var incident = await store.Query<Incident>().Where(i => i.Id == incidentId).FirstOrDefaultAsync(ct)
                       ?? throw new NotFoundException("IncidentNotFound", "Incident not found.");
        if (incident.Status == IncidentStatus.Resolved)
        {
            throw new ConflictException("IncidentAlreadyResolved", "The incident is already resolved.");
        }

        incident.Status = IncidentStatus.Resolved;
        incident.ResolvedBy = actor.UserId;
        incident.ResolvedByName = actor.Name;
        incident.ResolvedAt = time.GetUtcNow();
        incident.ResolutionNote = note.Trim();
        await store.SaveChangesAsync(ct);
        return incident;
    }

    public Task<List<IncidentView>> ListAsync(IncidentStatus? status, Guid? deviceId, CancellationToken ct = default)
    {
        var query = store.Query<Incident>();
        if (status is { } s)
        {
            query = query.Where(i => i.Status == s);
        }

        if (deviceId is { } id)
        {
            query = query.Where(i => i.DeviceId == id);
        }

        return query
            .OrderByDescending(i => i.LastOccurredAt)
            .Take(300)
            .Select(i => new IncidentView(
                i.Id, i.DeviceId, i.Device!.Name, i.Device.Room!.Code, i.Kind, i.Code, i.Message, i.OccurredAt,
                i.LastOccurredAt, i.OccurrenceCount, i.Status, i.ResolvedByName, i.ResolvedAt, i.ResolutionNote))
            .ToListAsync(ct);
    }
}
