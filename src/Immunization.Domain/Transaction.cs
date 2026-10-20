namespace Immunization.Domain;

public enum Outcome { Accepted, Observed, Rejected, Unknown }
public enum AssessmentReason
{
    Unspecified, Accepted, AcceptedWithObservations, ApplicationRejected, SourceIdentifierMissing,
    InvalidAcknowledgment, UnexpectedMessageType, AmbiguousAcknowledgment, CorrelationMismatch,
    ContradictoryAcknowledgment, CommitAcknowledgmentOnly, UnsupportedAcknowledgmentCode,
    RequestCancelled, TransportFailure, ResponseStreamFailure, HttpFailure, ResponseTooLarge, InvalidResponseEnvelope, RequestTimedOut
}
public sealed record Finding(string Code, string Severity, string Location, string Description);
public sealed record Assessment(Outcome Outcome, string Reason, string? AcknowledgmentCode = null,
    IReadOnlyList<Finding>? Findings = null, Guid? ServiceMessageId = null,
    AssessmentReason ReasonCode = AssessmentReason.Unspecified, int? HttpStatusCode = null, ResponseEvidence? Evidence = null);
public sealed record Submission(Guid Id, string FileName, string Sha256, string? MessageId, string Content);
public sealed record Receipt(Guid Id, string FileName, string Sha256, string? MessageId,
    DateTimeOffset StartedAt, DateTimeOffset CompletedAt, Assessment Assessment);

public enum ResponseKind { Empty, Hl7Like, JsonLike, Markup, Text, Missing }
public sealed record ResponseEvidence(string? MediaType, int BodyBytes, string BodySha256,
    ResponseKind BodyKind, ResponseKind ResultKind = ResponseKind.Missing, int? ResultCharacters = null, string? ResultSha256 = null, string? ContractField = null);
