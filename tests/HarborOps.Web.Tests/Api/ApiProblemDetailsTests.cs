using System.Text.Json;
using HarborOps.Web.Api;

namespace HarborOps.Web.Tests.Api;

public sealed class ApiProblemDetailsTests
{
    [Fact]
    public void Model_deserializes_problem_and_validation_errors()
    {
        const string json = """
            {
              "type": "https://harborops.test/problems/validation",
              "title": "One or more validation errors occurred.",
              "status": 400,
              "detail": "The request was invalid.",
              "instance": "/api/fleet/vessels",
              "errors": {
                "name": ["Name is required."]
              }
            }
            """;

        var result = JsonSerializer.Deserialize<ApiProblemDetails>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(result);
        Assert.Equal(400, result.Status);
        Assert.Equal("One or more validation errors occurred.", result.Title);
        Assert.Equal("Name is required.", Assert.Single(result.Errors["name"]));
    }
}
