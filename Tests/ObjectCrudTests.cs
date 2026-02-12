using System.Net;
using System.Text.Json;
using RestfulApiDev.Tests.Api;
using RestfulApiDev.Tests.Fixtures;
using Xunit;

namespace RestfulApiDev.Tests.Tests;

[Collection("RestfulApiDev")]
public sealed class ObjectsCrudTests : IClassFixture<ApiFixture>
{
   private readonly RestfulApiClient _api;

   public ObjectsCrudTests(ApiFixture fixture) => _api = fixture.Client;

   // Create unique names per run to avoid collisions in shared public API.
   private static ObjectCreateRequest NewObject(string prefix = "xunit")
       => new()
       {
           Name = $"{prefix}-{Guid.NewGuid():N}",
           Data = new Dictionary<string, object?>
           {
               ["source"] = "xunit",
               ["version"] = 1,
               ["active"] = true
           }
       };

   private static void AssertOkOrCreated(HttpStatusCode status)
       => Assert.True(status is HttpStatusCode.OK or HttpStatusCode.Created,
           $"Expected 200/201 but got {(int)status} {status}");

   private static void AssertDeleteAcceptable(HttpStatusCode status)
       => Assert.True(status is HttpStatusCode.OK or HttpStatusCode.Accepted or HttpStatusCode.NoContent,
           $"Expected 200/202/204 but got {(int)status} {status}");

   private static int? TryReadInt(JsonElement? element, string property)
   {
       if (element is null || !element.Value.ValueKind.Equals(JsonValueKind.Object)) return null;
       return element.Value.TryGetProperty(property, out var prop) && prop.ValueKind == JsonValueKind.Number
           ? prop.GetInt32()
           : null;
   }

   private static bool? TryReadBool(JsonElement? element, string property)
   {
       if (element is null || !element.Value.ValueKind.Equals(JsonValueKind.Object)) return null;
       return element.Value.TryGetProperty(property, out var prop) && prop.ValueKind is JsonValueKind.True or JsonValueKind.False
           ? prop.GetBoolean()
           : null;
   }

   [Fact(DisplayName = "1) GET /objects returns a list with ids")]
   public async Task GetAll_Returns_List_With_Ids()
   {
       var (status, list) = await _api.GetAllAsync();

       Assert.Equal(HttpStatusCode.OK, status);
       Assert.NotNull(list);
       Assert.NotEmpty(list!);
       Assert.All(list!, o => Assert.False(string.IsNullOrWhiteSpace(o.Id)));
   }

   [Fact(DisplayName = "2) POST /objects returns id, echoes name, includes createdAt")]
   public async Task Post_Creates_Object_Returns_Expected_Fields()
   {
       var req = NewObject("create");
       var (status, created) = await _api.CreateAsync(req);

       AssertOkOrCreated(status);
       Assert.NotNull(created);
       Assert.False(string.IsNullOrWhiteSpace(created!.Id));
       Assert.Equal(req.Name, created.Name);
       Assert.NotNull(created.CreatedAt);

       // cleanup
       var (delStatus, _) = await _api.DeleteAsync(created.Id!);
       AssertDeleteAcceptable(delStatus);
   }

