using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using NhatVuong.Contracts;
using NhatVuong.Contracts.Api;

// Nhat Vuong device-module simulator. Examples:
//   dotnet run --project simulator                                   (the 4 demo devices seeded by the dev server)
//   dotnet run --project simulator -- --fleet 300 --prefix LOAD- --credentials fleet.json --headless
//   dotnet run --project simulator -- --provision --api https://localhost:7180 --admin admin@nhatvuong.edu.vn
//        --admin-password Demo@12345 --room A101 --count 300 --prefix LOAD- --credentials fleet.json
namespace NhatVuong.Simulator;

internal static class SimulatorProgram
{
    public static async Task<int> Main(string[] args)
    {
        var options = SimulatorOptions.Parse(args);
        if (options.ShowHelp)
        {
            Console.WriteLine(SimulatorOptions.Usage);
            return 0;
        }

        if (options.Provision)
        {
            return await Provisioning.RunAsync(options);
        }

        var credentials = options.LoadCredentials();
        var broker = new BrokerEndpoint(options.BrokerHost, options.BrokerPort, options.UseTls, options.Insecure);
        var devices = new ConcurrentDictionary<string, VirtualDevice>(StringComparer.OrdinalIgnoreCase);
        void Log(string line)
        {
            if (options.Verbose || credentials.Count <= 20)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {line}");
            }
        }

        LanServer? lan = null;
        if (options.LanPort > 0)
        {
            lan = await LanServer.StartAsync(hw => devices.GetValueOrDefault(hw), options.LanPort, options.LanHost);
            Console.WriteLine($"LAN endpoint: {lan.BaseUrl} (certificate SHA-256 {lan.Thumbprint[..16]}…)");
        }

        foreach (var (hardwareId, password) in credentials)
        {
            var device = new VirtualDevice(hardwareId, password, broker, log: Log)
            {
                ReportInterval = TimeSpan.FromSeconds(options.ReportIntervalSeconds),
                LanEndpoint = lan?.EndpointFor(hardwareId),
                LanCertThumbprint = lan?.Thumbprint,
            };
            devices[hardwareId] = device;
            await device.StartAsync();
        }

        Console.WriteLine($"Simulating {devices.Count} device(s) against mqtt{(options.UseTls ? "s" : string.Empty)}://{options.BrokerHost}:{options.BrokerPort}.");
        using var shutdown = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            shutdown.Cancel();
        };

        if (options.Headless || Console.IsInputRedirected)
        {
            await Task.Delay(Timeout.Infinite, shutdown.Token).ContinueWith(_ => { }, TaskScheduler.Default);
        }
        else
        {
            Console.WriteLine("Type 'help' for commands.");
            await ConsoleCommands.RunAsync(devices, shutdown.Token);
        }

        foreach (var device in devices.Values)
        {
            await device.DisposeAsync();
        }

        if (lan is not null)
        {
            await lan.DisposeAsync();
        }

        return 0;
    }
}

internal sealed class SimulatorOptions
{
    public const string Usage = """
        Options:
          --broker host:port        MQTT broker (default localhost:1883)
          --tls                     connect with TLS        --insecure   accept a self-signed broker certificate
          --devices A,B,C           hardware ids (default: SIM-A101-1,SIM-A101-2,SIM-A102-1,SIM-B201-1)
          --password SECRET         password shared by --devices (default: sim-device-secret, the dev seed)
          --credentials FILE        JSON map { hardwareId: password } (written by --provision)
          --report-interval SEC     state report period (default 5)
          --lan-port PORT           HTTPS LAN endpoint port, 0 to disable (default 7443)
          --lan-host HOST           host advertised to apps for LAN control (default localhost)
          --headless                no interactive console        --verbose   log every device
          --provision               register devices through the API, then exit:
              --api URL --admin EMAIL --admin-password PW --room CODE --count N [--prefix LOAD-]
        """;

