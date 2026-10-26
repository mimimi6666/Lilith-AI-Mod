using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace LilithGameBridgeContracts;

public enum LilithMiniGamePhase { Active, Finished }
public enum LilithMiniGameActor { None, Player, Lilith }
public enum LilithMiniGameWinner { None, Player, Lilith, Draw }
public enum LilithMiniGameResultReason
{
    None, AllPairs, Checkmate, Timeout, Resignation, Stalemate, DeadPosition,
    ClaimedDraw, Fivefold, SeventyFiveMove
}
public enum LilithMiniGameChessSide { White, Pink }
public enum LilithMiniGameChessMode { Casual, Challenge }

// A request-time snapshot, not a game command or dialogue line. Optional v3
// committed events may be retained separately as typed conversation-turn metadata;
// the current snapshot must not be retained with them. No free-form instructions,
// full player hidden hand, engine analysis or renderer objects.
public sealed class LilithMiniGamePublicContext
{
    public const string Schema = "lilith.minigame.public.v1";
    public const string LilithViewSchema = "lilith.minigame.lilith-view.v2";
    public const string ConversationSchema = "lilith.minigame.conversation.v3";
    public const int MaximumBytes = 4096;
    public const int MaximumConversationBytes = 32768;
    public const int MaximumAgeMilliseconds = 120000;
    public const int MaximumFutureMilliseconds = 5000;
    public Guid GameEpoch { get; }
    public Guid MatchId { get; }
    public long CapturedAtUnixTimeMilliseconds { get; }
    public int PublicRevision { get; }
    public LilithMiniGamePhase Phase { get; }
    public LilithMiniGameActor Actor { get; }
    public LilithMiniGameWinner Winner { get; }
    public LilithMiniGameResultReason ResultReason { get; }
    public LilithMiniGameChessContext? Chess { get; }
    public LilithMiniGameOldMaidContext? OldMaid { get; }
    private readonly LilithMiniGameEventBatch? _events;
    public LilithMiniGameEventBatch? Events => _events;
    public string GameKind => Chess is null ? "old-maid" : "chess";
    public string SchemaName => Events is not null ? ConversationSchema
        : OldMaid?.LilithHand is not null || Chess?.Awareness is not null ? LilithViewSchema : Schema;

