namespace ChainPoker.Domain.Entities;

public sealed class Pot
{
    // Total chips contributed by each player in the *current hand*
    private readonly Dictionary<PlayerId, long> _contrib = new();

    public IReadOnlyDictionary<PlayerId, long> Contributions => _contrib;

    public void Add(PlayerId p, long amount)
        => _contrib[p] = _contrib.TryGetValue(p, out var v) ? v + amount : amount;

    public long Total => _contrib.Values.Sum();

    // Build side pots at showdown based on contribution caps (due to all-ins).
    // Returns pots ordered by ascending cap; each pot has eligible players.
    public IReadOnlyList<(long Amount, HashSet<PlayerId> Eligible)> BuildSidePots(IEnumerable<Player> playersInHand)
    {
        var inHand = playersInHand.Where(p => p.InHand).ToList();

        if (inHand.Count == 0 || _contrib.Count == 0)
            return Array.Empty<(long, HashSet<PlayerId>)>();

        // Distinct contribution levels (caps)
        var caps = _contrib.Values.Where(v => v > 0).Distinct().OrderBy(v => v).ToArray();
        var pots = new List<(long Amount, HashSet<PlayerId> Eligible)>();
        long prev = 0;

        foreach (var cap in caps)
        {
            long slice = 0;
            foreach (var (pid, amt) in _contrib)
                slice += Math.Min(Math.Max(amt - prev, 0), cap - prev);

            var elig = new HashSet<PlayerId>(inHand.Select(p => p.Id));
            pots.Add((slice, elig));
            prev = cap;
        }

        return pots;
    }
}

public sealed class Table
{
    public IReadOnlyList<Player> Players => _players;
    public int SeatCount => _players.Count;
    public int DealerButton { get; private set; } // seat index
    public long SmallBlind { get; }
    public long BigBlind { get; }
    public List<Card> Board { get; } = new(5);

    private readonly List<Player> _players;
    private readonly Deck _deck;

    public Table(IEnumerable<(string name, long chips)> seats, long smallBlind, long bigBlind, int? seed = null)
    {
        _players = seats.Select((s, i) =>
            new Player(PlayerId.New(), s.name, new Seat(i), s.chips)).ToList();

        SmallBlind = smallBlind;
        BigBlind = bigBlind;
        DealerButton = 0;
        _deck = new Deck(seed);
    }

    public void AdvanceButton() => DealerButton = (DealerButton + 1) % SeatCount;

    public Player PlayerAt(int index) => _players[index];

    public int NextOccupiedSeat(int fromExclusive)
    {
        var i = fromExclusive;
        for (int step = 0; step < SeatCount; step++)
        {
            i = (i + 1) % SeatCount;
            if (_players[i].Status != PlayerStatus.SittingOut) return i;
        }

        return -1;
    }

    public void NewDeckAndShuffle()
    {
        Board.Clear();
        _deck.Reset();
        _deck.Shuffle();
    }

    public (Player sb, Player bb) PostBlinds(Pot pot)
    {
        var sbIndex = NextOccupiedSeat(DealerButton);
        var bbIndex = NextOccupiedSeat(sbIndex);

        var sb = PlayerAt(sbIndex);
        var bb = PlayerAt(bbIndex);

        pot.Add(sb.Id, sb.CommitChips(SmallBlind));
        pot.Add(bb.Id, bb.CommitChips(BigBlind));
        return (sb, bb);
    }

    public void DealHoleCards()
    {
        // Two rounds of one card per active player, starting left of dealer
        var start = NextOccupiedSeat(DealerButton);
        for (int round = 0; round < 2; round++)
        {
            var i = start;
            do
            {
                var p = PlayerAt(i);
                if (p.Status != PlayerStatus.SittingOut)
                {
                    var c = _deck.Draw();
                    if (round == 0) p.GiveHoleCards(c, default);
                    else p.GiveHoleCards(p.Hole1!.Value, _deck.Draw());
                }
                i = NextOccupiedSeat(i);
            } while (i != start);
        }
    }

    public void BurnOne() => _ = _deck.Draw();

    public void DealFlop()
    {
        BurnOne();
        Board.AddRange(new[] { _deck.Draw(), _deck.Draw(), _deck.Draw() });
    }

    public void DealTurn()
    {
        BurnOne();
        Board.Add(_deck.Draw());
    }