    public bool ShowHelp { get; private set; }
    public string BrokerHost { get; private set; } = "localhost";
    public int BrokerPort { get; private set; } = 1883;
    public bool UseTls { get; private set; }
    public bool Insecure { get; private set; }
    public List<string> Devices { get; private set; } = ["SIM-A101-1", "SIM-A101-2", "SIM-A102-1", "SIM-B201-1"];
    public string Password { get; private set; } = "sim-device-secret";
    public string? CredentialsFile { get; private set; }
    public int ReportIntervalSeconds { get; private set; } = 5;
    public int LanPort { get; private set; } = 7443;
    public string LanHost { get; private set; } = "localhost";
    public bool Headless { get; private set; }
    public bool Verbose { get; private set; }
    public bool Provision { get; private set; }
    public bool Fleet { get; private set; }
    public string ApiUrl { get; private set; } = "https://localhost:7180";
    public string AdminEmail { get; private set; } = "admin@nhatvuong.edu.vn";
    public string AdminPassword { get; private set; } = "Demo@12345";
    public string RoomCode { get; private set; } = "A101";
    public int Count { get; private set; } = 10;
    public string Prefix { get; private set; } = "LOAD-";

    public static SimulatorOptions Parse(string[] args)
    {
        var o = new SimulatorOptions();
        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");
            switch (args[i])
            {
                case "--help" or "-h": o.ShowHelp = true; break;
                case "--broker":
                    var parts = Next().Split(':');
                    o.BrokerHost = parts[0];
                    if (parts.Length > 1) o.BrokerPort = int.Parse(parts[1], CultureInfo.InvariantCulture);
                    break;
                case "--tls": o.UseTls = true; if (o.BrokerPort == 1883) o.BrokerPort = 8883; break;
                case "--insecure": o.Insecure = true; break;
                case "--devices": o.Devices = [.. Next().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]; break;
                case "--password": o.Password = Next(); break;
                case "--credentials": o.CredentialsFile = Next(); break;
                case "--report-interval": o.ReportIntervalSeconds = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--lan-port": o.LanPort = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--lan-host": o.LanHost = Next(); break;
                case "--headless": o.Headless = true; break;
                case "--verbose": o.Verbose = true; break;
                case "--provision": o.Provision = true; break;
                case "--api": o.ApiUrl = Next().TrimEnd('/'); break;
                case "--admin": o.AdminEmail = Next(); break;
                case "--admin-password": o.AdminPassword = Next(); break;
                case "--room": o.RoomCode = Next(); break;
                case "--count": o.Count = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--prefix": o.Prefix = Next(); break;
                case "--fleet": o.Count = int.Parse(Next(), CultureInfo.InvariantCulture); o.Fleet = true; break;
                default: throw new ArgumentException($"Unknown option {args[i]}\n{Usage}");
            }
        }

        return o;
    }

    public Dictionary<string, string> LoadCredentials()
    {
        if (CredentialsFile is not null)
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(CredentialsFile))
                   ?? throw new InvalidOperationException("Empty credentials file.");
        }

        var ids = Fleet ? Enumerable.Range(1, Count).Select(i => $"{Prefix}{i:D4}").ToList() : Devices;
        return ids.ToDictionary(d => d, _ => Password, StringComparer.OrdinalIgnoreCase);
    }
}

internal static class Provisioning
{
    /// <summary>Registers devices exactly as an administrator would after scanning their QR codes (US-19).</summary>
    public static async Task<int> RunAsync(SimulatorOptions o)
    {
        using var handler = new HttpClientHandler();
        if (o.Insecure)
        {
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }

        using var http = new HttpClient(handler) { BaseAddress = new Uri(o.ApiUrl) };
        var login = await http.PostAsJsonAsync($"{ApiRoutes.Prefix}/auth/login", new LoginRequest(o.AdminEmail, o.AdminPassword), NvcJson.Options);
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<LoginResponse>(NvcJson.Options))!.AccessToken;
        http.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var rooms = await http.GetFromJsonAsync<List<RoomDto>>($"{ApiRoutes.Prefix}/rooms", NvcJson.Options) ?? [];
        var room = rooms.FirstOrDefault(r => string.Equals(r.Code, o.RoomCode, StringComparison.OrdinalIgnoreCase))
                   ?? throw new InvalidOperationException($"Room {o.RoomCode} not found.");

