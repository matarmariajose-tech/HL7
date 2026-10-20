using System.Text.Json;

namespace Immunization.Infrastructure;

public sealed record RegistryOptions
{
    public required string BaseUrl { get; init; }
    public required string EchoPath { get; init; }
    public required string SendPath { get; init; }
    public string EnvironmentName { get; init; } = "Servicio externo";
    public bool IsSimulation { get; init; }
    public string ReportTimeZoneId { get; init; } = "America/Argentina/Buenos_Aires";
    public int TimeoutSeconds { get; init; } = 30;
    public long MaxInputBytes { get; init; } = 1048576;
    public long MaxBatchBytes { get; init; } = 33554432;
    public int MaxFiles { get; init; } = 1000;
    public int MaxResponseBytes { get; init; } = 1048576;
    public bool UseBasicAuthentication { get; init; } = true;
    public bool AllowLoopbackHttp { get; init; }
    public static RegistryOptions Load(string path)
    {
        var options = JsonSerializer.Deserialize<RegistryOptions>(File.ReadAllText(path))
            ?? throw new InvalidDataException("Missing registry configuration.");
        if (new[] { options.BaseUrl, options.EchoPath, options.SendPath }
            .Any(x => string.IsNullOrWhiteSpace(x) || x.Contains("__", StringComparison.Ordinal)))
            throw new InvalidDataException("Missing endpoint configuration.");
        var uri = new Uri(options.BaseUrl, UriKind.Absolute);
        if (uri.Scheme != "https" && !(options.AllowLoopbackHttp && uri.IsLoopback && uri.Scheme == "http"))
            throw new InvalidDataException("HTTPS is required outside the local demo.");
        if (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidDataException("Base URL must not contain credentials, query or fragment.");
        foreach (var route in new[] { options.EchoPath, options.SendPath })
            if (route.StartsWith('/') || route.Contains("..", StringComparison.Ordinal) ||
                Uri.TryCreate(route, UriKind.Absolute, out _) || route.Contains('?') || route.Contains('#'))
                throw new InvalidDataException("Endpoint paths must be relative to the configured base URL.");
        if (!options.EchoPath.EndsWith("/echo", StringComparison.OrdinalIgnoreCase) && options.EchoPath != "echo" ||
            !options.SendPath.EndsWith("/send", StringComparison.OrdinalIgnoreCase) && options.SendPath != "send")
            throw new InvalidDataException("Only echo and send endpoints are allowed.");
        if (options.TimeoutSeconds is < 1 or > 300 || options.MaxInputBytes < 1 || options.MaxResponseBytes < 1 || options.MaxBatchBytes < 1 || options.MaxFiles < 1)
            throw new InvalidDataException("Invalid resource limits.");
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(options.ReportTimeZoneId); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        { throw new InvalidDataException("Invalid report timezone.", ex); }
        return options;
    }
}
