using Immunization.Domain;

namespace Immunization.Application;

public interface IRegistryGateway
{
    Task<Assessment> SendAsync(Submission submission, CancellationToken cancellationToken);
}
public interface ITransactionJournal
{
    Task RecordPendingAsync(Submission submission, DateTimeOffset startedAt, CancellationToken cancellationToken);
    Task RecordCompletedAsync(Receipt receipt, CancellationToken cancellationToken);
}
public sealed class BatchProcessor(IRegistryGateway gateway, ITransactionJournal journal, TimeProvider clock)
{
    public async Task<IReadOnlyList<Receipt>> RunAsync(IEnumerable<Submission> submissions, CancellationToken cancellationToken)
    {
        var receipts = new List<Receipt>();
        foreach (var submission in submissions)
        {
            var started = clock.GetUtcNow();
            await journal.RecordPendingAsync(submission, started, cancellationToken);
            Assessment assessment;
            try { assessment = await gateway.SendAsync(submission, cancellationToken); }
            catch (OperationCanceledException) { assessment = new(Outcome.Unknown, "Request cancelled; delivery may have occurred. Do not retry automatically."); }
            catch (IOException) { assessment = new(Outcome.Unknown, "Response stream failed; delivery may have occurred. Do not retry automatically."); }
            catch (HttpRequestException) { assessment = new(Outcome.Unknown, "Transport failed; delivery may have occurred. Do not retry automatically."); }
            var receipt = new Receipt(submission.Id, submission.FileName, submission.Sha256, submission.MessageId,
                started, clock.GetUtcNow(), assessment);
            await journal.RecordCompletedAsync(receipt, CancellationToken.None);
            receipts.Add(receipt);
            if (cancellationToken.IsCancellationRequested) break;
        }
        return receipts;
    }
}