        var credentials = new Dictionary<string, string>();
        for (var i = 1; i <= o.Count; i++)
        {
            var hardwareId = $"{o.Prefix}{i:D4}";
            var response = await http.PostAsJsonAsync(
                $"{ApiRoutes.Prefix}/devices", new RegisterDeviceRequest($"NVC:{hardwareId}", room.Id, $"Sim {hardwareId}"), NvcJson.Options);
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"{hardwareId}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
                continue;
            }

            var created = (await response.Content.ReadFromJsonAsync<DeviceCredentialsResponse>(NvcJson.Options))!;
            credentials[created.MqttUsername] = created.MqttPassword;
        }

        var file = o.CredentialsFile ?? "simulator-fleet.json";
        await File.WriteAllTextAsync(file, JsonSerializer.Serialize(credentials, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Registered {credentials.Count} device(s) in {room.Code}; credentials written to {file} (keep it private).");
        return 0;
    }
}

internal static class ConsoleCommands
{
    private const string Help = """
        list                                  show every device's board state
        remote <id> on|off|temp <C>|mode <Cool|Dry|Fan>|fan <Auto|Low|Medium|High>   physical remote (US-09)
        error <id> <code> [message]           report an error code (US-16)      clear <id>   clear it
        noreply <id> on|off                   stop answering commands (US-10 timeout)
        delay <id> <ms>                       delay replies (NFR-01)
        drop <id> <seconds>                   lose the network (US-18, US-25)
        quit
        """;

    public static async Task RunAsync(ConcurrentDictionary<string, VirtualDevice> devices, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var line = await Task.Run(Console.ReadLine, ct).ContinueWith(t => t.IsCompletedSuccessfully ? t.Result : null, TaskScheduler.Default);
            if (line is null)
            {
                return;
            }

            var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
            {
                continue;
            }

            try
            {
                if (words[0] is "quit" or "exit")
                {
                    return;
                }

                await ExecuteAsync(devices, words);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"! {ex.Message}");
            }
        }
    }

    private static async Task ExecuteAsync(ConcurrentDictionary<string, VirtualDevice> devices, string[] w)
    {
        VirtualDevice Device() => devices.TryGetValue(w[1], out var d) ? d : throw new ArgumentException($"Unknown device {w[1]}");
        switch (w[0])
        {
            case "help":
                Console.WriteLine(Help);
                break;
            case "list":
                foreach (var d in devices.Values.OrderBy(d => d.HardwareId))
                {
                    Console.WriteLine(
                        $"{d.HardwareId,-14} {(d.IsConnected ? "online " : "OFFLINE")} {d.Power,-3} {d.Mode,-4} {d.Fan,-6} " +
                        $"set {d.Setpoint:0.0}°C room {d.RoomTemperature:0.0}°C comp {(d.CompressorRunning ? "on " : "off")} err {d.ErrorCode ?? "-"} facts {d.PendingFactCount}");
                }

                break;
            case "remote":
                var (action, value) = w[2] switch
                {
                    "on" => (CommandAction.PowerOn, (string?)null),
                    "off" => (CommandAction.PowerOff, null),
                    "temp" => (CommandAction.SetTemperature, w[3]),
                    "mode" => (CommandAction.SetMode, w[3]),
                    "fan" => (CommandAction.SetFanSpeed, w[3]),
                    _ => throw new ArgumentException("remote on|off|temp|mode|fan"),
                };
                await Device().UseRemoteAsync(action, value);
                break;
            case "error":
                await Device().InjectErrorAsync(w[2], w.Length > 3 ? string.Join(' ', w[3..]) : null);
                break;
            case "clear":
                await Device().ClearErrorAsync();
                break;
            case "noreply":
                Device().NoReply = w[2] == "on";
                break;
            case "delay":
                Device().ReplyDelay = TimeSpan.FromMilliseconds(int.Parse(w[2], CultureInfo.InvariantCulture));
                break;
            case "drop":
                await Device().DropConnectionAsync(TimeSpan.FromSeconds(int.Parse(w[2], CultureInfo.InvariantCulture)));
                break;
            default:
                Console.WriteLine(Help);
                break;
        }
    }
}
