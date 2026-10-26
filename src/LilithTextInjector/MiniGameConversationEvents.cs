using System;
using System.Collections.Generic;
using LilithGameBridgeContracts;

namespace LilithTextInjector;

// Optional observation only: successful local commits append typed facts. One
// shared lease serves human and existing proactive turns; no timer or dispatch.
internal sealed class MiniGameConversationEvents
{
    internal static MiniGameConversationEvents Shared { get; } = new();
    internal const int MaximumPendingEvents = 4096;
    internal const int MaximumBatchEvents = 128;
    private const long MaximumSequence = 1000000;
    private readonly object gate = new();
    private readonly Queue<LilithMiniGameEvent> pending = new();
    private Guid match, epoch, operation;
    private string kind = string.Empty;
    private object? owner;
    private long sequence, acknowledged;
    private LilithMiniGamePublicContext? leased;
    private LilithMiniGameEventBatch? retryBatch;
    private int recordedDraws, recordedPairs, discardedCards, playerOrderRevision, lilithOrderRevision, recordedPly;
    private bool resultRecorded, exhausted, capacityNotice;

    internal void StartOldMaid(Guid matchId, OldMaidGame game)
    {
        lock (gate)
        {
            if (game == null || !game.IsStarted || matchId == Guid.Empty) return;
            Start(matchId, "old-maid", game);
        }
    }

    internal void StartChess(Guid matchId, ChessSession game)
    {
        lock (gate)
        {
            if (game == null || game.Phase == ChessPhase.Setup || matchId == Guid.Empty) return;
            Start(matchId, "chess", game);
        }
    }

    private void Start(Guid matchId, string gameKind, object game)
    {
        if (match == matchId && ReferenceEquals(owner, game)) return;
        Reset(); match = matchId; kind = gameKind; owner = game;
        Append(LilithMiniGameEvent.Started);
    }

    internal void Clear(Guid matchId)
    {
        lock (gate) if (matchId == match) Reset();
    }

    private void Reset()
    {
        pending.Clear(); match = epoch = operation = Guid.Empty;
        kind = string.Empty; owner = null; sequence = acknowledged = 0; leased = null; retryBatch = null;
        recordedDraws = recordedPairs = discardedCards = playerOrderRevision = lilithOrderRevision = recordedPly = 0;
        resultRecorded = exhausted = capacityNotice = false;
    }

    private bool Owns(object game, string gameKind) => ReferenceEquals(owner, game) && kind == gameKind && !exhausted;

    private bool Append(Func<long, LilithMiniGameEvent> create)
    {
        if (exhausted) return false;
        if (sequence >= MaximumSequence)
        {
            // Do not wrap identities or pretend later actions were delivered.
            // The ordinary fresh snapshot remains usable without event claims.
            exhausted = capacityNotice = true; pending.Clear(); leased = null; retryBatch = null; operation = Guid.Empty;
            return false;
        }
        try
        {
            var value = create(sequence + 1);
            sequence++;
            pending.Enqueue(value);
            if (pending.Count > MaximumPendingEvents) pending.Dequeue();
            return true;
        }
        catch (ArgumentException) { }
        catch (System.IO.InvalidDataException) { }
        return false;
    }

    internal void RecordOldMaidDraw(OldMaidGame game, OldMaidOwner actor, OldMaidCard knownCard)
    {
        lock (gate)
        {
            if (!Owns(game, "old-maid") || game.DrawCount <= recordedDraws
                || actor is not (OldMaidOwner.Player or OldMaidOwner.Opponent)) return;
            // A player draw names only the card previously held by Lilith. It
            // does not take a snapshot of any remaining player hidden cards.
            if (Append(seq => LilithMiniGameEvent.CardDrawn(seq, Actor(actor), Card(knownCard))))
                recordedDraws = game.DrawCount;
        }
    }

    internal void RecordOldMaidPair(OldMaidGame game, OldMaidPair pair)
    {
        lock (gate)
        {
            if (!Owns(game, "old-maid") || game.DiscardedPairCount <= recordedPairs
                || pair.First.IsJoker || pair.Second.IsJoker || pair.First.Id == pair.Second.Id
                || pair.First.Rank != pair.Second.Rank
                || pair.Owner is not (OldMaidOwner.Player or OldMaidOwner.Opponent)) return;
            var mask = (1 << pair.First.Id) | (1 << pair.Second.Id);
            if ((discardedCards & mask) != 0) return;
            if (Append(seq => LilithMiniGameEvent.PairDiscarded(seq, Actor(pair.Owner), OldMaidCardArtPolicy.Rank(pair.First))))
            { discardedCards |= mask; recordedPairs++; }
        }
    }

    internal void RecordOldMaidReorder(OldMaidGame game, OldMaidOwner actor, OldMaidHandMove move)
    {
        lock (gate)
        {
            if (!Owns(game, "old-maid") || actor is not (OldMaidOwner.Player or OldMaidOwner.Opponent)
                || game.Revision != move.ExpectedRevision + 1 || move.SourceSlot == move.TargetSlot) return;
            var hand = actor == OldMaidOwner.Player ? game.PlayerHand : game.OpponentHand;
            var lastRevision = actor == OldMaidOwner.Player ? playerOrderRevision : lilithOrderRevision;
            if (game.Revision <= lastRevision || move.ExpectedCount != hand.Count
                || move.SourceSlot < 0 || move.SourceSlot >= hand.Count || move.TargetSlot < 0
                || move.TargetSlot >= hand.Count || hand[move.TargetSlot].Id != move.CardId) return;
            // Slot-only even for the player: no hidden identity crosses here.
            if (Append(seq => LilithMiniGameEvent.HandReordered(seq, Actor(actor), move.SourceSlot, move.TargetSlot)))
            {
                if (actor == OldMaidOwner.Player) playerOrderRevision = game.Revision;
                else lilithOrderRevision = game.Revision;
            }
        }
    }