    public LilithMiniGamePublicContext(Guid gameEpoch, Guid matchId,
        long capturedAtUnixTimeMilliseconds, int publicRevision,
        LilithMiniGamePhase phase, LilithMiniGameActor actor,
        LilithMiniGameWinner winner, LilithMiniGameResultReason resultReason,
        LilithMiniGameChessContext? chess = null,
        LilithMiniGameOldMaidContext? oldMaid = null)
    {
        if (gameEpoch == Guid.Empty || matchId == Guid.Empty
            || capturedAtUnixTimeMilliseconds <= 0 || capturedAtUnixTimeMilliseconds > 253402300799999L
            || publicRevision < 0 || publicRevision > 100000
            || !Enum.IsDefined(typeof(LilithMiniGamePhase), phase)
            || !Enum.IsDefined(typeof(LilithMiniGameActor), actor)
            || !Enum.IsDefined(typeof(LilithMiniGameWinner), winner)
            || !Enum.IsDefined(typeof(LilithMiniGameResultReason), resultReason)
            || (chess is null) == (oldMaid is null))
            throw Rejected();
        if (phase == LilithMiniGamePhase.Active
            ? actor == LilithMiniGameActor.None || winner != LilithMiniGameWinner.None || resultReason != LilithMiniGameResultReason.None
            : actor != LilithMiniGameActor.None || winner == LilithMiniGameWinner.None || resultReason == LilithMiniGameResultReason.None)
            throw Rejected();
        if (chess is not null)
        {
            if (resultReason == LilithMiniGameResultReason.AllPairs) throw Rejected();
            var drawn = resultReason is LilithMiniGameResultReason.Stalemate or LilithMiniGameResultReason.DeadPosition
                or LilithMiniGameResultReason.ClaimedDraw or LilithMiniGameResultReason.Fivefold or LilithMiniGameResultReason.SeventyFiveMove;
            if ((winner == LilithMiniGameWinner.Draw) != drawn) throw Rejected();
            var expectedActor = chess.PlayerSide == chess.SideToMove ? LilithMiniGameActor.Player : LilithMiniGameActor.Lilith;
            if (phase == LilithMiniGamePhase.Active && actor != expectedActor) throw Rejected();
            if (chess.Awareness is { } awareness
                && (phase == LilithMiniGamePhase.Active ? awareness.TurnActor != actor
                    : awareness.TurnActor != LilithMiniGameActor.None || awareness.CheckingMoveAvailable || awareness.CaptureTargetSquares.Count != 0))
                throw Rejected();
        }
        else if (oldMaid is not null)
        {
            if (winner == LilithMiniGameWinner.Draw
                || (phase == LilithMiniGamePhase.Finished && resultReason != LilithMiniGameResultReason.AllPairs)
                || (phase == LilithMiniGamePhase.Active && (oldMaid.PlayerHandCount == 0 || oldMaid.LilithHandCount == 0))
                || (winner == LilithMiniGameWinner.Player && (oldMaid.PlayerHandCount != 0 || oldMaid.LilithHandCount != 1))
                || (winner == LilithMiniGameWinner.Lilith && (oldMaid.LilithHandCount != 0 || oldMaid.PlayerHandCount != 1)))
                throw Rejected();
            if (winner == LilithMiniGameWinner.Player && oldMaid.LilithHand is { } hand
                && (hand.Cards.Count != 1 || hand.Cards[0].Rank != "JOKER")) throw Rejected();
        }
        GameEpoch = gameEpoch; MatchId = matchId;
        CapturedAtUnixTimeMilliseconds = capturedAtUnixTimeMilliseconds; PublicRevision = publicRevision;
        Phase = phase; Actor = actor; Winner = winner; ResultReason = resultReason;
        Chess = chess; OldMaid = oldMaid;
    }

    public bool IsFresh(long nowUnixTimeMilliseconds)
        => nowUnixTimeMilliseconds > 0
            && CapturedAtUnixTimeMilliseconds >= nowUnixTimeMilliseconds - MaximumAgeMilliseconds
            && CapturedAtUnixTimeMilliseconds <= nowUnixTimeMilliseconds + MaximumFutureMilliseconds;

    // Preserve the existing public constructor ABI. A leased batch is attached only
    // to a new immutable snapshot copy, never to a shared presenter observation.
    public LilithMiniGamePublicContext WithEvents(LilithMiniGameEventBatch events)
    {
        if (events is null || (Chess is not null ? Chess.Awareness is null : OldMaid?.LilithHand is null))
            throw Rejected();
        events.ValidateFor(this);
        return new LilithMiniGamePublicContext(this, events);
    }

    private LilithMiniGamePublicContext(LilithMiniGamePublicContext context, LilithMiniGameEventBatch events)
        : this(context.GameEpoch, context.MatchId, context.CapturedAtUnixTimeMilliseconds, context.PublicRevision,
            context.Phase, context.Actor, context.Winner, context.ResultReason, context.Chess, context.OldMaid)
    { _events = events; }

