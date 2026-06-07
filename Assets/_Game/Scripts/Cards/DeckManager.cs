// DeckManager.cs — Initializes, owns, and exposes all market decks + planet encounter decks.
// Deck contents are NEVER sent to clients directly (anti-cheat).
// Only the Market Row is revealed, via ClientRpcs.
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

namespace OuterRim
{
    // Payload types for market row client RPC broadcasts.
    [System.Serializable]
    public struct MarketRowEntry : Unity.Netcode.INetworkSerializable
    {
        public MarketDeckType DeckType;
        public int[] CardIds;

        public void NetworkSerialize<T>(Unity.Netcode.BufferSerializer<T> serializer) where T : Unity.Netcode.IReaderWriter
        {
            int deckTypeInt = (int)DeckType;
            serializer.SerializeValue(ref deckTypeInt);
            DeckType = (MarketDeckType)deckTypeInt;

            int length = CardIds?.Length ?? 0;
            serializer.SerializeValue(ref length);
            if (serializer.IsReader)
                CardIds = new int[length];
            for (int i = 0; i < length; i++)
                serializer.SerializeValue(ref CardIds[i]);
        }
    }

    public class DeckManager : NetworkBehaviour
    {
        public static DeckManager Instance { get; private set; }

        [Header("Market Deck Sources — ScriptableObjects")]
        [SerializeField] private List<BountyCardData>   bountyCards;
        [SerializeField] private List<CargoCardData>    cargoCards;
        [SerializeField] private List<GearCardData>     gearCards;
        [SerializeField] private List<ModCardData>      modCards;
        [SerializeField] private List<JobCardData>      jobCards;
        [SerializeField] private List<LuxuryCardData>   luxuryCards;
        [SerializeField] private List<EncounterCardData> encounterCards;

        [Header("JSON Alternative")]
        [SerializeField] private bool      useJsonPipeline;
        [SerializeField] private TextAsset cardDatabaseJson;

        [Header("Market Row Settings")]
        [SerializeField] private int defaultMarketRowSize = 3;
        [SerializeField] private int cycleCost = 200;

        // ─── Runtime Decks ──────────────────────────────────────────────────
        private Dictionary<MarketDeckType, MarketDeck> marketDecks;
        private Dictionary<string, Queue<EncounterCardData>> planetEncounterDecks;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else { Destroy(gameObject); return; }
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;
            InitializeDecks();
        }

        // ─── Initialization ─────────────────────────────────────────────────

        private void InitializeDecks()
        {
            marketDecks = new Dictionary<MarketDeckType, MarketDeck>();

            if (useJsonPipeline && cardDatabaseJson != null)
                LoadFromJson();
            else
                LoadFromScriptableObjects();

            BuildPlanetEncounterDecks();
            BroadcastAllMarketRowsClientRpc(BuildMarketRowPayload());

            Debug.Log("[DeckManager] All decks initialized.");
        }

        private void LoadFromScriptableObjects()
        {
            marketDecks[MarketDeckType.Bounties] = CreateAndInit(MarketDeckType.Bounties, bountyCards.Cast<CardData>().ToList());
            marketDecks[MarketDeckType.Cargo]    = CreateAndInit(MarketDeckType.Cargo,    cargoCards.Cast<CardData>().ToList());
            marketDecks[MarketDeckType.Gear]     = CreateAndInit(MarketDeckType.Gear,     gearCards.Cast<CardData>().ToList());
            marketDecks[MarketDeckType.Mods]     = CreateAndInit(MarketDeckType.Mods,     modCards.Cast<CardData>().ToList());
            marketDecks[MarketDeckType.Jobs]     = CreateAndInit(MarketDeckType.Jobs,     jobCards.Cast<CardData>().ToList());
            marketDecks[MarketDeckType.Luxury]   = CreateAndInit(MarketDeckType.Luxury,   luxuryCards.Cast<CardData>().ToList());
        }

        private MarketDeck CreateAndInit(MarketDeckType type, List<CardData> cards)
        {
            var deck = new MarketDeck(type, defaultMarketRowSize);
            deck.Initialize(cards);
            return deck;
        }

