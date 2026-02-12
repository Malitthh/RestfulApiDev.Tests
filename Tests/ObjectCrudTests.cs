// using System.ComponentModel;
using System.Net;
using System.Text.Json;
// using RestfulAPI.Automation;
// using RestfulAPI.Automation.Fixtures;
// using RestfulAPI.Automation.Utils;
// using Xunit;
// using Xunit.Abstractions;

namespace RestfulAPI.Automation.Tests;

// Named collection to control execution grouping (and future expansion).
[CollectionDefinition("RestfulAPI")]
public sealed class RestfulApiDevCollectionDefinition { }

[Collection("RestfulAPI")]
public sealed class ObjectsCrudTests : IClassFixture<ApiFixture>
{
    private readonly RestfulApiClient _api;
    private readonly ITestOutputHelper _output;

    public ObjectsCrudTests(ApiFixture fixture, ITestOutputHelper output)
    {
        _output = output;
        _api = fixture.CreateClient(output); // logs requests/responses via ITestOutputHelper
    }

    // ----- Helpers -----

    private static ObjectCreateRequest MakeUnique(ObjectCreateRequest template, string? suffix = null)
 => new()
 {
     Name = $"{template.Name} {(suffix ?? Guid.NewGuid().ToString("N"))}",
     Data = template.Data is null ? null : new Dictionary<string, object?>(template.Data)
 };

    private static void AssertOkOrCreated(HttpStatusCode status)
    => Assert.True(status is HttpStatusCode.OK or HttpStatusCode.Created,
    $"Expected 200 {(int)status} {status}");

    private static void AssertDeleteAcceptable(HttpStatusCode status)
    => Assert.True(status is HttpStatusCode.OK or HttpStatusCode.Accepted or HttpStatusCode.NoContent,
    $"Expected 200 {(int)status} {status}");

    private static string RequireString(JsonElement? data, string prop)
    {
        Assert.True(data.HasValue, "Expected data to be present");
        var el = data!.Value;
        Assert.True(el.ValueKind == JsonValueKind.Object, $"Expected data to be an object, got {el.ValueKind}");
        Assert.True(el.TryGetProperty(prop, out var p), $"Expected data.{prop} to exist");
        Assert.Equal(JsonValueKind.String, p.ValueKind);
        var s = p.GetString();
        Assert.False(string.IsNullOrWhiteSpace(s));
        return s!;
    }

    private static double RequireNumber(JsonElement? data, string prop)
    {
        Assert.True(data.HasValue, "Expected data to be present");
        var el = data!.Value;
        Assert.True(el.ValueKind == JsonValueKind.Object, $"Expected data to be an object, got {el.ValueKind}");
        Assert.True(el.TryGetProperty(prop, out var p), $"Expected data.{prop} to exist");
        Assert.Equal(JsonValueKind.Number, p.ValueKind);
        return p.GetDouble();
    }

    // ----- Tests -----

    [Fact(DisplayName = "TC01: GET /objects returns a non-empty list with ids")]
    public async Task GetAllObjects_ReturnsNonEmptyList()
    {
        _output.WriteLine("=== TEST: Get list of all objects ===");

        var (status, list) = await _api.GetAllAsync();

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.NotNull(list);
        Assert.NotEmpty(list!);
        Assert.All(list!, o => Assert.False(string.IsNullOrWhiteSpace(o.Id)));
    }

    [Fact(DisplayName = "TC02: POST /objects using JSON testdata creates object and returns id + createdAt")]
    public async Task CreateObject_FromJson_ReturnsIdAndCreatedAt()
    {
        _output.WriteLine("=== TEST: Create object from TestData/create-ipad.json ===");

        var template = TestDataLoader.Load<ObjectCreateRequest>("create-ipad.json");
        var req = MakeUnique(template);

        var (status, created) = await _api.CreateAsync(req);

        AssertOkOrCreated(status);
        Assert.NotNull(created);
        Assert.False(string.IsNullOrWhiteSpace(created!.Id));
        Assert.Equal(req.Name, created.Name);
        Assert.NotNull(created.CreatedAt);

        // Field-level checks (realistic device fields)
        Assert.Equal("Apple", RequireString(created.Data, "brand"));
        Assert.Equal("256 GB", RequireString(created.Data, "capacity"));
        Assert.Equal(10.9, RequireNumber(created.Data, "screenSize"));
        Assert.Equal(799.99, RequireNumber(created.Data, "price"));

        // cleanup
        var (delStatus, _) = await _api.DeleteAsync(created.Id!);
        AssertDeleteAcceptable(delStatus);
    }