    public JsonElement ToJsonElement()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) Write(writer);
        if (stream.Length > (Events is null ? MaximumBytes : MaximumConversationBytes)) throw Rejected();
        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    public void Write(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("schema", SchemaName);
        writer.WriteString("gameEpoch", GameEpoch.ToString("N"));
        writer.WriteString("matchId", MatchId.ToString("N"));
        writer.WriteNumber("capturedAtUnixTimeMilliseconds", CapturedAtUnixTimeMilliseconds);
        writer.WriteNumber("publicRevision", PublicRevision);
        writer.WriteString("gameKind", GameKind);
        writer.WriteString("phase", Phase == LilithMiniGamePhase.Active ? "active" : "finished");
        writer.WriteString("actor", Actor switch { LilithMiniGameActor.Player => "player", LilithMiniGameActor.Lilith => "lilith", _ => "none" });
        writer.WriteString("winner", Winner switch { LilithMiniGameWinner.Player => "player", LilithMiniGameWinner.Lilith => "lilith", LilithMiniGameWinner.Draw => "draw", _ => "none" });
        writer.WriteString("resultReason", ReasonToken(ResultReason));
        if (Chess is not null) { writer.WritePropertyName("chess"); Chess.Write(writer); }
        else { writer.WritePropertyName("oldMaid"); OldMaid!.Write(writer); }
        if (Events is not null) { writer.WritePropertyName("events"); Events.Write(writer); }
        writer.WriteEndObject();
    }

    public static LilithMiniGamePublicContext Read(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Rejected();
        var schema = Text(value, "schema");
        var conversation = schema == ConversationSchema;
        if (schema is not (Schema or LilithViewSchema or ConversationSchema)
            || Encoding.UTF8.GetByteCount(value.GetRawText()) > (conversation ? MaximumConversationBytes : MaximumBytes)) throw Rejected();
        var kind = Text(value, "gameKind");
        if (kind is not ("chess" or "old-maid")) throw Rejected();
        var fields = new[] { "schema", "gameEpoch", "matchId", "capturedAtUnixTimeMilliseconds", "publicRevision",
            "gameKind", "phase", "actor", "winner", "resultReason", kind == "chess" ? "chess" : "oldMaid" };
        Exact(value, conversation ? fields.Concat(new[] { "events" }).ToArray() : fields);
        var lilithView = schema != Schema;
        var context = new LilithMiniGamePublicContext(Identity(value, "gameEpoch"), Identity(value, "matchId"),
            Integer(value, "capturedAtUnixTimeMilliseconds", 1, 253402300799999L),
            (int)Integer(value, "publicRevision", 0, 100000),
            Text(value, "phase") switch { "active" => LilithMiniGamePhase.Active, "finished" => LilithMiniGamePhase.Finished, _ => throw Rejected() },
            Text(value, "actor") switch { "none" => LilithMiniGameActor.None, "player" => LilithMiniGameActor.Player, "lilith" => LilithMiniGameActor.Lilith, _ => throw Rejected() },
            Text(value, "winner") switch { "none" => LilithMiniGameWinner.None, "player" => LilithMiniGameWinner.Player, "lilith" => LilithMiniGameWinner.Lilith, "draw" => LilithMiniGameWinner.Draw, _ => throw Rejected() },
            Reason(Text(value, "resultReason")),
            kind == "chess" ? LilithMiniGameChessContext.Read(value.GetProperty("chess"), lilithView) : null,
            kind == "old-maid" ? LilithMiniGameOldMaidContext.Read(value.GetProperty("oldMaid"), lilithView) : null);
        return conversation ? context.WithEvents(LilithMiniGameEventBatch.Read(value.GetProperty("events"))) : context;
    }

    internal static string ReasonToken(LilithMiniGameResultReason reason) => reason switch
    {
        LilithMiniGameResultReason.None => "none", LilithMiniGameResultReason.AllPairs => "all-pairs",
        LilithMiniGameResultReason.Checkmate => "checkmate", LilithMiniGameResultReason.Timeout => "timeout",
        LilithMiniGameResultReason.Resignation => "resignation", LilithMiniGameResultReason.Stalemate => "stalemate",
        LilithMiniGameResultReason.DeadPosition => "dead-position", LilithMiniGameResultReason.ClaimedDraw => "claimed-draw",
        LilithMiniGameResultReason.Fivefold => "fivefold", LilithMiniGameResultReason.SeventyFiveMove => "seventy-five-move",
        _ => throw Rejected()
    };
    internal static LilithMiniGameResultReason Reason(string token)
    {
        foreach (LilithMiniGameResultReason reason in Enum.GetValues(typeof(LilithMiniGameResultReason)))
            if (ReasonToken(reason) == token) return reason;
        throw Rejected();
    }
    internal static InvalidDataException Rejected() => new("Mini-game public context was rejected.");
    internal static void Exact(JsonElement value, params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Rejected();
        var expected = new HashSet<string>(names, StringComparer.Ordinal);
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            if (!expected.Contains(property.Name) || !found.Add(property.Name)) throw Rejected();
        if (!found.SetEquals(expected)) throw Rejected();
    }
    internal static string Text(JsonElement value, string key)
        => value.TryGetProperty(key, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString()! : throw Rejected();
    internal static long Integer(JsonElement value, string key, long min, long max)
        => value.TryGetProperty(key, out var item) && item.ValueKind == JsonValueKind.Number
            && item.TryGetInt64(out var number) && number >= min && number <= max ? number : throw Rejected();
    internal static bool Boolean(JsonElement value, string key)
        => value.TryGetProperty(key, out var item) && item.ValueKind is JsonValueKind.True or JsonValueKind.False ? item.GetBoolean() : throw Rejected();
    internal static Guid Identity(JsonElement value, string key)
    {
        var text = Text(value, key);
        if (!Guid.TryParseExact(text, "N", out var identity) || identity == Guid.Empty || identity.ToString("N") != text) throw Rejected();
        return identity;
    }
}

