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
            cancellationToken.ThrowIfCancellationRequested();
            var started = clock.GetUtcNow();
            await journal.RecordPendingAsync(submission, started, cancellationToken);
            Assessment assessment;
            try { assessment = await gateway.SendAsync(submission, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { assessment = new(Outcome.Unknown, "Request cancelled; delivery may have occurred. Do not retry automatically.", ReasonCode: AssessmentReason.RequestCancelled); }
            catch (DeliveryException exception)
            {
                assessment = exception.Failure switch
                {
                    DeliveryFailure.TimedOut => new(Outcome.Unknown, "Request timed out; delivery may have occurred. Do not retry automatically.", ReasonCode: AssessmentReason.RequestTimedOut),
                    DeliveryFailure.InterruptedResponse => new(Outcome.Unknown, "Response stream failed; delivery may have occurred. Do not retry automatically.", ReasonCode: AssessmentReason.ResponseStreamFailure),
                    _ => new(Outcome.Unknown, "Transport failed; delivery may have occurred. Do not retry automatically.", ReasonCode: AssessmentReason.TransportFailure)
                };
            }
            var receipt = new Receipt(submission.Id, submission.FileName, submission.Sha256, submission.MessageId,
                started, clock.GetUtcNow(), assessment);
            await journal.RecordCompletedAsync(receipt, CancellationToken.None);
            receipts.Add(receipt);
            if (cancellationToken.IsCancellationRequested) break;
        }
        return receipts;
    }
}
