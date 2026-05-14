using GridGameAPI.UtilityInfrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System.Text.Json;

namespace GridGameAPI.Database
{
    public class IntMultiDimensionalArrayConverter : ValueConverter<int[,]?, string>
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            Converters =
            {
                new TwoDimensionalIntArrayJsonConverter()
            }
        };

        public IntMultiDimensionalArrayConverter() : base(
            v => JsonSerializer.Serialize(v, Options),
            v => JsonSerializer.Deserialize<int[,]>(v, Options)) { }
    }
}
