using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VFMS.Contracts.Common;

/// <summary>
/// Reads an optional time as "HH:mm" (what an HTML time input sends) or "HH:mm:ss"; writes "HH:mm:ss".
/// The default System.Text.Json converter only accepts the long form.
/// </summary>
public sealed class FlexibleTimeOnlyConverter : JsonConverter<TimeOnly?>
{
    private static readonly string[] Formats = ["HH:mm", "HH:mm:ss", "HH:mm:ss.FFFFFFF"];

    public override bool HandleNull => true;

    public override TimeOnly? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("A time must be a string in HH:mm or HH:mm:ss format.");
        var text = reader.GetString()?.Trim();
        if (string.IsNullOrEmpty(text))
            return null;
        if (TimeOnly.TryParseExact(text, Formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            return time;
        throw new JsonException($"'{text}' is not a valid time. Use HH:mm or HH:mm:ss.");
    }

    public override void Write(Utf8JsonWriter writer, TimeOnly? value, JsonSerializerOptions options)
    {
        if (value is { } time)
            writer.WriteStringValue(time.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
        else
            writer.WriteNullValue();
    }
}
