using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using NhatVuong.Client.Localization;
using NhatVuong.Contracts;
using NhatVuong.Contracts.Api;

namespace NhatVuong.Client.Services;

/// <summary>A failed API call, carrying the server's stable error code for localisation.</summary>
public sealed class ApiException(HttpStatusCode status, string? code, string? detail)
    : Exception(L.Error(code ?? (status == HttpStatusCode.Unauthorized ? "Unauthorized" : status == HttpStatusCode.Forbidden ? "Forbidden" : null), detail))
{
    public HttpStatusCode Status { get; } = status;

    public string? Code { get; } = code;
}

/// <summary>Raised when the server cannot be reached at all — the trigger for LAN fallback (US-24).</summary>
public sealed class ServerUnreachableException(Exception inner) : Exception(L.Get("Common_ServerUnreachable"), inner);

/// <summary>Typed client for the v1 REST API. HTTPS only (NFR-04); the server decides every permission (NFR-05).</summary>
public sealed class ApiClient
{
    private readonly HttpClient _http;
    private readonly SessionService _session;

    public ApiClient(SessionService session)
    {
        _session = session;
        _http = new HttpClient(CreateHandler()) { Timeout = TimeSpan.FromSeconds(15) };
    }

    public static string DefaultServerUrl =>
        DeviceInfo.Platform == DevicePlatform.Android ? "https://10.0.2.2:7180" : "https://localhost:7180";

    public static string ServerUrl
    {
        get => Preferences.Default.Get("server_url", DefaultServerUrl);
        set => Preferences.Default.Set("server_url", value.Trim().TrimEnd('/'));
    }

    // ---- Identity ----
    public Task<LoginResponse> LoginAsync(string email, string password) =>
        SendAsync<LoginResponse>(HttpMethod.Post, "/auth/login", new LoginRequest(email, password), authenticated: false);

    public Task<ControlBoundsDto> GetControlBoundsAsync() => GetAsync<ControlBoundsDto>("/control-bounds");

    // ---- Devices and commands ----
    public Task<List<DeviceDto>> GetDevicesAsync() => GetAsync<List<DeviceDto>>("/devices");

    public Task<DeviceDto> GetDeviceAsync(Guid id) => GetAsync<DeviceDto>($"/devices/{id}");

    public Task<CommandOutcomeDto> SendCommandAsync(Guid deviceId, CommandAction action, string? value) =>
        SendAsync<CommandOutcomeDto>(HttpMethod.Post, $"/devices/{deviceId}/commands", new SendCommandRequest(action, value));

    public Task<RoomCommandResultDto> SendRoomCommandAsync(Guid roomId, CommandAction action, string? value) =>
        SendAsync<RoomCommandResultDto>(HttpMethod.Post, $"/rooms/{roomId}/commands", new SendCommandRequest(action, value));

    public Task<LanGrantResponse> GetLanGrantAsync(Guid deviceId, string clientPublicKey) =>
        SendAsync<LanGrantResponse>(HttpMethod.Post, $"/devices/{deviceId}/lan-grant", new LanGrantRequest(clientPublicKey));

    // ---- Classes and pre-cool ----
    public Task<List<ClassDto>> GetMyClassesAsync() => GetAsync<List<ClassDto>>("/me/classes?days=7");

    public Task<PreCoolDto> CreatePreCoolAsync(Guid entryId, int leadMinutes) =>
        SendAsync<PreCoolDto>(HttpMethod.Post, "/precool", new CreatePreCoolRequest(entryId, leadMinutes));

    public Task CancelPreCoolAsync(Guid id) => SendAsync(HttpMethod.Delete, $"/precool/{id}");

    // ---- Maintenance and notifications ----
    public Task<List<IncidentDto>> GetIncidentsAsync(IncidentStatus status) => GetAsync<List<IncidentDto>>($"/incidents?status={status}");

    public Task ResolveIncidentAsync(Guid id, string note) => SendAsync(HttpMethod.Post, $"/incidents/{id}/resolve", new ResolveIncidentRequest(note));

    public Task<List<NotificationDto>> GetNotificationsAsync() => GetAsync<List<NotificationDto>>("/notifications");

    public Task<UnreadCountDto> GetUnreadCountAsync() => GetAsync<UnreadCountDto>("/notifications/unread-count");

    public Task MarkAllReadAsync() => SendAsync(HttpMethod.Post, "/notifications/read-all");

    // ---- Administration ----
    public Task<List<BuildingDto>> GetBuildingsAsync() => GetAsync<List<BuildingDto>>("/buildings");

    public Task CreateBuildingAsync(SaveBuildingRequest request) => SendAsync(HttpMethod.Post, "/buildings", request);

    public Task DeleteBuildingAsync(Guid id) => SendAsync(HttpMethod.Delete, $"/buildings/{id}");

    public Task<List<RoomDto>> GetRoomsAsync() => GetAsync<List<RoomDto>>("/rooms");

    public Task CreateRoomAsync(SaveRoomRequest request) => SendAsync(HttpMethod.Post, "/rooms", request);

    public Task DeleteRoomAsync(Guid id) => SendAsync(HttpMethod.Delete, $"/rooms/{id}");

    public Task<List<UserDto>> GetUsersAsync() => GetAsync<List<UserDto>>("/users");

