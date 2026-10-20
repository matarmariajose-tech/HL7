using System.Text.Json.Serialization;

namespace Immunization.Infrastructure;

public sealed record EchoRequest(
    [property: JsonPropertyName("user")] string User,
    [property: JsonPropertyName("password")] string Password)
{
    public override string ToString() => nameof(EchoRequest);
}
public sealed record EchoResponse([property: JsonPropertyName("echoBack")] string? EchoBack);
public sealed record SendRequest(
    [property: JsonPropertyName("clinicOID")] string ClinicOid,
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("password")] string Password,
    [property: JsonPropertyName("hl7")] string[] Hl7,
    [property: JsonPropertyName("nameHl7")] string NameHl7,
    [property: JsonPropertyName("dateSend")] DateTimeOffset DateSend)
{
    public override string ToString() => nameof(SendRequest);
}
public sealed record SendResponse(
    [property: JsonPropertyName("messageId")] Guid MessageId,
    [property: JsonPropertyName("result")] string? Result);
