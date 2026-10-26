using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Shouldly;

namespace TradeSim.IntegrationTests.Api;

[Collection(ApiCollection.Name)]
public class AuthAndErrorTests(ApiFactory api)
{
    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<JsonElement> ShouldBeErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.ShouldBe(status);
        var body = await ReadJsonAsync(response);
        body.GetProperty("success").GetBoolean().ShouldBeFalse();
        body.GetProperty("data").ValueKind.ShouldBe(JsonValueKind.Null);
        body.GetProperty("errors")[0].GetProperty("code").GetString().ShouldBe(code);
        return body;
    }

    [Fact]
    public async Task Login_returns_a_bearer_token_in_the_envelope()
    {
        var response = await api.CreateClient().PostAsJsonAsync("/api/v1/auth/login",
            new { username = "trader1", password = ApiFactory.TraderPassword });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await ReadJsonAsync(response);
        body.GetProperty("success").GetBoolean().ShouldBeTrue();
        body.GetProperty("errors").ValueKind.ShouldBe(JsonValueKind.Null);

        var data = body.GetProperty("data");
        data.GetProperty("accessToken").GetString().ShouldNotBeNullOrEmpty();
        data.GetProperty("tokenType").GetString().ShouldBe("Bearer");
        data.GetProperty("expiresIn").GetInt32().ShouldBe(3600);
        data.GetProperty("userId").GetGuid().ShouldNotBe(Guid.Empty);
        data.GetProperty("accountId").GetGuid().ShouldNotBe(Guid.Empty);
    }

    [Theory]
    [InlineData("trader1", "wrong-password")]
    [InlineData("nobody", "Password123!")]
    public async Task Bad_credentials_get_the_same_401(string username, string password)
    {
        var response = await api.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { username, password });

        await ShouldBeErrorAsync(response, HttpStatusCode.Unauthorized, "INVALID_CREDENTIALS");
    }

    [Fact]
    public async Task A_missing_field_is_a_validation_error_naming_the_field()
    {
        var response = await api.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { username = "trader1" });

        var body = await ShouldBeErrorAsync(response, HttpStatusCode.BadRequest, "VALIDATION_FAILED");
        body.GetProperty("errors")[0].GetProperty("field").GetString().ShouldBe("password");
    }

    [Fact]
    public async Task Malformed_json_is_a_validation_error()
    {
        var content = new StringContent("{ not json", Encoding.UTF8, "application/json");
        var response = await api.CreateClient().PostAsync("/api/v1/auth/login", content);

        await ShouldBeErrorAsync(response, HttpStatusCode.BadRequest, "VALIDATION_FAILED");
    }

    [Fact]
    public async Task Protected_endpoints_need_a_token()
    {
        var response = await api.CreateClient().GetAsync($"/api/v1/users/{Guid.NewGuid()}");

        await ShouldBeErrorAsync(response, HttpStatusCode.Unauthorized, "UNAUTHORIZED");
    }

    [Fact]
    public async Task A_tampered_token_is_rejected()
    {
        var (client, userId, _) = await api.LoginAsync("trader1");
        var token = client.DefaultRequestHeaders.Authorization!.Parameter!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token[..^4] + "AAAA");

        var response = await client.GetAsync($"/api/v1/users/{userId}");

        await ShouldBeErrorAsync(response, HttpStatusCode.Unauthorized, "UNAUTHORIZED");
    }

    [Theory]
    [InlineData("trader1", "CASH")]
    [InlineData("trader4", "MARGIN")]
    public async Task Users_can_read_their_own_profile(string username, string accountType)
    {
        var (client, userId, accountId) = await api.LoginAsync(username);

        var response = await client.GetAsync($"/api/v1/users/{userId}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var data = (await ReadJsonAsync(response)).GetProperty("data");
        data.GetProperty("username").GetString().ShouldBe(username);
        data.GetProperty("accountId").GetGuid().ShouldBe(accountId);
        data.GetProperty("accountType").GetString().ShouldBe(accountType);
    }

    [Fact]
    public async Task Users_cannot_read_someone_elses_data()
    {
        var (_, otherUserId, _) = await api.LoginAsync("trader2");
        var (client, _, _) = await api.LoginAsync("trader1");

        var response = await client.GetAsync($"/api/v1/users/{otherUserId}");

        await ShouldBeErrorAsync(response, HttpStatusCode.Forbidden, "FORBIDDEN");
    }

    [Fact]
    public async Task Unknown_routes_get_an_enveloped_404()
    {
        var response = await api.CreateClient().GetAsync("/api/v1/does-not-exist");

        await ShouldBeErrorAsync(response, HttpStatusCode.NotFound, "NOT_FOUND");
    }

    [Fact]
    public async Task Responses_carry_a_correlation_id()
    {
        var client = api.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "test-correlation-1");

        var response = await client.GetAsync("/api/v1/does-not-exist");

        response.Headers.GetValues("X-Correlation-ID").Single().ShouldBe("test-correlation-1");
    }

    [Fact]
    public async Task OpenApi_document_declares_bearer_auth_and_swagger_ui_is_served()
    {
        var client = api.CreateClient();

        var document = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        document.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer")
            .GetProperty("scheme").GetString().ShouldBe("bearer");
        document.GetProperty("paths").TryGetProperty("/api/v1/auth/login", out _).ShouldBeTrue();

        (await client.GetAsync("/swagger/index.html")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
