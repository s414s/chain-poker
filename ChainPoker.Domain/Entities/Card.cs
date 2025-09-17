namespace ChainPoker.Domain.Entities;

public readonly struct Card
{
    public CardRank Rank { get; }
    public Suits Suit { get; }

    public Card(CardRank rank, Suits suit)
    {
        //if (number is < 1 or > 13)
        //    throw new ArgumentOutOfRangeException(nameof(number), "Card number must be between 1 (Ace) and 13 (King).");

        if (!Enum.IsDefined(typeof(Card), suit))
            throw new ArgumentException("Invalid suit value.", nameof(suit));

        if (!Enum.IsDefined(typeof(CardRank), rank))
            throw new ArgumentException("Invalid suit value.", nameof(suit));

        Rank = rank;
        Suit = suit;
    }

    public static Card Create(CardRank rank, Suits suit)
    {
        // Guards catch invalid enum casts coming from the outside world.
        if (!Enum.IsDefined(typeof(CardRank), rank))
            throw new ArgumentOutOfRangeException(nameof(rank));

        if (!Enum.IsDefined(typeof(Card), suit))
            throw new ArgumentOutOfRangeException(nameof(suit));

        return new Card(rank, suit);
    }

    public static bool IsValid(CardRank rank, Card suit)
    {
        return Enum.IsDefined(typeof(CardRank), rank)
            && Enum.IsDefined(typeof(Card), suit);
    }

    /// <summary>
    /// Safe creation from numeric input (e.g., parsing). No exceptions, no invalid cards.
    /// </summary>
    public static bool TryCreate(int rankValue, Suits suit, out Card card)
    {
        card = default;

        if (!Enum.IsDefined(typeof(Card), suit))
            return false;

        if (!Enum.IsDefined(typeof(CardRank), rankValue))
            return false;

        card = new Card((CardRank)rankValue, suit);

        return true;
    }

    public override string ToString() => $"{Rank} of {Suit}";
}


public enum Suits
{
    Hearts,
    Diamonds,
    Clubs,
    Spades,
}


public enum CardRank
{
    Ace = 1,
    Two,
    Three,
    Four,
    Five,
    Six,
    Seven,
    Eight,
    Nine,
    Ten,
    Jack,
    Queen,
    King
}
