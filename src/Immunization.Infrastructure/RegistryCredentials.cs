namespace Immunization.Infrastructure;

public sealed record RegistryCredentials(string ClinicOid, string TransferUser, string TransferPassword)
{
    public static RegistryCredentials Load() => new(Required("REGISTRY_CLINIC_OID"),
        Required("REGISTRY_TRANSFER_USER"), Required("REGISTRY_TRANSFER_PASSWORD"));
    public static string Required(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
        ? value : throw new InvalidOperationException($"Missing environment variable: {name}");
    public override string ToString() => nameof(RegistryCredentials);
}
