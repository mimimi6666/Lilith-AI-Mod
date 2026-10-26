using System.Diagnostics;
using LilithRuntimeProviderBridge;

namespace LilithRuntime;

internal interface ILilithHermesProfileEnvironment
{
    ILilithHermesProfileEnvironmentLease Open();
}

internal interface ILilithHermesProfileEnvironmentLease : IDisposable
{
    void ApplyTo(ProcessStartInfo startInfo);

    void ScrubFrom(ProcessStartInfo startInfo);
}

internal sealed class LilithCurrentUserHermesProfileEnvironment
    : ILilithHermesProfileEnvironment
{
    private readonly LilithRuntimeProviderBridge.LilithRuntimeProviderBridge
        _bridge;

    internal LilithCurrentUserHermesProfileEnvironment(string hermesHomePath)
    {
        _bridge = LilithRuntimeProviderBridge.LilithRuntimeProviderBridge
            .ForCurrentUser(hermesHomePath);
    }

    public ILilithHermesProfileEnvironmentLease Open()
    {
        var result = _bridge.OpenNativeProfileBinding();
        if (!result.Accepted || result.Binding is null)
        {
            throw new LilithRuntimeException(
                "The dedicated Hermes profile could not be bound (admission="
                + result.Admission + ").");
        }

        return new Lease(result.Binding);
    }

    private sealed class Lease : ILilithHermesProfileEnvironmentLease
    {
        private readonly LilithHermesNativeProfileBinding _binding;
        private bool _disposed;

        internal Lease(LilithHermesNativeProfileBinding binding)
            => _binding = binding
                ?? throw new ArgumentNullException(nameof(binding));

        public void ApplyTo(ProcessStartInfo startInfo)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var admission = _binding.ApplyTo(startInfo);
            if (admission != LilithRuntimeProviderBridgeAdmission.Accepted)
            {
                throw new LilithRuntimeException(
                    "The dedicated Hermes profile was rejected before startup (admission="
                    + admission + ").");
            }
        }

        public void ScrubFrom(ProcessStartInfo startInfo)
        {
            var admission = _binding.ScrubFrom(startInfo);
            if (admission != LilithRuntimeProviderBridgeAdmission.Accepted)
            {
                throw new LilithRuntimeException(
                    "The dedicated Hermes profile environment could not be scrubbed (admission="
                    + admission + ").");
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _binding.Dispose();
        }
    }
}
