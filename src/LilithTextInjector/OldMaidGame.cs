using System;
using System.Collections.Generic;

namespace LilithTextInjector;

internal enum OldMaidPhase { PlayerTurn, OpponentTurn, Finished }
internal enum OldMaidOwner { Player, Opponent }
internal enum OldMaidWinner { None, Player, Opponent }

internal readonly struct OldMaidCard
{
    internal OldMaidCard(int id)
    {
        if (id < 0 || id >= OldMaidGame.DeckSize)
            throw new ArgumentOutOfRangeException(nameof(id));
        Id = id;
    }

    internal int Id { get; }
    internal bool IsJoker => Id == OldMaidGame.DeckSize - 1;
    internal int Rank => IsJoker ? 0 : Id / 2 + 1;
}

internal readonly struct OldMaidPair
{
    internal OldMaidPair(OldMaidOwner owner, OldMaidCard first, OldMaidCard second)
    {
        Owner = owner;
        First = first;
        Second = second;
    }

    internal OldMaidOwner Owner { get; }
    internal OldMaidCard First { get; }
    internal OldMaidCard Second { get; }
}

// Local, main-thread round state. No model, native character state, persistence,
// timers or rendering participates in the rules. Animation consumes pair events
// after the logical move has committed. The presenter may pace the next input,
// but no animation callback is required to commit or recover this rules state.
internal sealed class OldMaidGame
{
    internal const int PairCount = 13;
    internal const int DeckSize = PairCount * 2 + 1;

    private readonly List<OldMaidCard> _player = new List<OldMaidCard>(DeckSize);
    private readonly List<OldMaidCard> _opponent = new List<OldMaidCard>(DeckSize);
    private readonly List<OldMaidPair> _pendingPairs = new List<OldMaidPair>(PairCount);
    private Random? _random;
    private int _reservedOpponentSlot = -1;

    internal OldMaidGame()
    {
        PlayerHand = _player.AsReadOnly();
        OpponentHand = _opponent.AsReadOnly();
    }

    internal IReadOnlyList<OldMaidCard> PlayerHand { get; }
    internal IReadOnlyList<OldMaidCard> OpponentHand { get; }
    internal OldMaidPhase Phase { get; private set; } = OldMaidPhase.Finished;
    internal OldMaidWinner Winner { get; private set; }
    internal bool IsStarted { get; private set; }
    internal bool IsPaused { get; private set; }
    internal int Revision { get; private set; }
    internal int DiscardedPairCount { get; private set; }
    internal int DrawCount { get; private set; }
    internal bool HasReservedOpponentDraw => _reservedOpponentSlot >= 0;

    internal void Start(Random random)
    {
        if (random == null) throw new ArgumentNullException(nameof(random));

        // Construct the complete new deal before replacing an existing round.
        var deck = new OldMaidCard[DeckSize];
        for (var i = 0; i < deck.Length; i++) deck[i] = new OldMaidCard(i);
        for (var i = deck.Length - 1; i > 0; i--)
        {
            var selected = random.Next(i + 1);
            var card = deck[i];
            deck[i] = deck[selected];
            deck[selected] = card;
        }
        var playerStarts = random.Next(2) == 0;

        _random = random;
        _reservedOpponentSlot = -1;
        _player.Clear();
        _opponent.Clear();
        _pendingPairs.Clear();
        DiscardedPairCount = 0;
        DrawCount = 0;
        Winner = OldMaidWinner.None;
        IsStarted = true;
        IsPaused = false;
        for (var i = 0; i < deck.Length; i++)
        {
            // Alternate the odd card with the randomly selected opening player.
            var belongsToPlayer = (i % 2 == 0) == playerStarts;
            (belongsToPlayer ? _player : _opponent).Add(deck[i]);
        }
        RemovePairs(_player, OldMaidOwner.Player);
        RemovePairs(_opponent, OldMaidOwner.Opponent);
        Phase = playerStarts ? OldMaidPhase.PlayerTurn : OldMaidPhase.OpponentTurn;
        FinishIfEmpty();
        Revision++;
    }

    internal bool TryPlayerDraw(int slot, int expectedRevision)
    {
        if (!IsStarted || IsPaused || Phase != OldMaidPhase.PlayerTurn ||
            expectedRevision != Revision || slot < 0 || slot >= _opponent.Count)
            return false;
        CommitDraw(_opponent, _player, slot, OldMaidOwner.Player);
        return true;
    }

