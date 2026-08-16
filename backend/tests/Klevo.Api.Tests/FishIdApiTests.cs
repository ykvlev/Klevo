using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Klevo.Api.Tests;

public class FishIdApiTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static byte[] MakeTestPng(int w = 256, int h = 256)
    {
        using var img = new Image<Rgba32>(w, h);
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var c = x < w / 2 && y < h / 2
                ? new Rgba32(210, 120, 60)
                : x >= w / 2 && y >= h / 2
                    ? new Rgba32(40, 40, 40)
                    : new Rgba32(40, 120, 200);
            img[x, y] = c;
        }
        using var ms = new MemoryStream();
        img.SaveAsPng(ms);
        return ms.ToArray();
    }

    private static MultipartFormDataContent PhotoContent(byte[] bytes, string name = "fish.png")
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        content.Add(file, "file", name);
        return content;
    }

    [Fact]
    public async Task UploadPhoto_ThenGet_ReturnsImage()
    {
        using var content = PhotoContent(MakeTestPng());
        var post = await _client.PostAsync("/api/uploads", content);
        Assert.Equal(HttpStatusCode.OK, post.StatusCode);

        using var doc = JsonDocument.Parse(await post.Content.ReadAsStringAsync());
        var url = doc.RootElement.GetProperty("url").GetString();
        Assert.NotNull(url);
        Assert.StartsWith("/uploads/", url);

        var get = await _client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal("image/png", get.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task FishId_WithPhoto_ReturnsTop3Predictions()
    {
        using var content = PhotoContent(MakeTestPng());
        var post = await _client.PostAsync("/api/fish-id", content);
        if (post.StatusCode == HttpStatusCode.ServiceUnavailable)
            return; // модель fishid не развёрнута локально — эндпоинт корректно отвечает 503

        Assert.Equal(HttpStatusCode.OK, post.StatusCode);
        using var doc = JsonDocument.Parse(await post.Content.ReadAsStringAsync());
        Assert.Equal("fishid-v1-mobilenet-v3-small", doc.RootElement.GetProperty("modelVersion").GetString());

        var top = doc.RootElement.GetProperty("top").EnumerateArray().ToList();
        Assert.Equal(3, top.Count);
        var confs = top.Select(t => t.GetProperty("confidence").GetDecimal()).ToList();
        Assert.True(confs[0] >= confs[1] && confs[1] >= confs[2], "уверенности должны убывать");
        foreach (var t in top)
            Assert.True(t.GetProperty("nameRu").GetString() is { Length: > 0 });
    }

    [Fact]
    public async Task FishId_WithDataUrl_ReturnsOk()
    {
        var b64 = Convert.ToBase64String(MakeTestPng());
        var post = await _client.PostAsync("/api/fish-id",
            JsonContent.Create(new { dataUrl = $"data:image/png;base64,{b64}" }));
        Assert.True(post.StatusCode is HttpStatusCode.OK or HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task FishId_WithoutImage_ReturnsBadRequest()
    {
        using var content = new MultipartFormDataContent();
        var post = await _client.PostAsync("/api/fish-id", content);
        if (post.StatusCode != HttpStatusCode.BadRequest)
            Assert.Fail(await post.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, post.StatusCode);
    }
}
