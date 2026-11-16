using System.Net;
using System.Net.Http.Json;
using LogAnalytics.IntegrationTests.Support;

namespace LogAnalytics.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class AuthTests(ApiFactory factory)
{
    [Fact]
    public async Task Signing_in_sets_an_http_only_strict_cookie()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new { email = "ADMIN@example.com", password = ApiFactory.AdminPassword });
        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("la.session=", StringComparison.Ordinal));
        var me = await (await client.GetAsync("/api/me")).ReadAsync<UserDto>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Admin", me.Role);
    }

    [Theory]
    [InlineData(ApiFactory.AdminEmail, "wrong-password-123")]
    [InlineData("nobody@example.com", "wrong-password-123")]
    public async Task Wrong_credentials_get_the_same_answer(string email, string password)
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("invalid_credentials", await response.ProblemCodeAsync());
    }

    [Fact]
    public async Task Without_a_session_the_api_answers_401_instead_of_redirecting()
    {
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        var response = await client.GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Signing_out_ends_the_session()
    {
        using var client = await factory.AdminAsync();

        await client.PostAsync("/api/auth/logout", null);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me")).StatusCode);
    }

    [Fact]
    public async Task Viewers_read_but_do_not_manage()
    {
        using var viewer = await factory.ViewerAsync();

        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/sources")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync("/api/sources", new { name = "Nope" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/api/users")).StatusCode);
    }

    [Fact]
    public async Task The_published_demo_sign_in_is_a_viewer_that_cannot_manage_anything()
    {
        using var demo = factory.CreateClient();
        var published = await (await demo.GetAsync("/api/auth/demo")).ReadAsync<DemoSignInDto>();
        await demo.SignInAsync(published.Email, published.Password);

        var me = await (await demo.GetAsync("/api/me")).ReadAsync<UserDto>();

        Assert.Equal(ApiFactory.DemoViewerEmail, published.Email);
        Assert.Equal("Viewer", me.Role);
        Assert.Equal(HttpStatusCode.Forbidden, (await demo.PostAsJsonAsync("/api/sources", new { name = "Spam" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await demo.GetAsync("/api/users")).StatusCode);
    }

    [Fact]
    public async Task A_source_key_cannot_read_anything()
    {
        var (client, _) = await factory.NewSourceAsync();
        using var _ = client;

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/sources")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me")).StatusCode);
    }

    [Fact]
    public async Task Admins_add_users_with_unique_emails_and_long_passwords()
    {
        using var admin = await factory.AdminAsync();
        var email = $"new-{Guid.NewGuid():N}@example.com";

        var created = await admin.PostAsJsonAsync("/api/users", new { email, displayName = "New", password = ApiClient.Password });
        var again = await admin.PostAsJsonAsync("/api/users", new { email = email.ToUpperInvariant(), displayName = "New", password = ApiClient.Password });
        var shortPassword = await admin.PostAsJsonAsync("/api/users", new { email = "short@example.com", displayName = "Short", password = "short" });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("Viewer", (await created.ReadAsync<UserDto>()).Role);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("email_taken", await again.ProblemCodeAsync());
        Assert.Equal(HttpStatusCode.BadRequest, shortPassword.StatusCode);
    }
}

public sealed record DemoSignInDto(string Email, string Password);
