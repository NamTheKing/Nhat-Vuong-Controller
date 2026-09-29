namespace NhatVuong.Contracts;

// Wire mirrors of NhatVuong.Domain enums. Names and values must match the domain; a unit test enforces it.
// Serialised as strings (see NvcJson).

public enum UserRole
{
    Administrator = 1,
    Lecturer = 2,
    ClassMonitor = 3,
    MaintenanceStaff = 4,
}

public enum PowerState
{
    Off = 0,
    On = 1,
}

public enum AcMode
{
    Cool = 0,
    Dry = 1,
    Fan = 2,
}

public enum FanSpeed
{
    Auto = 0,
    Low = 1,
    Medium = 2,
    High = 3,
}

public enum Connectivity
{
    Online = 0,
    Offline = 1,
    Fault = 2,
}

public enum CommandAction
{
    PowerOn = 0,
    PowerOff = 1,
    SetTemperature = 2,
    SetMode = 3,
    SetFanSpeed = 4,
}

public enum CommandSource
{
    App = 0,
    Scheduler = 1,
    Lan = 2,
    DeviceSchedule = 3,
}

public enum CommandResult
{
    Pending = 0,
    Succeeded = 1,
    Failed = 2,
    Rejected = 3,
    Timeout = 4,
}

public enum RejectionReason
{
    None = 0,
    NoAccess = 1,
    OutsideOperatingHours = 2,
    BelowMinimumSetpoint = 3,
    AboveMaximumSetpoint = 4,
    LecturerPrecedence = 5,
    InvalidValue = 6,
    DeviceOffline = 7,
}

public enum GrantSource
{
    Timetable = 0,
    Temporary = 1,
}

public enum IncidentKind
{
    DeviceError = 0,
    ProlongedDisconnect = 1,
    LongRun = 2,
}

public enum IncidentStatus
{
    Open = 0,
    Resolved = 1,
}

public enum PreCoolStatus
{
    Pending = 0,
    Done = 1,
    Cancelled = 2,
    Skipped = 3,
    Failed = 4,
}
