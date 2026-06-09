// DeckManager.cs — V2: 6-deck market (Bounty, Cargo, GearAndMod, Job, Luxury, Ship)
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

namespace OuterRim
{
    public class DeckManager : NetworkBehaviour
    {
        public static DeckManager Instance { get; private set; }

        [Header("Card Data — ScriptableObjects")]
        [SerializeField] private List<BountyCardData> bountyCards;
        [SerializeField] private List<CargoCardData> cargoCards;
        [SerializeField] private List<CardData> gearAndModCards;
        [SerializeField] private List<JobCardData> jobCards;
        [SerializeField] private List<LuxuryCardData> luxuryCards;
        [SerializeField] private List<ShipCardData> shipCards;
        [SerializeField] private List<EncounterCardData> encounterCards;

        [Header("Settings")]
        [SerializeField] private int marketRowSize = 3;
        [SerializeField] private int cycleCost = 200;

        private Dictionary<MarketDeckType, MarketDeck> marketDecks;
        private Dictionary<string, Queue<EncounterCardData>> planetEncounterDecks;

        // Client-visible market state: one NetworkList<int> per deck (card IDs in market row)
        private Dictionary<MarketDeckType, NetworkList<int>> clientMarketRows;

        private void Awake()
        {
            if (Instance == null) Instance = this; else { Destroy(gameObject); return; }
            clientMarketRows = new();
            clientMarketRows[MarketDeckType.Bounty]     = new NetworkList<int>();
            clientMarketRows[MarketDeckType.Cargo]      = new NetworkList<int>();
            clientMarketRows[MarketDeckType.GearAndMod] = new NetworkList<int>();
            clientMarketRows[MarketDeckType.Job]        = new NetworkList<int>();
            clientMarketRows[MarketDeckType.Luxury]     = new NetworkList<int>();
            clientMarketRows[MarketDeckType.Ship]       = new NetworkList<int>();
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;
            InitializeDecks();
            SyncAllMarketRowsToClients();
        }

        private void InitializeDecks()
        {
            // Fallback: if SO lists are empty, use stub data for development
            var bountyList   = bountyCards != null && bountyCards.Count > 0   ? bountyCards.Cast<CardData>().ToList() : StubCardDatabase.GetBountyCards();
            var cargoList    = cargoCards != null && cargoCards.Count > 0    ? cargoCards.Cast<CardData>().ToList() : StubCardDatabase.GetCargoCards();
            var gearModList  = gearAndModCards != null && gearAndModCards.Count > 0 ? gearAndModCards : StubCardDatabase.GetGearCards();
            var jobList      = jobCards != null && jobCards.Count > 0       ? jobCards.Cast<CardData>().ToList() : StubCardDatabase.GetJobCards();
            var luxuryList   = luxuryCards != null && luxuryCards.Count > 0  ? luxuryCards.Cast<CardData>().ToList() : StubCardDatabase.GetLuxuryCards();
            var shipList     = shipCards != null && shipCards.Count > 0     ? shipCards.Cast<CardData>().ToList() : new List<CardData>();

            marketDecks = new()
            {
                [MarketDeckType.Bounty]     = CreateDeck(MarketDeckType.Bounty, bountyList),
                [MarketDeckType.Cargo]      = CreateDeck(MarketDeckType.Cargo, cargoList),
                [MarketDeckType.GearAndMod] = CreateDeck(MarketDeckType.GearAndMod, gearModList),
                [MarketDeckType.Job]        = CreateDeck(MarketDeckType.Job, jobList),
                [MarketDeckType.Luxury]     = CreateDeck(MarketDeckType.Luxury, luxuryList),
                [MarketDeckType.Ship]       = CreateDeck(MarketDeckType.Ship, shipList),
            };

            planetEncounterDecks = new();
            if (encounterCards != null && encounterCards.Count > 0)
            {
                foreach (var g in encounterCards.Where(c => c != null && !string.IsNullOrEmpty(c.PlanetId)).GroupBy(c => c.PlanetId))
                {
                    var list = g.ToList(); Shuffle(list);
                    planetEncounterDecks[g.Key] = new Queue<EncounterCardData>(list);
                }
            }
            else
            {
                // Fallback: use stub encounter cards grouped by planet
                var stubs = StubCardDatabase.GetStubEncounters();
                foreach (var g in stubs.Where(c => c != null && !string.IsNullOrEmpty(c.PlanetId)).GroupBy(c => c.PlanetId))
                {
                    var list = g.ToList(); Shuffle(list);
                    planetEncounterDecks[g.Key] = new Queue<EncounterCardData>(list);
                }
            }

            Debug.Log("[DeckManager] 6 market decks initialized.");
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

        private void SyncDeckRowToClients(MarketDeckType deckType, MarketDeck deck)
        {
            if (!IsServer || !clientMarketRows.TryGetValue(deckType, out var list)) return;
            list.Clear();
            foreach (var c in deck.MarketRow) list.Add(c.CardId);
        }

        private void SyncAllMarketRowsToClients()
        {
            if (!IsServer) return;
            foreach (var kvp in marketDecks)
            {
                if (clientMarketRows.TryGetValue(kvp.Key, out var list))
                {
                    list.Clear();
                    foreach (var c in kvp.Value.MarketRow) list.Add(c.CardId);
                }
            }
        }

        public int[] GetClientMarketRow(MarketDeckType deckType)
        {
            if (clientMarketRows.TryGetValue(deckType, out var list))
            {
                int[] arr = new int[list.Count];
                for (int i = 0; i < list.Count; i++) arr[i] = list[i];
                return arr;
            }
            return new int[0];
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
