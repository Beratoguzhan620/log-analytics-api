using System.Net;

namespace LogAnalytics.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class DashboardTests(ApiFactory factory)
{
    [Fact]
    public async Task The_dashboard_is_served_with_a_strict_content_security_policy()
    {
        using var client = factory.CreateClient();

        var page = await client.GetAsync("/");
        var script = await client.GetAsync("/js/app.js");
        var csp = page.Headers.GetValues("Content-Security-Policy").Single();

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("<script type=\"module\" src=\"js/app.js\">", await page.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, script.StatusCode);
        Assert.Contains("script-src 'self'", csp, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", csp, StringComparison.Ordinal);
        Assert.Equal("nosniff", page.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Fact]
    public async Task The_api_reference_is_left_alone()
    {
        using var client = factory.CreateClient();

        var reference = await client.GetAsync("/scalar");

        Assert.False(reference.Headers.Contains("Content-Security-Policy"));
    }
}