public sealed class LilithMiniGameChessContext
{
    public string Board { get; }
    public LilithMiniGameChessSide PlayerSide { get; }
    public LilithMiniGameChessSide SideToMove { get; }
    public int Ply { get; }
    public string LastMoveUci { get; }
    public bool InCheck { get; }
    public LilithMiniGameChessMode Mode { get; }
    public int PlayerRemainingMilliseconds { get; }
    public int LilithRemainingMilliseconds { get; }
    public LilithMiniGameChessAwareness? Awareness { get; }

    public LilithMiniGameChessContext(string board, LilithMiniGameChessSide playerSide,
        LilithMiniGameChessSide sideToMove, int ply, string lastMoveUci, bool inCheck,
        LilithMiniGameChessMode mode, int playerRemainingMilliseconds, int lilithRemainingMilliseconds)
        : this(board, playerSide, sideToMove, ply, lastMoveUci, inCheck, mode,
            playerRemainingMilliseconds, lilithRemainingMilliseconds, null) { }

    public LilithMiniGameChessContext(string board, LilithMiniGameChessSide playerSide,
        LilithMiniGameChessSide sideToMove, int ply, string lastMoveUci, bool inCheck,
        LilithMiniGameChessMode mode, int playerRemainingMilliseconds, int lilithRemainingMilliseconds,
        LilithMiniGameChessAwareness? awareness)
    {
        if (board is null || board.Length != 64 || board.Any(c => ".prnbqkPRNBQK".IndexOf(c) < 0)
            || board.Count(c => c == 'K') != 1 || board.Count(c => c == 'k') != 1 || board.Count(c => c != '.') > 32
            || !Enum.IsDefined(typeof(LilithMiniGameChessSide), playerSide)
            || !Enum.IsDefined(typeof(LilithMiniGameChessSide), sideToMove)
            || !Enum.IsDefined(typeof(LilithMiniGameChessMode), mode)
            || ply < 0 || ply > 100000 || !ValidUci(lastMoveUci)
            || (ply == 0 && lastMoveUci.Length != 0) || (ply > 0 && lastMoveUci.Length == 0)
            || (mode == LilithMiniGameChessMode.Casual
                ? playerRemainingMilliseconds != -1 || lilithRemainingMilliseconds != -1
                : playerRemainingMilliseconds < 0 || playerRemainingMilliseconds > 86400000
                    || lilithRemainingMilliseconds < 0 || lilithRemainingMilliseconds > 86400000))
            throw LilithMiniGamePublicContext.Rejected();
        Board = board; PlayerSide = playerSide; SideToMove = sideToMove; Ply = ply;
        LastMoveUci = lastMoveUci; InCheck = inCheck; Mode = mode;
        PlayerRemainingMilliseconds = playerRemainingMilliseconds; LilithRemainingMilliseconds = lilithRemainingMilliseconds;
        awareness?.Validate(this);
        Awareness = awareness;
    }

    private static bool ValidUci(string? value) => value is not null && (value.Length == 0
        || ((value.Length == 4 || (value.Length == 5 && "qrbn".IndexOf(value[4]) >= 0))
            && value[0] >= 'a' && value[0] <= 'h' && value[1] >= '1' && value[1] <= '8'
            && value[2] >= 'a' && value[2] <= 'h' && value[3] >= '1' && value[3] <= '8'
            && value.Substring(0, 2) != value.Substring(2, 2)));

