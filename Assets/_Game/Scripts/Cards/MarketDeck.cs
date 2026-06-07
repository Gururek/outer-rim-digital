// MarketDeck.cs — Plain C# class (not MonoBehaviour). One instance per market deck type.
// Managed entirely by DeckManager on the server.
using System.Collections.Generic;
using UnityEngine;

namespace OuterRim
{
    public class MarketDeck
    {
        public MarketDeckType DeckType      { get; private set; }
        public int            MarketRowSize { get; private set; }

        private List<CardData> drawPile    = new List<CardData>();
        private List<CardData> discardPile = new List<CardData>();
        private List<CardData> marketRow   = new List<CardData>();

        public int                    DrawCount    => drawPile.Count;
        public int                    DiscardCount => discardPile.Count;
        public IReadOnlyList<CardData> MarketRow   => marketRow.AsReadOnly();

        public MarketDeck(MarketDeckType type, int rowSize = 3)
        {
            DeckType      = type;
            MarketRowSize = rowSize;
        }

        // ─── Initialization ─────────────────────────────────────────────────

        public void Initialize(List<CardData> cards)
        {
            drawPile = new List<CardData>(cards);
            Shuffle();
            RefillMarketRow();
        }

        private void Shuffle()
        {
            for (int i = drawPile.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (drawPile[i], drawPile[j]) = (drawPile[j], drawPile[i]);
            }
        }

        // ─── Draw ───────────────────────────────────────────────────────────

        public CardData DrawCard()
        {
            if (drawPile.Count == 0)
            {
                if (discardPile.Count == 0) return null;
                drawPile = new List<CardData>(discardPile);
                discardPile.Clear();
                Shuffle();
                Debug.Log($"[MarketDeck:{DeckType}] Reshuffled discard into draw pile.");
            }

            var card = drawPile[0];
            drawPile.RemoveAt(0);
            return card;
        }

        public void Discard(CardData card)
        {
            if (card != null) discardPile.Add(card);
        }

        // ─── Market Row Operations ──────────────────────────────────────────

        public CardData PurchaseFromMarket(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= marketRow.Count) return null;
            var card = marketRow[rowIndex];
            marketRow.RemoveAt(rowIndex);
            RefillMarketRow();
            return card;
        }

        public CardData CycleMarketCard(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= marketRow.Count) return null;
            var old = marketRow[rowIndex];
            Discard(old);
            var newCard = DrawCard();
            if (newCard != null) marketRow[rowIndex] = newCard;
            else marketRow.RemoveAt(rowIndex);
            return newCard;
        }

        private void RefillMarketRow()
        {
            while (marketRow.Count < MarketRowSize)
            {
                var card = DrawCard();
                if (card == null) break;
                marketRow.Add(card);
            }
        }
    }
}
