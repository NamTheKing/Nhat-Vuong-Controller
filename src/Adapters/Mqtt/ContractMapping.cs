using NhatVuong.Application.Abstractions;
using NhatVuong.Application.Devices;
using NhatVuong.Contracts.Mqtt;
using Wire = NhatVuong.Contracts;
using Dom = NhatVuong.Domain;

namespace NhatVuong.Adapters.Mqtt;

/// <summary>Maps wire messages to application types. Enum values are identical by contract.</summary>
internal static class ContractMapping
{
    public static ObservedState ToObserved(this BoardStateDto dto) => new(
        (Dom.PowerState)(int)dto.Power,
        dto.Setpoint,
        (Dom.AcMode)(int)dto.Mode,
        (Dom.FanSpeed)(int)dto.Fan,
        dto.RoomTemperature,
        dto.CompressorRunning,
        dto.ErrorCode);

    public static CommandMessage ToMessage(this DeviceCommand command) => new(
        EnvelopeVersion.Current,
        command.CommandId,
        command.HardwareId,
        command.IssuedAt,
        (Wire.CommandAction)(int)command.Action,
        command.Value);

    public static ConfigMessage ToMessage(this DeviceConfig config) => new(
        EnvelopeVersion.Current,
        config.ServerTime,
        config.ServerPublicKey,
        config.OfflineThresholdSeconds,
        config.Schedule.Select(a => new CachedActionDto(a.ActionId, a.At, (Wire.CommandAction)(int)a.Action, a.Reason)).ToList(),
        config.RevokedGrantIds);

    public static OfflineFact ToFact(this OfflineFactDto dto) => new(
        dto.EventId,
        dto.OccurredAt.ToUniversalTime(),
        (Dom.CommandAction)(int)dto.Action,
        dto.Value,
        dto.Success,
        dto.Source == FactSources.Lan ? Dom.CommandSource.Lan : Dom.CommandSource.DeviceSchedule,
        dto.GrantId,
        dto.SubjectUserId,
        dto.SubjectName,
        dto.ScheduleActionId);
}
