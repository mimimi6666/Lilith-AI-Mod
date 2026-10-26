using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace LilithGameBridgeContracts;

public enum LilithMiniGameEventKind { Started, CardDrawn, PairDiscarded, HandReordered, ChessMoved, Finished }

// Closed, committed observations. No free-form model instructions or hidden hand lists.
public sealed class LilithMiniGameEvent
{
    public const long MaximumSequence = 1000000;
    public long Sequence { get; }
    public LilithMiniGameEventKind Kind { get; }
    public LilithMiniGameActor Actor { get; }
    public LilithMiniGameOldMaidCard? Card { get; }
    public string Rank { get; }
    public int FromIndex { get; }
    public int ToIndex { get; }
    public string Uci { get; }
    public LilithMiniGameChessLastMove? Move { get; }
    public LilithMiniGameWinner Winner { get; }
    public LilithMiniGameResultReason ResultReason { get; }

    private LilithMiniGameEvent(long sequence, LilithMiniGameEventKind kind,
        LilithMiniGameActor actor = LilithMiniGameActor.None, LilithMiniGameOldMaidCard? card = null,
        string rank = "", int fromIndex = -1, int toIndex = -1, string uci = "",
        LilithMiniGameChessLastMove? move = null, LilithMiniGameWinner winner = LilithMiniGameWinner.None,
        LilithMiniGameResultReason reason = LilithMiniGameResultReason.None)
    {
        if (sequence < 1 || sequence > MaximumSequence) throw LilithMiniGamePublicContext.Rejected();
        Sequence = sequence; Kind = kind; Actor = actor; Card = card; Rank = rank;
        FromIndex = fromIndex; ToIndex = toIndex; Uci = uci; Move = move; Winner = winner; ResultReason = reason;
    }

