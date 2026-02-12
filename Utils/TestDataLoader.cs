using System.Text.Json;

namespace RestfulAPI.Automation.Utils;

public static class TestDataLoader
{
    public static T Load<T>(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", fileName);

        if (!File.Exists(path))
            throw new FileNotFoundException($"Test data file not found: {path}");

        var json = File.ReadAllText(path);

        return JsonSerializer.Deserialize<T>(json,
        new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }
}