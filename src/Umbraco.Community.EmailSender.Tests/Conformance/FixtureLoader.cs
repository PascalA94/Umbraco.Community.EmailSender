using System.Text.Json;

namespace Umbraco.Community.EmailSender.Tests.Conformance;

/// <summary>Reads the provider fixtures copied into the test output (design 9.5).</summary>
internal static class FixtureLoader
{
    public static string Root => Path.Combine(AppContext.BaseDirectory, "Fixtures");

    public static string Text(string folder, string file) => File.ReadAllText(Path.Combine(Root, folder, file));

    /// <summary>A file name, or the first file matching a prefix (so <c>response-error-auth</c> finds the synthesised one).</summary>
    public static string Find(string folder, string prefix)
        => Directory.GetFiles(Path.Combine(Root, folder), prefix + "*").Select(Path.GetFileName).Order().First()!;

    /// <summary>The union of the shapes of every <c>request-*.json</c> in the folder.</summary>
    public static HashSet<string> RequestShapes(string folder)
    {
        var shapes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.GetFiles(Path.Combine(Root, folder), "request-*.json"))
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(file));
            Shape(doc.RootElement, string.Empty, shapes);
        }

        return shapes;
    }

    /// <summary>
    /// Every value as <c>path:kind</c>, with arrays as <c>path[]</c>, so two documents compare by structure and JSON type, not
    /// by value (design 9.5, "compared by shape").
    /// </summary>
    public static HashSet<string> Shape(JsonElement element)
    {
        var shapes = new HashSet<string>(StringComparer.Ordinal);
        Shape(element, string.Empty, shapes);
        return shapes;
    }

    private static void Shape(JsonElement element, string path, HashSet<string> shapes)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var child = $"{path}.{property.Name}";
                    shapes.Add($"{child}:{Kind(property.Value)}");
                    Shape(property.Value, child, shapes);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    shapes.Add($"{path}[]:{Kind(item)}");
                    Shape(item, path + "[]", shapes);
                }

                break;
        }
    }

    private static string Kind(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.True or JsonValueKind.False => "bool",
        var kind => kind.ToString().ToLowerInvariant(),
    };
}
