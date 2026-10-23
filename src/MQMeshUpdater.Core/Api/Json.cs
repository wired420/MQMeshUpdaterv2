using System.Text.Json;
using System.Text.Json.Serialization;

namespace MQMeshUpdater.Core.Api;

public static class Json
{
    /// <summary>
    /// Options matching the API's snake_case JSON, also used for local caches.
    /// </summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = null,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    public static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Options) ?? throw new JsonException($"Empty {typeof(T).Name} JSON.");

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
}

/// <summary>
/// Reads "files" as an object, or as empty when the API sends [].
/// </summary>
public sealed class ZoneFilesConverter : JsonConverter<ZoneFiles>
{
    public override ZoneFiles Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return new ZoneFiles();

            case JsonTokenType.StartArray:
                reader.Skip();
                return new ZoneFiles();

            case JsonTokenType.StartObject:
                var files = new ZoneFiles();

                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    var name = reader.GetString();
                    reader.Read();

                    if (name == FileTypes.Mesh)
                    {
                        files.Mesh = JsonSerializer.Deserialize<FileSummary>(ref reader, options);
                    }
                    else if (name == FileTypes.Doors)
                    {
                        files.Doors = JsonSerializer.Deserialize<FileSummary>(ref reader, options);
                    }
                    else
                    {
                        reader.Skip();
                    }
                }

                return files;

            default:
                throw new JsonException($"Unexpected {reader.TokenType} for zone files.");
        }
    }

    public override void Write(Utf8JsonWriter writer, ZoneFiles value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        if (value.Mesh is not null)
        {
            writer.WritePropertyName(FileTypes.Mesh);
            JsonSerializer.Serialize(writer, value.Mesh, options);
        }

        if (value.Doors is not null)
        {
            writer.WritePropertyName(FileTypes.Doors);
            JsonSerializer.Serialize(writer, value.Doors, options);
        }

        writer.WriteEndObject();
    }
}
