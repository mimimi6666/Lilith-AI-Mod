using LilithRuntimeContracts;
namespace LilithControlCenter;
public sealed record LocalCompatibilitySnapshot(bool Supported, HermesLocalCompatibilityPreference? Preference);
public interface ILocalCompatibilityProjection
{
    Task<LocalCompatibilitySnapshot> ReadLocalCompatibilityAsync(CancellationToken cancellationToken);
    Task<HermesLocalCompatibilityCommit> SaveLocalCompatibilityAsync(HermesLocalCompatibilityUpdate request, CancellationToken cancellationToken);
}
public sealed class LocalCompatibilityOperationException(string code) : Exception(code)
{
    public string Code { get; } = code;
}
