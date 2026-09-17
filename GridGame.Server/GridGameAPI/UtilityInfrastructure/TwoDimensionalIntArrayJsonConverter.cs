using System.Text.Json.Serialization;
using System.Text.Json;

namespace GridGameAPI.UtilityInfrastructure
{
    public class TwoDimensionalIntArrayJsonConverter : JsonConverter<int[,]>
    {
        public override int[,]? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var jsonDoc = JsonDocument.ParseValue(ref reader);

            if (jsonDoc.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new JsonException("Expected a JSON array of arrays.");
            }

            var rows = jsonDoc.RootElement.EnumerateArray().ToList();
            if (rows.Count == 0)
            {
                return new int[0, 0];
            }
            if (rows.Any(row => row.ValueKind != JsonValueKind.Array))
            {
                throw new JsonException("Expected a JSON array of arrays.");
            }

            var columnLength = rows[0].GetArrayLength();
            if (rows.Any(row => row.GetArrayLength() != columnLength))
            {
                throw new JsonException("All rows must be the same length.");
            }

            var grid = new int[rows.Count, columnLength];
            for (var row = 0; row < rows.Count; row++)
            {
                var column = 0;
                foreach (var number in rows[row].EnumerateArray())
                {
                    if (number.ValueKind != JsonValueKind.Number || !number.TryGetInt32(out var value))
                    {
                        throw new JsonException("Expected an integer.");
                    }
                    grid[row, column] = value;
                    column++;
                }
            }

            return grid;
        }

        public override void Write(Utf8JsonWriter writer, int[,] value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            for (int i = 0; i < value.GetLength(0); i++)
            {
                writer.WriteStartArray();
                for (int j = 0; j < value.GetLength(1); j++)
                {
                    writer.WriteNumberValue(value[i, j]);
                }
                writer.WriteEndArray();
            }
            writer.WriteEndArray();
        }
    }
}