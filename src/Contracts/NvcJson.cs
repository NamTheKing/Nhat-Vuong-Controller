using System.Text.Json;
using System.Text.Json.Serialization;

namespace NhatVuong.Contracts;

/// <summary>JSON settings shared by the REST API, MQTT payloads and LAN protocol: camelCase, enums as strings.</summary>
public static class NvcJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    public static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Configure(options);
        return options;
    }

    public static void Configure(JsonSerializerOptions options)
    {
        options.Converters.Add(new JsonStringEnumConverter());
        options.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
    }

    public static byte[] Serialize<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Options);

    public static T? Deserialize<T>(ReadOnlySpan<byte> json) => JsonSerializer.Deserialize<T>(json, Options);

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);
}