    internal void Write(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("board", Board);
        writer.WriteString("playerSide", PlayerSide == LilithMiniGameChessSide.White ? "white" : "pink");
        writer.WriteString("sideToMove", SideToMove == LilithMiniGameChessSide.White ? "white" : "pink");
        writer.WriteNumber("ply", Ply); writer.WriteString("lastMoveUci", LastMoveUci);
        writer.WriteBoolean("inCheck", InCheck); writer.WriteString("mode", Mode == LilithMiniGameChessMode.Casual ? "casual" : "challenge");
        writer.WriteNumber("playerRemainingMilliseconds", PlayerRemainingMilliseconds);
        writer.WriteNumber("lilithRemainingMilliseconds", LilithRemainingMilliseconds);
        if (Awareness is not null) { writer.WritePropertyName("awareness"); Awareness.Write(writer); }
        writer.WriteEndObject();
    }
    internal static LilithMiniGameChessContext Read(JsonElement value, bool lilithView = false)
    {
        var fields = new[] { "board", "playerSide", "sideToMove", "ply", "lastMoveUci", "inCheck",
            "mode", "playerRemainingMilliseconds", "lilithRemainingMilliseconds" };
        LilithMiniGamePublicContext.Exact(value, lilithView ? fields.Concat(new[] { "awareness" }).ToArray() : fields);
        return new LilithMiniGameChessContext(LilithMiniGamePublicContext.Text(value, "board"),
            Side(LilithMiniGamePublicContext.Text(value, "playerSide")), Side(LilithMiniGamePublicContext.Text(value, "sideToMove")),
            (int)LilithMiniGamePublicContext.Integer(value, "ply", 0, 100000), LilithMiniGamePublicContext.Text(value, "lastMoveUci"),
            LilithMiniGamePublicContext.Boolean(value, "inCheck"),
            LilithMiniGamePublicContext.Text(value, "mode") switch { "casual" => LilithMiniGameChessMode.Casual, "challenge" => LilithMiniGameChessMode.Challenge, _ => throw LilithMiniGamePublicContext.Rejected() },
            (int)LilithMiniGamePublicContext.Integer(value, "playerRemainingMilliseconds", -1, 86400000),
            (int)LilithMiniGamePublicContext.Integer(value, "lilithRemainingMilliseconds", -1, 86400000),
            lilithView ? LilithMiniGameChessAwareness.Read(value.GetProperty("awareness")) : null);
    }
    private static LilithMiniGameChessSide Side(string token) => token switch
    {
        "white" => LilithMiniGameChessSide.White, "pink" => LilithMiniGameChessSide.Pink,
        _ => throw LilithMiniGamePublicContext.Rejected()
    };
}

public sealed class LilithMiniGameOldMaidContext
{
    public int PlayerHandCount { get; }
    public int LilithHandCount { get; }
    public int DrawCount { get; }
    public int PublicPairCount { get; }
    public LilithMiniGameOldMaidHand? LilithHand { get; }
    public LilithMiniGameOldMaidContext(int playerHandCount, int lilithHandCount, int drawCount, int publicPairCount)
        : this(playerHandCount, lilithHandCount, drawCount, publicPairCount, null) { }

