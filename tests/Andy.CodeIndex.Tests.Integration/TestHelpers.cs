using System.Text.Json;
using System.Text.Json.Serialization;

namespace Andy.CodeIndex.Tests.Integration;

public static class TestJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };
}
