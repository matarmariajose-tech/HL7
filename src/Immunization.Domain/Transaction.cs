namespace Immunization.Domain;

public enum Outcome { Accepted, Observed, Rejected, Unknown }
public enum AssessmentReason
{
    Unspecified, Accepted, AcceptedWithObservations, ApplicationRejected, SourceIdentifierMissing,
    InvalidAcknowledgment, UnexpectedMessageType, AmbiguousAcknowledgment, CorrelationMismatch,
    ContradictoryAcknowledgment, CommitAcknowledgmentOnly, UnsupportedAcknowledgmentCode,
    RequestCancelled, TransportFailure, ResponseStreamFailure, HttpFailure, ResponseTooLarge, InvalidResponseEnvelope
}
public sealed record Finding(string Code, string Severity, string Location, string Description);
public sealed record Assessment(Outcome Outcome, string Reason, string? AcknowledgmentCode = null,
    IReadOnlyList<Finding>? Findings = null, Guid? ServiceMessageId = null,
    AssessmentReason ReasonCode = AssessmentReason.Unspecified, int? HttpStatusCode = null);
public sealed record Submission(Guid Id, string FileName, string Sha256, string? MessageId, string Content);
public sealed record Receipt(Guid Id, string FileName, string Sha256, string? MessageId,
    DateTimeOffset StartedAt, DateTimeOffset CompletedAt, Assessment Assessment);
