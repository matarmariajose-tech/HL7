namespace Immunization.Application;

public enum DeliveryFailure { TimedOut, ConnectionFailed, InterruptedResponse }

public sealed class DeliveryException(DeliveryFailure failure, Exception innerException)
    : Exception("The delivery outcome could not be confirmed.", innerException)
{
    public DeliveryFailure Failure { get; } = failure;
}
