using System.ComponentModel;
using System.Net;
using System.Text.Json;
using RestfulAPI.Automation;
using RestfulAPI.Automation.Fixtures;
using RestfulAPI.Automation.Utils;
using Xunit;
using Xunit.Abstractions;

namespace RestfulAPI.Automation.Tests;

//Named collection to control execution grouping (and future expansion).
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
        _api = fixture.CreateClient(output);
    }

    //Helpers
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
        Assert.True(el.ValueKind == JsonValueKind.Object, $"Expected data to be an object {el.ValueKind}");
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
        Assert.True(el.ValueKind == JsonValueKind.Object, $"Expected data to be an object {el.ValueKind}");
        Assert.True(el.TryGetProperty(prop, out var p), $"Expected data.{prop} to exist");
        Assert.Equal(JsonValueKind.Number, p.ValueKind);
        return p.GetDouble();
    }

    //Tests
    [Fact(DisplayName = "TC01: GET-objects returns a non-empty list with ids")]
    public async Task GetAllObjects_ReturnsNonEmptyList()
    {
        var (status, list) = await _api.GetAllAsync();
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.NotNull(list);
        Assert.NotEmpty(list!);
        Assert.All(list!, o => Assert.False(string.IsNullOrWhiteSpace(o.Id)));
    }

    //TC02–TC05 are designed as independent CRUD validation tests.
    //Each test creates and cleans up its own data to ensure isolation.
    [Fact(DisplayName = "TC02: POST-objects using JSON testdata creates object and returns id")]
    public async Task CreateObject_FromJson_ReturnsId()
    {
        var template = TestDataLoader.Load<ObjectCreateRequest>("create-ipad.json");
        var req = MakeUnique(template);

        var (status, created) = await _api.CreateAsync(req);

        AssertOkOrCreated(status);
        Assert.NotNull(created);
        Assert.False(string.IsNullOrWhiteSpace(created!.Id));
        Assert.Equal(req.Name, created.Name);
        Assert.NotNull(created.CreatedAt);

        Assert.Equal("Apple", RequireString(created.Data, "brand"));
        Assert.Equal("256 GB", RequireString(created.Data, "capacity"));
        Assert.Equal(10.9, RequireNumber(created.Data, "screenSize"));
        Assert.Equal(799.99, RequireNumber(created.Data, "price"));

        //cleanup
        var (delStatus, _) = await _api.DeleteAsync(created.Id!);
        AssertDeleteAcceptable(delStatus);
    }

    [Fact(DisplayName = "TC03: GET-objects {id} returns the object created via POST")]
    public async Task GetById_ReturnsCreatedObject()
    {
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

            // Validate key fields
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

    [Fact(DisplayName = "TC04: PUT-objects {id} using JSON testdata updates and validate fields")]
    public async Task UpdateObject_FromJson_ValidateChanges()
    {
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
            var updateReq = MakeUnique(updateTemplate);
            var (putStatus, updated) = await _api.PutAsync(id, updateReq);

            Assert.Equal(HttpStatusCode.OK, putStatus);
            Assert.NotNull(updated);
            Assert.Equal(id, updated!.Id);
            Assert.Equal(updateReq.Name, updated.Name);
            Assert.NotNull(updated.UpdatedAt);

            if (createdAt is not null && updated.UpdatedAt is not null)
                Assert.True(updated.UpdatedAt >= createdAt, "updatedAt should be >= createdAt");
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

    [Fact(DisplayName = "TC05: DELETE-objects {id} removes object")]
    public async Task DeleteObject_RemovesResource()
    {
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

    //Additional edge case tests
    [Fact(DisplayName = "TC06: GET-object unknown {id} returns non-OK")]
    public async Task GetUnknownId_ReturnsNonOk()
    {
        var unknownId = Guid.NewGuid().ToString("N");
        var (status, _) = await _api.GetByIdAsync(unknownId);
        Assert.NotEqual(HttpStatusCode.OK, status);
    }

    [Fact(DisplayName = "TC07:GET unknown {id} should return non-success")]
    public async Task Get_Unknown_Id_Should_Return_NonSuccess()
    {
        var unknownId = Guid.NewGuid().ToString("N");
        var (status, _) = await _api.GetByIdAsync(unknownId);
        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    //This test validates the complete object lifecycle (Create → Get → Update → Delete)
    //within a single flow.
    [Fact(DisplayName = "TC08: Object full lifecycle validation (Create, Get, Update, Delete)")]
    [Trait("Category", "Lifecycle")]
    public async Task Object_FullLifecycle_Validation()
    {
        //CREATE
        var createTemplate = TestDataLoader.Load<ObjectCreateRequest>("create-ipad.json");
        var createReq = MakeUnique(createTemplate);

        var (createStatus, created) = await _api.CreateAsync(createReq);
        AssertOkOrCreated(createStatus);
        Assert.NotNull(created);
        Assert.False(string.IsNullOrWhiteSpace(created!.Id));

        var id = created.Id!;
        var createdAt = created.CreatedAt;

        //GET
        var (getStatus, fetchedAfterCreate) = await _api.GetByIdAsync(id);
        Assert.Equal(HttpStatusCode.OK, getStatus);
        Assert.NotNull(fetchedAfterCreate);
        Assert.Equal(id, fetchedAfterCreate!.Id);
        Assert.Equal(createReq.Name, fetchedAfterCreate.Name);

        Assert.Equal("Apple", RequireString(fetchedAfterCreate.Data, "brand"));
        Assert.Equal("256 GB", RequireString(fetchedAfterCreate.Data, "capacity"));

        //UPDATE
        var updateTemplate = TestDataLoader.Load<ObjectCreateRequest>("update-ipad.json");
        var updateReq = MakeUnique(updateTemplate);

        var (updateStatus, updated) = await _api.PutAsync(id, updateReq);
        Assert.Equal(HttpStatusCode.OK, updateStatus);
        Assert.NotNull(updated);
        Assert.Equal(id, updated!.Id);
        Assert.Equal(updateReq.Name, updated.Name);
        Assert.NotNull(updated.UpdatedAt);

        if (createdAt is not null && updated.UpdatedAt is not null)
            Assert.True(updated.UpdatedAt >= createdAt, "updatedAt should be >= createdAt");

        //GET(after update)
        var (getAfterUpdateStatus, fetchedAfterUpdate) = await _api.GetByIdAsync(id);
        Assert.Equal(HttpStatusCode.OK, getAfterUpdateStatus);
        Assert.NotNull(fetchedAfterUpdate);

        Assert.Equal("512 GB", RequireString(fetchedAfterUpdate!.Data, "capacity"));
        Assert.Equal(999.99, RequireNumber(fetchedAfterUpdate.Data, "price"));
        Assert.Equal("Space Gray", RequireString(fetchedAfterUpdate.Data, "color"));

        //DELETE
        var (deleteStatus, _) = await _api.DeleteAsync(id);
        AssertDeleteAcceptable(deleteStatus);
        var (getAfterDeleteStatus, _) = await _api.GetByIdAsync(id);
        Assert.Equal(HttpStatusCode.NotFound, getAfterDeleteStatus);
    }


}