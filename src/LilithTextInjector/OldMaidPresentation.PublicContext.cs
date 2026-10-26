using System;
using LilithGameBridgeContracts;

namespace LilithTextInjector;

internal static partial class OldMaidPresentation
{
    private static Guid publicContextMatch;
    internal static MiniGameMatchIdentity? CurrentMatchIdentity(Guid epoch)
        => _game != null && publicContextMatch != Guid.Empty
            ? new(epoch, publicContextMatch, "old-maid") : null;

    internal static LilithMiniGamePublicContext? CapturePublicContext(Guid epoch, long capturedAt)
        => CapturePublicContext(epoch, capturedAt, out _);

    internal static LilithMiniGamePublicContext? CapturePublicContext(Guid epoch, long capturedAt,
        out GameChatTestMiniGameCaptureReason reason)
    {
        reason = GameChatTestMiniGameCaptureReason.NoGame;
        var current = _game;
        if (current == null) return null;
        reason = GameChatTestMiniGameCaptureReason.NoMatch;
        if (publicContextMatch == Guid.Empty) return null;
        // Logical commits precede the draw/pair animation. Wait for the reveal;
        // never disclose its future outcome while a card is still in flight.
        var visualReady = !DrawMotion.IsBusy && !PairReveal.IsBusy && _discardDelay <= 0 && !Scale.IsAnimating;
        if (visualReady) MiniGameConversationEvents.Shared.RecordOldMaidResult(current);
        reason = !current.IsStarted ? GameChatTestMiniGameCaptureReason.Setup
            : DrawMotion.IsBusy ? GameChatTestMiniGameCaptureReason.DrawAnimation
            : PairReveal.IsBusy ? GameChatTestMiniGameCaptureReason.PairReveal
            : _discardDelay > 0 ? GameChatTestMiniGameCaptureReason.DiscardDelay
            : Scale.IsAnimating ? GameChatTestMiniGameCaptureReason.ScaleAnimation
            : _opponentOrder.HasValue ? GameChatTestMiniGameCaptureReason.PublicOnlyReorder
            : GameChatTestMiniGameCaptureReason.Captured;
        // Only Lilith's own settled fan may be described. An active reorder keeps
        // the counts-only fallback rather than inventing a transient screen order.
        var ownHand = visualReady
            ? MiniGamePublicContextProjection.OldMaidOwnHand(current.OpponentHand, !_opponentOrder.HasValue)
            : null;
        var context = MiniGamePublicContextProjection.OldMaid(epoch, publicContextMatch, capturedAt,
            current.IsStarted, visualReady,
            current.PlayerHand.Count, current.OpponentHand.Count, current.DrawCount,
            current.DiscardedPairCount, current.Phase, current.Winner, ownHand, current.Revision);
        if (context == null && reason == GameChatTestMiniGameCaptureReason.Captured)
            reason = GameChatTestMiniGameCaptureReason.ProjectionUnavailable;
        return context;
    }
}
