using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Immunization.Application;
using Immunization.Domain;

namespace Immunization.Infrastructure;

public sealed class HttpRegistryGateway(HttpClient client, RegistryOptions options,
    RegistryCredentials credentials, AcknowledgmentInterpreter interpreter, TimeProvider clock) : IRegistryGateway
{
    public static HttpClient CreateClient(RegistryOptions options, ApiCredentials? apiCredentials = null)
    {
        if (options.UseBasicAuthentication && apiCredentials is null)
            throw new InvalidOperationException("API credentials are required for Basic authentication.");
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
        { BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/"), Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (options.UseBasicAuthentication)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{apiCredentials!.User}:{apiCredentials.Password}")));
        return client;
    }

    public async Task<bool> EchoAsync(CancellationToken cancellationToken)
    {
        using var deadline = CreateDeadline(cancellationToken);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, options.EchoPath)
            { Content = JsonContent.Create(new EchoRequest(credentials.Transfer.User, credentials.Transfer.Password)) };
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (!response.IsSuccessStatusCode) return false;
            var body = await ReadBoundedAsync(response, deadline.Token);
            return JsonSerializer.Deserialize<EchoResponse>(body)?.EchoBack == "echo";
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return false; }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or HttpRequestException or IOException)
        { return false; }
    }

    public async Task<Assessment> SendAsync(Submission submission, CancellationToken cancellationToken)
    {
        using var deadline = CreateDeadline(cancellationToken);
        try { return await SendCoreAsync(submission, deadline.Token); }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        { throw new DeliveryException(DeliveryFailure.TimedOut, exception); }
        catch (HttpRequestException exception) { throw new DeliveryException(DeliveryFailure.ConnectionFailed, exception); }
        catch (IOException exception) { throw new DeliveryException(DeliveryFailure.InterruptedResponse, exception); }
    }

    private async Task<Assessment> SendCoreAsync(Submission submission, CancellationToken token)
    {
        var payload = new SendRequest(credentials.Clinic.Oid, credentials.Transfer.User, credentials.Transfer.Password,
            submission.Content.TrimStart('\uFEFF').Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries),
            submission.FileName, clock.GetUtcNow());
        using var request = new HttpRequestMessage(HttpMethod.Post, options.SendPath) { Content = JsonContent.Create(payload) };
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        byte[] body;
        try { body = await ReadBoundedAsync(response, token); }
        catch (InvalidDataException)
        {
            return new(Outcome.Unknown, "Service response exceeds configured size limit.",
                ReasonCode: AssessmentReason.ResponseTooLarge, HttpStatusCode: (int)response.StatusCode);
        }
        var evidence = Evidence(response, body);
        if (!response.IsSuccessStatusCode)
            return new(Outcome.Unknown, $"HTTP {(int)response.StatusCode}; no verified application acknowledgment. Delivery may have occurred.",
                ReasonCode: AssessmentReason.HttpFailure, HttpStatusCode: (int)response.StatusCode, Evidence: evidence);
        SendResponse? envelope;
        try { envelope = JsonSerializer.Deserialize<SendResponse>(body); }
        catch (JsonException exception)
        {
            evidence = evidence with { ContractField = exception.Path is "$.result" ? "result" : exception.Path is "$.messageId" ? "messageId" : "$" };
            return new(Outcome.Unknown, "Service response does not match the documented JSON contract.",
                ReasonCode: AssessmentReason.InvalidResponseEnvelope, HttpStatusCode: (int)response.StatusCode, Evidence: evidence);
        }
        evidence = evidence with
        {
            ResultKind = Identify(envelope?.Result),
            ResultCharacters = envelope?.Result?.Length,
            ResultSha256 = envelope?.Result is { } result ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(result))) : null
        };
        var assessment = interpreter.Interpret(envelope?.Result ?? "", submission.MessageId);
        return assessment with
        {
            ServiceMessageId = envelope is not null && envelope.MessageId != Guid.Empty ? envelope.MessageId : null,
            HttpStatusCode = (int)response.StatusCode,
            Evidence = evidence
        };
    }

    private CancellationTokenSource CreateDeadline(CancellationToken token)
    {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        return deadline;
    }

    private async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response, CancellationToken token)
    {
        if (response.Content.Headers.ContentLength > options.MaxResponseBytes) throw new InvalidDataException();
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var content = new MemoryStream();
        var buffer = new byte[8192];
        int length;
        while ((length = await stream.ReadAsync(buffer, token)) > 0)
        {
            if (length > options.MaxResponseBytes - content.Length) throw new InvalidDataException();
            content.Write(buffer, 0, length);
        }
        return content.ToArray();
    }

    private static ResponseEvidence Evidence(HttpResponseMessage response, byte[] body) => new(
        response.Content.Headers.ContentType?.MediaType, body.Length, Convert.ToHexString(SHA256.HashData(body)),
        Identify(Encoding.UTF8.GetString(body)));

    private static ResponseKind Identify(string? value)
    {
        if (value is null) return ResponseKind.Missing;
        var text = value.TrimStart('\uFEFF', ' ', '\r', '\n', '\t');
        if (text.Length == 0) return ResponseKind.Empty;
        if (text.StartsWith("MSH", StringComparison.Ordinal)) return ResponseKind.Hl7Like;
        return text[0] switch { '{' or '[' => ResponseKind.JsonLike, '<' => ResponseKind.Markup, _ => ResponseKind.Text };
    }
}
