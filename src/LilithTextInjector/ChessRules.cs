using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Chess;

namespace LilithTextInjector;

// Only this adapter owns legal chess state. UCI output and screen coordinates
// are proposals; callers must commit against the current position revision.
internal sealed class ChessRules
{
    internal const string InitialFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
    private readonly ChessBoard board;
    private readonly Dictionary<string, int> repetitions = new(StringComparer.Ordinal);
    private readonly List<string> history = new();
    private Dictionary<string, Move> legal = new(StringComparer.Ordinal);
    private int claimCheckedRevision = -1;
    private bool intendedClaimAvailable;
    internal string StartFen { get; }
    internal int Revision { get; private set; }
    internal string Fen => board.ToFen();
    internal bool WhiteTurn => board.Turn == PieceColor.White;
    internal IReadOnlyList<string> History => history;
    internal string[] LegalMoves => legal.Keys.ToArray();
    internal ChessResult Result { get; private set; }
    internal bool InCheck => WhiteTurn ? board.WhiteKingChecked : board.BlackKingChecked;
    internal bool CanClaimDraw => Result == ChessResult.None && (HalfMoves >= 100 || RepeatCount >= 3);
    internal bool CanClaimByIntendedMove
    {
        get
        {
            if (Result != ChessResult.None) return false;
            if (claimCheckedRevision != Revision)
            {
                claimCheckedRevision = Revision;
                intendedClaimAvailable = (HalfMoves >= 99 || repetitions.Values.Any(n => n >= 2)) && legal.Keys.Any(CanClaimWith);
            }
            return intendedClaimAvailable;
        }
    }
    private int HalfMoves => int.Parse(Fen.Split(' ')[4], CultureInfo.InvariantCulture);
    private int RepeatCount => repetitions.TryGetValue(PositionKey(), out var count) ? count : 0;

    internal ChessRules(string? fen = null)
    {
        StartFen = fen ?? InitialFen;
        board = ChessBoard.LoadFromFen(StartFen);
        // Claim-based draws are not silently treated as automatic draws.
        Refresh();
    }

    internal char PieceAt(int square) => square is >= 0 and < 64
        ? board[square % 8, square / 8]?.ToFenChar() ?? '.' : '.';

    internal bool TryMove(string uci, int revision)
    {
        if (Result != ChessResult.None || revision != Revision || !legal.TryGetValue(uci, out var move)) return false;
        if (!board.Move(move)) return false;
        history.Add(uci);
        Revision++;
        Refresh();
        return true;
    }

    internal bool ClaimDraw()
    {
        if (!CanClaimDraw) return false;
        Result = ChessResult.ClaimedDraw;
        return true;
    }

    internal bool CanClaimWith(string intendedMove)
    {
        if (Result != ChessResult.None || !legal.TryGetValue(intendedMove, out var move)) return false;
        var clone = new ChessRules(Fen);
        if (!clone.TryMove(intendedMove, 0)) return false;
        if (clone.Result is ChessResult.WhiteMate or ChessResult.PinkMate) return false;
        return clone.HalfMoves >= 100 || (repetitions.TryGetValue(clone.PositionKey(), out var count) && count >= 2);
    }

    internal bool ClaimDrawWith(string intendedMove)
    {
        if (!CanClaimWith(intendedMove)) return false;
        Result = ChessResult.ClaimedDraw;
        return true;
    }

    internal void Timeout(bool white)
    {
        if (Result != ChessResult.None) return;
        Result = CanPossiblyMate(!white) ? white ? ChessResult.WhiteTimeout : ChessResult.PinkTimeout : ChessResult.DeadDraw;
    }

    internal void Resign(bool white)
    {
        if (Result == ChessResult.None) Result = white ? ChessResult.WhiteResigned : ChessResult.PinkResigned;
    }

    private void Refresh()
    {
        legal = new Dictionary<string, Move>(StringComparer.Ordinal);
        // Do NOT use library Moves() which creates one thread-pool task per
        // piece. Sequential per-piece generation has a small, fixed workload.
        for (short rank = 0; rank < 8; rank++)
        for (short file = 0; file < 8; file++)
        {
            if (board[file, rank]?.Color != board.Turn) continue;
            foreach (var move in board.Moves(new Position(file, rank), false, false)) legal[Uci(move)] = move;
        }
        var key = PositionKey();
        repetitions[key] = repetitions.TryGetValue(key, out var count) ? count + 1 : 1;
        if (legal.Count == 0) Result = InCheck ? WhiteTurn ? ChessResult.PinkMate : ChessResult.WhiteMate : ChessResult.Stalemate;
        else if (!CanPossiblyMate(true) && !CanPossiblyMate(false)) Result = ChessResult.DeadDraw;
        else if (HalfMoves >= 150) Result = ChessResult.SeventyFiveMoveDraw;
        else if (RepeatCount >= 5) Result = ChessResult.FivefoldDraw;
    }

    // Conservative possible-mate test, including opponent help. K+NN can
    // possibly mate, even though it cannot force mate. Do not auto-draw it.
    internal bool CanPossiblyMate(bool white)
    {
        var own = new List<(char kind, int square)>();
        var other = new List<(char kind, int square)>();
        for (var s = 0; s < 64; s++)
        {
            var p = PieceAt(s);
            if (p == '.' || char.ToLowerInvariant(p) == 'k') continue;
            (char.IsUpper(p) == white ? own : other).Add((char.ToLowerInvariant(p), s));
        }
        if (own.Count == 0) return false;
        if (own.Any(p => p.kind is 'p' or 'r' or 'q')) return true;
        if (own.All(p => p.kind == 'b'))
        {
            var colors = own.Select(p => (p.square % 8 + p.square / 8) % 2).Distinct().ToArray();
            if (colors.Length == 2) return true;
            return other.Any(p => p.kind != 'b' || (p.square % 8 + p.square / 8) % 2 != colors[0]);
        }
        if (own.Count >= 2) return true;
        // A lone knight needs a blocker other than the opposite king/queen.
        return other.Any(p => p.kind != 'q');
    }

    private string PositionKey()
    {
        var fields = Fen.Split(' ');
        // FEN can contain an en-passant target even if no legal EP exists.
        // Such a target must not prevent repetition recognition.
        var ep = fields[3];
        if (ep != "-" && !legal.Values.Any(m => m.Parameter is MoveEnPassant)) ep = "-";
        return string.Join(" ", fields[0], fields[1], fields[2], ep);
    }

    internal static string Square(int s) => string.Concat((char)('a' + s % 8), (char)('1' + s / 8));
    internal static int Square(string text) => text.Length == 2 && text[0] is >= 'a' and <= 'h' && text[1] is >= '1' and <= '8'
        ? (text[1] - '1') * 8 + text[0] - 'a' : -1;
    private static string Uci(Move m) => m.OriginalPosition.ToString().ToLowerInvariant() + m.NewPosition.ToString().ToLowerInvariant()
        + (m.Parameter is MovePromotion promotion ? promotion.ShortStr[1].ToString().ToLowerInvariant() : "");
}

internal enum ChessResult { None, WhiteMate, PinkMate, WhiteTimeout, PinkTimeout, WhiteResigned, PinkResigned,
    Stalemate, DeadDraw, ClaimedDraw, FivefoldDraw, SeventyFiveMoveDraw }
