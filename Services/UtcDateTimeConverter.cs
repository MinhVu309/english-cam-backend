using System.Text.Json;
using System.Text.Json.Serialization;

namespace EnglishCam.Api.Services;

/// <summary>
/// Mọi DateTime lưu trong DB là UTC nhưng EF đọc lên với Kind=Unspecified nên JSON không có "Z".
/// Converter này coi Unspecified là UTC và ghi "Z" để client (Dart) không hiểu nhầm là giờ máy.
/// </summary>
public class UtcDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.GetDateTime();

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
        writer.WriteStringValue(utc);
    }
}