    public LilithMiniGameOldMaidContext(int playerHandCount, int lilithHandCount, int drawCount, int publicPairCount,
        LilithMiniGameOldMaidHand? lilithHand)
    {
        if (playerHandCount < 0 || playerHandCount > 27 || lilithHandCount < 0 || lilithHandCount > 27
            || drawCount < 0 || drawCount > 100000 || publicPairCount < 0 || publicPairCount > 13
            || playerHandCount + lilithHandCount + publicPairCount * 2 != 27
            || (lilithHand is not null && lilithHand.Cards.Count != lilithHandCount))
            throw LilithMiniGamePublicContext.Rejected();
        PlayerHandCount = playerHandCount; LilithHandCount = lilithHandCount;
        DrawCount = drawCount; PublicPairCount = publicPairCount;
        LilithHand = lilithHand;
    }
    internal void Write(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteNumber("playerHandCount", PlayerHandCount); writer.WriteNumber("lilithHandCount", LilithHandCount);
        writer.WriteNumber("drawCount", DrawCount); writer.WriteNumber("publicPairCount", PublicPairCount);
        if (LilithHand is not null) { writer.WritePropertyName("lilithHand"); LilithHand.Write(writer); }
        writer.WriteEndObject();
    }
    internal static LilithMiniGameOldMaidContext Read(JsonElement value, bool lilithView = false)
    {
        var fields = new[] { "playerHandCount", "lilithHandCount", "drawCount", "publicPairCount" };
        LilithMiniGamePublicContext.Exact(value, lilithView ? fields.Concat(new[] { "lilithHand" }).ToArray() : fields);
        return new LilithMiniGameOldMaidContext((int)LilithMiniGamePublicContext.Integer(value, "playerHandCount", 0, 27),
            (int)LilithMiniGamePublicContext.Integer(value, "lilithHandCount", 0, 27),
            (int)LilithMiniGamePublicContext.Integer(value, "drawCount", 0, 100000),
            (int)LilithMiniGamePublicContext.Integer(value, "publicPairCount", 0, 13),
            lilithView ? LilithMiniGameOldMaidHand.Read(value.GetProperty("lilithHand")) : null);
    }
}

public sealed class LilithMiniGameOldMaidCard
{
    public string Rank { get; }
    public string Suit { get; }
    public LilithMiniGameOldMaidCard(string rank, string suit)
    {
        if (rank == "JOKER" ? suit != "none"
            : rank is not ("A" or "2" or "3" or "4" or "5" or "6" or "7" or "8" or "9" or "10" or "J" or "Q" or "K")
                || suit is not ("hearts" or "spades")) throw LilithMiniGamePublicContext.Rejected();
        Rank = rank; Suit = suit;
    }
    internal void Write(Utf8JsonWriter writer)
    {
        writer.WriteStartObject(); writer.WriteString("rank", Rank); writer.WriteString("suit", Suit); writer.WriteEndObject();
    }
    internal static LilithMiniGameOldMaidCard Read(JsonElement value)
    {
        LilithMiniGamePublicContext.Exact(value, "rank", "suit");
        return new(LilithMiniGamePublicContext.Text(value, "rank"), LilithMiniGamePublicContext.Text(value, "suit"));
    }
}

public sealed class LilithMiniGameOldMaidHand
{
    public string Order => "screen-left-to-right";
    public IReadOnlyList<LilithMiniGameOldMaidCard> Cards { get; }
    public LilithMiniGameOldMaidHand(IReadOnlyList<LilithMiniGameOldMaidCard> cards)
    {
        if (cards is null || cards.Count > 27) throw LilithMiniGamePublicContext.Rejected();
        var copy = cards.ToArray();
        if (copy.Length > 27) throw LilithMiniGamePublicContext.Rejected();
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var card in copy)
            if (card is null || !found.Add(card.Rank + "/" + card.Suit)) throw LilithMiniGamePublicContext.Rejected();
        Cards = Array.AsReadOnly(copy);
    }
    internal void Write(Utf8JsonWriter writer)
    {
        writer.WriteStartObject(); writer.WriteString("order", Order); writer.WriteStartArray("cards");
        foreach (var card in Cards) card.Write(writer);
        writer.WriteEndArray(); writer.WriteEndObject();
    }
    internal static LilithMiniGameOldMaidHand Read(JsonElement value)
    {
        LilithMiniGamePublicContext.Exact(value, "order", "cards");
        var cards = value.GetProperty("cards");
        if (LilithMiniGamePublicContext.Text(value, "order") != "screen-left-to-right"
            || cards.ValueKind != JsonValueKind.Array || cards.GetArrayLength() > 27) throw LilithMiniGamePublicContext.Rejected();
        return new(cards.EnumerateArray().Select(LilithMiniGameOldMaidCard.Read).ToArray());
    }
}

