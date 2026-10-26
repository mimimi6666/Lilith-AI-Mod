using System;
using LilithRuntime.Configuration;

namespace LilithTextInjector;

internal enum D046OfficialVoiceSyncOutcome
{
    Unchanged,
    FollowingOfficial,
    ControlCenterApplied,
    OfficialWriteUnconfirmed,
    OfficialUnavailable,
    StoreUnavailable,
    MirrorPending,
    RejectedRevision
}

// Unity-free state machine. Only a NEW voice edit after a verified baseline is
// an outgoing request. Startup, reconnect and simultaneous edits favour the
// official game. Mirroring an observation must never become another request.
internal sealed class D046OfficialVoiceSynchronization
{
    private D046GameControlPreferencesDocument? _baseline;
    private D046GameVoiceLanguage? _lastOfficial;
    private bool _mirrorPending;

    internal D046GameVoiceLanguage? EffectiveVoice { get; private set; }

    internal static bool IsSupported(D046GameVoiceLanguage? voice)
        => voice is D046GameVoiceLanguage.Japanese
            or D046GameVoiceLanguage.TraditionalChinese;

    internal D046OfficialVoiceSyncOutcome Refresh(
        D046GameVoiceLanguage? official,
        D046GameControlLoadResult loaded,
        Func<D046GameVoiceLanguage, D046GameVoiceLanguage?> applyAndReadOfficial,
        Func<long, D046GameVoiceLanguage, D046GameControlApplyResult> mirror)
    {
        if (!IsSupported(official))
        {
            ResetBaseline();
            // Keep the last verified AI language, not a guessed Chinese mapping.
            return D046OfficialVoiceSyncOutcome.OfficialUnavailable;
        }
        EffectiveVoice = official;
        if (loaded.Disposition != D046GameControlLoadDisposition.Ready)
        {
            ResetBaseline();
            return D046OfficialVoiceSyncOutcome.StoreUnavailable;
        }
        var current = loaded.Snapshot;
        if (_baseline != null
            && (current.Revision < _baseline.Revision
                || (current.Revision == _baseline.Revision
                    && current.VoiceLanguage != _baseline.VoiceLanguage)))
            return D046OfficialVoiceSyncOutcome.RejectedRevision;

        var request = _baseline != null && !_mirrorPending
            && official == _lastOfficial
            && current.Revision > _baseline.Revision
            && current.VoiceLanguage != _baseline.VoiceLanguage;
        Remember(current, official!.Value);
        var outcome = D046OfficialVoiceSyncOutcome.Unchanged;
        if (request)
        {
            var readback = applyAndReadOfficial(current.VoiceLanguage);
            if (!IsSupported(readback))
            {
                // Unknown postcondition is not success and is not retry authority.
                ResetBaseline();
                return D046OfficialVoiceSyncOutcome.OfficialWriteUnconfirmed;
            }
            EffectiveVoice = readback;
            _lastOfficial = readback;
            outcome = readback == current.VoiceLanguage
                ? D046OfficialVoiceSyncOutcome.ControlCenterApplied
                : D046OfficialVoiceSyncOutcome.OfficialWriteUnconfirmed;
        }

        if (current.VoiceLanguage == EffectiveVoice)
        {
            _mirrorPending = false;
            return outcome;
        }

        _mirrorPending = true;
        var result = mirror(current.Revision, EffectiveVoice!.Value);
        if (result.Admission == D046GameControlApplyAdmission.Applied
            && result.Snapshot.VoiceLanguage == EffectiveVoice
            && result.Snapshot.Revision >= current.Revision)
        {
            Remember(result.Snapshot, EffectiveVoice.Value);
            _mirrorPending = false;
            return outcome == D046OfficialVoiceSyncOutcome.OfficialWriteUnconfirmed
                ? outcome : D046OfficialVoiceSyncOutcome.FollowingOfficial;
        }
        return D046OfficialVoiceSyncOutcome.MirrorPending;
    }

    private void Remember(D046GameControlPreferencesDocument snapshot,
        D046GameVoiceLanguage official)
    {
        _baseline = snapshot.Copy();
        _lastOfficial = official;
    }

    private void ResetBaseline()
    {
        _baseline = null;
        _lastOfficial = null;
        _mirrorPending = false;
    }
}
