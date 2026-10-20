using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Immunization.Application;
using Immunization.Domain;
using Immunization.Infrastructure;
using Xunit;

namespace Immunization.Tests;

public sealed class BoundaryTests
{
    private static RegistryOptions Options => new() { BaseUrl = "https://registry.example/", EchoPath = "echo", SendPath = "send" };
    private static Submission Submission => new(Guid.NewGuid(), "sample.hl7", "hash", "source", "MSH|^~\\&|SOURCE|FACILITY|||20260101||VXU^V04|source|T|2.5.1\r");

    [Fact]
    public void BasicIdentityIsExplicitAndIndependentOfTransferIdentity()
    {
        var api = new ApiCredentials("api-user", "api-password");
        var registry = new RegistryCredentials(new ClinicIdentity("clinic-oid"), new TransferCredentials("transfer-user", "transfer-password"));
        using var client = HttpRegistryGateway.CreateClient(Options, api);
        var authorization = client.DefaultRequestHeaders.Authorization!;
        Assert.Equal("Basic", authorization.Scheme);
        Assert.Equal("api-user:api-password", Encoding.UTF8.GetString(Convert.FromBase64String(authorization.Parameter!)));
        Assert.Equal("clinic-oid", registry.Clinic.Oid);
        Assert.Equal("transfer-user", registry.Transfer.User);
        Assert.Equal(Timeout.InfiniteTimeSpan, client.Timeout);
        Assert.Throws<InvalidOperationException>(() => HttpRegistryGateway.CreateClient(Options));
    }

    [Theory]
    [InlineData("api:user", "password")]
    [InlineData("", "password")]
    [InlineData("user", "")]
    public void InvalidBasicIdentityIsRejectedWithoutEchoingItsValue(string user, string password)
    {
        var exception = Assert.Throws<ArgumentException>(() => new ApiCredentials(user, password));
        Assert.DoesNotContain("api:user", exception.Message);
    }

    [Fact]
    public void IdentityDiagnosticsNeverFormatSecretValues()
    {
        var api = new ApiCredentials("api-user", "secret-api");
        var transfer = new TransferCredentials("transfer-user", "secret-transfer");
        var clinic = new ClinicIdentity("clinic-identifier");
        var registry = new RegistryCredentials(clinic, transfer);
        var diagnostic = $"{api} {transfer} {registry} {clinic}";
        Assert.DoesNotContain("secret-api", diagnostic);
        Assert.DoesNotContain("secret-transfer", diagnostic);
        Assert.DoesNotContain("clinic-identifier", diagnostic);
    }

    [Theory]
    [InlineData(null, ResponseKind.Missing)]
    [InlineData("", ResponseKind.Empty)]
    [InlineData("   ", ResponseKind.Empty)]
    [InlineData("<Fault>private detail</Fault>", ResponseKind.Markup)]
    [InlineData("{\"error\":\"private detail\"}", ResponseKind.JsonLike)]
    [InlineData("private detail", ResponseKind.Text)]
    public async Task UnexpectedResultRetainsShapeAndFingerprintWithoutItsBody(string? result, ResponseKind expected)
    {
        var serviceId = Guid.NewGuid();
        var body = JsonSerializer.Serialize(new { messageId = serviceId, result });
        using var handler = new Handler((_, _) => Task.FromResult(Response(body)));
        using var client = new HttpClient(handler) { BaseAddress = new Uri(Options.BaseUrl) };
        var assessment = await Gateway(client).SendAsync(Submission, default);
        Assert.Equal(Outcome.Unknown, assessment.Outcome);
        Assert.Equal(serviceId, assessment.ServiceMessageId);
        Assert.Equal(expected, assessment.Evidence!.ResultKind);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))), assessment.Evidence.BodySha256);
        Assert.Equal(Encoding.UTF8.GetByteCount(body), assessment.Evidence.BodyBytes);
        Assert.Equal(result?.Length, assessment.Evidence.ResultCharacters);
        Assert.DoesNotContain("private detail", JsonSerializer.Serialize(assessment));
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("{\"result\":42}", "result")]
    [InlineData("{\"messageId\":\"not-a-guid\"}", "messageId")]
    [InlineData("<html>upstream error</html>", "$")]
    public async Task ContractFailuresExposeOnlyKnownFieldNames(string body, string field)
    {
        using var handler = new Handler((_, _) => Task.FromResult(Response(body)));
        using var client = new HttpClient(handler) { BaseAddress = new Uri(Options.BaseUrl) };
        var assessment = await Gateway(client).SendAsync(Submission, default);
        Assert.Equal(AssessmentReason.InvalidResponseEnvelope, assessment.ReasonCode);
        Assert.Equal(field, assessment.Evidence!.ContractField);
        Assert.DoesNotContain("upstream error", JsonSerializer.Serialize(assessment));
    }

    [Fact]
    public async Task ConnectionErrorsCrossThePortWithoutHttpExceptionCoupling()
    {
        using var handler = new Handler((_, _) => throw new HttpRequestException("sensitive transport detail"));
        using var client = new HttpClient(handler) { BaseAddress = new Uri(Options.BaseUrl) };
        var exception = await Assert.ThrowsAsync<DeliveryException>(() => Gateway(client).SendAsync(Submission, default));
        Assert.Equal(DeliveryFailure.ConnectionFailed, exception.Failure);
        Assert.DoesNotContain("sensitive transport detail", exception.Message);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task EchoFailureNeverEscapesAsAClinicalRejection()
    {
        using var handler = new Handler((_, _) => throw new HttpRequestException());
        using var client = new HttpClient(handler) { BaseAddress = new Uri(Options.BaseUrl) };
        Assert.False(await Gateway(client).EchoAsync(default));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task TimeoutAndUserCancellationRemainDistinct()
    {
        using var timeoutHandler = new Handler((_, _) => throw new TaskCanceledException());
        using var timeoutClient = new HttpClient(timeoutHandler) { BaseAddress = new Uri(Options.BaseUrl) };
        var failure = await Assert.ThrowsAsync<DeliveryException>(() => Gateway(timeoutClient).SendAsync(Submission, default));
        Assert.Equal(DeliveryFailure.TimedOut, failure.Failure);
        using var cancellation = new CancellationTokenSource();
        using var cancelledHandler = new Handler((_, token) => { cancellation.Cancel(); throw new OperationCanceledException(token); });
        using var cancelledClient = new HttpClient(cancelledHandler) { BaseAddress = new Uri(Options.BaseUrl) };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Gateway(cancelledClient).SendAsync(Submission, cancellation.Token));
    }

    private static HttpRegistryGateway Gateway(HttpClient client) => new(client, Options,
        new RegistryCredentials(new ClinicIdentity("clinic"), new TransferCredentials("transfer", "secret")), new(), TimeProvider.System);
    private static HttpResponseMessage Response(string body) => new(HttpStatusCode.Created)
    { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> action) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Calls++; return action(request, cancellationToken); }
    }
}
