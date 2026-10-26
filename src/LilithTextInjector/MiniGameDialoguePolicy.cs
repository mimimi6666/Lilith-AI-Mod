using System;
using System.Collections.Generic;
using System.Linq;

namespace LilithTextInjector;

// Only committed, locally verified events enter this policy. In particular it
// has no hand contents, joker location, evaluation score or model output input.
internal enum MiniGameDialogueCue
{
    OldMaidStart, OldMaidPlayerDraw, OldMaidLilithDraw,
    OldMaidPlayerPair, OldMaidLilithPair, OldMaidPlayerNoPair, OldMaidLilithNoPair,
    OldMaidPlayerWon, OldMaidLilithWon,
    ChessStartPlayerWhite, ChessStartLilithWhite,
    ChessPlayerMove, ChessLilithMove, ChessPlayerCapture, ChessLilithCapture,
    ChessPlayerEnPassant, ChessLilithEnPassant, ChessPlayerChecked, ChessLilithChecked,
    ChessPlayerEscapedCheck, ChessLilithEscapedCheck, ChessPlayerCastle, ChessLilithCastle,
    ChessPlayerPromotion, ChessLilithPromotion, ChessPlayerKnightPromotion, ChessLilithQueenPromotion,
    ChessPlayerWon, ChessLilithWon, ChessPlayerTimeout, ChessLilithTimeout,
    ChessDraw, ChessStalemate, ChessDeadDraw, ChessRepetitionDraw, ChessMoveRuleDraw, ChessTimeoutDraw
}

internal sealed class MiniGameDialoguePolicy
{
    private readonly Func<double> random;
    private readonly HashSet<string> spoken = new(StringComparer.Ordinal);
    private readonly HashSet<MiniGameDialogueCue> essentials = new();
    private int lastRevision = -1;
    private double lastSpoken = double.NegativeInfinity;
    private bool previousOrdinarySpoke;

    internal MiniGameDialoguePolicy(Func<double>? random = null) => this.random = random ?? new Random().NextDouble;
    internal void Reset()
    {
        spoken.Clear(); essentials.Clear(); lastRevision = -1;
        lastSpoken = double.NegativeInfinity; previousOrdinarySpoke = false;
    }

    internal static bool IsResult(MiniGameDialogueCue cue) => cue is
        MiniGameDialogueCue.OldMaidPlayerWon or MiniGameDialogueCue.OldMaidLilithWon
        or MiniGameDialogueCue.ChessPlayerWon or MiniGameDialogueCue.ChessLilithWon
        or MiniGameDialogueCue.ChessPlayerTimeout or MiniGameDialogueCue.ChessLilithTimeout
        or MiniGameDialogueCue.ChessDraw or MiniGameDialogueCue.ChessStalemate
        or MiniGameDialogueCue.ChessDeadDraw or MiniGameDialogueCue.ChessRepetitionDraw
        or MiniGameDialogueCue.ChessMoveRuleDraw or MiniGameDialogueCue.ChessTimeoutDraw;
    internal static bool IsEssential(MiniGameDialogueCue cue) => IsResult(cue) || cue is
        MiniGameDialogueCue.OldMaidStart or MiniGameDialogueCue.ChessStartPlayerWhite or MiniGameDialogueCue.ChessStartLilithWhite;
    internal static int Priority(MiniGameDialogueCue cue) => IsResult(cue) ? 10 : IsEssential(cue) ? 8 : cue switch
    {
        MiniGameDialogueCue.ChessPlayerChecked or MiniGameDialogueCue.ChessLilithChecked => 6,
        MiniGameDialogueCue.ChessPlayerPromotion or MiniGameDialogueCue.ChessLilithPromotion
            or MiniGameDialogueCue.ChessPlayerKnightPromotion or MiniGameDialogueCue.ChessLilithQueenPromotion => 5,
        MiniGameDialogueCue.OldMaidPlayerPair or MiniGameDialogueCue.OldMaidLilithPair
            or MiniGameDialogueCue.ChessPlayerEnPassant or MiniGameDialogueCue.ChessLilithEnPassant
            or MiniGameDialogueCue.ChessPlayerCastle or MiniGameDialogueCue.ChessLilithCastle
            or MiniGameDialogueCue.ChessPlayerEscapedCheck or MiniGameDialogueCue.ChessLilithEscapedCheck
            or MiniGameDialogueCue.ChessPlayerCapture or MiniGameDialogueCue.ChessLilithCapture => 4,
        _ => 1
    };