    public void DealRiver()
    {
        BurnOne();
        Board.Add(_deck.Draw());
    }
}


// Betting state & turn order engine
public sealed class BettingState
{
    public Street Street { get; set; } = Street.Preflop;
    public long CurrentBet { get; set; } = 0;      // highest contribution this street
    public long MinRaise { get; set; }             // typically BigBlind preflop, else last raise size
    public int ToActSeat { get; set; } = -1;       // seat index whose turn it is
    public int LastAggressorSeat { get; set; } = -1;

    // Per-street commitments
    private readonly Dictionary<PlayerId, long> _streetContrib = [];

    public long ContribOf(PlayerId p) => _streetContrib.TryGetValue(p, out var v) ? v : 0;

    public void ClearStreet()
    {
        _streetContrib.Clear();
        CurrentBet = 0;
        LastAggressorSeat = -1;
    }

    public void Post(PlayerId p, long amount)
    {
        _streetContrib[p] = ContribOf(p) + amount;

        if (_streetContrib[p] > CurrentBet)
            CurrentBet = _streetContrib[p];
    }

    public void SetMinRaise(long value) => MinRaise = value;

    public void SetToAct(int seat)
    {
        ToActSeat = seat;
        Street = Street.Preflop;
    }

    public void SetAggressor(int seat) => LastAggressorSeat = seat;

    public void AdvanceStreet() => Street = Street switch
    {
        Street.Preflop => Street.Flop,
        Street.Flop => Street.Turn,
        Street.Turn => Street.River,
        Street.River => Street.Showdown,
        _ => Street.Showdown
    };
}

//Turn manager (processing actions, moving the button/order)
public sealed class HandEngine
{
    private readonly Table _table;
    private readonly Pot _pot = new();

    public BettingState State { get; } = new();

    public HandEngine(Table table) => _table = table;

    public void StartHand()
    {
        _table.NewDeckAndShuffle();
        foreach (var p in _table.Players)
            if (p.Status == PlayerStatus.Folded || p.Status == PlayerStatus.AllIn)
                p.Fold(); // reset folded; (you may keep AllIn until chips refill)

        // Post blinds
        var (sb, bb) = _table.PostBlinds(_pot);
        State.ClearStreet();
        State.SetMinRaise(_table.BigBlind);

        // Deal hole cards
        _table.DealHoleCards();

        // Preflop action starts left of big blind
        var first = _table.NextOccupiedSeat(((IList<Player>)_table.Players).IndexOf(bb));
        State.SetToAct(first);
        //State.Street = Street.Preflop;
        State.CurrentBet = _table.BigBlind;
    }

    public void DealNextStreet()
    {
        State.ClearStreet();
        switch (State.Street)
        {
            case Street.Preflop:
                _table.DealFlop(); break;
            case Street.Flop:
                _table.DealTurn(); break;
            case Street.Turn:
                _table.DealRiver(); break;
        }
        State.AdvanceStreet();

        if (State.Street != Street.Showdown)
        {
            // First to act is left of dealer on postflop
            var first = _table.NextOccupiedSeat(_table.DealerButton);
            State.SetToAct(first);
            State.SetMinRaise(_table.BigBlind);
        }
    }

