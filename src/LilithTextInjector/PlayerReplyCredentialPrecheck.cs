using System;

namespace LilithTextInjector;

internal enum PlayerReplyCredentialAuthority
{
    Invalid = 0,
    LegacyConfiguration = 1,
    ProtectedControlPlane = 2
}

internal enum PlayerReplyCredentialPrecheckAdmission
{
    Invalid = 0,
    Accepted = 1,
    ProviderInvalid = 2,
    CredentialUnavailable = 3,
    ProtectedOwnerCredentialUnavailable = 4
}

internal sealed class PlayerReplyCredentialPrecheckFacts
{
    internal ProviderKind Provider { get; }
    internal bool LegacyCredentialConfigured { get; }
    internal bool ProtectedControlPlaneRouteEnabled { get; }
    internal bool ProtectedControlPlaneOwnerReady { get; }
    internal bool ProtectedControlPlaneCredentialConfigured { get; }

    internal PlayerReplyCredentialPrecheckFacts(
        ProviderKind provider,
        bool legacyCredentialConfigured,
        bool protectedControlPlaneRouteEnabled,
        bool protectedControlPlaneOwnerReady,
        bool protectedControlPlaneCredentialConfigured)
    {
        Provider = provider;
        LegacyCredentialConfigured = legacyCredentialConfigured;
        ProtectedControlPlaneRouteEnabled =
            protectedControlPlaneRouteEnabled;
        ProtectedControlPlaneOwnerReady =
            protectedControlPlaneOwnerReady;
        ProtectedControlPlaneCredentialConfigured =
            protectedControlPlaneCredentialConfigured;
    }
}

internal sealed class PlayerReplyCredentialPrecheckResult
{
    internal PlayerReplyCredentialPrecheckAdmission Admission { get; }
    internal ProviderKind Provider { get; }
    internal PlayerReplyCredentialAuthority Authority { get; }

    internal bool Accepted =>
        Admission == PlayerReplyCredentialPrecheckAdmission.Accepted;
    internal bool ProhibitsLegacyFallback =>
        Authority == PlayerReplyCredentialAuthority.ProtectedControlPlane;

    internal PlayerReplyCredentialPrecheckResult(
        PlayerReplyCredentialPrecheckAdmission admission,
        ProviderKind provider,
        PlayerReplyCredentialAuthority authority)
    {
        Admission = admission;
        Provider = provider;
        Authority = authority;
    }
}

internal static class PlayerReplyCredentialPrecheckPolicy
{
    internal static PlayerReplyCredentialPrecheckResult Evaluate(
        PlayerReplyCredentialPrecheckFacts facts)
    {
        if (facts == null)
            throw new ArgumentNullException(nameof(facts));
        if (facts.Provider == ProviderKind.Invalid)
        {
            return Reject(
                facts.Provider,
                PlayerReplyCredentialPrecheckAdmission.ProviderInvalid,
                PlayerReplyCredentialAuthority.Invalid);
        }

        // An owner-ready protected route is an atomic ownership boundary.
        // It may never fall back to a legacy credential after this point.
        if (facts.ProtectedControlPlaneOwnerReady)
        {
            if (!facts.ProtectedControlPlaneRouteEnabled
                || !facts.ProtectedControlPlaneCredentialConfigured)
            {
                return Reject(
                    facts.Provider,
                    PlayerReplyCredentialPrecheckAdmission
                        .ProtectedOwnerCredentialUnavailable,
                    PlayerReplyCredentialAuthority.ProtectedControlPlane);
            }
            return new PlayerReplyCredentialPrecheckResult(
                PlayerReplyCredentialPrecheckAdmission.Accepted,
                facts.Provider,
                PlayerReplyCredentialAuthority.ProtectedControlPlane);
        }

        if (facts.LegacyCredentialConfigured)
        {
            return new PlayerReplyCredentialPrecheckResult(
                PlayerReplyCredentialPrecheckAdmission.Accepted,
                facts.Provider,
                PlayerReplyCredentialAuthority.LegacyConfiguration);
        }

        return Reject(
            facts.Provider,
            PlayerReplyCredentialPrecheckAdmission.CredentialUnavailable,
            PlayerReplyCredentialAuthority.Invalid);
    }

    private static PlayerReplyCredentialPrecheckResult Reject(
        ProviderKind provider,
        PlayerReplyCredentialPrecheckAdmission admission,
        PlayerReplyCredentialAuthority authority)
        => new(admission, provider, authority);
}