    internal string? Choose(MiniGameDialogueCue cue, int revision, double now)
    {
        if (revision < 0 || !double.IsFinite(now) || !Enum.IsDefined(typeof(MiniGameDialogueCue), cue)) return null;
        var essential = IsEssential(cue);
        if (essential)
        {
            // Timeouts/claims can end a game without a new move revision.
            if (!essentials.Add(cue)) return null;
        }
        else
        {
            if (revision <= lastRevision) return null;
            lastRevision = revision;
            var special = Priority(cue) > 1;
            if (!special && previousOrdinarySpoke) { previousOrdinarySpoke = false; return null; }
            previousOrdinarySpoke = false;
            if (now - lastSpoken < (special ? 12 : 18) || random() >= (special ? .4 : .2)) return null;
        }
        var choices = Pool(cue).Where(id => !spoken.Contains(id)).ToArray();
        if (choices.Length == 0) return null;
        var selected = choices[Math.Clamp((int)(random() * choices.Length), 0, choices.Length - 1)];
        spoken.Add(selected); lastSpoken = now;
        previousOrdinarySpoke = !essential && Priority(cue) == 1;
        return selected;
    }

    internal static IReadOnlyList<string> Pool(MiniGameDialogueCue cue)
    {
        var (prefix, numbers) = cue switch
        {
            MiniGameDialogueCue.OldMaidStart => ("OM", new[] { 1, 2, 3, 4, 6, 7, 8 }),
            MiniGameDialogueCue.OldMaidPlayerDraw => ("OM", new[] { 14, 16 }),
            MiniGameDialogueCue.OldMaidLilithDraw => ("OM", new[] { 21, 22, 23, 24 }),
            // Bluff variants are independent of Joker ownership and never
            // become factual game events or change the public draw outcome.
            MiniGameDialogueCue.OldMaidLilithPair => ("OM", new[] { 25, 26, 27, 28, 73, 74 }),
            MiniGameDialogueCue.OldMaidPlayerPair => ("OM", new[] { 29, 30, 31, 32 }),
            MiniGameDialogueCue.OldMaidLilithNoPair => ("OM", new[] { 33, 34, 35, 36, 73, 74 }),
            MiniGameDialogueCue.OldMaidPlayerNoPair => ("OM", new[] { 37, 38, 39, 40 }),
            MiniGameDialogueCue.OldMaidLilithWon => ("OM", new[] { 57, 58, 59, 62, 63, 64 }),
            MiniGameDialogueCue.OldMaidPlayerWon => ("OM", new[] { 65, 67, 68, 69, 71, 72 }),
            MiniGameDialogueCue.ChessStartPlayerWhite => ("CH", new[] { 1, 4, 7, 8 }),
            MiniGameDialogueCue.ChessStartLilithWhite => ("CH", new[] { 2, 4, 7, 8 }),
            MiniGameDialogueCue.ChessLilithMove => ("CH", new[] { 9, 10, 11, 12 }),
            MiniGameDialogueCue.ChessPlayerMove => ("CH", new[] { 13, 14, 15, 16 }),
            MiniGameDialogueCue.ChessLilithCapture => ("CH", new[] { 25, 26, 27, 28 }),
            MiniGameDialogueCue.ChessPlayerCapture => ("CH", new[] { 29, 30, 31, 32 }),
            MiniGameDialogueCue.ChessPlayerEnPassant => ("CH", new[] { 35 }),
            MiniGameDialogueCue.ChessLilithEnPassant => ("CH", new[] { 36 }),
            MiniGameDialogueCue.ChessPlayerChecked => ("CH", new[] { 37, 38 }),
            MiniGameDialogueCue.ChessLilithChecked => ("CH", new[] { 39, 40 }),
            MiniGameDialogueCue.ChessPlayerEscapedCheck => ("CH", new[] { 41, 42 }),
            MiniGameDialogueCue.ChessLilithEscapedCheck => ("CH", new[] { 43, 44 }),
            MiniGameDialogueCue.ChessLilithCastle => ("CH", new[] { 45 }),
            MiniGameDialogueCue.ChessPlayerCastle => ("CH", new[] { 46 }),
            MiniGameDialogueCue.ChessPlayerPromotion => ("CH", new[] { 48, 52 }),
            MiniGameDialogueCue.ChessLilithPromotion => ("CH", new[] { 49, 52 }),
            MiniGameDialogueCue.ChessPlayerKnightPromotion => ("CH", new[] { 48, 51, 52 }),
            MiniGameDialogueCue.ChessLilithQueenPromotion => ("CH", new[] { 49, 50, 52 }),
            MiniGameDialogueCue.ChessPlayerTimeout => ("CH", new[] { 57 }),
            MiniGameDialogueCue.ChessLilithTimeout => ("CH", new[] { 58 }),
            MiniGameDialogueCue.ChessTimeoutDraw => ("CH", new[] { 59 }),
            MiniGameDialogueCue.ChessLilithWon => ("CH", new[] { 61, 62, 63, 64 }),
            MiniGameDialogueCue.ChessPlayerWon => ("CH", new[] { 65, 66, 67, 68 }),
            MiniGameDialogueCue.ChessDraw => ("CH", new[] { 69, 70 }),
            MiniGameDialogueCue.ChessStalemate => ("CH", new[] { 71 }),
            MiniGameDialogueCue.ChessDeadDraw => ("CH", new[] { 72 }),
            MiniGameDialogueCue.ChessRepetitionDraw => ("CH", new[] { 73 }),
            MiniGameDialogueCue.ChessMoveRuleDraw => ("CH", new[] { 74 }),
            _ => ("", Array.Empty<int>())
        };
        return numbers.Select(n => $"GD-{prefix}{n:000}").ToArray();
    }
}