public sealed class LilithMiniGameChessLastMove
{
    public LilithMiniGameActor Actor { get; }
    public char Piece { get; }
    public char CapturedPiece { get; }
    public string CapturedSquare { get; }
    public bool GaveCheck { get; }
    public LilithMiniGameChessLastMove(LilithMiniGameActor actor, char piece, char capturedPiece, string capturedSquare, bool gaveCheck)
    {
        if (actor is not (LilithMiniGameActor.Player or LilithMiniGameActor.Lilith) || "prnbqkPRNBQK".IndexOf(piece) < 0
            || ".prnbqPRNBQ".IndexOf(capturedPiece) < 0
            || (capturedPiece == '.' ? capturedSquare != string.Empty
                : !LilithMiniGameChessAwareness.ValidSquare(capturedSquare) || char.IsUpper(piece) == char.IsUpper(capturedPiece)))
            throw LilithMiniGamePublicContext.Rejected();
        Actor = actor; Piece = piece; CapturedPiece = capturedPiece; CapturedSquare = capturedSquare; GaveCheck = gaveCheck;
    }
    internal void Write(Utf8JsonWriter writer)
    {
        writer.WriteStartObject(); writer.WriteString("actor", Actor == LilithMiniGameActor.Player ? "player" : "lilith");
        writer.WriteString("piece", Piece.ToString()); writer.WriteString("capturedPiece", CapturedPiece.ToString());
        writer.WriteString("capturedSquare", CapturedSquare); writer.WriteBoolean("gaveCheck", GaveCheck); writer.WriteEndObject();
    }
    internal static LilithMiniGameChessLastMove Read(JsonElement value)
    {
        LilithMiniGamePublicContext.Exact(value, "actor", "piece", "capturedPiece", "capturedSquare", "gaveCheck");
        var piece = LilithMiniGamePublicContext.Text(value, "piece");
        var captured = LilithMiniGamePublicContext.Text(value, "capturedPiece");
        if (piece.Length != 1 || captured.Length != 1) throw LilithMiniGamePublicContext.Rejected();
        return new(LilithMiniGamePublicContext.Text(value, "actor") switch
            { "player" => LilithMiniGameActor.Player, "lilith" => LilithMiniGameActor.Lilith, _ => throw LilithMiniGamePublicContext.Rejected() },
            piece[0], captured[0], LilithMiniGamePublicContext.Text(value, "capturedSquare"), LilithMiniGamePublicContext.Boolean(value, "gaveCheck"));
    }
}

