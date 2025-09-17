namespace ChainPoker.Domain.Entities;

public readonly record struct PlayerId(Guid Value)
{
    public static PlayerId New() => new(Guid.NewGuid());
}

public readonly record struct Seat(int Index); // 0..N-1 clockwise

public readonly record struct ActionRequest(
    PlayerId Player,
    ActionType Type,
    long Chips // for Bet/Raise amount (big blind units or smallest chip unit)
);

public sealed class Player
{
    public PlayerId Id { get; }
    public string Name { get; }
    public Seat Seat { get; }
    public long Chips { get; private set; }
    public PlayerStatus Status { get; private set; } = PlayerStatus.Active;
    public Card? Hole1 { get; private set; }
    public Card? Hole2 { get; private set; }

    public bool InHand => Status is PlayerStatus.Active or PlayerStatus.AllIn;
    public bool HasCards => Hole1.HasValue && Hole2.HasValue;

    public Player(PlayerId id, string name, Seat seat, long chips)
        => (Id, Name, Seat, Chips) = (id, name, seat, chips);

    public void GiveHoleCards(Card c1, Card c2)
    {
        Hole1 = c1;
        Hole2 = c2;
        Status = PlayerStatus.Active;
    }

    public void Fold() => Status = PlayerStatus.Folded;

    public long CommitChips(long amount)
    {
        var commit = Math.Min(amount, Chips);
        Chips -= commit;
        if (Chips == 0 && Status == PlayerStatus.Active) Status = PlayerStatus.AllIn;
        return commit;
    }

    public override string ToString() => $"{Name}({Seat.Index}) Chips:{Chips} Status:{Status}";
}

public enum PlayerStatus { Active, Folded, AllIn, SittingOut }
public enum Street { Preflop, Flop, Turn, River, Showdown }
public enum ActionType { Check, Call, Bet, Raise, Fold }
