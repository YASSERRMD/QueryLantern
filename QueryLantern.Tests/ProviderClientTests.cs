namespace QueryLantern.Tests;

using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using QueryLantern.Models;
using QueryLantern.Providers;
using Xunit;

public class ProviderClientTests
{
    private sealed class MockHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public HttpRequestMessage? LastRequest { get; private set; }

        public MockHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(_respond(request));
        }
    }

    private static HttpResponseMessage OkCompletion() => new(HttpStatusCode.OK)
    {
        Content = new StringContent("""{"choices":[{"message":{"content":"pong"}}]}""")
    };

    [Fact]
    public async Task TestProvider_Returns_Reply_Text()
    {
        var handler = new MockHandler(_ => OkCompletion());
        using var http = new HttpClient(handler);
        using var client = new ProviderClient(http);
        var profile = new ProviderProfile
        {
            Name = "test",
            Kind = ProviderKind.OpenAI,
            BaseUrl = "https://api.openai.com/v1",
            ModelId = "gpt-test"
        };

        var reply = await client.TestProviderAsync(profile, "sk-fake");
        Assert.Equal("pong", reply);
        Assert.EndsWith("/chat/completions", handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task TestProvider_Passes_Auth_Header()
    {
        var handler = new MockHandler(_ => OkCompletion());
        using var http = new HttpClient(handler);
        using var client = new ProviderClient(http);
        var profile = new ProviderProfile
        {
            Name = "novita",
            Kind = ProviderKind.Novita,
            BaseUrl = "https://novita.ai/v1",
            ModelId = "tencent/hy3"
        };

        await client.TestProviderAsync(profile, "sk-novita");
        Assert.True(handler.LastRequest!.Headers.Contains("Authorization"));
    }

    [Fact]
    public async Task TestProvider_Surfaces_Error_Status()
    {
        var handler = new MockHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("""{"error":"invalid key"}""")
        });
        using var http = new HttpClient(handler);
        using var client = new ProviderClient(http);
        var profile = new ProviderProfile
        {
            Name = "bad",
            Kind = ProviderKind.Custom,
            BaseUrl = "https://example.com/v1",
            ModelId = "m"
        };

        var reply = await client.TestProviderAsync(profile, "wrong");
        Assert.StartsWith("error:", reply);
    }
}
