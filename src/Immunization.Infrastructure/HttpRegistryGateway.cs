using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Immunization.Application;
using Immunization.Domain;

namespace Immunization.Infrastructure;

public sealed class HttpRegistryGateway(HttpClient client, RegistryOptions options,
    RegistryCredentials credentials, AcknowledgmentInterpreter interpreter, TimeProvider clock) : IRegistryGateway
{
    public static HttpClient CreateClient(RegistryOptions options)
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
        { BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds) };
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (options.UseBasicAuthentication)
        {
            var user = RegistryCredentials.Required("REGISTRY_API_USER");
            var password = RegistryCredentials.Required("REGISTRY_API_PASSWORD");
            if (user.Contains(':')) throw new InvalidOperationException("Basic authentication username cannot contain a colon.");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}")));
        }
        return client;
    }
    public async Task<bool> EchoAsync(CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, options.EchoPath)
        { Content = JsonContent.Create(new EchoRequest(credentials.TransferUser, credentials.TransferPassword)) };
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        if (!response.IsSuccessStatusCode) return false;
        try
        {
            var body = await ReadBoundedAsync(response, token);
            return JsonSerializer.Deserialize<EchoResponse>(body)?.EchoBack == "echo";
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException) { return false; }
    }
    public async Task<Assessment> SendAsync(Submission submission, CancellationToken cancellationToken)
    {
        var payload = new SendRequest(credentials.ClinicOid, credentials.TransferUser, credentials.TransferPassword,
            submission.Content.TrimStart('\uFEFF').Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries),
            submission.FileName, clock.GetUtcNow());
        using var request = new HttpRequestMessage(HttpMethod.Post, options.SendPath) { Content = JsonContent.Create(payload) };
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return new(Outcome.Unknown, $"HTTP {(int)response.StatusCode}; no verified application acknowledgment. Delivery may have occurred.");
        string body;
        try { body = await ReadBoundedAsync(response, cancellationToken); }
        catch (InvalidDataException) { return new(Outcome.Unknown, "Service response exceeds configured size limit."); }
        SendResponse? envelope;
        try { envelope = JsonSerializer.Deserialize<SendResponse>(body); }
        catch (JsonException) { return new(Outcome.Unknown, "Service response does not match the documented JSON contract."); }
        var assessment = interpreter.Interpret(envelope?.Result ?? "", submission.MessageId);
        return assessment with { ServiceMessageId = envelope?.MessageId };
    }
    private async Task<string> ReadBoundedAsync(HttpResponseMessage response, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
        using var buffer = new MemoryStream();
        var bytes = new byte[8192];
        int length;
        while ((length = await stream.ReadAsync(bytes, deadline.Token)) > 0)
        {
            if (buffer.Length + length > options.MaxResponseBytes) throw new InvalidDataException();
            buffer.Write(bytes, 0, length);
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
