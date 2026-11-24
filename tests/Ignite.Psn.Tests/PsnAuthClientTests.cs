using System.Net;
using System.Text;
using System.Web;

namespace Ignite.Psn.Tests;

public class PsnAuthClientTests
{
    private static readonly PsnOAuthOptions Options = new("client-id", "client-secret");

    [Fact]
    public void LoginUri_ContainsClientAndRedirect()
    {
        var client = new PsnAuthClient(new HttpClient(), Options);

        var query = HttpUtility.ParseQueryString(client.LoginUri.Query);

        Assert.StartsWith(PsnOAuthOptions.AuthorizeEndpoint, client.LoginUri.ToString(), StringComparison.Ordinal);
        Assert.Equal("client-id", query["client_id"]);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal(PsnOAuthOptions.RedirectUri, query["redirect_uri"]);
        Assert.Equal(PsnOAuthOptions.Scope, query["scope"]);
    }

    [Theory]
    [InlineData("https://remoteplay.dl.playstation.net/remoteplay/redirect?code=abc123&cid=1", "abc123")]
    [InlineData("  https://remoteplay.dl.playstation.net/remoteplay/redirect?code=x  ", "x")]
    [InlineData("https://remoteplay.dl.playstation.net/remoteplay/redirect?error=denied", null)]
    [InlineData("not a url", null)]
    public void ExtractCode_ReadsQueryParameter(string url, string? expected)
    {
        Assert.Equal(expected, PsnAuthClient.ExtractCode(url));
    }

    [Fact]
    public async Task ExchangeCodeAndGetUserId_UseBasicAuth()
    {
        var handler = new FakeHandler(request =>
        {
            Assert.Equal("Basic", request.Headers.Authorization?.Scheme);
            Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes("client-id:client-secret")), request.Headers.Authorization?.Parameter);

            if (request.Method == HttpMethod.Post)
            {
                var form = HttpUtility.ParseQueryString(request.Content!.ReadAsStringAsync().Result);
                Assert.Equal("authorization_code", form["grant_type"]);
                Assert.Equal("the-code", form["code"]);
                return Json("""{"access_token":"tok","refresh_token":"ref","expires_in":3600}""");
            }

            Assert.EndsWith("/oauth/token/tok", request.RequestUri!.AbsolutePath, StringComparison.Ordinal);
            return Json("""{"user_id":"1234567890123456789"}""");
        });
        var client = new PsnAuthClient(new HttpClient(handler), Options);

        var tokens = await client.ExchangeCodeAsync("the-code");
        var userId = await client.GetUserIdAsync(tokens.AccessToken);

        Assert.Equal("tok", tokens.AccessToken);
        Assert.Equal("ref", tokens.RefreshToken);
        Assert.Equal(1234567890123456789L, userId);
    }

    [Fact]
    public async Task ErrorsBecomePsnException()
    {
        var client = new PsnAuthClient(new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("bad") })), Options);

        var e = await Assert.ThrowsAsync<PsnException>(() => client.ExchangeCodeAsync("x"));
        Assert.Contains("400", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingConfiguration_IsReported()
    {
        var client = new PsnAuthClient(new HttpClient(new FakeHandler(_ => Json("{}"))), new PsnOAuthOptions("", ""));

        await Assert.ThrowsAsync<PsnException>(() => client.ExchangeCodeAsync("x"));
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
