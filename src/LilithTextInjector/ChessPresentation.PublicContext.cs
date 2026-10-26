using System;
using LilithGameBridgeContracts;

namespace LilithTextInjector;

internal static partial class ChessPresentation
{
    private static WeakReference<ChessSession>? publicContextSession;
    private static Guid publicContextMatch;
    private static readonly ChessConversationAwareness conversationAwareness = new();
    internal static MiniGameMatchIdentity? CurrentMatchIdentity(Guid epoch)
        => !closing && session != null && publicContextSession != null
            && publicContextSession.TryGetTarget(out var captured) && ReferenceEquals(session, captured)
            && publicContextMatch != Guid.Empty ? new(epoch, publicContextMatch, "chess") : null;

    internal static LilithMiniGamePublicContext? CapturePublicContext(Guid epoch, long capturedAt)
        => CapturePublicContext(epoch, capturedAt, out _);

    internal static LilithMiniGamePublicContext? CapturePublicContext(Guid epoch, long capturedAt,
        out GameChatTestMiniGameCaptureReason reason)
    {
        reason = GameChatTestMiniGameCaptureReason.NoGame;
        var current = session;
        if (current == null) return null;
        // A restart always constructs a new session, even if board/side match.
        // Weak identity does not retain closed games or their histories.
        EnsurePublicContextIdentity(current);
        var visualReady = Now >= animationUntil && !closing;
        if (visualReady) MiniGameConversationEvents.Shared.RecordChessResult(current);
        reason = current.Phase == ChessPhase.Setup ? GameChatTestMiniGameCaptureReason.Setup
            : closing ? GameChatTestMiniGameCaptureReason.Closing
            : !visualReady ? GameChatTestMiniGameCaptureReason.MoveAnimation
            : GameChatTestMiniGameCaptureReason.Captured;
        var context = MiniGamePublicContextProjection.Chess(current, epoch, publicContextMatch,
            capturedAt, visualReady, visualReady ? conversationAwareness.Capture(current) : null);
        if (context != null && context.Chess?.Awareness == null)
            reason = GameChatTestMiniGameCaptureReason.PublicOnlyAwareness;
        else if (context == null && reason == GameChatTestMiniGameCaptureReason.Captured)
            reason = GameChatTestMiniGameCaptureReason.ProjectionUnavailable;
        return context;
    }

    private static void EnsurePublicContextIdentity(ChessSession current)
    {
        if (publicContextSession == null || !publicContextSession.TryGetTarget(out var previous)
            || !ReferenceEquals(previous, current))
        {
            publicContextSession = new WeakReference<ChessSession>(current);
            publicContextMatch = Guid.NewGuid();
        }
    }
}
