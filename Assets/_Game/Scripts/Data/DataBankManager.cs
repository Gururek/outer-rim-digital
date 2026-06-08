// DataBankManager.cs — V2: Numbered reference deck (never shuffled)
// Per Outer Rim rules: databank cards retrieved by number for contacts, jobs, encounters.
using System.Collections.Generic;
using UnityEngine;

namespace OuterRim
{
    /// <summary>
    /// A single databank card. Each has a unique number (1-53+ in base game).
    /// These are NOT shuffled — retrieved by number via GetCard().
    /// </summary>
    [CreateAssetMenu(fileName = "DatabankCard_1", menuName = "Outer Rim/DataBank/Databank Card")]
    public class DatabankCard : ScriptableObject
    {
        public int CardNumber;
        public string CardName;
        [TextArea(3, 8)] public string Description;
        public FactionType Faction;
        public ContactClass ContactClass; // White/Green/Yellow/Orange
        public int BountyReward;
        public int CombatValue;
        public Sprite CardArt;
    }

    /// <summary>
    /// Server-authoritative manager for the databank deck.
    /// Cards are indexed by number and never shuffled.
    /// </summary>
    public class DataBankManager : MonoBehaviour
    {
        public static DataBankManager Instance { get; private set; }

        [SerializeField] private List<DatabankCard> databankCards = new();
        private Dictionary<int, DatabankCard> cardLookup = new();

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else { Destroy(gameObject); return; }
            BuildLookup();
        }

        private void BuildLookup()
        {
            cardLookup.Clear();
            foreach (var card in databankCards)
            {
                if (card != null && !cardLookup.ContainsKey(card.CardNumber))
                    cardLookup[card.CardNumber] = card;
            }
            Debug.Log($"[DataBank] Indexed {cardLookup.Count} databank cards.");
        }

        /// <summary>Retrieve a databank card by its number. Returns null if not found.</summary>
        public DatabankCard GetCard(int cardNumber)
        {
            return cardLookup.TryGetValue(cardNumber, out var card) ? card : null;
        }

        /// <summary>Get all cards for a specific faction (for contact token placement).</summary>
        public List<DatabankCard> GetCardsByFaction(FactionType faction)
        {
            var result = new List<DatabankCard>();
            foreach (var card in databankCards)
                if (card != null && card.Faction == faction)
                    result.Add(card);
            return result;
        }

        /// <summary>Get cards of a specific contact class.</summary>
        public List<DatabankCard> GetCardsByClass(ContactClass contactClass)
        {
            var result = new List<DatabankCard>();
            foreach (var card in databankCards)
                if (card != null && card.ContactClass == contactClass)
                    result.Add(card);
            return result;
        }

        public int CardCount => cardLookup.Count;
    }
}
