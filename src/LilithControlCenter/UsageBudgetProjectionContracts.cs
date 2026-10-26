using LilithRuntimeContracts;

namespace LilithControlCenter;

public sealed record UsageBudgetSnapshot(bool Supported, HermesUsageBudgetPreference? Preference,
    HermesUsageBudgetPreview? Preview = null);
public interface IUsageBudgetProjection
{
    Task<UsageBudgetSnapshot> ReadUsageBudgetAsync(CancellationToken cancellationToken);
    Task<HermesUsageBudgetPreview?> ReadUsageBudgetPreviewAsync(CancellationToken cancellationToken)
        => Task.FromResult<HermesUsageBudgetPreview?>(null);
    Task<HermesUsageBudgetCommit> SaveUsageBudgetAsync(HermesUsageBudgetUpdate request, CancellationToken cancellationToken);
}
public sealed class UsageBudgetOperationException(string code) : Exception(code)
{
    public string Code { get; } = code;
}
