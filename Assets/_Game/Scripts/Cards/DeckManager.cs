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

        // Client-visible market state: per deck, array of card IDs in market row
        private NetworkList<MarketRowEntry> clientMarketRows;

        private void Awake()
        {
            if (Instance == null) Instance = this; else { Destroy(gameObject); return; }
            clientMarketRows = new NetworkList<MarketRowEntry>();
        }

        public override void OnNetworkSpawn()
        {
            clientMarketRows.OnListChanged += OnClientMarketRowsChanged;
            if (!IsServer) return;
            InitializeDecks();
            SyncAllMarketRowsToClients();
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
            SyncDeckRowToClients(deckType, deck);
            return card;
        }

        public CardData TryCycleCard(PlayerState player, MarketDeckType deckType, int rowIndex)
        {
            if (!IsServer || !player.SpendCredits(cycleCost)) return null;
            if (!marketDecks.TryGetValue(deckType, out var deck)) return null;
            var result = deck.CycleMarketCard(rowIndex);
            SyncDeckRowToClients(deckType, deck);
            return result;
        }

        // ─── Client-visible market state ────────────────────────────────────

        /// <summary>Syncs one deck's market row to all clients via NetworkList.</summary>
        private void SyncDeckRowToClients(MarketDeckType deckType, MarketDeck deck)
        {
            if (!IsServer) return;
            // Remove old entry for this deck type, then add new one
            for (int i = clientMarketRows.Count - 1; i >= 0; i--)
                if (clientMarketRows[i].DeckType == deckType)
                    clientMarketRows.RemoveAt(i);
            var entry = new MarketRowEntry
            {
                DeckType = deckType,
                CardIds = deck.MarketRow.Select(c => c.CardId).ToArray()
            };
            clientMarketRows.Add(entry);
        }

        private void SyncAllMarketRowsToClients()
        {
            if (!IsServer) return;
            clientMarketRows.Clear();
            foreach (var kvp in marketDecks)
            {
                var entry = new MarketRowEntry
                {
                    DeckType = kvp.Key,
                    CardIds = kvp.Value.MarketRow.Select(c => c.CardId).ToArray()
                };
                clientMarketRows.Add(entry);
            }
        }

        /// <summary>Client-side: get the synced card IDs for a deck's market row.</summary>
        public int[] GetClientMarketRow(MarketDeckType deckType)
        {
            foreach (var entry in clientMarketRows)
                if (entry.DeckType == deckType)
                    return entry.CardIds;
            return new int[0];
        }

        private void OnClientMarketRowsChanged(NetworkListEvent<MarketRowEntry> changeEvent)
        {
            // UI can react to market row changes here — GameUIManager polls via GetClientMarketRow
        }

        public event System.Action OnMarketRowChanged;

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
