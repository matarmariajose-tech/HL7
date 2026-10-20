using System.Net;
using System.Text;
using System.Text.Json;
using Immunization.Application;
using Immunization.Domain;
using Immunization.Infrastructure;
using Xunit;

namespace Immunization.Tests;

public sealed class WorkflowTests
{
    private static readonly Submission Submission = new(Guid.NewGuid(), "sample.hl7", "hash", "source", "sensitive payload");
    private static RegistryOptions Options => new() { BaseUrl = "https://registry.example/", EchoPath = "echo", SendPath = "send", UseBasicAuthentication = false };

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task HttpFailureDoesNotImplyRegistryRejection(int status)
    {
        using var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)));
        using var client = new HttpClient(handler) { BaseAddress = new Uri(Options.BaseUrl) };
        var gateway = new HttpRegistryGateway(client, Options, new RegistryCredentials("clinic", "user", "password"), new(), TimeProvider.System);
        var result = await gateway.SendAsync(Submission, default);
        Assert.Equal(Outcome.Unknown, result.Outcome);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task JsonRequestMatchesSwaggerAndUnwrapsResult()
    {
        var submission = Submission with { Content = "MSH|^~\\&|A|B|||20260101||VXU^V04|source|P|2.5.1\rPID|1\r" };
        var serviceId = Guid.NewGuid();
        using var handler = new StubHandler(async (request, token) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/send", request.RequestUri!.AbsolutePath);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Assert.Equal("clinic", body.RootElement.GetProperty("clinicOID").GetString());
            Assert.Equal("user", body.RootElement.GetProperty("username").GetString());
            Assert.Equal("password", body.RootElement.GetProperty("password").GetString());
            Assert.Equal(submission.FileName, body.RootElement.GetProperty("nameHl7").GetString());
            var segments = body.RootElement.GetProperty("hl7").EnumerateArray().Select(x => x.GetString()).ToArray();
            Assert.Equal(submission.Content.Split('\r', StringSplitOptions.RemoveEmptyEntries), segments);
            Assert.True(body.RootElement.GetProperty("dateSend").TryGetDateTimeOffset(out _));
            return new(HttpStatusCode.Created) { Content = new StringContent(JsonSerializer.Serialize(new { messageId = serviceId, result = AcknowledgmentTests.Ack("AA") })) };
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri(Options.BaseUrl) };
        var assessment = await new HttpRegistryGateway(client, Options, new RegistryCredentials("clinic", "user", "password"), new(), TimeProvider.System).SendAsync(submission, default);
        Assert.Equal(Outcome.Accepted, assessment.Outcome);
        Assert.Equal(serviceId, assessment.ServiceMessageId);
    }

    [Theory]
    [InlineData("not JSON")]
    [InlineData("{}")]
    [InlineData("{\"result\":42}")]
    public async Task InvalidEnvelopeIsUnknown(string response)
    {
        using var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent(response) }));
        using var client = new HttpClient(handler) { BaseAddress = new Uri(Options.BaseUrl) };
        var result = await new HttpRegistryGateway(client, Options, new RegistryCredentials("clinic", "user", "password"), new(), TimeProvider.System).SendAsync(Submission, default);
        Assert.Equal(Outcome.Unknown, result.Outcome);
    }

    [Fact]
    public async Task ResponseSizeIsBounded()
    {
        using var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent(new string('x', 30)) }));
        using var client = new HttpClient(handler) { BaseAddress = new Uri(Options.BaseUrl) };
        var result = await new HttpRegistryGateway(client, Options with { MaxResponseBytes = 10 }, new RegistryCredentials("clinic", "user", "password"), new(), TimeProvider.System).SendAsync(Submission, default);
        Assert.Equal(Outcome.Unknown, result.Outcome);
    }

    [Theory]
    [InlineData("{\"echoBack\":\"echo\"}", true)]
    [InlineData("{\"echoBack\":\"other\"}", false)]
    [InlineData("{}", false)]
    [InlineData("not JSON", false)]
    public async Task EchoValidatesDocumentedResponseAndUsesTransferCredentials(string response, bool expected)
    {
        using var handler = new StubHandler(async (request, token) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/echo", request.RequestUri!.AbsolutePath);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Assert.Equal("user", body.RootElement.GetProperty("user").GetString());
            Assert.Equal("password", body.RootElement.GetProperty("password").GetString());
            return new(HttpStatusCode.OK) { Content = new StringContent(response) };
        });
        using var client = new HttpClient(handler) { BaseAddress = new Uri(Options.BaseUrl) };
        Assert.Equal(expected, await new HttpRegistryGateway(client, Options, new RegistryCredentials("clinic", "user", "password"), new(), TimeProvider.System).EchoAsync(default));
    }

    [Fact]
    public async Task TransportFailureIsJournaledAndNextMessageStillRuns()
    {
        var journal = new MemoryJournal();
        var gateway = new ThrowingGateway();
        var result = await new BatchProcessor(gateway, journal, TimeProvider.System)
            .RunAsync([Submission, Submission with { Id = Guid.NewGuid() }], default);
        Assert.Equal(2, result.Count);
        Assert.All(result, x => Assert.Equal(Outcome.Unknown, x.Assessment.Outcome));
        Assert.Equal(new[] { "Pending", "Completed", "Pending", "Completed" }, journal.Events);
        Assert.Equal(2, gateway.Calls);
    }

    [Fact]
    public async Task JournalRefusesOverwriteAndExcludesMessageContent()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var file = Path.Combine(directory, "report.jsonl");
        try
        {
            using (var journal = new JsonJournal(file))
            {
                await journal.RecordPendingAsync(Submission, DateTimeOffset.UtcNow, default);
                await journal.RecordCompletedAsync(new(Submission.Id, Submission.FileName, Submission.Sha256,
                    Submission.MessageId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, new(Outcome.Accepted, "accepted")), default);
            }
            var lines = await File.ReadAllLinesAsync(file);
            Assert.Equal(2, lines.Length);
            foreach (var line in lines) { using var document = JsonDocument.Parse(line); Assert.True(document.RootElement.TryGetProperty("State", out _)); }
            Assert.DoesNotContain(Submission.Content, await File.ReadAllTextAsync(file));
            Assert.Throws<IOException>(() => new JsonJournal(file));
            if (!OperatingSystem.IsWindows()) Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task InputBytesArePreservedAndDuplicateControlIdsRemainSeparate()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(directory);
        var text = "MSH|^~\\&|SOURCE|SITE|||20260101||VXU^V04|duplicate|P|2.5.1\rPID|1\r";
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "a.hl7"), text, new UTF8Encoding(false));
            await File.WriteAllTextAsync(Path.Combine(directory, "b.hl7"), text, new UTF8Encoding(false));
            var result = await InputReader.ReadAsync(directory, 10000, default);
            Assert.Equal(2, result.Count);
            Assert.NotEqual(result[0].Id, result[1].Id);
            Assert.All(result, x => { Assert.Equal("duplicate", x.MessageId); Assert.Equal(text, x.Content); });
            Assert.Equal(result[0].Sha256, result[1].Sha256);
            await Assert.ThrowsAsync<InvalidDataException>(() => InputReader.ReadAsync(directory, 1, default));
        }
        finally { Directory.Delete(directory, true); }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> action) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Calls++; return action(request, cancellationToken); }
    }
    private sealed class ThrowingGateway : IRegistryGateway
    {
        public int Calls { get; private set; }
        public Task<Assessment> SendAsync(Submission submission, CancellationToken cancellationToken)
        { Calls++; throw new HttpRequestException(); }
    }
    private sealed class MemoryJournal : ITransactionJournal
    {
        public List<string> Events { get; } = [];
        public Task RecordPendingAsync(Submission submission, DateTimeOffset startedAt, CancellationToken cancellationToken)
        { Events.Add("Pending"); return Task.CompletedTask; }
        public Task RecordCompletedAsync(Receipt receipt, CancellationToken cancellationToken)
        { Events.Add("Completed"); return Task.CompletedTask; }
    }
}
