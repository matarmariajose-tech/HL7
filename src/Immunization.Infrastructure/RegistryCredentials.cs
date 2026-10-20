namespace Immunization.Infrastructure;

public sealed record ClinicIdentity(string Oid)
{
    public override string ToString() => nameof(ClinicIdentity);
}
public sealed record TransferCredentials(string User, string Password)
{
    public override string ToString() => nameof(TransferCredentials);
}
public sealed record ApiCredentials
{
    public string User { get; }
    public string Password { get; }
    public ApiCredentials(string user, string password)
    {
        if (string.IsNullOrWhiteSpace(user) || user.Contains(':'))
            throw new ArgumentException("A nonempty Basic username without colon is required.", nameof(user));
        if (string.IsNullOrEmpty(password)) throw new ArgumentException("A Basic password is required.", nameof(password));
        User = user;
        Password = password;
    }
    public static ApiCredentials Load() => new(EnvironmentValue.Required("REGISTRY_API_USER"),
        EnvironmentValue.Required("REGISTRY_API_PASSWORD"));
    public override string ToString() => nameof(ApiCredentials);
}
public sealed record RegistryCredentials(ClinicIdentity Clinic, TransferCredentials Transfer)
{
    public RegistryCredentials(string clinicOid, string user, string password) : this(new ClinicIdentity(clinicOid), new TransferCredentials(user, password)) { }
    public static RegistryCredentials Load() => new(EnvironmentValue.Required("REGISTRY_CLINIC_OID"),
        EnvironmentValue.Required("REGISTRY_TRANSFER_USER"), EnvironmentValue.Required("REGISTRY_TRANSFER_PASSWORD"));
    public override string ToString() => nameof(RegistryCredentials);
}
internal static class EnvironmentValue
{
    public static string Required(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value && !string.IsNullOrWhiteSpace(value)
        ? value : throw new InvalidOperationException($"Missing environment variable: {name}");
}
