namespace LilithControlCenter;

// Presentation only: no key, native-state assertion, retry or permission store.
// An observed catalog remains authoritative even while a mutation's message is
// kept visible. Manual refresh or a deliberate editor change dismisses it.
internal sealed class CredentialMutationPresentationState
{
    private string? _providerId;
    private string? _outcome;

    internal void Remember(string providerId, string outcome)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(outcome);
        _providerId = providerId;
        _outcome = outcome;
    }

    internal void Clear() => (_providerId, _outcome) = (null, null);

    internal string Observe(string? providerId, string observedMessage,
        bool background, bool failed)
    {
        if (!background) Clear();
        // Never conceal a new read failure or show another provider's result.
        return background && !failed && _outcome is not null
            && string.Equals(providerId, _providerId, StringComparison.Ordinal)
                ? _outcome : observedMessage;
    }

    internal static bool CanDispatchDeletion(bool confirmed, bool shuttingDown,
        bool catalogCurrent, bool selectedProviderMatches, bool hostCurrent)
        => confirmed && !shuttingDown && catalogCurrent
            && selectedProviderMatches && hostCurrent;
}
