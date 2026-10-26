using System;
using System.Collections.Generic;
using LilithGameBridgeContracts;

namespace LilithTextInjector;

// Read-only and Unity-free. Public observations plus Lilith's own visible cards;
// never send another participant's cards, engine scores, move lists or commands.
internal static class MiniGamePublicContextProjection
{
    internal static LilithMiniGamePublicContext? Chess(ChessSession? session,
        Guid epoch, Guid match, long capturedAt, bool visualReady,
        LilithMiniGameChessAwareness? awareness = null)
    {
        if (session == null || session.Phase == ChessPhase.Setup || !visualReady) return null;
        var rules = session.Rules;
        var board = new char[64];
        for (var square = 0; square < board.Length; square++) board[square] = rules.PieceAt(square);
        var finished = session.Phase == ChessPhase.Finished;
        var ply = rules.History.Count;
        var chess = new LilithMiniGameChessContext(new string(board),
            session.PlayerWhite ? LilithMiniGameChessSide.White : LilithMiniGameChessSide.Pink,
            rules.WhiteTurn ? LilithMiniGameChessSide.White : LilithMiniGameChessSide.Pink,
            ply, ply == 0 ? string.Empty : rules.History[ply - 1], rules.InCheck,
            session.Mode == ChessMode.Casual ? LilithMiniGameChessMode.Casual : LilithMiniGameChessMode.Challenge,
            Milliseconds(session, session.PlayerWhite), Milliseconds(session, !session.PlayerWhite), awareness);
        return new LilithMiniGamePublicContext(epoch, match, capturedAt,
            Math.Min(100000, ply + (finished ? 1 : 0)),
            finished ? LilithMiniGamePhase.Finished : LilithMiniGamePhase.Active,
            finished ? LilithMiniGameActor.None : session.PlayerTurn ? LilithMiniGameActor.Player : LilithMiniGameActor.Lilith,
            !finished ? LilithMiniGameWinner.None : session.PlayerOutcome switch
            { 1 => LilithMiniGameWinner.Player, -1 => LilithMiniGameWinner.Lilith, _ => LilithMiniGameWinner.Draw },
            Reason(rules.Result), chess: chess);
    }

    // Legacy callers remain counts-only. A stable self-view upgrades the payload;
    // a transient fan reorder falls back to the unchanged public observation.
    internal static LilithMiniGamePublicContext? OldMaid(Guid epoch, Guid match,
        long capturedAt, bool started, bool visualReady, int playerCount, int lilithCount,
        int drawCount, int pairCount, OldMaidPhase phase, OldMaidWinner winner,
        LilithMiniGameOldMaidHand? lilithHand = null, int? stateRevision = null)
    {
        if (!started || !visualReady) return null;
        var finished = phase == OldMaidPhase.Finished;
        var revision = lilithHand is null ? Math.Min(100000, drawCount + (finished ? 1 : 0))
            : Math.Clamp(stateRevision ?? throw new ArgumentException("A stable self-view requires its state revision."), 0, 100000);
        return new LilithMiniGamePublicContext(epoch, match, capturedAt,
            revision,
            finished ? LilithMiniGamePhase.Finished : LilithMiniGamePhase.Active,
            finished ? LilithMiniGameActor.None : phase == OldMaidPhase.PlayerTurn ? LilithMiniGameActor.Player : LilithMiniGameActor.Lilith,
            winner switch { OldMaidWinner.Player => LilithMiniGameWinner.Player,
                OldMaidWinner.Opponent => LilithMiniGameWinner.Lilith, _ => LilithMiniGameWinner.None },
            finished ? LilithMiniGameResultReason.AllPairs : LilithMiniGameResultReason.None,
            oldMaid: new LilithMiniGameOldMaidContext(playerCount, lilithCount, drawCount, pairCount, lilithHand));
    }

    internal static LilithMiniGameOldMaidHand? OldMaidOwnHand(IReadOnlyList<OldMaidCard> ownHand, bool orderStable)
    {
        // The presenter supplies only Lilith's hand. Its settled array order is
        // the fan's left-to-right slot order; do not sort or expose internal IDs.
        if (!orderStable) return null;
        if (ownHand is null || ownHand.Count > OldMaidGame.DeckSize) throw new ArgumentException("Invalid self-view hand.");
        var cards = new LilithMiniGameOldMaidCard[ownHand.Count];
        for (var i = 0; i < cards.Length; i++)
        {
            var card = ownHand[i];
            cards[i] = new LilithMiniGameOldMaidCard(OldMaidCardArtPolicy.Rank(card),
                card.IsJoker ? "none" : card.Id % 2 == 0 ? "hearts" : "spades");
        }
        return new LilithMiniGameOldMaidHand(cards);
    }

    private static int Milliseconds(ChessSession session, bool white)
        => session.Mode == ChessMode.Casual ? -1
            : (int)Math.Clamp(Math.Ceiling((white ? session.WhiteSeconds : session.PinkSeconds) * 1000), 0, 86400000);

    internal static LilithMiniGameResultReason Reason(ChessResult result) => result switch
    {
        ChessResult.None => LilithMiniGameResultReason.None,
        ChessResult.WhiteMate or ChessResult.PinkMate => LilithMiniGameResultReason.Checkmate,
        ChessResult.WhiteTimeout or ChessResult.PinkTimeout => LilithMiniGameResultReason.Timeout,
        ChessResult.WhiteResigned or ChessResult.PinkResigned => LilithMiniGameResultReason.Resignation,
        ChessResult.Stalemate => LilithMiniGameResultReason.Stalemate,
        ChessResult.DeadDraw => LilithMiniGameResultReason.DeadPosition,
        ChessResult.ClaimedDraw => LilithMiniGameResultReason.ClaimedDraw,
        ChessResult.FivefoldDraw => LilithMiniGameResultReason.Fivefold,
        ChessResult.SeventyFiveMoveDraw => LilithMiniGameResultReason.SeventyFiveMove,
        _ => throw new ArgumentOutOfRangeException(nameof(result))
    };
}