    internal void RecordOldMaidResult(OldMaidGame game)
    {
        lock (gate)
        {
            if (!Owns(game, "old-maid") || resultRecorded || game.Phase != OldMaidPhase.Finished
                || game.Winner == OldMaidWinner.None || recordedPairs != game.DiscardedPairCount
                || recordedDraws != game.DrawCount) return;
            resultRecorded = Append(seq => LilithMiniGameEvent.Finished(seq,
                game.Winner == OldMaidWinner.Player ? LilithMiniGameWinner.Player : LilithMiniGameWinner.Lilith,
                LilithMiniGameResultReason.AllPairs));
        }
    }

    internal void RecordChessMove(ChessSession game, string uci, char piece, char victim, int capturedSquare)
    {
        lock (gate)
        {
            if (!Owns(game, "chess") || game.Rules.History.Count <= recordedPly
                || game.Rules.History[game.Rules.History.Count - 1] != uci
                || "prnbqkPRNBQK".IndexOf(piece) < 0 || char.IsUpper(piece) == game.Rules.WhiteTurn) return;
            var actor = char.IsUpper(piece) == game.PlayerWhite ? LilithMiniGameActor.Player : LilithMiniGameActor.Lilith;
            if (Append(seq => LilithMiniGameEvent.ChessMoved(seq, uci,
                new LilithMiniGameChessLastMove(actor, piece, victim,
                    victim == '.' ? string.Empty : ChessRules.Square(capturedSquare), game.Rules.InCheck))))
                recordedPly = game.Rules.History.Count;
        }
    }

    internal void RecordChessResult(ChessSession game)
    {
        lock (gate)
        {
            if (!Owns(game, "chess") || resultRecorded || game.Phase != ChessPhase.Finished
                || recordedPly != game.Rules.History.Count) return;
            resultRecorded = Append(seq => LilithMiniGameEvent.Finished(seq,
                game.PlayerOutcome switch { 1 => LilithMiniGameWinner.Player, -1 => LilithMiniGameWinner.Lilith, _ => LilithMiniGameWinner.Draw },
                MiniGamePublicContextProjection.Reason(game.Rules.Result)));
        }
    }

    // Called with an already settled/fresh main-thread snapshot. Repeated calls
    // for this operation retain its exact capture, including while moves occur.
    internal LilithMiniGamePublicContext? Reserve(LilithMiniGamePublicContext? context, Guid operationId)
    {
        lock (gate)
        {
            if (operationId == Guid.Empty) return context;
            if (operation == operationId && leased != null) return leased;
            if (context == null || context.MatchId != match || context.GameKind != kind || exhausted
                || (epoch != Guid.Empty && epoch != context.GameEpoch) || operation != Guid.Empty) return context;
            var events = new List<LilithMiniGameEvent>(MaximumBatchEvents);
            var prior = acknowledged;
            var omitted = pending.Count == 0 ? sequence - prior : pending.Peek().Sequence - prior - 1;
            foreach (var value in pending)
            {
                if (events.Count == MaximumBatchEvents) break;
                events.Add(value);
            }
            var through = events.Count == 0 ? prior + omitted : events[events.Count - 1].Sequence;
            // A lost receipt is not permission to invent another batch ID for
            // the same facts. Native history can deduplicate this frozen retry.
            try
            {
                var batch = retryBatch ?? new LilithMiniGameEventBatch(Guid.NewGuid(), prior, through,
                    checked((int)omitted), checked((int)(sequence - through)), events);
                var captured = context.WithEvents(batch);
                // Enforce the whole carrier byte bound before taking a lease.
                captured.ToJsonElement();
                epoch = context.GameEpoch; operation = operationId; leased = captured; retryBatch = batch;
                return captured;
            }
            catch (ArgumentException) { return context; }
            catch (System.IO.InvalidDataException) { return context; }
        }
    }

    internal bool Acknowledge(Guid operationId, LilithMiniGameEventReceipt receipt)
    {
        lock (gate)
        {
            if (receipt == null || operationId != operation || leased?.Events is not { } batch
                || receipt.GameEpoch != epoch || receipt.MatchId != match || receipt.BatchId != batch.BatchId
                || receipt.ThroughSequence != batch.ThroughSequence) return false;
            acknowledged = batch.ThroughSequence;
            while (pending.Count > 0 && pending.Peek().Sequence <= acknowledged) pending.Dequeue();
            leased = null; retryBatch = null; operation = Guid.Empty;
            return true;
        }
    }

    internal void Release(Guid operationId)
    {
        lock (gate)
        {
            if (operationId != operation) return;
            leased = null; operation = Guid.Empty;
        }
    }

    internal bool TryConsumeCapacityNotice()
    {
        lock (gate)
        {
            var notice = capacityNotice; capacityNotice = false; return notice;
        }
    }

    private static LilithMiniGameActor Actor(OldMaidOwner owner)
        => owner == OldMaidOwner.Player ? LilithMiniGameActor.Player : LilithMiniGameActor.Lilith;
    private static LilithMiniGameOldMaidCard Card(OldMaidCard card)
        => new(OldMaidCardArtPolicy.Rank(card), card.IsJoker ? "none" : card.Id % 2 == 0 ? "hearts" : "spades");
}