internal static class ChessDialogueEvents
{
    internal static MiniGameDialogueCue? Move(bool player, char piece, char victim,
        int from, int to, string move, bool wasInCheck, bool nextInCheck, bool finished, bool enPassant = false)
    {
        if (finished) return null; // A result, never an ordinary capture, owns the terminal move.
        if (nextInCheck) return player ? MiniGameDialogueCue.ChessLilithChecked : MiniGameDialogueCue.ChessPlayerChecked;
        if (move.Length == 5) return player
            ? move[4] == 'n' ? MiniGameDialogueCue.ChessPlayerKnightPromotion : MiniGameDialogueCue.ChessPlayerPromotion
            : move[4] == 'q' ? MiniGameDialogueCue.ChessLilithQueenPromotion : MiniGameDialogueCue.ChessLilithPromotion;
        if (char.ToLowerInvariant(piece) == 'k' && Math.Abs(from - to) == 2)
            return player ? MiniGameDialogueCue.ChessPlayerCastle : MiniGameDialogueCue.ChessLilithCastle;
        // Caller supplies an en-passant victim already resolved at the captured square.
        if (enPassant && char.ToLowerInvariant(piece) == 'p' && victim != '.' && from % 8 != to % 8)
            return player ? MiniGameDialogueCue.ChessPlayerEnPassant : MiniGameDialogueCue.ChessLilithEnPassant;
        if (victim != '.') return player ? MiniGameDialogueCue.ChessPlayerCapture : MiniGameDialogueCue.ChessLilithCapture;
        if (wasInCheck) return player ? MiniGameDialogueCue.ChessPlayerEscapedCheck : MiniGameDialogueCue.ChessLilithEscapedCheck;
        return player ? MiniGameDialogueCue.ChessPlayerMove : MiniGameDialogueCue.ChessLilithMove;
    }

    internal static MiniGameDialogueCue? Result(ChessResult result, int outcome, bool timedOut = false) => result switch
    {
        ChessResult.WhiteTimeout or ChessResult.PinkTimeout when outcome != 0 => outcome < 0 ? MiniGameDialogueCue.ChessPlayerTimeout : MiniGameDialogueCue.ChessLilithTimeout,
        ChessResult.Stalemate => MiniGameDialogueCue.ChessStalemate,
        ChessResult.DeadDraw => timedOut ? MiniGameDialogueCue.ChessTimeoutDraw : MiniGameDialogueCue.ChessDeadDraw,
        ChessResult.FivefoldDraw => MiniGameDialogueCue.ChessRepetitionDraw,
        ChessResult.SeventyFiveMoveDraw => MiniGameDialogueCue.ChessMoveRuleDraw,
        ChessResult.WhiteMate or ChessResult.PinkMate when outcome != 0 => outcome > 0 ? MiniGameDialogueCue.ChessPlayerWon : MiniGameDialogueCue.ChessLilithWon,
        ChessResult.ClaimedDraw => MiniGameDialogueCue.ChessDraw,
        _ => null // No invented draw/checkmate for unsupported resignation or non-terminal states.
    };
}
