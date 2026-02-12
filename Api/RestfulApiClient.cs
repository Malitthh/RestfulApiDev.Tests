using System.Net;
using System.Text;
using System.Text.Json;
using Xunit.Abstractions;

namespace RestfulAPI.Automation;

public sealed class RestfulApiClient
{
    private static readonly JsonSerializerOptions JsonOptions =
    new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly HttpClient _http;
    private readonly ITestOutputHelper _output;

    public RestfulApiClient(HttpClient http, ITestOutputHelper output)
    {
        _http = http;
        _http.BaseAddress ??= new Uri("https://api.restful-api.dev/");
        _http.Timeout = TimeSpan.FromSeconds(30);
        _output = output;
    }

    // --------- Public methods expected by tests ---------

    public async Task<(HttpStatusCode Status, List<ObjectResponse>? Body)> GetAllAsync()
    {
        _output.WriteLine("----- REQUEST: GET /objects -----");
        var response = await _http.GetAsync("objects");
        await LogResponse(response);

        var body = await Deserialize<List<ObjectResponse>>(response);
        return (response.StatusCode, body);
    }

    public async Task<(HttpStatusCode Status, ObjectResponse? Body)> CreateAsync(ObjectCreateRequest req)
    {
        var json = JsonSerializer.Serialize(req, JsonOptions);

        _output.WriteLine("----- REQUEST: POST /objects -----");
        _output.WriteLine(json);

        var response = await _http.PostAsync("objects",
        new StringContent(json, Encoding.UTF8, "application/json"));

        await LogResponse(response);

        var body = await Deserialize<ObjectResponse>(response);
        return (response.StatusCode, body);
    }

    public async Task<(HttpStatusCode Status, ObjectResponse? Body)> GetByIdAsync(string id)
    {
        _output.WriteLine($"----- REQUEST: GET /objects/{id} -----");

        var response = await _http.GetAsync($"objects/{id}");
        await LogResponse(response);

        var body = await Deserialize<ObjectResponse>(response);
        return (response.StatusCode, body);
    }

    public async Task<(HttpStatusCode Status, ObjectResponse? Body)> PutAsync(string id, ObjectCreateRequest req)
    {
        var json = JsonSerializer.Serialize(req, JsonOptions);

        _output.WriteLine($"----- REQUEST: PUT /objects/{id} -----");
        _output.WriteLine(json);

        var response = await _http.PutAsync($"objects/{id}",
        new StringContent(json, Encoding.UTF8, "application/json"));

        await LogResponse(response);

        var body = await Deserialize<ObjectResponse>(response);
        return (response.StatusCode, body);
    }

    public async Task<(HttpStatusCode Status, DeleteResponse? Body)> DeleteAsync(string id)
    {
        _output.WriteLine($"----- REQUEST: DELETE /objects/{id} -----");

        var response = await _http.DeleteAsync($"objects/{id}");
        await LogResponse(response);

        var body = await Deserialize<DeleteResponse>(response);
        return (response.StatusCode, body);
    }

    // --------- Logging + JSON helpers ---------

    private async Task LogResponse(HttpResponseMessage response)
    {
        var body = response.Content is null ? "" : await response.Content.ReadAsStringAsync();

        _output.WriteLine("----- RESPONSE -----");
        _output.WriteLine($"Status: {(int)response.StatusCode} {response.StatusCode}");
        if (!string.IsNullOrWhiteSpace(body))
            _output.WriteLine(body);
        _output.WriteLine("--------------------");
    }

    private static async Task<T?> Deserialize<T>(HttpResponseMessage response)
    {
        if (response.Content is null) return default;

        var text = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(text)) return default;

        try
        {
            return JsonSerializer.Deserialize<T>(text, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch
        {
            return default;
        }
    }
}