    public static LilithMiniGameEvent Started(long sequence) => new(sequence, LilithMiniGameEventKind.Started);
    public static LilithMiniGameEvent CardDrawn(long sequence, LilithMiniGameActor actor, LilithMiniGameOldMaidCard card)
    {
        RequireActor(actor);
        if (card is null) throw LilithMiniGamePublicContext.Rejected();
        return new(sequence, LilithMiniGameEventKind.CardDrawn, actor, card);
    }
    public static LilithMiniGameEvent PairDiscarded(long sequence, LilithMiniGameActor actor, string rank)
    {
        RequireActor(actor);
        if (rank == "JOKER") throw LilithMiniGamePublicContext.Rejected();
        _ = new LilithMiniGameOldMaidCard(rank, "hearts");
        return new(sequence, LilithMiniGameEventKind.PairDiscarded, actor, rank: rank);
    }
    public static LilithMiniGameEvent HandReordered(long sequence, LilithMiniGameActor actor, int fromIndex, int toIndex)
    {
        RequireActor(actor);
        if (fromIndex < 0 || fromIndex > 26 || toIndex < 0 || toIndex > 26 || fromIndex == toIndex)
            throw LilithMiniGamePublicContext.Rejected();
        return new(sequence, LilithMiniGameEventKind.HandReordered, actor, fromIndex: fromIndex, toIndex: toIndex);
    }
    public static LilithMiniGameEvent ChessMoved(long sequence, string uci, LilithMiniGameChessLastMove move)
    {
        if (move is null || uci is null || (uci.Length != 4 && uci.Length != 5)
            || !LilithMiniGameChessAwareness.ValidSquare(uci.Substring(0, 2))
            || !LilithMiniGameChessAwareness.ValidSquare(uci.Substring(2, 2))
            || uci.Substring(0, 2) == uci.Substring(2, 2)
            || (uci.Length == 5 && ("qrbn".IndexOf(uci[4]) < 0 || char.ToLowerInvariant(move.Piece) != 'p'
                || uci[1] != (char.IsUpper(move.Piece) ? '7' : '2') || uci[3] != (char.IsUpper(move.Piece) ? '8' : '1'))))
            throw LilithMiniGamePublicContext.Rejected();
        if (move.CapturedPiece != '.' && move.CapturedSquare != uci.Substring(2, 2))
        {
            var white = char.IsUpper(move.Piece);
            if (char.ToLowerInvariant(move.Piece) != 'p' || char.ToLowerInvariant(move.CapturedPiece) != 'p'
                || Math.Abs(uci[0] - uci[2]) != 1 || uci[3] - uci[1] != (white ? 1 : -1)
                || uci[3] != (white ? '6' : '3') || move.CapturedSquare != string.Concat(uci[2], uci[1]))
                throw LilithMiniGamePublicContext.Rejected();
        }
        return new(sequence, LilithMiniGameEventKind.ChessMoved, move.Actor, uci: uci, move: move);
    }
    public static LilithMiniGameEvent Finished(long sequence, LilithMiniGameWinner winner, LilithMiniGameResultReason reason)
    {
        if (winner is not (LilithMiniGameWinner.Player or LilithMiniGameWinner.Lilith or LilithMiniGameWinner.Draw)
            || reason == LilithMiniGameResultReason.None || !Enum.IsDefined(typeof(LilithMiniGameResultReason), reason))
            throw LilithMiniGamePublicContext.Rejected();
        var draw = reason is LilithMiniGameResultReason.Stalemate or LilithMiniGameResultReason.DeadPosition
            or LilithMiniGameResultReason.ClaimedDraw or LilithMiniGameResultReason.Fivefold or LilithMiniGameResultReason.SeventyFiveMove;
        if ((winner == LilithMiniGameWinner.Draw) != draw) throw LilithMiniGamePublicContext.Rejected();
        return new(sequence, LilithMiniGameEventKind.Finished, winner: winner, reason: reason);
    }
    private static void RequireActor(LilithMiniGameActor actor)
    {
        if (actor is not (LilithMiniGameActor.Player or LilithMiniGameActor.Lilith)) throw LilithMiniGamePublicContext.Rejected();
    }
    internal static string ActorToken(LilithMiniGameActor actor) => actor == LilithMiniGameActor.Player ? "player" : "lilith";
    internal static string WinnerToken(LilithMiniGameWinner winner) => winner switch
    { LilithMiniGameWinner.Player => "player", LilithMiniGameWinner.Lilith => "lilith", LilithMiniGameWinner.Draw => "draw", _ => throw LilithMiniGamePublicContext.Rejected() };
    internal void Write(Utf8JsonWriter writer)
    {
        writer.WriteStartObject(); writer.WriteNumber("sequence", Sequence);
        writer.WriteString("kind", Kind switch
        {
            LilithMiniGameEventKind.Started => "started", LilithMiniGameEventKind.CardDrawn => "card-drawn",
            LilithMiniGameEventKind.PairDiscarded => "pair-discarded", LilithMiniGameEventKind.HandReordered => "hand-reordered",
            LilithMiniGameEventKind.ChessMoved => "chess-moved", _ => "finished"
        });
        switch (Kind)
        {
            case LilithMiniGameEventKind.CardDrawn:
                writer.WriteString("actor", ActorToken(Actor)); writer.WritePropertyName("card"); Card!.Write(writer); break;
            case LilithMiniGameEventKind.PairDiscarded:
                writer.WriteString("actor", ActorToken(Actor)); writer.WriteString("rank", Rank); break;
            case LilithMiniGameEventKind.HandReordered:
                writer.WriteString("actor", ActorToken(Actor)); writer.WriteNumber("fromIndex", FromIndex); writer.WriteNumber("toIndex", ToIndex); break;
            case LilithMiniGameEventKind.ChessMoved:
                writer.WriteString("uci", Uci); writer.WritePropertyName("move"); Move!.Write(writer); break;
            case LilithMiniGameEventKind.Finished:
                writer.WriteString("winner", WinnerToken(Winner)); writer.WriteString("resultReason", LilithMiniGamePublicContext.ReasonToken(ResultReason)); break;
        }
        writer.WriteEndObject();
    }
    internal static LilithMiniGameEvent Read(JsonElement value)
    {
        var kind = LilithMiniGamePublicContext.Text(value, "kind");
        var extra = kind switch
        {
            "started" => Array.Empty<string>(), "card-drawn" => new[] { "actor", "card" },
            "pair-discarded" => new[] { "actor", "rank" }, "hand-reordered" => new[] { "actor", "fromIndex", "toIndex" },
            "chess-moved" => new[] { "uci", "move" }, "finished" => new[] { "winner", "resultReason" },
            _ => throw LilithMiniGamePublicContext.Rejected()
        };
        LilithMiniGamePublicContext.Exact(value, new[] { "sequence", "kind" }.Concat(extra).ToArray());
        var seq = LilithMiniGamePublicContext.Integer(value, "sequence", 1, MaximumSequence);
        LilithMiniGameActor Actor() => LilithMiniGamePublicContext.Text(value, "actor") switch
        { "player" => LilithMiniGameActor.Player, "lilith" => LilithMiniGameActor.Lilith, _ => throw LilithMiniGamePublicContext.Rejected() };
        return kind switch
        {
            "started" => Started(seq), "card-drawn" => CardDrawn(seq, Actor(), LilithMiniGameOldMaidCard.Read(value.GetProperty("card"))),
            "pair-discarded" => PairDiscarded(seq, Actor(), LilithMiniGamePublicContext.Text(value, "rank")),
            "hand-reordered" => HandReordered(seq, Actor(), (int)LilithMiniGamePublicContext.Integer(value, "fromIndex", 0, 26),
                (int)LilithMiniGamePublicContext.Integer(value, "toIndex", 0, 26)),
            "chess-moved" => ChessMoved(seq, LilithMiniGamePublicContext.Text(value, "uci"), LilithMiniGameChessLastMove.Read(value.GetProperty("move"))),
            _ => Finished(seq, LilithMiniGamePublicContext.Text(value, "winner") switch
                { "player" => LilithMiniGameWinner.Player, "lilith" => LilithMiniGameWinner.Lilith, "draw" => LilithMiniGameWinner.Draw, _ => throw LilithMiniGamePublicContext.Rejected() },
                LilithMiniGamePublicContext.Reason(LilithMiniGamePublicContext.Text(value, "resultReason")))
        };
    }
}