    public Task CreateUserAsync(SaveUserRequest request) => SendAsync(HttpMethod.Post, "/users", request);

    public Task UpdateUserAsync(Guid id, SaveUserRequest request) => SendAsync(HttpMethod.Put, $"/users/{id}", request);

    public Task<DeleteUserResponse> DeleteUserAsync(Guid id) => SendAsync<DeleteUserResponse>(HttpMethod.Delete, $"/users/{id}");

    public Task<DeviceCredentialsResponse> RegisterDeviceAsync(RegisterDeviceRequest request) =>
        SendAsync<DeviceCredentialsResponse>(HttpMethod.Post, "/devices", request);

    public Task DeleteDeviceAsync(Guid id) => SendAsync(HttpMethod.Delete, $"/devices/{id}");

    public async Task<TimetableImportResultDto> ImportTimetableAsync(Stream file, string fileName)
    {
        using var content = new MultipartFormDataContent();
        using var stream = new StreamContent(file);
        stream.Headers.ContentType = new MediaTypeHeaderValue(fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
            ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
            : "text/csv");
        content.Add(stream, "file", fileName);
        using var request = new HttpRequestMessage(HttpMethod.Post, Url("/timetable/import")) { Content = content };
        using var response = await SendRawAsync(request, authenticated: true);

        // 422 carries the per-row errors (US-20-2); it is an answer, not a failure of the call.
        if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.UnprocessableEntity)
        {
            return (await response.Content.ReadFromJsonAsync<TimetableImportResultDto>(NvcJson.Options))!;
        }

        throw await ToExceptionAsync(response);
    }

    public Task<List<GrantDto>> GetGrantsAsync() => GetAsync<List<GrantDto>>("/grants?activeOnly=true");

    public Task CreateGrantAsync(CreateGrantRequest request) => SendAsync(HttpMethod.Post, "/grants", request);

    public Task RevokeGrantAsync(Guid id) => SendAsync(HttpMethod.Delete, $"/grants/{id}");

    public Task<PolicyDto> GetPolicyAsync() => GetAsync<PolicyDto>("/policy");

    public Task<PolicyDto> SavePolicyAsync(PolicyDto policy) => SendAsync<PolicyDto>(HttpMethod.Put, "/policy", policy);

    public Task<PagedDto<AuditEntryDto>> GetAuditAsync(int page, CommandResult? result) =>
        GetAsync<PagedDto<AuditEntryDto>>($"/audit?page={page}&pageSize=50{(result is { } r ? $"&result={r}" : string.Empty)}");

    public Task<RuntimeReportDto> GetRuntimeReportAsync(int year, int month) => GetAsync<RuntimeReportDto>($"/reports/runtime?year={year}&month={month}");

    public Task<LatencyStatsDto> GetLatencyAsync() => GetAsync<LatencyStatsDto>("/metrics/command-latency?last=100");

    // ---- Plumbing ----

    private Task<T> GetAsync<T>(string path) => SendAsync<T>(HttpMethod.Get, path);

    private async Task SendAsync(HttpMethod method, string path, object? body = null)
    {
        using var request = new HttpRequestMessage(method, Url(path)) { Content = body is null ? null : JsonContent.Create(body, body.GetType(), options: NvcJson.Options) };
        using var response = await SendRawAsync(request, authenticated: true);
        if (!response.IsSuccessStatusCode)
        {
            throw await ToExceptionAsync(response);
        }
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body = null, bool authenticated = true)
    {
        using var request = new HttpRequestMessage(method, Url(path)) { Content = body is null ? null : JsonContent.Create(body, body.GetType(), options: NvcJson.Options) };
        using var response = await SendRawAsync(request, authenticated);
        if (!response.IsSuccessStatusCode)
        {
            throw await ToExceptionAsync(response);
        }

        return (await response.Content.ReadFromJsonAsync<T>(NvcJson.Options))!;
    }

    private async Task<HttpResponseMessage> SendRawAsync(HttpRequestMessage request, bool authenticated)
    {
        if (authenticated && _session.AccessToken is { } token)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new ServerUnreachableException(ex);
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized && authenticated)
        {
            _session.NotifyExpired();
        }

        return response;
    }

    private static async Task<ApiException> ToExceptionAsync(HttpResponseMessage response)
    {
        string? code = null;
        string? detail = null;
        try
        {
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            code = json.RootElement.TryGetProperty("code", out var c) ? c.GetString() : null;
            detail = json.RootElement.TryGetProperty("detail", out var d) ? d.GetString() : null;
        }
        catch (JsonException)
        {
        }

        return new ApiException(response.StatusCode, code, detail);
    }

    private static string Url(string path) => $"{ServerUrl}{ApiRoutes.Prefix}{path}";

    private static HttpMessageHandler CreateHandler()
    {
        var handler = new HttpClientHandler();
#if DEBUG
        // Development only: the local dev server uses the self-signed ASP.NET Core development certificate,
        // which the Android emulator (10.0.2.2) does not trust. Release builds validate certificates normally.
        handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) =>
            errors == System.Net.Security.SslPolicyErrors.None
            || message.RequestUri?.Host is "localhost" or "10.0.2.2" or "127.0.0.1";
#endif
        return handler;
    }
}
