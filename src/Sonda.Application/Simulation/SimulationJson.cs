using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sonda.Application.Simulation;

public static class SimulationJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();
    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            WriteIndented = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return options;
    }

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Deserialize<T>(string json)
    {
        // Omit optional settings; explicit nulls would bypass non-nullable collection contracts.
        if (typeof(T) == typeof(SimulationRequest))
        {
            using var document = JsonDocument.Parse(json);
            RejectNull(document.RootElement, "$");
        }
        return JsonSerializer.Deserialize<T>(json, Options)
            ?? throw new JsonException("A non-null JSON document is required.");
    }

    private static void RejectNull(JsonElement element, string path)
    {
        if (element.ValueKind == JsonValueKind.Null)
            throw new JsonException($"Explicit null is not supported in sample input at {path}; omit optional properties instead.");
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject()) RejectNull(property.Value, path + "." + property.Name);
        if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray()) RejectNull(item, $"{path}[{index++}]");
        }
    }
    public static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Serialize(value))));
}