    internal bool TryOpponentDraw()
    {
        return TryPrepareOpponentDraw(out var slot, out var revision) && TryOpponentDraw(slot, revision);
    }

    // Selection is reserved before its decorative hesitation begins. Repeated
    // reads (including a pause/resume) cannot reroll after seeing a card.
    internal bool TryPrepareOpponentDraw(out int slot, out int expectedRevision)
    {
        slot = -1;
        expectedRevision = -1;
        if (!IsStarted || IsPaused || Phase != OldMaidPhase.OpponentTurn ||
            _random == null || _player.Count == 0)
            return false;

        // Deliberately choose a slot using ONLY the number of hidden cards.
        // The opponent has no rank/Joker-aware strategy or redraw opportunity.
        if (_reservedOpponentSlot < 0) _reservedOpponentSlot = _random.Next(_player.Count);
        slot = _reservedOpponentSlot;
        expectedRevision = Revision;
        return true;
    }

    internal bool TryOpponentDraw(int slot, int expectedRevision)
    {
        if (!IsStarted || IsPaused || Phase != OldMaidPhase.OpponentTurn ||
            expectedRevision != Revision || slot != _reservedOpponentSlot || slot < 0 || slot >= _player.Count)
            return false;
        CommitDraw(_player, _opponent, slot, OldMaidOwner.Opponent);
        return true;
    }

    // A reorder is only a permutation of an existing own hand. The presenter
    // separately excludes flight/reveal/selection and owns its decorative clock.
    // Never move a reserved hand or clear the reservation to permit a redraw.
    internal bool TryReorderHand(OldMaidOwner owner, int cardId, int targetSlot,
        int expectedCount, int expectedRevision)
    {
        if (!IsStarted || IsPaused || Phase == OldMaidPhase.Finished ||
            HasReservedOpponentDraw || expectedRevision != Revision ||
            (owner != OldMaidOwner.Player && owner != OldMaidOwner.Opponent))
            return false;

        var hand = owner == OldMaidOwner.Player ? _player : _opponent;
        if (expectedCount != hand.Count || targetSlot < 0 || targetSlot >= hand.Count)
            return false;
        var sourceSlot = -1;
        for (var i = 0; i < hand.Count; i++)
            if (hand[i].Id == cardId) { sourceSlot = i; break; }
        if (sourceSlot < 0 || sourceSlot == targetSlot) return false;

        var card = hand[sourceSlot];
        hand.RemoveAt(sourceSlot);
        hand.Insert(targetSlot, card);
        Revision++;
        return true;
    }

    internal void Pause()
    {
        if (!IsStarted || IsPaused) return;
        IsPaused = true;
        Revision++;
    }

    internal void Resume()
    {
        if (!IsStarted || !IsPaused) return;
        IsPaused = false;
        Revision++;
    }

    internal IReadOnlyList<OldMaidPair> DrainPairs()
    {
        if (_pendingPairs.Count == 0) return Array.Empty<OldMaidPair>();
        var pairs = _pendingPairs.ToArray();
        _pendingPairs.Clear();
        return pairs;
    }

    private void CommitDraw(List<OldMaidCard> from, List<OldMaidCard> to,
        int slot, OldMaidOwner owner)
    {
        _reservedOpponentSlot = -1;
        var card = from[slot];
        from.RemoveAt(slot);
        to.Add(card);
        RemovePairs(to, owner);
        DrawCount++;
        Phase = owner == OldMaidOwner.Player ? OldMaidPhase.OpponentTurn : OldMaidPhase.PlayerTurn;
        FinishIfEmpty();
        Revision++;
    }

    private void RemovePairs(List<OldMaidCard> hand, OldMaidOwner owner)
    {
        for (var first = hand.Count - 1; first >= 0; first--)
        {
            if (hand[first].IsJoker) continue;
            for (var second = first - 1; second >= 0; second--)
            {
                if (hand[second].Rank != hand[first].Rank) continue;
                _pendingPairs.Add(new OldMaidPair(owner, hand[second], hand[first]));
                DiscardedPairCount++;
                hand.RemoveAt(first);
                hand.RemoveAt(second);
                first--;
                break;
            }
        }
    }

    private void FinishIfEmpty()
    {
        if (_player.Count != 0 && _opponent.Count != 0) return;
        Phase = OldMaidPhase.Finished;
        Winner = _player.Count == 0 ? OldMaidWinner.Player : OldMaidWinner.Opponent;
    }
}
