using Immunization.Domain;
using Immunization.Infrastructure;
using Xunit;

namespace Immunization.Tests;

public sealed class RecoveryTests
{
    [Fact]
    public async Task PendingAttemptRemainsVisibleWithoutInventingACompletion()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".jsonl");
        var report = path + ".html";
        var submission = new Submission(Guid.NewGuid(), "pending.hl7", "hash", "source", "private message");
        try
        {
            using (var journal = new JsonJournal(path))
                await journal.RecordPendingAsync(submission, DateTimeOffset.UtcNow, default);
            var snapshot = JournalReader.Read(path);
            Assert.Empty(snapshot.Completed);
            Assert.Equal(submission.Id, Assert.Single(snapshot.Pending).Id);
            await HtmlReport.WriteAsync(report, snapshot.Completed, default, snapshot.Pending);
            var html = await File.ReadAllTextAsync(report);
            Assert.Contains("Unknown · Pending", html);
            Assert.Contains("0 transacciones completadas", html);
            Assert.Contains("Intentos sin cierre: 1", html);
            Assert.DoesNotContain(submission.Content, html);
        }
        finally { File.Delete(path); File.Delete(report); }
    }

    [Fact]
    public async Task CompletedEventClosesOnlyMatchingTransaction()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".jsonl");
        var submission = new Submission(Guid.NewGuid(), "a.hl7", "hash", "source", "private message");
        var now = DateTimeOffset.UtcNow;
        try
        {
            using (var journal = new JsonJournal(path))
            {
                await journal.RecordPendingAsync(submission, now, default);
                await journal.RecordCompletedAsync(new(submission.Id, submission.FileName, "different hash", submission.MessageId,
                    now, now, new(Outcome.Accepted, "accepted")), default);
            }
            Assert.Throws<InvalidDataException>(() => JournalReader.Read(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task TruncatedJournalIsReportedInsteadOfSilentlyDroppingEvidence()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "{\"State\":\"Pend");
            Assert.ThrowsAny<System.Text.Json.JsonException>(() => JournalReader.Read(path));
        }
        finally { File.Delete(path); }
    }
}
