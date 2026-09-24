using System.Net;
using System.Net.Http.Headers;
using TechieRag.Embedded;
using Xunit;

namespace TechieRag.Tests.Embedding;

/// <summary>
/// Every model download reports its total size before the first byte and progress after
/// (REQ-RAG-055 / BRD-92), through the general, public <see cref="ModelDownloadService"/>.
/// </summary>
/// <remarks>
/// Each test owns a service built over a fake HTTP handler and a temporary folder, so nothing touches
/// the network or the real model root.
/// </remarks>
public sealed class ModelDownloadServiceTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "techierag-dl-" + Guid.NewGuid().ToString("N"));
    private readonly FakeFileServer server = new();

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// The size-known event fires with the total of every pending file before any request is sent,
    /// and byte-progress events follow it.
    /// </summary>
    [Fact(DisplayName = "REQ-RAG-055 SizeIsKnownBeforeTheFirstByte")]
    public async Task SizeIsKnownBeforeTheFirstByte()
    {
        var files = server.Serve(("model.onnx", 300_000), ("vocab.txt", 1_000));
        var service = new ModelDownloadService(server);
        var log = new List<string>();
        long reportedTotal = -1;
        var requestsWhenSizeKnown = -1;

        service.DownloadSizeKnown += (_, e) =>
        {
            reportedTotal = e.TotalBytes;
            requestsWhenSizeKnown = server.Requests.Count;
            log.Add("size");
        };
        service.ProgressChanged += (_, p) =>
        {
            if (p.BytesDownloaded > 0)
            {
                log.Add("bytes");
            }
        };

        await service.DownloadAsync("tiny", directory, files);

        Assert.Equal(301_000, reportedTotal);
        Assert.Equal(0, requestsWhenSizeKnown);
        Assert.Equal("size", log[0]);
        Assert.Contains("bytes", log);
        Assert.Equal(301_000, service.Progress.BytesDownloaded);
        Assert.Equal(ModelDownloadStatus.Completed, service.Progress.Status);
        Assert.Equal(300_000, new FileInfo(Path.Combine(directory, "model.onnx")).Length);
        Assert.False(File.Exists(Path.Combine(directory, "model.onnx.part")));
    }

    /// <summary>A host that declines stops the download before any request, with a typed exception.</summary>
    [Fact]
    public async Task DeclineStopsBeforeAnyRequest()
    {
        var files = server.Serve(("model.onnx", 5_000));
        var service = new ModelDownloadService(server);
        service.DownloadSizeKnown += (_, e) => e.Decline = true;

        var declined = await Assert.ThrowsAsync<ModelDownloadDeclinedException>(
            () => service.DownloadAsync("tiny", directory, files));

        Assert.Equal(5_000, declined.TotalBytes);
        Assert.Empty(server.Requests);
        Assert.False(File.Exists(Path.Combine(directory, "model.onnx")));
    }

    /// <summary>A complete model downloads nothing and raises no size event.</summary>
    [Fact]
    public async Task CompleteModelDownloadsNothing()
    {
        var files = server.Serve(("model.onnx", 2_000));
        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(Path.Combine(directory, "model.onnx"), new byte[2_000]);
        var service = new ModelDownloadService(server);
        var sizeEvents = 0;
        service.DownloadSizeKnown += (_, _) => sizeEvents++;

        await service.DownloadAsync("tiny", directory, files);

        Assert.Equal(0, sizeEvents);
        Assert.Empty(server.Requests);
        Assert.True(ModelDownloadService.IsComplete(directory, files));
        Assert.Equal(0, ModelDownloadService.GetPendingBytes(directory, files));
    }

    /// <summary>
    /// An interrupted download resumes from its partial file with a range request, and the size event
    /// reports only the missing part.
    /// </summary>
    [Fact]
    public async Task PartialFileResumesWithARangeRequest()
    {
        var files = server.Serve(("model.onnx", 10_000));
        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(Path.Combine(directory, "model.onnx.part"), server.Content("model.onnx")[..4_000]);
        var service = new ModelDownloadService(server);
        long reportedTotal = -1;
        service.DownloadSizeKnown += (_, e) => reportedTotal = e.TotalBytes;

        await service.DownloadAsync("tiny", directory, files);

        Assert.Equal(6_000, reportedTotal);
        Assert.Equal(4_000, server.Requests.Single().Headers.Range?.Ranges.Single().From);
        Assert.Equal(server.Content("model.onnx"), await File.ReadAllBytesAsync(Path.Combine(directory, "model.onnx")));
    }

    /// <summary>A server error fails the download and leaves the status at Failed.</summary>
    [Fact]
    public async Task ServerErrorIsReported()
    {
        var files = new[] { new ModelDownloadFile("missing.bin", new Uri("https://models.test/missing.bin"), 10) };
        var service = new ModelDownloadService(server);

        await Assert.ThrowsAsync<HttpRequestException>(() => service.DownloadAsync("tiny", directory, files));

        Assert.Equal(ModelDownloadStatus.Failed, service.Progress.Status);
    }

    /// <summary>Sizes are shown in decimal units, the way the phone refusal names bge-m3.</summary>
    [Fact]
    public void BytesFormatAsUserSizes()
    {
        Assert.Equal("2.3 GB", ModelDownloadService.FormatBytes(2_289_698_101));
        Assert.Equal("91 MB", ModelDownloadService.FormatBytes(90_636_722));
        Assert.Equal("5 KB", ModelDownloadService.FormatBytes(5_000));
        Assert.Equal("698 B", ModelDownloadService.FormatBytes(698));
    }

    /// <summary>Serves generated file bodies and records every request; honours range requests.</summary>
    private sealed class FakeFileServer : HttpMessageHandler
    {
        private readonly Dictionary<string, byte[]> contents = new(StringComparer.Ordinal);

        public List<HttpRequestMessage> Requests { get; } = [];

        public ModelDownloadFile[] Serve(params (string Name, int Size)[] files) => files
            .Select(f =>
            {
                var body = new byte[f.Size];
                new Random(f.Size).NextBytes(body);
                contents[f.Name] = body;
                return new ModelDownloadFile(f.Name, new Uri($"https://models.test/{f.Name}"), f.Size);
            })
            .ToArray();

        public byte[] Content(string name) => contents[name];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var name = request.RequestUri!.AbsolutePath.TrimStart('/');
            if (!contents.TryGetValue(name, out var body))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            var from = request.Headers.Range?.Ranges.Single().From ?? 0;
            var response = new HttpResponseMessage(from > 0 ? HttpStatusCode.PartialContent : HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(body[(int)from..])
            };
            response.Content.Headers.ContentLength = body.Length - from;
            if (from > 0)
            {
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, body.Length - 1, body.Length);
            }

            return Task.FromResult(response);
        }
    }
}