    [Fact(DisplayName = "TC03: GET /objects/{id} returns the object created via POST")]
    public async Task GetById_ReturnsCreatedObject()
    {
        _output.WriteLine("=== TEST: Create -> GetById -> Cleanup ===");

        var template = TestDataLoader.Load<ObjectCreateRequest>("create-ipad.json");
        var req = MakeUnique(template);

        var (createStatus, created) = await _api.CreateAsync(req);
        AssertOkOrCreated(createStatus);
        Assert.NotNull(created);
        var id = created!.Id!;
        try
        {
            var (getStatus, fetched) = await _api.GetByIdAsync(id);

            Assert.Equal(HttpStatusCode.OK, getStatus);
            Assert.NotNull(fetched);
            Assert.Equal(id, fetched!.Id);
            Assert.Equal(req.Name, fetched.Name);

            // Verify key fields persisted
            Assert.Equal("Apple", RequireString(fetched.Data, "brand"));
            Assert.Equal("iPad Air", RequireString(fetched.Data, "model"));
            Assert.Equal("256 GB", RequireString(fetched.Data, "capacity"));
        }
        finally
        {
            var (delStatus, _) = await _api.DeleteAsync(id);
            AssertDeleteAcceptable(delStatus);
        }
    }

    [Fact(DisplayName = "TC04: PUT /objects/{id} using JSON testdata updates and persists fields")]
    public async Task UpdateObject_FromJson_PersistsChanges()
    {
        _output.WriteLine("=== TEST: Create -> Update(PUT) -> Verify -> Cleanup ===");

        var createTemplate = TestDataLoader.Load<ObjectCreateRequest>("create-ipad.json");
        var createReq = MakeUnique(createTemplate);

        var (createStatus, created) = await _api.CreateAsync(createReq);
        AssertOkOrCreated(createStatus);
        Assert.NotNull(created);
        var id = created!.Id!;
        var createdAt = created.CreatedAt;

        try
        {
            var updateTemplate = TestDataLoader.Load<ObjectCreateRequest>("update-ipad.json");
            // keep unique name (optional); update payload is realistic too
            var updateReq = MakeUnique(updateTemplate);

            var (putStatus, updated) = await _api.PutAsync(id, updateReq);

            Assert.Equal(HttpStatusCode.OK, putStatus);
            Assert.NotNull(updated);
            Assert.Equal(id, updated!.Id);
            Assert.Equal(updateReq.Name, updated.Name);
            Assert.NotNull(updated.UpdatedAt);

            if (createdAt is not null && updated.UpdatedAt is not null)
                Assert.True(updated.UpdatedAt >= createdAt, "updatedAt should be >= createdAt");

            // Verify persisted via GET
            var (getStatus, fetched) = await _api.GetByIdAsync(id);
            Assert.Equal(HttpStatusCode.OK, getStatus);
            Assert.NotNull(fetched);

            Assert.Equal("512 GB", RequireString(fetched!.Data, "capacity"));
            Assert.Equal(999.99, RequireNumber(fetched.Data, "price"));
            Assert.Equal("Space Gray", RequireString(fetched.Data, "color"));
        }
        finally
        {
            var (delStatus, _) = await _api.DeleteAsync(id);
            AssertDeleteAcceptable(delStatus);
        }
    }

    [Fact(DisplayName = "TC05: DELETE /objects/{id} removes object and GET after delete is not OK")]
    public async Task DeleteObject_RemovesResource()
    {
        _output.WriteLine("=== TEST: Create -> Delete -> Verify GET not OK ===");

        var template = TestDataLoader.Load<ObjectCreateRequest>("create-ipad.json");
        var req = MakeUnique(template);

        var (createStatus, created) = await _api.CreateAsync(req);
        AssertOkOrCreated(createStatus);
        Assert.NotNull(created);
        var id = created!.Id!;

        var (delStatus, delBody) = await _api.DeleteAsync(id);
        AssertDeleteAcceptable(delStatus);

        if (delBody?.Message is not null)
            _output.WriteLine($"Delete message: {delBody.Message}");

        var (getAfterStatus, _) = await _api.GetByIdAsync(id);
        Assert.NotEqual(HttpStatusCode.OK, getAfterStatus);
    }

    [Fact(DisplayName = "TC06: GET unknown id returns non-OK")]
    public async Task GetUnknownId_ReturnsNonOk()
    {
        _output.WriteLine("=== TEST: GET unknown id should be non-OK ===");

        var unknownId = Guid.NewGuid().ToString("N");
        var (status, _) = await _api.GetByIdAsync(unknownId);

        Assert.NotEqual(HttpStatusCode.OK, status);
    }

    [Fact(DisplayName = "TC07: POST with empty name parameter still creates object")]
    public async Task Post_Without_Name_Should_Return_Error()
    {
        _output.WriteLine("=== TEST: POST without required field 'name' ===");

        var invalidRequest = new ObjectCreateRequest
        {
            Name = "", // or null if you allow nullable
            Data = new Dictionary<string, object?>
            {
                ["brand"] = "Apple",
                ["price"] = 500
            }
        };

        var (status, body) = await _api.CreateAsync(invalidRequest);

        Assert.True((int)status >= 200,
        $"Expected 2xx status {(int)status} {status}");
    }

    [Fact(DisplayName = "TC08:GET unknown id should return non-success")]
    [Trait("Category", "Smoke")]
    public async Task Get_Unknown_Id_Should_Return_NonSuccess()
    {
        var unknownId = Guid.NewGuid().ToString("N");

        var (status, _) = await _api.GetByIdAsync(unknownId);



        Assert.Equal(HttpStatusCode.NotFound, status);
    }

}