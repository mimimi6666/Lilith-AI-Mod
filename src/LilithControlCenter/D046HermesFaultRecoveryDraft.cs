using System.Text;

namespace LilithControlCenter;

internal sealed class D046HermesFaultRecoveryDraft
{
    internal const int MaximumDraftUtf8Bytes = 4 * 1024 * 1024;
    private const int MaximumIdentityCharacters = 1024;

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private D046HermesFaultRecoveryDraft(
        string taskId,
        string lineageId,
        string draftText,
        bool hadPendingImage)
    {
        TaskId = taskId;
        LineageId = lineageId;
        DraftText = draftText;
        HadPendingImage = hadPendingImage;
    }

    internal string TaskId { get; }
    internal string LineageId { get; }
    internal string DraftText { get; }
    internal bool HadPendingImage { get; }

    internal static bool TryCreate(
        string taskId,
        string lineageId,
        string draftText,
        bool hadPendingImage,
        out D046HermesFaultRecoveryDraft? recovery)
    {
        recovery = null;
        if (!IsIdentity(taskId)
            || !IsIdentity(lineageId)
            || draftText is null
            || (draftText.Length == 0 && !hadPendingImage)
            || !IsWithinPromptBoundary(draftText))
        {
            return false;
        }

        recovery = new D046HermesFaultRecoveryDraft(
            taskId,
            lineageId,
            draftText,
            hadPendingImage);
        return true;
    }

    internal static bool IsWithinPromptBoundary(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        try
        {
            return StrictUtf8.GetByteCount(value) <= MaximumDraftUtf8Bytes;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    public override string ToString()
        => nameof(D046HermesFaultRecoveryDraft);

    private static bool IsIdentity(string value)
        => !string.IsNullOrWhiteSpace(value)
            && value.Length <= MaximumIdentityCharacters
            && !value.Any(char.IsControl);
}

internal enum D046HermesFaultRecoveryRestoreDisposition
{
    None = 0,
    WaitingForDurableTask = 1,
    DestinationConflict = 2,
    Restored = 3
}

internal sealed class D046HermesFaultRecoveryDraftSlot
{
    private D046HermesFaultRecoveryDraft? _pending;

    internal D046HermesFaultRecoveryDraftSlot(
        D046HermesFaultRecoveryDraft? pending = null)
    {
        _pending = pending;
    }

    internal bool HasPending => _pending is not null;

    internal D046HermesFaultRecoveryDraft? PendingForRecapture => _pending;

    internal bool IsBoundTo(string? taskId, string? lineageId)
        => _pending is { } pending
            && string.Equals(taskId, pending.TaskId, StringComparison.Ordinal)
            && string.Equals(lineageId, pending.LineageId, StringComparison.Ordinal);

    internal D046HermesFaultRecoveryRestoreDisposition TryRestore(
        string? taskId,
        string? lineageId,
        string currentDraftText,
        out string restoredDraftText,
        out bool hadPendingImage)
    {
        ArgumentNullException.ThrowIfNull(currentDraftText);
        restoredDraftText = string.Empty;
        hadPendingImage = false;
        var pending = _pending;
        if (pending is null)
            return D046HermesFaultRecoveryRestoreDisposition.None;
        if (!string.Equals(taskId, pending.TaskId, StringComparison.Ordinal)
            || !string.Equals(
                lineageId,
                pending.LineageId,
                StringComparison.Ordinal))
        {
            return D046HermesFaultRecoveryRestoreDisposition.WaitingForDurableTask;
        }
        if (currentDraftText.Length != 0
            && !string.Equals(
                currentDraftText,
                pending.DraftText,
                StringComparison.Ordinal))
        {
            return D046HermesFaultRecoveryRestoreDisposition.DestinationConflict;
        }

        _pending = null;
        restoredDraftText = pending.DraftText;
        hadPendingImage = pending.HadPendingImage;
        return D046HermesFaultRecoveryRestoreDisposition.Restored;
    }

    internal void Clear()
        => _pending = null;
}