    // Returns true if the street is complete after processing the action
    public bool Apply(ActionRequest action)
    {
        var actorIndex = State.ToActSeat;
        if (actorIndex < 0) throw new InvalidOperationException("No player to act.");
        var actor = _table.PlayerAt(actorIndex);
        if (actor.Id != action.Player) throw new InvalidOperationException("Out-of-turn action.");

        switch (action.Type)
        {
            case ActionType.Fold:
                actor.Fold();
                break;

            case ActionType.Check:
                if (State.ContribOf(actor.Id) != State.CurrentBet)
                    throw new InvalidOperationException("Cannot check facing a bet.");
                break;

            case ActionType.Call:
                {
                    var need = State.CurrentBet - State.ContribOf(actor.Id);
                    if (need <= 0) throw new InvalidOperationException("Nothing to call.");
                    var paid = actor.CommitChips(need);
                    _pot.Add(actor.Id, paid);
                    State.Post(actor.Id, paid);
                }
                break;

            case ActionType.Bet:
                {
                    if (State.CurrentBet != 0) throw new InvalidOperationException("Bet not allowed when a bet exists. Use Raise.");
                    if (action.Chips < _table.BigBlind) throw new InvalidOperationException("Bet below minimum.");
                    var paid = actor.CommitChips(action.Chips);
                    _pot.Add(actor.Id, paid);
                    State.Post(actor.Id, paid);
                    State.SetMinRaise(paid); // initial bet size defines min raise
                    State.SetAggressor(actorIndex);
                }
                break;

            case ActionType.Raise:
                {
                    if (State.CurrentBet == 0) throw new InvalidOperationException("Nothing to raise. Use Bet.");
                    var needToCall = State.CurrentBet - State.ContribOf(actor.Id);
                    var totalPutIn = needToCall + action.Chips; // raise amount over call
                    if (action.Chips < State.MinRaise && actor.Status != PlayerStatus.AllIn)
                        throw new InvalidOperationException("Raise below minimum.");

                    var paid = actor.CommitChips(totalPutIn);
                    _pot.Add(actor.Id, paid);
                    State.Post(actor.Id, paid);
                    State.SetMinRaise(action.Chips);
                    State.SetAggressor(actorIndex);
                }
                break;

            default:
                throw new ArgumentOutOfRangeException();
        }

        // Check if betting round ends:
        // Street ends when:
        //  - Only one player remains active (others folded) -> hand ends to that player.
        //  - Every in-hand player has matched CurrentBet and no one has pending action since last raise.
        if (OnlyOneActive(out _))
        {
            State.Street = Street.Showdown;
            return true;
        }

        // Move to next to act
        var next = NextToActAfter(actorIndex);
        State.SetToAct(next);

        // If we wrapped around to last aggressor (or nobody bet) and all are matched, street ends
        if (StreetSettled(next))
            return true;

        return false;
    }

    private bool OnlyOneActive(out Player winner)
    {
        var actives = _table.Players.Where(p => p.InHand).ToList();
        winner = actives.Count == 1 ? actives[0] : null!;
        return actives.Count == 1;
    }

    private int NextToActAfter(int seatIdx)
    {
        var i = seatIdx;
        for (int step = 0; step < _table.SeatCount; step++)
        {
            i = _table.NextOccupiedSeat(i);
            var p = _table.PlayerAt(i);
            if (p.InHand && State.ContribOf(p.Id) <= State.CurrentBet)
                return i;
        }
        return -1;
    }

    private bool StreetSettled(int nextToAct)
    {
        // Settled if:
        //  - nextToAct == -1 (nobody needs to act)
        //  - or all in-hand players have Contrib == CurrentBet (or are AllIn)
        if (nextToAct == -1) return true;

        var allMatched = _table.Players
            .Where(p => p.InHand)
            .All(p => p.Status == PlayerStatus.AllIn || State.ContribOf(p.Id) == State.CurrentBet);

        return allMatched;
    }
}

/*
How this flows (per hand)

StartHand()

Shuffle, post blinds, deal hole cards.

Set Street = Preflop, ToActSeat = left of big blind, CurrentBet = BB.

Repeatedly call Apply(ActionRequest) for players in order.

Engine validates and mutates state/pot/chips.

When a street settles, call DealNextStreet() (flop/turn/river) or go to Showdown.

At Showdown, use Pot.BuildSidePots() and your hand evaluator to rank winners per pot.
*/



// Esto ya es más chungo

public enum HandCategory
{
    HighCard = 1,
    OnePair = 2,
    TwoPair = 3,
    ThreeOfAKind = 4,
    Straight = 5,
    Flush = 6,
    FullHouse = 7,
    FourOfAKind = 8,
    StraightFlush = 9
}

public sealed record HandResult(
    HandCategory Category,
    IReadOnlyList<Card> Cards,            // the 5 cards that make up the best hand (descending)
    IReadOnlyList<CardRank> Kickers,      // tie-break ranks used (descending, category-specific)
    ulong Score                           // packed score for quick comparisons (bigger = better)
) : IComparable<HandResult>
{
    public int CompareTo(HandResult? other) => other is null ? 1 : Score.CompareTo(other.Score);
    public override string ToString()
        => $"{Category}: {string.Join(", ", Cards)}";
}

