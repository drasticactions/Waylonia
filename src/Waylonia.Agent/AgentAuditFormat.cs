using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Waylonia.Agent;

internal static class AgentAuditFormat
{
    public static bool HidesText(string method) => method is "input/text" or "seat/text" or "clipboard/write";

    public static string Hash(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    public static string Stamp(DateTimeOffset time) =>
        time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    public static string FileName(DateTimeOffset started, int pid) =>
        $"{started.UtcDateTime.ToString("yyyy-MM-dd-HHmmss", CultureInfo.InvariantCulture)}-{pid.ToString(CultureInfo.InvariantCulture)}.jsonl";

    public static string Call(
        DateTimeOffset time, string method, ReadOnlySpan<byte> parameters, bool keepText, string? error, double ms,
        string? before, string? after, string? approval)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("t", Stamp(time));
            writer.WriteString("method", method);
            writer.WritePropertyName("params");
            WriteParams(writer, method, parameters, keepText);
            writer.WriteBoolean("ok", error is null);
            if (error is not null)
            {
                writer.WriteString("error", error);
            }

            writer.WriteNumber("ms", Math.Round(ms, 1));
            WriteNullable(writer, "before", before);
            WriteNullable(writer, "after", after);
            WriteNullable(writer, "approval", approval);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    public static string Event(DateTimeOffset time, string name, IReadOnlyList<(string Key, string? Value)> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("t", Stamp(time));
            writer.WriteString("event", name);
            foreach (var (key, value) in fields)
            {
                WriteNullable(writer, key, value);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteNullable(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null)
        {
            writer.WriteNull(name);
        }
        else
        {
            writer.WriteString(name, value);
        }
    }

    private static void WriteParams(Utf8JsonWriter writer, string method, ReadOnlySpan<byte> parameters, bool keepText)
    {
        if (parameters.IsEmpty)
        {
            writer.WriteStartObject();
            writer.WriteEndObject();
            return;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(parameters.ToArray());
        }
        catch (JsonException)
        {
            writer.WriteStringValue(Encoding.UTF8.GetString(parameters));
            return;
        }

        using (document)
        {
            var root = document.RootElement;
            if (keepText || !HidesText(method) || root.ValueKind != JsonValueKind.Object)
            {
                root.WriteTo(writer);
                return;
            }

            writer.WriteStartObject();
            foreach (var property in root.EnumerateObject())
            {
                if (property.Name == "text" && property.Value.ValueKind == JsonValueKind.String)
                {
                    var text = property.Value.GetString()!;
                    writer.WriteStartObject("text");
                    writer.WriteNumber("length", text.Length);
                    writer.WriteString("sha256", Hash(text));
                    writer.WriteEndObject();
                }
                else
                {
                    property.WriteTo(writer);
                }
            }

            writer.WriteEndObject();
        }
    }
}
