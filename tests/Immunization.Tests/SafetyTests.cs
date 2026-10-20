using System.Net;
using System.Text.Json;
using Immunization.Application;
using Immunization.Domain;
using Immunization.Infrastructure;
using Xunit;

namespace Immunization.Tests;

public sealed class SafetyTests
{
    [Theory]
    [InlineData("http://registry.example/", "echo", "send")]
    [InlineData("https://user:password@registry.example/", "echo", "send")]
    [InlineData("https://registry.example/?token=value", "echo", "send")]
    [InlineData("https://registry.example/", "https://other.example/echo", "send")]
    [InlineData("https://registry.example/", "echo", "../send")]
    [InlineData("https://registry.example/", "echo", "/send")]
    [InlineData("https://registry.example/", "echo", "__SEND_PATH__")]
    public void UnsafeOrIncompleteConfigurationIsRejected(string url, string echo, string send)
    {
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, JsonSerializer.Serialize(new RegistryOptions { BaseUrl = url, EchoPath = echo, SendPath = send }));
            Assert.Throws<InvalidDataException>(() => RegistryOptions.Load(file));
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public async Task AuditFailurePreventsExternalSideEffect()
    {
        var gateway = new CountingGateway();
        var batch = new BatchProcessor(gateway, new FailingJournal(), TimeProvider.System);
        await Assert.ThrowsAsync<IOException>(() => batch.RunAsync([new(Guid.NewGuid(), "a.hl7", "hash", "source", "message")], default));
        Assert.Equal(0, gateway.Calls);
    }

    [Fact]
    public async Task CancellationRecordsActiveAttemptAndStopsFollowingSubmissions()
    {
        using var cancellation = new CancellationTokenSource();
        var journal = new RecordingJournal();
        var gateway = new CancelGateway(cancellation);
        var submission = new Submission(Guid.NewGuid(), "a.hl7", "hash", "source", "message");
        var results = await new BatchProcessor(gateway, journal, TimeProvider.System)
            .RunAsync([submission, submission with { Id = Guid.NewGuid() }], cancellation.Token);
        Assert.Equal(Outcome.Unknown, Assert.Single(results).Assessment.Outcome);
        Assert.Equal(1, journal.Completed);
        Assert.Equal(1, gateway.Calls);
    }

    [Fact]
    public async Task TimeoutRemainsUnknownWithoutRetry()
    {
        using var handler = new TimeoutHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://registry.example/") };
        var options = new RegistryOptions { BaseUrl = "https://registry.example/", EchoPath = "echo", SendPath = "send" };
        var gateway = new HttpRegistryGateway(client, options, new RegistryCredentials("clinic", "user", "password"), new(), TimeProvider.System);
        var results = await new BatchProcessor(gateway, new RecordingJournal(), TimeProvider.System)
            .RunAsync([new(Guid.NewGuid(), "a.hl7", "hash", "source", "message")], default);
        Assert.Equal(Outcome.Unknown, Assert.Single(results).Assessment.Outcome);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public void NonAckMessageWithMsaCannotBeReportedAsAccepted()
    {
        var response = AcknowledgmentTests.Ack("AA").Replace("ACK^V04", "VXU^V04", StringComparison.Ordinal);
        Assert.Equal(Outcome.Unknown, new AcknowledgmentInterpreter().Interpret(response, "source").Outcome);
    }

    [Fact]
    public async Task HtmlReportEscapesServiceContentAndUsesPrivatePermissions()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".html");
        var receipt = new Receipt(Guid.NewGuid(), "<script>alert(1)</script>", "hash", "source",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, new(Outcome.Rejected, "<img src=x onerror=alert(1)>"));
        try
        {
            await HtmlReport.WriteAsync(path, [receipt], default);
            var html = await File.ReadAllTextAsync(path);
            Assert.DoesNotContain("<script>alert(1)</script>", html);
            Assert.DoesNotContain("<img src=x", html);
            Assert.Contains("&lt;script&gt;", html);
            Assert.Contains("Rejected", html);
            if (!OperatingSystem.IsWindows()) Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
            await Assert.ThrowsAsync<IOException>(() => HtmlReport.WriteAsync(path, [receipt], default));
        }
        finally { File.Delete(path); }
    }

    private sealed class CountingGateway : IRegistryGateway
    {
        public int Calls { get; private set; }
        public Task<Assessment> SendAsync(Submission submission, CancellationToken cancellationToken)
        { Calls++; return Task.FromResult(new Assessment(Outcome.Accepted, "accepted")); }
    }
    private sealed class CancelGateway(CancellationTokenSource source) : IRegistryGateway
    {
        public int Calls { get; private set; }
        public Task<Assessment> SendAsync(Submission submission, CancellationToken cancellationToken)
        { Calls++; source.Cancel(); throw new OperationCanceledException(cancellationToken); }
    }
    private sealed class FailingJournal : ITransactionJournal
    {
        public Task RecordPendingAsync(Submission submission, DateTimeOffset startedAt, CancellationToken cancellationToken) => throw new IOException();
        public Task RecordCompletedAsync(Receipt receipt, CancellationToken cancellationToken) => Task.CompletedTask;
    }
    private sealed class RecordingJournal : ITransactionJournal
    {
        public int Completed { get; private set; }
        public Task RecordPendingAsync(Submission submission, DateTimeOffset startedAt, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RecordCompletedAsync(Receipt receipt, CancellationToken cancellationToken)
        { Completed++; return Task.CompletedTask; }
    }
    private sealed class TimeoutHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Calls++; throw new TaskCanceledException("Simulated timeout"); }
    }
}
