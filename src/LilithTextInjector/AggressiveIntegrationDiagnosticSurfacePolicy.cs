namespace LilithTextInjector;

internal static class D029DiagnosticSurfacePolicy
{
    internal static bool MayUseUnityStatusSurface(
        bool isolatedProfileActive)
        => !isolatedProfileActive;

    internal static bool CoordinatorOwnsSurface(
        bool coordinatorStatusVisible)
        => coordinatorStatusVisible;

    internal static bool MayRenderSubordinateStatus(
        bool coordinatorStatusVisible)
        => !CoordinatorOwnsSurface(coordinatorStatusVisible);
}
