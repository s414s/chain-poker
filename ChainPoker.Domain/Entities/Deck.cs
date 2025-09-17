namespace ChainPoker.Domain.Entities;

public class Deck
{
    private Stack<Card> _cards;
    private readonly Random _rng;

    public int Count => _cards.Count;

    public Deck(int? seed = null)
    {
        _rng = seed.HasValue ? new Random(seed.Value) : new Random();
        _cards = new Stack<Card>();
        Reset();
    }

    private static List<Card> GenerateFullDeck()
    {
        var suits = Enum.GetValues<Suits>();
        var ranks = Enum.GetValues<CardRank>();
        var deck = new List<Card>(52);

        foreach (var suit in suits)
            foreach (var rank in ranks)
                deck.Add(Card.Create(rank, suit));

        return deck;
    }

    public void Shuffle()
    {
        var list = _cards.ToList();

        //Random.Shared.Shuffle();

        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = _rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }

        _cards = new Stack<Card>(capacity: 52);

        //_cards = new Stack<Card>(list); // top of stack = first dealt
        foreach (var card in list)
        {
            _cards.Push(card);
        }
    }

    public Card Draw()
    {
        if (_cards.Count == 0)
            throw new InvalidOperationException("No cards left in the deck.");

        return _cards.Pop();
    }

    public IEnumerable<Card> Draw(int count)
    {
        if (count > _cards.Count)
            throw new InvalidOperationException("Not enough cards left to draw.");

        for (int i = 0; i < count; i++)
            yield return Draw();
    }

    public void Reset()
    {
        var fresh = GenerateFullDeck();
        _cards = new Stack<Card>(fresh);
    }
}