public static class HandEvaluator
{
    /// <summary>
    /// Evaluate best 5-card hand from up to 7 cards (Texas Hold’em).
    /// </summary>
    public static HandResult EvaluateBest(IEnumerable<Card> cards)
    {
        var list = cards.ToList();
        if (list.Count < 5 || list.Count > 7)
            throw new ArgumentException("HandEvaluator expects 5 to 7 cards total.");

        // Histograms
        var counts = new int[15]; // index 2..14 (Ace=14). We'll remap Ace=1 -> 14.
        var suitCount = new Dictionary<Suits, int> { [Suits.Spades] = 0, [Suits.Hearts] = 0, [Suits.Diamonds] = 0, [Suits.Clubs] = 0 };
        ulong rankMask = 0UL;
        var bySuit = new Dictionary<Suits, List<Card>>
        {
            [Suits.Spades] = [],
            [Suits.Hearts] = [],
            [Suits.Diamonds] = [],
            [Suits.Clubs] = []
        };

        foreach (var c in list)
        {
            int r = ToRankValue(c.Rank);       // 2..14
            counts[r]++;
            rankMask |= 1UL << r;
            suitCount[c.Suit]++;
            bySuit[c.Suit].Add(c);
        }

        // Detect flush (any suit 5+)
        Suits? flushSuit = null;
        foreach (var kv in suitCount)
        {
            if (kv.Value >= 5) { flushSuit = kv.Key; break; }
        }

        // Straight detection helpers
        // Ace-low straight: copy Ace bit to 1 position (rank 14 -> 1)
        ulong maskForStraights = rankMask | ((rankMask >> 13) & 1UL); // if 14 set, set bit1
        int straightHigh = FindStraightHigh(maskForStraights); // high rank of straight (5..14), 14 means Broadway A-high

        // Straight flush: if a flush suit exists, compute mask over that suit only
        int straightFlushHigh = -1;
        List<Card>? flushCardsDesc = null;

        if (flushSuit is not null)
        {
            var suited = bySuit[flushSuit.Value].OrderByDescending(x => ToRankValue(x.Rank)).ToList();
            flushCardsDesc = suited;

            ulong suitedMask = 0UL;
            foreach (var c in suited) suitedMask |= 1UL << ToRankValue(c.Rank);
            suitedMask |= ((suitedMask >> 13) & 1UL); // Ace-low support

            straightFlushHigh = FindStraightHigh(suitedMask);
        }

        // Multiples
        var quads = new List<int>();
        var trips = new List<int>();
        var pairs = new List<int>();
        for (int r = 14; r >= 2; r--)
        {
            if (counts[r] == 4) quads.Add(r);
            else if (counts[r] == 3) trips.Add(r);
            else if (counts[r] == 2) pairs.Add(r);
        }

        // 1) Straight Flush
        if (straightFlushHigh >= 5)
        {
            var used = PickStraightCards(flushCardsDesc!, straightFlushHigh);

            return BuildResult(HandCategory.StraightFlush, used, [(CardRank)FromRankValue(straightFlushHigh)]);
        }

        // 2) Four of a Kind
        if (quads.Count > 0)
        {
            int fourRank = quads[0];
            var quadCards = list
                .Where(c => ToRankValue(c.Rank) == fourRank)
                .ToList();

            var kicker = list
                .Where(c => ToRankValue(c.Rank) != fourRank)
                .OrderByDescending(c => ToRankValue(c.Rank))
                .First();

            var used = quadCards
                .Concat([kicker])
                .OrderByDescending(c => ToRankValue(c.Rank))
                .ToList();

            return BuildResult(HandCategory.FourOfAKind, used, [(CardRank)FromRankValue(fourRank), kicker.Rank]);
        }

        // 3) Full House
        if (trips.Count > 0 && (pairs.Count > 0 || trips.Count > 1))
        {
            int topTrip = trips[0];
            int topPair = trips.Count > 1 ? trips[1] : pairs[0];

            var tripCards = list.Where(c => ToRankValue(c.Rank) == topTrip).Take(3);
            var pairCards = list.Where(c => ToRankValue(c.Rank) == topPair).Take(2);
            var used = tripCards.Concat(pairCards).OrderByDescending(c => ToRankValue(c.Rank)).ToList();
            return BuildResult(HandCategory.FullHouse, used,
                new[] { (CardRank)FromRankValue(topTrip), (CardRank)FromRankValue(topPair) });
        }

        // 4) Flush
        if (flushSuit is not null)
        {
            var used = bySuit[flushSuit.Value]
                .OrderByDescending(c => ToRankValue(c.Rank))
                .Take(5)
                .ToList();
            var kickers = used.Select(c => c.Rank).ToArray();
            return BuildResult(HandCategory.Flush, used, kickers);
        }

        // 5) Straight
        if (straightHigh >= 5)
        {
            var used = PickStraightCards(list, straightHigh);
            return BuildResult(HandCategory.Straight, used, new[] { (CardRank)FromRankValue(straightHigh) });
        }

        // 6) Three of a Kind
        if (trips.Count > 0)
        {
            int t = trips[0];
            var tripCards = list.Where(c => ToRankValue(c.Rank) == t).Take(3);
            var kickers = list.Where(c => ToRankValue(c.Rank) != t)
                              .OrderByDescending(c => ToRankValue(c.Rank))
                              .Take(2)
                              .ToList();
            var used = tripCards.Concat(kickers).OrderByDescending(c => ToRankValue(c.Rank)).ToList();
            var tie = new[] { (CardRank)FromRankValue(t) }
                      .Concat(kickers.Select(k => k.Rank)).ToArray();
            return BuildResult(HandCategory.ThreeOfAKind, used, tie);
        }

        // 7) Two Pair
        if (pairs.Count >= 2)
        {
            int p1 = pairs[0], p2 = pairs[1];
            var pair1 = list.Where(c => ToRankValue(c.Rank) == p1).Take(2);
            var pair2 = list.Where(c => ToRankValue(c.Rank) == p2).Take(2);
            var kicker = list.Where(c => ToRankValue(c.Rank) != p1 && ToRankValue(c.Rank) != p2)
                             .OrderByDescending(c => ToRankValue(c.Rank))
                             .First();
            var used = pair1.Concat(pair2).Concat(new[] { kicker })
                            .OrderByDescending(c => ToRankValue(c.Rank)).ToList();
            var tie = new[] { (CardRank)FromRankValue(p1), (CardRank)FromRankValue(p2), kicker.Rank };
            return BuildResult(HandCategory.TwoPair, used, tie);
        }

        // 8) One Pair
        if (pairs.Count == 1)
        {
            int p = pairs[0];
            var pair = list.Where(c => ToRankValue(c.Rank) == p).Take(2);
            var kickers = list.Where(c => ToRankValue(c.Rank) != p)
                              .OrderByDescending(c => ToRankValue(c.Rank))
                              .Take(3)
                              .ToList();
            var used = pair.Concat(kickers).OrderByDescending(c => ToRankValue(c.Rank)).ToList();
            var tie = new[] { (CardRank)FromRankValue(p) }.Concat(kickers.Select(k => k.Rank)).ToArray();
            return BuildResult(HandCategory.OnePair, used, tie);
        }

        // 9) High Card
        {
            var used = list.OrderByDescending(c => ToRankValue(c.Rank)).Take(5).ToList();
            var tie = used.Select(c => c.Rank).ToArray();
            return BuildResult(HandCategory.HighCard, used, tie);
        }
    }

