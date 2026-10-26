using System.Diagnostics;

namespace LilithRuntimeProviderBridge;

public enum LilithRuntimeProviderBridgeAdmission
{
    Accepted = 1,
    HermesHomeRejected = 2,
    ReservedEnvironmentCollision = 3,
    ProcessStartInfoRejected = 4,
    ProcessEnvironmentCollision = 5,
    ProcessEnvironmentMutationFailed = 6,
    LeaseAlreadyConsumed = 7,
    WrongProcessStartInfo = 8
}

public sealed class LilithHermesNativeProfileBindingResult
{
    public LilithRuntimeProviderBridgeAdmission Admission { get; }

    public LilithHermesNativeProfileBinding? Binding { get; }

    public bool Accepted =>
        Admission == LilithRuntimeProviderBridgeAdmission.Accepted;

    internal LilithHermesNativeProfileBindingResult(
        LilithRuntimeProviderBridgeAdmission admission,
        LilithHermesNativeProfileBinding? binding = null)
    {
        if (!Enum.IsDefined(admission))
            throw new ArgumentOutOfRangeException(nameof(admission));
        if ((admission == LilithRuntimeProviderBridgeAdmission.Accepted)
            != (binding is not null))
        {
            throw new ArgumentException(
                "Only an accepted result may carry a Hermes profile binding.",
                nameof(binding));
        }

        Admission = admission;
        Binding = binding;
    }
}

/// <summary>
/// Binds only the dedicated Hermes profile home. Provider credentials,
/// provider selection, model selection and provider catalogs remain owned by
/// Hermes' native configuration and RPC surface.
/// </summary>
public sealed class LilithRuntimeProviderBridge
{
    private readonly string _hermesHome;

    private LilithRuntimeProviderBridge(string hermesHome)
        => _hermesHome = hermesHome;

    public static LilithRuntimeProviderBridge ForCurrentUser(
        string productHermesHome)
        => new(productHermesHome);

    internal static LilithRuntimeProviderBridge ForIsolatedHome(
        string productHermesHome)
        => new(productHermesHome);

    public LilithHermesNativeProfileBindingResult OpenNativeProfileBinding()
    {
        var admission = LilithHermesEnvironmentFileGuard.InspectNativeProfile(
            _hermesHome);
        return admission == LilithRuntimeProviderBridgeAdmission.Accepted
            ? new LilithHermesNativeProfileBindingResult(
                admission,
                new LilithHermesNativeProfileBinding(_hermesHome))
            : new LilithHermesNativeProfileBindingResult(admission);
    }
}

public sealed class LilithHermesNativeProfileBinding : IDisposable
{
    private readonly object _gate = new();
    private readonly string _hermesHome;
    private ProcessStartInfo? _appliedTo;
    private bool _applyAttempted;
    private bool _disposed;

    internal LilithHermesNativeProfileBinding(string hermesHome)
        => _hermesHome = Path.GetFullPath(hermesHome);

    public LilithRuntimeProviderBridgeAdmission ApplyTo(
        ProcessStartInfo processStartInfo)
    {
        lock (_gate)
        {
            if (_disposed || _applyAttempted)
                return LilithRuntimeProviderBridgeAdmission.LeaseAlreadyConsumed;
            _applyAttempted = true;

            if (processStartInfo is null || processStartInfo.UseShellExecute)
            {
                return LilithRuntimeProviderBridgeAdmission
                    .ProcessStartInfoRejected;
            }

            var admission = LilithHermesEnvironmentFileGuard
                .InspectNativeProfile(_hermesHome);
            if (admission != LilithRuntimeProviderBridgeAdmission.Accepted)
                return admission;

            if (processStartInfo.Environment.ContainsKey(
                    LilithHermesEnvironmentKeys.HermesHome))
            {
                return LilithRuntimeProviderBridgeAdmission
                    .ProcessEnvironmentCollision;
            }

            try
            {
                processStartInfo.Environment.Add(
                    LilithHermesEnvironmentKeys.HermesHome,
                    _hermesHome);
                _appliedTo = processStartInfo;
                return LilithRuntimeProviderBridgeAdmission.Accepted;
            }
            catch (Exception exception) when (
                exception is ArgumentException
                    or InvalidOperationException
                    or NotSupportedException)
            {
                processStartInfo.Environment.Remove(
                    LilithHermesEnvironmentKeys.HermesHome);
                return LilithRuntimeProviderBridgeAdmission
                    .ProcessEnvironmentMutationFailed;
            }
        }
    }

    public LilithRuntimeProviderBridgeAdmission ScrubFrom(
        ProcessStartInfo processStartInfo)
    {
        lock (_gate)
        {
            if (_appliedTo is null)
                return LilithRuntimeProviderBridgeAdmission.Accepted;
            if (!ReferenceEquals(processStartInfo, _appliedTo))
                return LilithRuntimeProviderBridgeAdmission.WrongProcessStartInfo;
            return ScrubAppliedEnvironment();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            if (_appliedTo is not null)
                _ = ScrubAppliedEnvironment();
            _disposed = true;
        }
    }

    private LilithRuntimeProviderBridgeAdmission ScrubAppliedEnvironment()
    {
        try
        {
            _appliedTo!.Environment.Remove(
                LilithHermesEnvironmentKeys.HermesHome);
            _appliedTo = null;
            return LilithRuntimeProviderBridgeAdmission.Accepted;
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or InvalidOperationException
                or NotSupportedException)
        {
            return LilithRuntimeProviderBridgeAdmission
                .ProcessEnvironmentMutationFailed;
        }
    }
}
