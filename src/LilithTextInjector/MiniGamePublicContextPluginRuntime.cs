using System;
using LilithGameBridgeContracts;
using LilithRuntime.Configuration;

namespace LilithTextInjector;

internal static partial class DialogueManagerUpdatePatch
{
    private static readonly Guid MiniGameContextEpoch = Guid.NewGuid();
    private static readonly MiniGameAnswerRepairLifetime MiniGameRepairLifetime = new();

    internal static void RefreshMiniGameAnswerRepairLifetime()
    {
        if (!MiniGameRepairLifetime.HasOwner) return;
        var current = ChessPresentation.CurrentMatchIdentity(MiniGameContextEpoch)
            ?? OldMaidPresentation.CurrentMatchIdentity(MiniGameContextEpoch);
        var retired = MiniGameRepairLifetime.RetireIfChanged(current, PlayerTurnOperations.IsActive);
        if (retired != null && PlayerTurnOperations.TryCancel(retired, PlayerTurnCancellationReason.Superseded))
        {
            // The original transport observes this same operation cancellation
            // and reconciles native interruption. No follow-up turn is created.
            HermesNoteCandidates.Retire(retired);
            CancelPublishedPlayerReplySpeech(D043SpeechCancellationReason.PublicationRetired);
        }
    }

    // Called only by the main-thread player submit path, before its first await.
    // Optional observation failures must never consume ordinary player chat.
    private static LilithMiniGamePublicContext? CaptureMiniGamePublicContext()
        => CaptureMiniGamePublicContext(null);

    private static LilithMiniGamePublicContext? CaptureMiniGamePublicContext(PlayerTurnOperationHandle? operation)
    {
        // Ordinary F7/F8 does not allocate or retain diagnostics. An active test
        // lease can observe only the same immutable operation's real capture.
        var observe = CanObserveGameChatTestContext(operation);
        var gameKind = GameChatTestMiniGameKind.None;
        var reason = GameChatTestMiniGameCaptureReason.NoGame;
        var exceptionType = GameChatTestMiniGameException.None;
        LilithMiniGamePublicContext? context = null;
        try
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var chess = ChessPresentation.IsOpen;
            gameKind = chess ? GameChatTestMiniGameKind.Chess : GameChatTestMiniGameKind.OldMaid;
            context = chess
                ? ChessPresentation.CapturePublicContext(MiniGameContextEpoch, now, out reason)
                : OldMaidPresentation.CapturePublicContext(MiniGameContextEpoch, now, out reason);
            if (reason == GameChatTestMiniGameCaptureReason.NoGame)
                gameKind = GameChatTestMiniGameKind.None;
        }
        catch (Exception exception)
        {
            // No raw game state or player text in diagnostics. A future turn can
            // capture afresh; do not retry, publish or make an extra model call.
            reason = GameChatTestMiniGameCaptureReason.Exception;
            if (observe)
                exceptionType = GameChatTestMiniGameCapture.ClassifyException(exception);
        }
        if (observe)
        {
            var schema = context == null ? GameChatTestMiniGameSchema.None
                : context.SchemaName == LilithMiniGamePublicContext.LilithViewSchema
                    ? GameChatTestMiniGameSchema.LilithViewV2 : GameChatTestMiniGameSchema.PublicV1;
            ObserveGameChatTestContext(operation,
                new GameChatTestMiniGameCapture(gameKind, schema, context != null, reason, exceptionType));
        }
        return context;
    }
}
