using System;
using System.Collections.Generic;
using System.Linq;

namespace LilithTextInjector;

// A dismissed native menu is not a completed promotion. Keep the request while
// the exact player's position is still current, and reoffer when UI is free.
internal static class ChessPromotionChoicePolicy
{
    internal static bool CanOffer(ChessPhase phase, bool playerTurn, int currentRevision,
        int pendingRevision, string? prefix, IReadOnlyList<string> legalMoves)
        => phase == ChessPhase.Playing && playerTurn && currentRevision >= 0
            && currentRevision == pendingRevision && prefix?.Length == 4
            && "qrbn".All(piece => legalMoves.Contains(prefix + piece, StringComparer.Ordinal));

    internal static string? SelectedMove(ChessPhase phase, bool playerTurn, int currentRevision,
        int pendingRevision, string? prefix, IReadOnlyList<string> legalMoves, int choice)
        => choice is >= 0 and < 4 && CanOffer(phase, playerTurn, currentRevision, pendingRevision, prefix, legalMoves)
            ? prefix + "qrbn"[choice] : null;
}
