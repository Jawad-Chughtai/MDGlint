using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using MDGlint.Core.Models;
using MDGlint.Desktop.Helpers;
using MDGlint.Desktop.Services;

namespace MDGlint.Desktop.Tests;

/// <summary>Export ▸ HTML's images (<see cref="ExportImages"/>) and styles (<see cref="ExportStyles"/>).</summary>
public sealed class ExportTests : IDisposable
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47];

    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"mdglint-export-{Guid.NewGuid():N}");

    public ExportTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public async Task EmbedsALocalImage()
    {
        File.WriteAllBytes(Path.Combine(_folder, "a.png"), Png);
        var images = new ExportImages(new HttpClient(new Handler()), allowRemoteContent: false);

        Assert.Equal("data:image/png;base64,iVBORw==", await images.EmbedAsync(LocalUrl("a.png")));
    }

    [Fact]
    public async Task LoadsAnImagesBytesForWord()
    {
        File.WriteAllBytes(Path.Combine(_folder, "a.png"), Png);
        var handler = new Handler { Respond = _ => Image(Png, "image/gif") };
        var images = new ExportImages(new HttpClient(handler), allowRemoteContent: true);

        var local = await images.LoadAsync(LocalUrl("a.png"));
        Assert.Equal("image/png", local!.ContentType);
        Assert.Equal(Png, local.Bytes);
        var remote = await images.LoadAsync(new Uri("https://example.com/a.gif"));
        Assert.Equal("image/gif", remote!.ContentType);
        Assert.Equal(Png, remote.Bytes);
    }

    [Theory]
    [InlineData("a.mp4")] // Video, not an image.
    [InlineData("a.txt")] // Never served to the preview.
    [InlineData("missing.png")]
    public async Task LeavesOutOtherLocalFiles(string name)
    {
        File.WriteAllBytes(Path.Combine(_folder, "a.mp4"), Png);
        File.WriteAllBytes(Path.Combine(_folder, "a.txt"), Png);
        var images = new ExportImages(new HttpClient(new Handler()), allowRemoteContent: true);

        Assert.Null(await images.EmbedAsync(LocalUrl(name)));
    }

    [Fact]
    public async Task LeavesOutALocalImageThatIsTooLarge()
    {
        using (var file = File.Create(Path.Combine(_folder, "huge.png")))
        {
            file.SetLength(ExportImages.MaxBytes + 1);
        }

        var images = new ExportImages(new HttpClient(new Handler()), allowRemoteContent: false);
        Assert.Null(await images.EmbedAsync(LocalUrl("huge.png")));
    }

    [Fact]
    public async Task DownloadsRemoteImagesOnlyWhenAllowed()
    {
        var handler = new Handler { Respond = _ => Image(Png, "image/png") };
        var uri = new Uri("https://example.com/a.png");

        Assert.Null(await new ExportImages(new HttpClient(handler), allowRemoteContent: false).EmbedAsync(uri));
        Assert.Empty(handler.Requests);

        Assert.Equal("data:image/png;base64,iVBORw==", await new ExportImages(new HttpClient(handler), allowRemoteContent: true).EmbedAsync(uri));
        Assert.Equal([uri], handler.Requests);
    }

    [Fact]
    public async Task ReadsARemoteImageOfUnknownLength()
    {
        var handler = new Handler { Respond = _ => Streamed(Png, "image/gif") };
        var images = new ExportImages(new HttpClient(handler), allowRemoteContent: true);

        Assert.Equal("data:image/gif;base64,iVBORw==", await images.EmbedAsync(new Uri("http://example.com/a.gif")));
    }

    public static TheoryData<string> Refused => ["error", "html", "untyped", "long", "streamed too long", "broken", "throws", "ftp"];

    [Theory]
    [MemberData(nameof(Refused))]
    public async Task LeavesOutRemoteImagesThatFailOrArent(string kind)
    {
        var handler = new Handler
        {
            Respond = _ => kind switch
            {
                "error" => new HttpResponseMessage(HttpStatusCode.NotFound),
                "html" => Image(Png, "text/html"),
                "untyped" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Png) },
                "long" => Long(),
                "streamed too long" => Streamed(new byte[ExportImages.MaxBytes + 1], "image/png"),
                "broken" => Broken(),
                _ => throw new HttpRequestException("offline"),
            },
        };
        var images = new ExportImages(new HttpClient(handler), allowRemoteContent: true);

        Assert.Null(await images.EmbedAsync(new Uri(kind == "ftp" ? "ftp://example.com/a.png" : "https://example.com/a.png")));
    }

    [Fact]
    public void StylesComeWithTheirLicensesAndTheTheme()
    {
        WriteAsset("vendor/github-markdown-css/LICENSE", "MIT License */ end");
        WriteAsset("vendor/github-markdown-css/github-markdown-light.css", ".light {}");
        WriteAsset("vendor/github-markdown-css/github-markdown-dark.css", ".dark {}");
        WriteAsset("vendor/highlight.js/LICENSE", "BSD 3-Clause License");
        WriteAsset("vendor/highlight.js/github.min.css", ".hljs-light {}");
        WriteAsset("vendor/highlight.js/github-dark.min.css", ".hljs-dark {}");
        WriteAsset("export.css", ".page {}");

        Assert.Equal(
            "/*\n  github-markdown-css\n\nMIT License * / end\n*/\n.light {}\n\n"
            + "/*\n  highlight.js\n\nBSD 3-Clause License\n*/\n.hljs-light {}\n\n.page {}",
            ExportStyles.For(_folder, ThemeKind.Light));
        var dark = ExportStyles.For(_folder, ThemeKind.Dark);
        Assert.Contains(".dark {}", dark);
        Assert.Contains(".hljs-dark {}", dark);
    }

    [Fact]
    public void StylesComeFromTheAppsAssets()
    {
        var styles = ExportStyles.For(Path.Combine(AppContext.BaseDirectory, "Assets"), ThemeKind.Dark);

        Assert.Contains("Sindre Sorhus", styles);
        Assert.Contains(".markdown-body", styles);
        Assert.Contains("Theme: GitHub Dark", styles);
        Assert.Contains("html[data-theme=\"dark\"] body", styles);
    }

    private Uri LocalUrl(string name) => new(DocumentUrls.ToFolderUrl(_folder) + name);

    private void WriteAsset(string relative, string text)
    {
        var path = Path.Combine(_folder, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private static HttpResponseMessage Image(byte[] bytes, string type)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(type);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private static HttpResponseMessage Long()
    {
        var response = Image(Png, "image/png");
        response.Content.Headers.ContentLength = ExportImages.MaxBytes + 1;
        return response;
    }

    private static HttpResponseMessage Streamed(byte[] bytes, string type)
    {
        var content = new UnknownLengthContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(type);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private static HttpResponseMessage Broken()
    {
        var content = new BrokenContent();
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private sealed class Handler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; init; } = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(Respond(request));
        }
    }

    /// <summary>A body whose connection drops partway.</summary>
    private sealed class BrokenContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) =>
            throw new IOException("The connection dropped.");

        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new BrokenStream());

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class BrokenStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            throw new IOException("The connection dropped.");
    }

    /// <summary>A body sent without a Content-Length, as a server streaming it would.</summary>
    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) =>
            stream.WriteAsync(bytes).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
