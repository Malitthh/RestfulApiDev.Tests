// using RestfulAPI.Automation;
// using Xunit.Abstractions;

namespace RestfulAPI.Automation.Fixtures;

public sealed class ApiFixture
{
    public RestfulApiClient CreateClient(ITestOutputHelper output)
    {
        var http = new HttpClient
        {
            BaseAddress = new Uri("https://api.restful-api.dev/"),
            Timeout = TimeSpan.FromSeconds(30)
        };

        return new RestfulApiClient(http, output);
    }
}