    // ----- helpers -----

    // Convert your CardRank (Ace=1) to 2..14 with Ace high=14.
    private static int ToRankValue(CardRank r)
    {
        int v = (int)r;
        return v == 1 ? 14 : v; // Ace -> 14
    }
    private static int FromRankValue(int v) => v == 14 ? 1 : v;

    // Returns high card of any straight present in mask (5..14), or -1 if none.
    private static int FindStraightHigh(ulong mask)
    {
        // look for sequences of 5 bits set among ranks 1..14 (we stored Ace also at bit1)
        // Check from Ace-high (14) down to 5-high
        for (int high = 14; high >= 5; high--)
        {
            int low = high - 4;
            ulong window = 0UL;
            for (int r = low; r <= high; r++)
                window |= 1UL << r;

            if ((mask & window) == window)
                return high;
        }
        return -1;
    }

    // Pick the actual 5 cards that make the straight ending at 'high'
    private static List<Card> PickStraightCards(IEnumerable<Card> pool, int high)
    {
        int low = high - 4;
        // Handle wheel: if high == 5 straight, ranks are A(14),2,3,4,5
        var need = new HashSet<int>(Enumerable.Range(low, 5));
        if (high == 5) { need.Remove(1); need.Add(14); } // ensure Ace as 14 present

        var used = new List<Card>(5);
        // pick the highest instance per rank
        for (int r = high; r >= low; r--)
        {
            int want = r == 1 ? 14 : r; // not used, but keep consistent
            int rr = r == 1 ? 14 : r;
            if (!need.Contains(rr)) continue;

            var chosen = pool.Where(c => ToRankValue(c.Rank) == rr)
                             .OrderByDescending(c => ToRankValue(c.Rank))
                             .FirstOrDefault();
            if (!chosen.Equals(default(Card)))
            {
                used.Add(chosen);
                need.Remove(rr);
            }
        }

        // If it’s the wheel, make sure Ace included
        if (high == 5 && !used.Any(c => ToRankValue(c.Rank) == 14))
        {
            var ace = pool.Where(c => ToRankValue(c.Rank) == 14)
                          .OrderByDescending(c => ToRankValue(c.Rank)).First();
            used.Add(ace);
        }

        // Ensure size 5; if duplicates or ordering issues, fill by searching remaining
        if (used.Count != 5)
        {
            // Fallback robust fill (shouldn't happen, but safe)
            var targetRanks = new HashSet<int>(Enumerable.Range(low, 5));
            if (high == 5) { targetRanks.Remove(1); targetRanks.Add(14); }
            used = pool.Where(c => targetRanks.Contains(ToRankValue(c.Rank)))
                       .GroupBy(c => ToRankValue(c.Rank))
                       .Select(g => g.OrderByDescending(x => ToRankValue(x.Rank)).First())
                       .OrderByDescending(x => ToRankValue(x.Rank))
                       .Take(5).ToList();
        }

        return used.OrderByDescending(c => ToRankValue(c.Rank)).ToList();
    }

