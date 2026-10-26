using System.IO.Pipes;

namespace LilithRuntimeContracts;

public static class LilithRuntimePipeEndpoint
{
    public const string PipeName = "lilith-runtime-ui-v1";
    public const int MaximumServerInstances = 1;
    public const PipeOptions RequiredServerOptions =
        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly;
    public const PipeOptions ClientOptions = PipeOptions.Asynchronous;

    public static NamedPipeServerStream CreateCurrentUserServer()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Lilith Runtime current-user pipes require Windows.");
        }

        return new NamedPipeServerStream(
            PipeName,
            PipeDirection.InOut,
            MaximumServerInstances,
            PipeTransmissionMode.Byte,
            RequiredServerOptions,
            LilithRuntimeProtocol.MaximumFrameBytes,
            LilithRuntimeProtocol.MaximumFrameBytes);
    }

    public static NamedPipeClientStream CreateCurrentUserClient()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Lilith Runtime current-user pipes require Windows.");
        }

        return new NamedPipeClientStream(
            ".",
            PipeName,
            PipeDirection.InOut,
            ClientOptions);
    }
}