public sealed class LilithMiniGameEventBatch
{
    public const int MaximumEvents = 128;
    public Guid BatchId { get; }
    public long PriorSequence { get; }
    public long ThroughSequence { get; }
    public int OmittedEventCount { get; }
    public int DeferredEventCount { get; }
    public IReadOnlyList<LilithMiniGameEvent> Events { get; }
    public LilithMiniGameEventBatch(Guid batchId, long priorSequence, long throughSequence,
        int omittedEventCount, int deferredEventCount, IReadOnlyList<LilithMiniGameEvent> events)
    {
        if (batchId == Guid.Empty || priorSequence < 0 || throughSequence < priorSequence
            || throughSequence > LilithMiniGameEvent.MaximumSequence || omittedEventCount < 0 || deferredEventCount < 0
            || (long)deferredEventCount + throughSequence > LilithMiniGameEvent.MaximumSequence
            || events is null || events.Count > MaximumEvents) throw LilithMiniGamePublicContext.Rejected();
        var copy = events.ToArray();
        if (copy.Length > MaximumEvents || throughSequence != priorSequence + omittedEventCount + copy.Length)
            throw LilithMiniGamePublicContext.Rejected();
        for (var i = 0; i < copy.Length; i++)
            if (copy[i] is null || copy[i].Sequence != priorSequence + omittedEventCount + i + 1)
                throw LilithMiniGamePublicContext.Rejected();
        if (copy.Skip(1).Any(e => e.Kind == LilithMiniGameEventKind.Started)
            || copy.Take(Math.Max(0, copy.Length - 1)).Any(e => e.Kind == LilithMiniGameEventKind.Finished)
            || copy.Any(e => e.Kind == LilithMiniGameEventKind.Started && e.Sequence != 1))
            throw LilithMiniGamePublicContext.Rejected();
        BatchId = batchId; PriorSequence = priorSequence; ThroughSequence = throughSequence;
        OmittedEventCount = omittedEventCount; DeferredEventCount = deferredEventCount; Events = Array.AsReadOnly(copy);
    }
    internal void ValidateFor(LilithMiniGamePublicContext context)
    {
        foreach (var item in Events)
        {
            if (context.Chess is { } chess)
            {
                if (item.Kind is LilithMiniGameEventKind.CardDrawn or LilithMiniGameEventKind.PairDiscarded or LilithMiniGameEventKind.HandReordered
                    || item.ResultReason == LilithMiniGameResultReason.AllPairs) throw LilithMiniGamePublicContext.Rejected();
                if (item.Move is { } move && move.Actor != (char.IsUpper(move.Piece) == (chess.PlayerSide == LilithMiniGameChessSide.White)
                    ? LilithMiniGameActor.Player : LilithMiniGameActor.Lilith)) throw LilithMiniGamePublicContext.Rejected();
            }
            else if (item.Kind == LilithMiniGameEventKind.ChessMoved
                || (item.Kind == LilithMiniGameEventKind.Finished && item.ResultReason != LilithMiniGameResultReason.AllPairs))
                throw LilithMiniGamePublicContext.Rejected();
            if (item.Kind == LilithMiniGameEventKind.Finished && (context.Phase != LilithMiniGamePhase.Finished
                || item.Winner != context.Winner || item.ResultReason != context.ResultReason || DeferredEventCount != 0))
                throw LilithMiniGamePublicContext.Rejected();
        }
    }
    public void Write(Utf8JsonWriter writer)
    {
        writer.WriteStartObject(); writer.WriteString("batchId", BatchId.ToString("N"));
        writer.WriteNumber("priorSequence", PriorSequence); writer.WriteNumber("throughSequence", ThroughSequence);
        writer.WriteNumber("omittedEventCount", OmittedEventCount); writer.WriteNumber("deferredEventCount", DeferredEventCount);
        writer.WriteStartArray("events"); foreach (var item in Events) item.Write(writer); writer.WriteEndArray(); writer.WriteEndObject();
    }
    public static LilithMiniGameEventBatch Read(JsonElement value)
    {
        LilithMiniGamePublicContext.Exact(value, "batchId", "priorSequence", "throughSequence", "omittedEventCount", "deferredEventCount", "events");
        var events = value.GetProperty("events");
        if (events.ValueKind != JsonValueKind.Array || events.GetArrayLength() > MaximumEvents) throw LilithMiniGamePublicContext.Rejected();
        return new(LilithMiniGamePublicContext.Identity(value, "batchId"),
            LilithMiniGamePublicContext.Integer(value, "priorSequence", 0, LilithMiniGameEvent.MaximumSequence),
            LilithMiniGamePublicContext.Integer(value, "throughSequence", 0, LilithMiniGameEvent.MaximumSequence),
            (int)LilithMiniGamePublicContext.Integer(value, "omittedEventCount", 0, LilithMiniGameEvent.MaximumSequence),
            (int)LilithMiniGamePublicContext.Integer(value, "deferredEventCount", 0, LilithMiniGameEvent.MaximumSequence),
            events.EnumerateArray().Select(LilithMiniGameEvent.Read).ToArray());
    }
}

