using System;

namespace LilithTextInjector;

internal sealed class PlayerAuthoredIdentityProjectionShadowObservation
{
    internal PlayerAuthoredIdentityProjectionCandidate Projection { get; }
    internal int PromptWrites => 0;
    internal int ReplyChanges => 0;
    internal int PsychologyWrites => 0;
    internal int MotionWrites => 0;
    internal int ProviderRequests => 0;

    internal PlayerAuthoredIdentityProjectionShadowObservation(
        PlayerAuthoredIdentityProjectionCandidate projection)
    {
        Projection = projection ?? throw new ArgumentNullException(nameof(projection));
    }
}

/// <summary>
/// Read-only boundary for the future identity-continuity context.
/// Its public surface intentionally accepts numeric identity state only, never player text,
/// and exposes no provider, prompt, psychology, or motion dependency.
/// </summary>
internal static class PlayerAuthoredIdentityProjectionShadowRuntime
{
    internal static PlayerAuthoredIdentityProjectionShadowObservation Observe(
        PlayerAuthoredIdentityState state)
        => new(PlayerAuthoredIdentityProjection.Project(state));
}
