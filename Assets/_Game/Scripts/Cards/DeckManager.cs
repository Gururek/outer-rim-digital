// DeckManager.cs — V2: 6-deck market (Bounty, Cargo, GearAndMod, Job, Luxury, Ship)
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

namespace OuterRim
{
    [System.Serializable]
    public struct MarketRowEntry : INetworkSerializable
    {
        public MarketDeckType DeckType;
        public int[] CardIds;
        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            int dt = (int)DeckType; s.SerializeValue(ref dt); DeckType = (MarketDeckType)dt;
            int len = CardIds?.Length ?? 0; s.SerializeValue(ref len);
            if (s.IsReader) CardIds = new int[len];
            for (int i = 0; i < len; i++) s.SerializeValue(ref CardIds[i]);
        }
    }

    public class DeckManager : NetworkBehaviour
    {
        public static DeckManager Instance { get; private set; }

        [Header("Card Data — ScriptableObjects")]
        [SerializeField] private List<BountyCardData> bountyCards;
        [SerializeField] private List<CargoCardData> cargoCards;
        [SerializeField] private List<CardData> gearAndModCards; // V2: combined
        [SerializeField] private List<JobCardData> jobCards;
        [SerializeField] private List<LuxuryCardData> luxuryCards;
        [SerializeField] private List<ShipCardData> shipCards; // V2: new
        [SerializeField] private List<EncounterCardData> encounterCards;

        [Header("Settings")]
        [SerializeField] private int marketRowSize = 3;
        [SerializeField] private int cycleCost = 200;

        private Dictionary<MarketDeckType, MarketDeck> marketDecks;
        private Dictionary<string, Queue<EncounterCardData>> planetEncounterDecks;

        private void Awake() { if (Instance == null) Instance = this; else Destroy(gameObject); }

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;
            InitializeDecks();
        }

        private void InitializeDecks()
        {
            marketDecks = new()
            {
                [MarketDeckType.Bounty]     = CreateDeck(MarketDeckType.Bounty, bountyCards.Cast<CardData>().ToList()),
                [MarketDeckType.Cargo]      = CreateDeck(MarketDeckType.Cargo, cargoCards.Cast<CardData>().ToList()),
                [MarketDeckType.GearAndMod] = CreateDeck(MarketDeckType.GearAndMod, gearAndModCards),
                [MarketDeckType.Job]        = CreateDeck(MarketDeckType.Job, jobCards.Cast<CardData>().ToList()),
                [MarketDeckType.Luxury]     = CreateDeck(MarketDeckType.Luxury, luxuryCards.Cast<CardData>().ToList()),
                [MarketDeckType.Ship]       = CreateDeck(MarketDeckType.Ship, shipCards.Cast<CardData>().ToList()),
            };

            planetEncounterDecks = new();
            foreach (var g in encounterCards.Where(c => c != null && !string.IsNullOrEmpty(c.PlanetId)).GroupBy(c => c.PlanetId))
            {
                var list = g.ToList(); Shuffle(list);
                planetEncounterDecks[g.Key] = new Queue<EncounterCardData>(list);
            }

            Debug.Log("[DeckManager] V2: 6 market decks initialized.");
        }

        private MarketDeck CreateDeck(MarketDeckType type, List<CardData> cards)
        {
            var d = new MarketDeck(type, marketRowSize);
            d.Initialize(cards);
            return d;
        }

        public MarketDeck GetDeck(MarketDeckType t) => marketDecks.TryGetValue(t, out var d) ? d : null;

        public CardData TryPurchaseCard(PlayerState buyer, MarketDeckType deckType, int rowIndex)
        {
            if (!IsServer || !marketDecks.TryGetValue(deckType, out var deck)) return null;
            var card = deck.MarketRow.ElementAtOrDefault(rowIndex);
            if (card == null || !buyer.SpendCredits(card.BuyCost)) return null;
            deck.PurchaseFromMarket(rowIndex);
            return card;
        }

        public CardData TryCycleCard(PlayerState player, MarketDeckType deckType, int rowIndex)
        {
            if (!IsServer || !player.SpendCredits(cycleCost)) return null;
            if (!marketDecks.TryGetValue(deckType, out var deck)) return null;
            return deck.CycleMarketCard(rowIndex);
        }

        public EncounterCardData DrawPlanetEncounterCard(string planetId)
        {
            if (!IsServer || !planetEncounterDecks.TryGetValue(planetId, out var q)) return null;
            if (q.Count == 0)
            {
                var all = encounterCards.Where(c => c.PlanetId == planetId).ToList();
                Shuffle(all);
                foreach (var c in all) q.Enqueue(c);
            }
            return q.Count > 0 ? q.Dequeue() : null;
        }

        private void Shuffle<T>(List<T> l)
        { for (int i = l.Count - 1; i > 0; i--) { int j = Random.Range(0, i + 1); (l[i], l[j]) = (l[j], l[i]); } }
    }
}
