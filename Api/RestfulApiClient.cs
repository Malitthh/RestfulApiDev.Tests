using System.Net;
using System.Text;
using System.Text.Json;

namespace RestfulApiDev.Tests.Api;

public sealed class RestfulApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;

    public RestfulApiClient(HttpClient http)
    {
        _http = http;
        _http.BaseAddress ??= new Uri("https://api.restful-api.dev/");
        _http.Timeout = TimeSpan.FromSeconds(30);
    }

    // ---- Public API methods ----

    public Task<HttpResponseMessage> GetAllRawAsync() => SendWithRetryAsync(() => _http.GetAsync("objects"));

    public async Task<(HttpStatusCode Status, List<ObjectResponse>? Body)> GetAllAsync()
    {
        using var resp = await GetAllRawAsync();
        return (resp.StatusCode, await ReadJsonOrNullAsync<List<ObjectResponse>>(resp));
    }

    public async Task<(HttpStatusCode Status, ObjectResponse? Body)> CreateAsync(ObjectCreateRequest req)
    {
        var json = JsonSerializer.Serialize(req, JsonOptions);
        using var resp = await SendWithRetryAsync(() =>
        _http.PostAsync("objects", new StringContent(json, Encoding.UTF8, "application/json")));

        return (resp.StatusCode, await ReadJsonOrNullAsync<ObjectResponse>(resp));
    }

    public async Task<(HttpStatusCode Status, ObjectResponse? Body)> GetByIdAsync(string id)
    {
        using var resp = await SendWithRetryAsync(() => _http.GetAsync($"objects/{Uri.EscapeDataString(id)}"));
        return (resp.StatusCode, await ReadJsonOrNullAsync<ObjectResponse>(resp));
    }

    public async Task<(HttpStatusCode Status, ObjectResponse? Body)> PutAsync(string id, ObjectCreateRequest req)
    {
        var json = JsonSerializer.Serialize(req, JsonOptions);
        using var resp = await SendWithRetryAsync(() =>
        _http.PutAsync($"objects/{Uri.EscapeDataString(id)}", new StringContent(json, Encoding.UTF8, "application/json")));

        return (resp.StatusCode, await ReadJsonOrNullAsync<ObjectResponse>(resp));
    }

    public async Task<(HttpStatusCode Status, DeleteResponse? Body)> DeleteAsync(string id)
    {
        using var resp = await SendWithRetryAsync(() => _http.DeleteAsync($"objects/{Uri.EscapeDataString(id)}"));
        return (resp.StatusCode, await ReadJsonOrNullAsync<DeleteResponse>(resp));
    }

    // ---- Helpers ----

    private static async Task<T?> ReadJsonOrNullAsync<T>(HttpResponseMessage resp)
    {
        if (resp.Content is null) return default;
        var text = await resp.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(text)) return default;

        try { return JsonSerializer.Deserialize<T>(text, JsonOptions); }
        catch { return default; }
    }

    /// <summary>
       /// Minimal retry to reduce flakiness against public APIs. Retries transient status codes + network issues.
       /// </summary>
    private static async Task<HttpResponseMessage> SendWithRetryAsync(Func<Task<HttpResponseMessage>> action)
    {
        const int maxAttempts = 3;
        var delay = TimeSpan.FromMilliseconds(250);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var resp = await action();

                if (IsTransient(resp.StatusCode) && attempt < maxAttempts)
                {
                    resp.Dispose();
                    await Task.Delay(delay);
                    delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * 2);
                    continue;
                }

                return resp;
            }
            catch when (attempt < maxAttempts)
            {
                await Task.Delay(delay);
                delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * 2);
            }
        }

        // If we got here, last attempt threw; let it bubble naturally.
        return await action();
    }

    private static bool IsTransient(HttpStatusCode code)
    {
        var n = (int)code;
        return n == 429 || (n >= 500 && n <= 599);
    }
}