namespace LilithRuntime;

internal enum LilithRuntimeVoiceCapturePhase
{
    Ready = 1,
    Capturing = 2,
    Transcribing = 3
}

internal sealed record LilithRuntimeVoiceCaptureOwner(
    Guid GenerationId,
    LilithRuntimeSessionSurface Surface,
    string SessionId,
    Guid CaptureId,
    uint PeerProcessId,
    ulong PeerProcessCreationTime)
{
    internal LilithRuntimeVoiceCaptureOwner Validate()
    {
        if (GenerationId == Guid.Empty)
            throw new ArgumentException("A Runtime generation is required.");
        if (!Enum.IsDefined(Surface))
            throw new ArgumentOutOfRangeException(nameof(Surface));
        if (string.IsNullOrWhiteSpace(SessionId))
            throw new ArgumentException("A voice session is required.");
        if (Surface == LilithRuntimeSessionSurface.GameBridge
            && (CaptureId == Guid.Empty
                || PeerProcessId == 0
                || PeerProcessCreationTime == 0))
        {
            throw new ArgumentException(
                "A game voice owner requires an exact capture and peer process identity.");
        }
        if (Surface == LilithRuntimeSessionSurface.Workbench
            && (PeerProcessId != 0 || PeerProcessCreationTime != 0))
        {
            throw new ArgumentException(
                "A Workbench voice owner cannot claim a game peer identity.");
        }
        return this;
    }

    internal bool SameSurfaceSessionAndPeer(
        LilithRuntimeVoiceCaptureOwner other)
        => GenerationId == other.GenerationId
            && Surface == other.Surface
            && string.Equals(SessionId, other.SessionId, StringComparison.Ordinal)
            && PeerProcessId == other.PeerProcessId
            && PeerProcessCreationTime == other.PeerProcessCreationTime;
}

/// <summary>
/// Owns the single Hermes microphone for the whole Runtime process. Router-local
/// lifecycle state remains a protocol validator; this object is the admission
/// authority that prevents the Workbench and game bridge from recording at the
/// same time.
/// </summary>
internal sealed class LilithRuntimeVoiceCaptureArbiter
{
    private readonly object _sync = new();
    private LilithRuntimeVoiceCaptureOwner? _owner;
    private LilithRuntimeVoiceCapturePhase _phase;
    private bool _generationRetiring;

    internal bool TryAcquireReady(LilithRuntimeVoiceCaptureOwner owner)
    {
        owner.Validate();
        lock (_sync)
        {
            if (_generationRetiring)
                return false;
            if (_owner is null)
            {
                _owner = owner;
                _phase = LilithRuntimeVoiceCapturePhase.Ready;
                return true;
            }
            if (_phase != LilithRuntimeVoiceCapturePhase.Ready)
                return _owner == owner;

            // A Ready microphone has no recording or transcription in flight,
            // so ownership can safely move between Runtime surfaces.
            _owner = owner;
            return true;
        }
    }

    internal bool TryRebindReady(
        LilithRuntimeVoiceCaptureOwner expectedOwner,
        string newSessionId)
    {
        expectedOwner.Validate();
        var reboundOwner = (expectedOwner with { SessionId = newSessionId }).Validate();
        lock (_sync)
        {
            // Session activation may only move the exact idle owner that was
            // admitted before the request. Never acquire or replace another
            // surface/capture while a native activation response is in flight.
            if (_generationRetiring
                || expectedOwner.Surface != LilithRuntimeSessionSurface.Workbench
                || _phase != LilithRuntimeVoiceCapturePhase.Ready
                || _owner != expectedOwner)
            {
                return false;
            }
            _owner = reboundOwner;
            return true;
        }
    }

    internal bool TryBeginCapture(LilithRuntimeVoiceCaptureOwner owner)
    {
        owner.Validate();
        if (owner.CaptureId == Guid.Empty)
            throw new ArgumentException("A capture identity is required.", nameof(owner));
        lock (_sync)
        {
            if (_generationRetiring)
                return false;
            if (_owner is null || _phase == LilithRuntimeVoiceCapturePhase.Ready)
            {
                _owner = owner;
                _phase = LilithRuntimeVoiceCapturePhase.Capturing;
                return true;
            }
            return _owner == owner
                && _phase == LilithRuntimeVoiceCapturePhase.Capturing;
        }
    }

    internal bool TryMarkTranscribing(LilithRuntimeVoiceCaptureOwner owner)
    {
        owner.Validate();
        lock (_sync)
        {
            if (_owner != owner)
            {
                return false;
            }
            if (_phase == LilithRuntimeVoiceCapturePhase.Ready)
                return true;
            if (_phase is not (
                LilithRuntimeVoiceCapturePhase.Capturing
                or LilithRuntimeVoiceCapturePhase.Transcribing))
            {
                return false;
            }
            _phase = LilithRuntimeVoiceCapturePhase.Transcribing;
            return true;
        }
    }

    internal bool TryMarkReady(LilithRuntimeVoiceCaptureOwner owner)
    {
        owner.Validate();
        lock (_sync)
        {
            if (_owner != owner)
                return false;
            _phase = LilithRuntimeVoiceCapturePhase.Ready;
            return true;
        }
    }

    internal bool IsOwnedBy(LilithRuntimeVoiceCaptureOwner owner)
    {
        owner.Validate();
        lock (_sync)
            return _owner == owner;
    }

    internal bool TryGetOwner(
        out LilithRuntimeVoiceCaptureOwner owner,
        out LilithRuntimeVoiceCapturePhase phase)
    {
        lock (_sync)
        {
            if (_owner is null)
            {
                owner = null!;
                phase = default;
                return false;
            }
            owner = _owner;
            phase = _phase;
            return true;
        }
    }

    internal bool Release(LilithRuntimeVoiceCaptureOwner owner)
    {
        owner.Validate();
        lock (_sync)
        {
            if (_owner != owner)
                return false;
            _owner = null;
            _phase = default;
            return true;
        }
    }

    internal void Reset()
    {
        lock (_sync)
        {
            _owner = null;
            _phase = default;
            _generationRetiring = false;
        }
    }

    internal void BeginGenerationRetirement()
    {
        lock (_sync)
            _generationRetiring = true;
    }

    internal void CompleteGenerationRetirement()
    {
        lock (_sync)
        {
            _generationRetiring = true;
            _owner = null;
            _phase = default;
        }
    }
}
