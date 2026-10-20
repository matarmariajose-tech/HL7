using Immunization.Domain;
using Immunization.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
var scenarios = builder.Configuration.GetSection("Demo:Scenarios").Get<string[]>()
    ?? throw new InvalidOperationException("Demo scenarios are required.");
if (scenarios.Length == 0) throw new InvalidOperationException("At least one scenario is required.");
var credentials = builder.Configuration.GetSection("Demo:Credentials");
var app = builder.Build();
var sequence = -1;
app.MapPost("/echo", (EchoRequest request) => request.User == credentials["User"] && request.Password == credentials["Password"]
    ? Results.Json(new EchoResponse("echo")) : Results.Unauthorized());
app.MapPost("/send", (SendRequest request) =>
{
    if (request.Username != credentials["User"] || request.Password != credentials["Password"] || request.ClinicOid != credentials["ClinicOid"])
        return Results.Unauthorized();
    var message = new Hl7(string.Join('\r', request.Hl7));
    var scenario = scenarios[Interlocked.Increment(ref sequence) % scenarios.Length];
    var id = message.MessageId;
    if (scenario == "unavailable") return Results.StatusCode(503);
    if (scenario == "malformed") return Results.Json(new SendResponse(Guid.NewGuid(), "no acknowledgment"), statusCode: 201);
    var code = scenario switch { "rejected" => "AE", "commit" => "CA", _ => "AA" };
    if (scenario == "uncorrelated") id = Guid.NewGuid().ToString();
    var response = $"MSH|^~\\&|DEMO|LOCAL|||{DateTimeOffset.UtcNow:yyyyMMddHHmmss}||ACK^V04^ACK|{Guid.NewGuid():N}|T|2.5.1\rMSA|{code}|{id}\r";
    response += scenario switch
    {
        "observed" => "ERR||RXA^1^5|103^Table value not found^HL70357|W|||Review vaccine coding.\r",
        "rejected" => "ERR||RXA^1^5|101^Required field missing^HL70357|E|||Vaccine code required.\r",
        _ => ""
    };
    return Results.Json(new SendResponse(Guid.NewGuid(), response), statusCode: 201);
});
app.Run();