// This is host proof of successful turn retention, not a model-generated assertion.
public sealed class LilithMiniGameEventReceipt
{
    public const string Schema = "lilith.minigame.events.receipt.v1";
    public Guid GameEpoch { get; }
    public Guid MatchId { get; }
    public Guid BatchId { get; }
    public long ThroughSequence { get; }
    public LilithMiniGameEventReceipt(Guid gameEpoch, Guid matchId, Guid batchId, long throughSequence)
    {
        if (gameEpoch == Guid.Empty || matchId == Guid.Empty || batchId == Guid.Empty
            || throughSequence < 0 || throughSequence > LilithMiniGameEvent.MaximumSequence) throw LilithMiniGamePublicContext.Rejected();
        GameEpoch = gameEpoch; MatchId = matchId; BatchId = batchId; ThroughSequence = throughSequence;
    }
    public bool Matches(LilithMiniGamePublicContext? context) => context?.Events is { } events
        && GameEpoch == context.GameEpoch && MatchId == context.MatchId && BatchId == events.BatchId && ThroughSequence == events.ThroughSequence;
    public void Write(Utf8JsonWriter writer)
    {
        writer.WriteStartObject(); writer.WriteString("schema", Schema); writer.WriteString("gameEpoch", GameEpoch.ToString("N"));
        writer.WriteString("matchId", MatchId.ToString("N")); writer.WriteString("batchId", BatchId.ToString("N"));
        writer.WriteNumber("throughSequence", ThroughSequence); writer.WriteEndObject();
    }
    public JsonElement ToJsonElement()
    {
        using var stream = new MemoryStream(); using (var writer = new Utf8JsonWriter(stream)) Write(writer);
        using var document = JsonDocument.Parse(stream.ToArray()); return document.RootElement.Clone();
    }
    public static LilithMiniGameEventReceipt Read(JsonElement value)
    {
        LilithMiniGamePublicContext.Exact(value, "schema", "gameEpoch", "matchId", "batchId", "throughSequence");
        if (LilithMiniGamePublicContext.Text(value, "schema") != Schema) throw LilithMiniGamePublicContext.Rejected();
        return new(LilithMiniGamePublicContext.Identity(value, "gameEpoch"), LilithMiniGamePublicContext.Identity(value, "matchId"),
            LilithMiniGamePublicContext.Identity(value, "batchId"), LilithMiniGamePublicContext.Integer(value, "throughSequence", 0, LilithMiniGameEvent.MaximumSequence));
    }
}
