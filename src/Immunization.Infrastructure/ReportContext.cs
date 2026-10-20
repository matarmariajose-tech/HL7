namespace Immunization.Infrastructure;

public sealed record ReportContext(string EnvironmentName, bool IsSimulation, string TimeZoneId, string? Host = null);