   [Fact(DisplayName = "3) GET by id returns the created object")]
   public async Task GetById_Returns_Created_Object()
   {
       var req = NewObject("getbyid");
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
       }
       finally
       {
           var (delStatus, _) = await _api.DeleteAsync(id);
           AssertDeleteAcceptable(delStatus);
       }
   }

   [Fact(DisplayName = "4) PUT updates object and persists changes")]
   public async Task Put_Updates_Object_And_Persists()
   {
       var req = NewObject("put");
       var (createStatus, created) = await _api.CreateAsync(req);

       AssertOkOrCreated(createStatus);
       Assert.NotNull(created);

       var id = created!.Id!;
       var createdAt = created.CreatedAt;

       try
       {
           var updatedReq = new ObjectCreateRequest
           {
               Name = req.Name + "-updated",
               Data = new Dictionary<string, object?>
               {
                   ["source"] = "xunit",
                   ["version"] = 2,
                   ["active"] = false,
                   ["note"] = "updated via PUT"
               }
           };

           var (putStatus, updated) = await _api.PutAsync(id, updatedReq);

           Assert.Equal(HttpStatusCode.OK, putStatus);
           Assert.NotNull(updated);
           Assert.Equal(id, updated!.Id);
           Assert.Equal(updatedReq.Name, updated.Name);
           Assert.NotNull(updated.UpdatedAt);

           // Optional stronger assertion if API provides createdAt consistently
           if (createdAt is not null && updated.UpdatedAt is not null)
               Assert.True(updated.UpdatedAt >= createdAt, "updatedAt should be >= createdAt");

           // Verify persisted
           var (getStatus, fetched) = await _api.GetByIdAsync(id);

           Assert.Equal(HttpStatusCode.OK, getStatus);
           Assert.NotNull(fetched);
           Assert.Equal(updatedReq.Name, fetched!.Name);

           // Verify data fields persisted (at least some)
           Assert.Equal(2, TryReadInt(fetched.Data, "version"));
           Assert.Equal(false, TryReadBool(fetched.Data, "active"));
       }
       finally
       {
           var (delStatus, _) = await _api.DeleteAsync(id);
           AssertDeleteAcceptable(delStatus);
       }
   }

   [Fact(DisplayName = "5) DELETE removes object and subsequent GET is not OK")]
   public async Task Delete_Removes_Object()
   {
       var req = NewObject("delete");
       var (createStatus, created) = await _api.CreateAsync(req);

       AssertOkOrCreated(createStatus);
       Assert.NotNull(created);
       var id = created!.Id!;

       var (delStatus, _) = await _api.DeleteAsync(id);
       AssertDeleteAcceptable(delStatus);

       var (getAfterStatus, _) = await _api.GetByIdAsync(id);
       Assert.NotEqual(HttpStatusCode.OK, getAfterStatus);
   }

   [Fact(DisplayName = "GET unknown id returns non-OK (contract/safety test)")]
   public async Task Get_Unknown_Id_Is_Not_Ok()
   {
       // Use a random GUID as an id. API might return 404, 400, etc.
       var unknownId = Guid.NewGuid().ToString("N");

       var (status, body) = await _api.GetByIdAsync(unknownId);

       Assert.NotEqual(HttpStatusCode.OK, status);
       // Some APIs return an error JSON; we don't enforce shape to keep test robust.
       _ = body;
   }

   [Fact(DisplayName = "POST creates data fields; GET confirms at least one data field exists")]
   public async Task Post_Data_RoundTrip_Sanity()
   {
       var req = new ObjectCreateRequest
       {
           Name = $"data-roundtrip-{Guid.NewGuid():N}",
           Data = new Dictionary<string, object?>
           {
               ["version"] = 123,
               ["flag"] = true
           }
       };

       var (createStatus, created) = await _api.CreateAsync(req);
       AssertOkOrCreated(createStatus);
       Assert.NotNull(created);
       var id = created!.Id!;
       try
       {
           var (getStatus, fetched) = await _api.GetByIdAsync(id);
           Assert.Equal(HttpStatusCode.OK, getStatus);
           Assert.NotNull(fetched);

           Assert.Equal(123, TryReadInt(fetched!.Data, "version"));
           Assert.Equal(true, TryReadBool(fetched.Data, "flag"));
       }
       finally
       {
           var (delStatus, _) = await _api.DeleteAsync(id);
           AssertDeleteAcceptable(delStatus);
       }
   }
}

// Named collection to control sharing + parallel behavior if you expand later.
[CollectionDefinition("RestfulApiDev")]
public sealed class RestfulApiDevCollectionDefinition
{
   // no code; just a marker for xUnit collection
}