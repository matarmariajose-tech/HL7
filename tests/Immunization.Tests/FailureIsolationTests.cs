using System.Net;
using Immunization.Application;
using Immunization.Domain;
using Immunization.Infrastructure;
using Xunit;

namespace Immunization.Tests;

public sealed class FailureIsolationTests
{
    private static Submission Submission => new(Guid.NewGuid(), "a.hl7", "hash", "source", "private payload");

    [Fact]
    public async Task AlreadyCancelledBatchDoesNotCreateAnAttempt()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var journal = new Journal();
        var gateway = new BugGateway();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new BatchProcessor(gateway, journal, TimeProvider.System).RunAsync([Submission], cancellation.Token));
        Assert.Equal(0, journal.Pending);
        Assert.Equal(0, gateway.Calls);
    }

    [Fact]
    public async Task UnexpectedProgrammingErrorsRemainVisibleAndDoNotBecomeBusinessResults()
    {
        var journal = new Journal();
        var gateway = new BugGateway();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new BatchProcessor(gateway, journal, TimeProvider.System).RunAsync([Submission, Submission], default));
        Assert.Equal(1, journal.Pending);
        Assert.Equal(0, journal.Completed);
        Assert.Equal(1, gateway.Calls);
    }

    [Fact(Timeout = 10000)]
    public async Task ResponseBodyIsCoveredByRequestDeadline()
    {
        using var handler = new HangingBodyHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://registry.example/"), Timeout = Timeout.InfiniteTimeSpan };
        var options = new RegistryOptions { BaseUrl = "https://registry.example/", EchoPath = "echo", SendPath = "send", TimeoutSeconds = 1 };
        var gateway = new HttpRegistryGateway(client, options, new RegistryCredentials("clinic", "user", "password"), new(), TimeProvider.System);
        var receipts = await new BatchProcessor(gateway, new Journal(), TimeProvider.System).RunAsync([Submission], default);
        var assessment = Assert.Single(receipts).Assessment;
        Assert.Equal(Outcome.Unknown, assessment.Outcome);
        Assert.Equal(AssessmentReason.RequestTimedOut, assessment.ReasonCode);
        Assert.Equal(1, handler.Calls);
    }

    private sealed class BugGateway : IRegistryGateway
    {
        public int Calls { get; private set; }
        public Task<Assessment> SendAsync(Submission submission, CancellationToken cancellationToken)
        { Calls++; throw new InvalidOperationException("Unexpected implementation defect."); }
    }
    private sealed class Journal : ITransactionJournal
    {
        public int Pending { get; private set; }
        public int Completed { get; private set; }
        public Task RecordPendingAsync(Submission submission, DateTimeOffset startedAt, CancellationToken cancellationToken)
        { Pending++; return Task.CompletedTask; }
        public Task RecordCompletedAsync(Receipt receipt, CancellationToken cancellationToken)
        { Completed++; return Task.CompletedTask; }
    }
    private sealed class HangingBodyHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created) { Content = new StreamContent(new HangingStream()) });
        }
    }
    private sealed class HangingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }
}
