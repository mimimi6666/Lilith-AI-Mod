using System;
using System.Collections.Generic;
using System.Linq;
using Chess;
using LilithGameBridgeContracts;

namespace LilithTextInjector;

// Request-time observation only. Never simulate a different side's turn or use
// geometric/pseudo attacks as evidence that a capture or checking move is legal.
internal sealed class ChessConversationAwareness
{
    internal const int MaximumLegalMoves = 256;
    private WeakReference<ChessSession>? observedSession;
    private int cachedRevision = -1, lastRevision = -1;
    private ChessPhase cachedPhase;
    private ChessResult cachedResult;
    private string lastUci = string.Empty;
    private LilithMiniGameChessLastMove? lastMove;
    private LilithMiniGameChessAwareness? cached;

    internal LilithMiniGameChessAwareness? Capture(ChessSession? session)
    {
        if (session == null || session.Phase == ChessPhase.Setup) { Clear(); return null; }
        Bind(session);
        var rules = session.Rules;
        if (cachedRevision == rules.Revision && cachedPhase == session.Phase && cachedResult == rules.Result)
            return cached;
        cachedRevision = rules.Revision; cachedPhase = session.Phase; cachedResult = rules.Result;
        cached = null;
        try
        {
            var last = lastRevision == rules.Revision && rules.History.Count > 0
                && rules.History[rules.History.Count - 1] == lastUci ? lastMove : null;
            if (session.Phase == ChessPhase.Finished)
                return cached = new LilithMiniGameChessAwareness(LilithMiniGameActor.None, false, Array.Empty<string>(), last);

            // Clone immutable FEN, not the live rule adapter. Sequential per-piece
            // generation shares its existing legality semantics and avoids the
            // library's whole-board thread-pool generation. No search/engine/PV.
            var fen = rules.Fen;
            var expected = new HashSet<string>(rules.LegalMoves, StringComparer.Ordinal);
            if (expected.Count > MaximumLegalMoves) return null;
            var board = ChessBoard.LoadFromFen(fen);
            if ((board.Turn == PieceColor.White) != rules.WhiteTurn
                || (rules.WhiteTurn ? board.WhiteKingChecked : board.BlackKingChecked) != rules.InCheck)
                return null;
            var generated = new HashSet<string>(StringComparer.Ordinal);
            var targets = new SortedSet<string>(StringComparer.Ordinal);
            var checking = false;
            for (short rank = 0; rank < 8; rank++)
            for (short file = 0; file < 8; file++)
            {
                if (board[file, rank]?.Color != board.Turn) continue;
                foreach (var move in board.Moves(new Position(file, rank), false, false))
                {
                    var uci = move.OriginalPosition.ToString().ToLowerInvariant() + move.NewPosition.ToString().ToLowerInvariant()
                        + (move.Parameter is MovePromotion promotion ? promotion.ShortStr[1].ToString().ToLowerInvariant() : "");
                    if (!generated.Add(uci) || generated.Count > MaximumLegalMoves || !expected.Contains(uci)) return null;
                    checking |= move.IsCheck;
                    if (move.CapturedPiece == null) continue;
                    var victimPosition = move.Parameter is MoveEnPassant ep ? ep.CapturedPawnPosition : move.NewPosition;
                    var victim = board[victimPosition];
                    if (victim == null || victim.Color == board.Turn || victim.Type == PieceType.King
                        || victim.ToFenChar() != move.CapturedPiece.ToFenChar()) return null;
                    targets.Add(victimPosition.ToString().ToLowerInvariant());
                    if (targets.Count > 32) return null;
                }
            }
            // Omission means unavailable, never a false assertion of no danger.
            if (!expected.SetEquals(generated) || board.ToFen() != fen) return null;
            return cached = new LilithMiniGameChessAwareness(
                session.PlayerTurn ? LilithMiniGameActor.Player : LilithMiniGameActor.Lilith,
                checking, targets.ToArray(), last);
        }
        catch
        {
            // Optional observations cannot consume chat or alter gameplay. The
            // ordinary v1 board/check facts remain available without awareness.
            return null;
        }
    }

    // Called once, only after the existing authoritative commit succeeded. Uses
    // its already captured piece/victim; no replay or new rule-state writer.
    internal void RecordCommittedMove(ChessSession session, string uci, char piece, char capturedPiece, int capturedSquare)
    {
        Bind(session);
        cachedRevision = -1; cached = null; lastRevision = -1; lastMove = null; lastUci = string.Empty;
        try
        {
            var rules = session.Rules;
            if (rules.Revision <= 0 || rules.History.Count != rules.Revision
                || rules.History[rules.History.Count - 1] != uci || uci.Length is not (4 or 5)
                || "prnbqkPRNBQK".IndexOf(piece) < 0 || char.IsUpper(piece) == rules.WhiteTurn)
                return;
            var from = ChessRules.Square(uci.Substring(0, 2)); var to = ChessRules.Square(uci.Substring(2, 2));
            if (from < 0 || to < 0 || rules.PieceAt(from) != '.') return;
            var afterPiece = piece;
            if (uci.Length == 5)
            {
                if (char.ToLowerInvariant(piece) != 'p' || "qrbn".IndexOf(uci[4]) < 0) return;
                afterPiece = char.IsUpper(piece) ? char.ToUpperInvariant(uci[4]) : uci[4];
            }
            if (rules.PieceAt(to) != afterPiece) return;
            if (capturedPiece != '.')
            {
                if (capturedSquare < 0 || capturedSquare >= 64) return;
                if (capturedSquare != to && (char.ToLowerInvariant(piece) != 'p'
                    || char.ToLowerInvariant(capturedPiece) != 'p'
                    || Math.Abs(from % 8 - to % 8) != 1
                    || to / 8 - from / 8 != (char.IsUpper(piece) ? 1 : -1)
                    || to / 8 != (char.IsUpper(piece) ? 5 : 2)
                    || capturedSquare != to + (char.IsUpper(piece) ? -8 : 8)
                    || rules.PieceAt(capturedSquare) != '.')) return;
            }
            lastMove = new LilithMiniGameChessLastMove(
                char.IsUpper(piece) == session.PlayerWhite ? LilithMiniGameActor.Player : LilithMiniGameActor.Lilith,
                piece, capturedPiece, capturedPiece == '.' ? string.Empty : ChessRules.Square(capturedSquare), rules.InCheck);
            lastRevision = rules.Revision; lastUci = uci;
        }
        catch
        {
            // In particular, malformed metadata must not escape into Commit.
            lastMove = null; lastRevision = -1; lastUci = string.Empty;
        }
    }

    private void Bind(ChessSession session)
    {
        if (observedSession != null && observedSession.TryGetTarget(out var previous) && ReferenceEquals(previous, session)) return;
        Clear(); observedSession = new WeakReference<ChessSession>(session);
    }

    internal void Clear()
    {
        observedSession = null; cached = null; lastMove = null;
        cachedRevision = lastRevision = -1; lastUci = string.Empty;
    }
}