        private void BuildPlanetEncounterDecks()
        {
            planetEncounterDecks = new Dictionary<string, Queue<EncounterCardData>>();

            var grouped = encounterCards
                .Where(c => c != null && !string.IsNullOrEmpty(c.PlanetId))
                .GroupBy(c => c.PlanetId);

            foreach (var group in grouped)
            {
                var shuffled = group.ToList();
                Shuffle(shuffled);
                planetEncounterDecks[group.Key] = new Queue<EncounterCardData>(shuffled);
            }
        }

        private void LoadFromJson()
        {
            Debug.LogWarning("[DeckManager] JSON pipeline not fully implemented. Falling back to SO pipeline.");
            LoadFromScriptableObjects();
        }

        // ─── Market Row Broadcasting ────────────────────────────────────────

        private MarketRowEntry[] BuildMarketRowPayload()
        {
            return marketDecks.Select(kvp => new MarketRowEntry
            {
                DeckType = kvp.Key,
                CardIds  = kvp.Value.MarketRow.Select(c => c.CardId).ToArray()
            }).ToArray();
        }

        [ClientRpc]
        private void BroadcastAllMarketRowsClientRpc(MarketRowEntry[] entries)
        {
            Debug.Log($"[DeckManager] Received {entries.Length} market row entries on client.");
            // UI refresh handled by UIManager subscriber — Phase 4.
        }

        [ClientRpc]
        private void NotifyMarketRowUpdateClientRpc(MarketDeckType deckType, int[] cardIds)
        {
            Debug.Log($"[DeckManager] Market row updated: {deckType}");
            // UI refresh handled by UIManager subscriber — Phase 4.
        }

        // ─── Public Actions ─────────────────────────────────────────────────

        public MarketDeck GetDeck(MarketDeckType type) =>
            marketDecks.TryGetValue(type, out var deck) ? deck : null;

        /// <summary>Attempts to purchase a card from the market row. Deducts cost from player.</summary>
        public CardData TryPurchaseCard(PlayerState buyer, MarketDeckType deckType, int rowIndex)
        {
            if (!IsServer) return null;
            if (!marketDecks.TryGetValue(deckType, out var deck)) return null;

            var card = deck.MarketRow.ElementAtOrDefault(rowIndex);
            if (card == null) return null;
            if (!buyer.SpendCredits(card.BuyCost)) return null;

            deck.PurchaseFromMarket(rowIndex);
            var updatedIds = deck.MarketRow.Select(c => c.CardId).ToArray();
            NotifyMarketRowUpdateClientRpc(deckType, updatedIds);
            return card;
        }

        /// <summary>Player pays the cycle fee to replace a market card.</summary>
        public CardData TryCycleCard(PlayerState player, MarketDeckType deckType, int rowIndex)
        {
            if (!IsServer) return null;
            if (!player.SpendCredits(cycleCost)) return null;
            if (!marketDecks.TryGetValue(deckType, out var deck)) return null;

            var newCard = deck.CycleMarketCard(rowIndex);
            var updatedIds = deck.MarketRow.Select(c => c.CardId).ToArray();
            NotifyMarketRowUpdateClientRpc(deckType, updatedIds);
            return newCard;
        }

        /// <summary>Draws an encounter card for the given planet. Reshuffles when exhausted.</summary>
        public EncounterCardData DrawPlanetEncounterCard(string planetId)
        {
            if (!IsServer) return null;
            if (!planetEncounterDecks.TryGetValue(planetId, out var queue)) return null;

            if (queue.Count == 0)
            {
                Debug.LogWarning($"[DeckManager] Encounter deck for planet '{planetId}' exhausted. Reshuffling.");
                var allForPlanet = encounterCards.Where(c => c.PlanetId == planetId).ToList();
                Shuffle(allForPlanet);
                foreach (var c in allForPlanet) queue.Enqueue(c);
            }

            return queue.Count > 0 ? queue.Dequeue() : null;
        }

        // ─── Helpers ────────────────────────────────────────────────────────

        private void Shuffle<T>(List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
