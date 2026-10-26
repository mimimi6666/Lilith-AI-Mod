namespace LilithControlCenter;

internal sealed partial class ControlCenterPlayerSaveClient
{
    internal ControlCenterPlayerSaveClient() : this(ControlCenterRuntimeExit.StopAsync,
        (command, path, token) => RunProcessAsync(
            LilithRuntimeBootstrap.ResolveRuntimeDirectory(AppContext.BaseDirectory), command, path, token)) { }
}
