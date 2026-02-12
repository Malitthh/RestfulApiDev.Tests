using RestfulApiDev.Tests.Api;

namespace RestfulApiDev.Tests.Fixtures;

public sealed class ApiFixture : IDisposable
{
    public RestfulApiClient Client { get; }

    private readonly HttpClient _http;

    public ApiFixture()
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri("https://api.restful-api.dev/"),
            Timeout = TimeSpan.FromSeconds(30),
        };

        Client = new RestfulApiClient(_http);
    }

    public void Dispose() => _http.Dispose();
}