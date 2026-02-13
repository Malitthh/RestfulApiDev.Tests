using System.Text.Json;
using System.Text.Json.Serialization;

namespace RestfulAPI.Automation;

public sealed class ObjectCreateRequest
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    // API accepts flexible "data" object
    [JsonPropertyName("data")]
    public Dictionary<string, object?>? Data { get; init; }
}

public sealed class ObjectResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("data")]
    public JsonElement? Data { get; init; }

    [JsonPropertyName("createdAt")]
    public long? CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public long? UpdatedAt { get; set; }
}

public sealed class DeleteResponse
{
    [JsonPropertyName("message")]
    public string? Message { get; init; }
}