    // Builds a sortable score: [category:4 bits][k1..k7 each 4 bits], high to low.
    private static HandResult BuildResult(HandCategory cat, List<Card> used, IReadOnlyList<CardRank> tiebreakRanks)
    {
        // Normalize kickers length to 7 nibbles (pad with zeros). Bigger first.
        var kicks = tiebreakRanks.Select(r => ToRankValue(r)).ToList();
        while (kicks.Count < 7) kicks.Add(0);
        ulong score = 0UL;
        // category in top bits
        score |= (ulong)((int)cat & 0xF);
        // shift & pack 4-bit ranks (k1..k7)
        foreach (var k in kicks)
        {
            score <<= 4;
            score |= (ulong)(k & 0xF);
        }
        // Make category dominant by shifting all kickers once more to give it the highest place value
        score <<= 4;

        var sortedUsed = used.OrderByDescending(c => ToRankValue(c.Rank)).ToList();
        var KickersRanks = tiebreakRanks.ToList().AsReadOnly();
        return new HandResult(cat, sortedUsed, KickersRanks, score);
    }
}




// How it works

/* 
 
 
// Given your types:
var hole = new[]
{
    MyCard.Create(CardRank.Ace, Suits.Spades),
    MyCard.Create(CardRank.King, Suits.Spades)
};

var board = new[]
{
    MyCard.Create(CardRank.Queen, Suits.Spades),
    MyCard.Create(CardRank.Jack, Suits.Spades),
    MyCard.Create(CardRank.Ten, Suits.Spades),
    MyCard.Create(CardRank.Four, Suits.Diamonds),
    MyCard.Create(CardRank.Two, Suits.Clubs)
};

var best = HandEvaluator.EvaluateBest(hole.Concat(board));
// best.Category == HandCategory.StraightFlush (Royal Flush)
// best.Cards contains the 5 used cards, best.Score is comparable
Console.WriteLine(best);

















On-chain: Solidity (L2 like Base/Arbitrum).

Off-chain engine (C#/.NET 8):
    - SignalR for the real-time room (low friction vs raw WebSockets).
    - Nethereum for Ethereum signing, abi/contract calls, keccak.
    - BouncyCastle for crypto helpers (optional).
    - Minimal API + Hosted Services for watchtower/timeout tasks.
    - Orleans


dotnet add package Microsoft.AspNetCore.SignalR
dotnet add package Microsoft.AspNetCore.SignalR.Client
dotnet add package Nethereum.Web3
dotnet add package Nethereum.Signer
dotnet add package Nethereum.Util
dotnet add package BouncyCastle.Cryptography
dotnet add package StackExchange.Redis        // optional

 */