public sealed class LilithMiniGameChessAwareness
{
    public string Scope => "side-to-move-legal";
    public LilithMiniGameActor TurnActor { get; }
    public bool CheckingMoveAvailable { get; }
    public IReadOnlyList<string> CaptureTargetSquares { get; }
    public LilithMiniGameChessLastMove? LastMove { get; }
    public LilithMiniGameChessAwareness(LilithMiniGameActor turnActor, bool checkingMoveAvailable,
        IReadOnlyList<string> captureTargetSquares, LilithMiniGameChessLastMove? lastMove)
    {
        if (!Enum.IsDefined(typeof(LilithMiniGameActor), turnActor) || captureTargetSquares is null || captureTargetSquares.Count > 32)
            throw LilithMiniGamePublicContext.Rejected();
        var copy = captureTargetSquares.ToArray();
        if (copy.Length > 32) throw LilithMiniGamePublicContext.Rejected();
        for (var i = 0; i < copy.Length; i++)
            if (!ValidSquare(copy[i]) || (i > 0 && StringComparer.Ordinal.Compare(copy[i - 1], copy[i]) >= 0))
                throw LilithMiniGamePublicContext.Rejected();
        TurnActor = turnActor; CheckingMoveAvailable = checkingMoveAvailable;
        CaptureTargetSquares = Array.AsReadOnly(copy); LastMove = lastMove;
    }
    internal static bool ValidSquare(string? square) => square is { Length: 2 }
        && square[0] >= 'a' && square[0] <= 'h' && square[1] >= '1' && square[1] <= '8';
    private static int SquareIndex(string square) => (square[1] - '1') * 8 + square[0] - 'a';
    internal void Validate(LilithMiniGameChessContext chess)
    {
        var whiteTurn = chess.SideToMove == LilithMiniGameChessSide.White;
        foreach (var square in CaptureTargetSquares)
        {
            var piece = chess.Board[SquareIndex(square)];
            if (piece == '.' || char.ToLowerInvariant(piece) == 'k' || char.IsUpper(piece) == whiteTurn)
                throw LilithMiniGamePublicContext.Rejected();
        }
        if (LastMove is not { } move) return;
        var movedWhite = char.IsUpper(move.Piece);
        var expectedActor = movedWhite == (chess.PlayerSide == LilithMiniGameChessSide.White) ? LilithMiniGameActor.Player : LilithMiniGameActor.Lilith;
        if (chess.Ply <= 0 || movedWhite == whiteTurn || move.Actor != expectedActor || move.GaveCheck != chess.InCheck)
            throw LilithMiniGamePublicContext.Rejected();
        var uci = chess.LastMoveUci;
        var destination = uci.Substring(2, 2);
        var expectedPiece = move.Piece;
        if (uci.Length == 5)
        {
            if (char.ToLowerInvariant(move.Piece) != 'p' || uci[1] != (movedWhite ? '7' : '2')
                || uci[3] != (movedWhite ? '8' : '1')) throw LilithMiniGamePublicContext.Rejected();
            expectedPiece = movedWhite ? char.ToUpperInvariant(uci[4]) : uci[4];
        }
        if (chess.Board[SquareIndex(destination)] != expectedPiece) throw LilithMiniGamePublicContext.Rejected();
        if (move.CapturedPiece == '.' || move.CapturedSquare == destination) return;
        // En passant names the actual removed pawn square, not the empty landing square.
        if (char.ToLowerInvariant(move.Piece) != 'p' || char.ToLowerInvariant(move.CapturedPiece) != 'p'
            || Math.Abs(uci[0] - uci[2]) != 1 || uci[3] - uci[1] != (movedWhite ? 1 : -1)
            || uci[3] != (movedWhite ? '6' : '3') || move.CapturedSquare != string.Concat(uci[2], uci[1])
            || chess.Board[SquareIndex(move.CapturedSquare)] != '.') throw LilithMiniGamePublicContext.Rejected();
    }
    internal void Write(Utf8JsonWriter writer)
    {
        writer.WriteStartObject(); writer.WriteString("scope", Scope);
        writer.WriteString("turnActor", TurnActor switch { LilithMiniGameActor.Player => "player", LilithMiniGameActor.Lilith => "lilith", _ => "none" });
        writer.WriteBoolean("checkingMoveAvailable", CheckingMoveAvailable); writer.WriteStartArray("captureTargetSquares");
        foreach (var square in CaptureTargetSquares) writer.WriteStringValue(square);
        writer.WriteEndArray(); writer.WritePropertyName("lastMove");
        if (LastMove is null) writer.WriteNullValue(); else LastMove.Write(writer);
        writer.WriteEndObject();
    }
    internal static LilithMiniGameChessAwareness Read(JsonElement value)
    {
        LilithMiniGamePublicContext.Exact(value, "scope", "turnActor", "checkingMoveAvailable", "captureTargetSquares", "lastMove");
        var targets = value.GetProperty("captureTargetSquares");
        if (LilithMiniGamePublicContext.Text(value, "scope") != "side-to-move-legal"
            || targets.ValueKind != JsonValueKind.Array || targets.GetArrayLength() > 32) throw LilithMiniGamePublicContext.Rejected();
        var lastMove = value.GetProperty("lastMove");
        return new(LilithMiniGamePublicContext.Text(value, "turnActor") switch
            { "none" => LilithMiniGameActor.None, "player" => LilithMiniGameActor.Player, "lilith" => LilithMiniGameActor.Lilith, _ => throw LilithMiniGamePublicContext.Rejected() },
            LilithMiniGamePublicContext.Boolean(value, "checkingMoveAvailable"),
            targets.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String ? item.GetString()! : throw LilithMiniGamePublicContext.Rejected()).ToArray(),
            lastMove.ValueKind == JsonValueKind.Null ? null : LilithMiniGameChessLastMove.Read(lastMove));
    }
}
