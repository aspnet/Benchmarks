using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace TodoApi.Tests;

public sealed class ContractTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task ListMatchesGoldenBytesWithoutRedirectOrCompression()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/todos");
        request.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, br, deflate");
        request.Headers.TryAddWithoutValidation("Accept", "application/xml");
        using var response = await fixture.Client.SendAsync(request);

        await AssertJson(response, fixture.Expected);
        Assert.Null(response.Headers.Location);
        Assert.Null(response.Headers.ETag);
        Assert.Null(response.Content.Headers.LastModified);
        Assert.Equal(HttpVersion.Version11, response.Version);
        Assert.NotEqual(true, response.Headers.ConnectionClose);

        using var document = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(5, document.RootElement.GetArrayLength());
        foreach (var item in document.RootElement.EnumerateArray())
        {
            Assert.Equal(["id", "title", "dueBy", "isComplete"],
                item.EnumerateObject().Select(property => property.Name));
            Assert.Equal(JsonValueKind.False, item.GetProperty("isComplete").ValueKind);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task IndividualMatchesExactFixtureObject(int id)
    {
        using var document = JsonDocument.Parse(fixture.Expected);
        var expected = Encoding.UTF8.GetBytes(document.RootElement[id - 1].GetRawText());
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/todos/{id}");
        request.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, br, deflate");
        using var response = await fixture.Client.SendAsync(request);
        await AssertJson(response, expected);
    }

    [Theory]
    [InlineData("6")]
    [InlineData("999999999999999999999999999999")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("01")]
    [InlineData("-1")]
    [InlineData("+1")]
    [InlineData("1.0")]
    [InlineData("1e0")]
    [InlineData("%201")]
    [InlineData("1%20")]
    [InlineData("%D9%A1")]
    public async Task MissingAndMalformedIdsHaveEmptyNotFoundResponses(string id)
    {
        using var response = await fixture.Client.GetAsync($"/todos/{id}");
        await AssertEmpty(response, HttpStatusCode.NotFound);
    }

    public static IEnumerable<object[]> Mutations()
    {
        foreach (var method in new[] { "POST", "PUT", "PATCH", "DELETE" })
        {
            foreach (var path in new[] { "/todos", "/todos/1", "/todos/2", "/todos/3", "/todos/4", "/todos/5" })
            {
                yield return [method, path];
            }
        }
    }

    [Theory]
    [MemberData(nameof(Mutations))]
    public async Task MutatingMethodsAreEmptyAndDoNotChangeData(string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = new StringContent("{\"id\":1,\"title\":\"Changed\",\"isComplete\":true}",
                Encoding.UTF8, "application/json")
        };
        using var response = await fixture.Client.SendAsync(request);
        await AssertEmpty(response, HttpStatusCode.MethodNotAllowed);
        using var after = await fixture.Client.GetAsync("/todos");
        await AssertJson(after, fixture.Expected);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/unrelated")]
    [InlineData("/todos/1/extra")]
    [InlineData("/swagger")]
    public async Task UnknownPathsHaveEmptyNotFoundResponses(string path)
    {
        using var response = await fixture.Client.GetAsync(path);
        await AssertEmpty(response, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task HealthIsExactUtf8PlainText()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/healthz");
        request.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, br, deflate");
        using var response = await fixture.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertContentType(response, "text/plain");
        Assert.Equal("ready"u8.ToArray(), await response.Content.ReadAsByteArrayAsync());
        Assert.Empty(response.Content.Headers.ContentEncoding);
    }

    [Fact]
    public async Task ConditionalHeadersDoNotChangeRepresentation()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/todos");
        request.Headers.TryAddWithoutValidation("If-None-Match", "*");
        request.Headers.TryAddWithoutValidation("If-Modified-Since", "Thu, 01 Jan 2099 00:00:00 GMT");
        using var response = await fixture.Client.SendAsync(request);
        await AssertJson(response, fixture.Expected);
    }

    [Fact]
    public async Task RepeatedAndConcurrentReadsLeaveFixtureUnchanged()
    {
        for (var index = 0; index < 10; index++)
        {
            using var response = await fixture.Client.GetAsync("/todos");
            await AssertJson(response, fixture.Expected);
        }

        using var document = JsonDocument.Parse(fixture.Expected);
        var objects = document.RootElement.EnumerateArray()
            .Select(item => Encoding.UTF8.GetBytes(item.GetRawText())).ToArray();
        await Task.WhenAll(Enumerable.Range(0, 60).Select(async index =>
        {
            var item = index % 6;
            var path = item == 0 ? "/todos" : $"/todos/{item}";
            using var response = await fixture.Client.GetAsync(path);
            await AssertJson(response, item == 0 ? fixture.Expected : objects[item - 1]);
        }));

        using var after = await fixture.Client.GetAsync("/todos");
        await AssertJson(after, fixture.Expected);
    }

    [Fact]
    public async Task SameHttp11ConnectionServesRepeatedRequests()
    {
        using var connection = new TcpClient();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await connection.ConnectAsync(IPAddress.Loopback, fixture.Client.BaseAddress!.Port, deadline.Token);
        await using var stream = connection.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, false, leaveOpen: true);

        for (var index = 0; index < 3; index++)
        {
            await stream.WriteAsync(
                "GET /healthz HTTP/1.1\r\nHost: localhost\r\n\r\n"u8.ToArray(), deadline.Token);
            Assert.Equal("HTTP/1.1 200 OK", await reader.ReadLineAsync(deadline.Token));
            var length = -1;
            while (await reader.ReadLineAsync(deadline.Token) is { Length: > 0 } header)
            {
                Assert.False(header.Equals("Connection: close", StringComparison.OrdinalIgnoreCase));
                if (header.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                {
                    length = int.Parse(header["Content-Length:".Length..],
                        System.Globalization.CultureInfo.InvariantCulture);
                }
            }

            Assert.Equal(5, length);
            var body = new char[length];
            Assert.Equal(length, await reader.ReadBlockAsync(body.AsMemory(), deadline.Token));
            Assert.Equal("ready", new string(body));
        }
    }

    private static async Task AssertJson(HttpResponseMessage response, byte[] expected)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertContentType(response, "application/json");
        Assert.Empty(response.Content.Headers.ContentEncoding);
        Assert.Equal(expected, await response.Content.ReadAsByteArrayAsync());
    }

    private static void AssertContentType(HttpResponseMessage response, string mediaType)
    {
        Assert.Equal(mediaType, response.Content.Headers.ContentType?.MediaType);
        var charset = response.Content.Headers.ContentType?.CharSet;
        Assert.True(charset is null || charset.Equals("utf-8", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task AssertEmpty(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }
}
