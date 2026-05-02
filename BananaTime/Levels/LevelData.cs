using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Xna.Framework;

namespace BananaTime.Levels;

public sealed class LevelData
{
    public string Picture { get; set; } = "";
    public Vector2 StartPosition { get; set; }
    public List<LevelShape> Shapes { get; set; } = new();
}

public sealed class LevelShape
{
    public List<Vector2> Points { get; set; } = new();
    public bool IsKill { get; set; }
}

public static class LevelStorage
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new Vector2JsonConverter() }
    };

    public static LevelData Load(string path)
        => JsonSerializer.Deserialize<LevelData>(File.ReadAllText(path), Options)
           ?? throw new InvalidDataException("level json was empty");

    public static string Serialize(LevelData level)
        => JsonSerializer.Serialize(level, Options);
}

internal sealed class Vector2JsonConverter : JsonConverter<Vector2>
{
    public override Vector2 Read(ref Utf8JsonReader reader, System.Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException($"expected StartObject, got {reader.TokenType}");

        float x = 0f, y = 0f;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                return new Vector2(x, y);

            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException();

            var name = reader.GetString();
            reader.Read();
            switch (name)
            {
                case "x": case "X": x = reader.GetSingle(); break;
                case "y": case "Y": y = reader.GetSingle(); break;
            }
        }
        throw new JsonException("unexpected end of Vector2 object");
    }

    public override void Write(Utf8JsonWriter writer, Vector2 value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("x", value.X);
        writer.WriteNumber("y", value.Y);
        writer.WriteEndObject();
    }
}
