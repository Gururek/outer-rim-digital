// StubCardDatabase.cs — provides fallback CardData when ScriptableObjects are empty
using System.Collections.Generic;
using UnityEngine;

namespace OuterRim
{
    /// <summary>Creates stub cards for all 6 market decks + encounter cards.
    /// Used when DeckManager's SO lists are empty (early dev / quickstart).</summary>
    public static class StubCardDatabase
    {
        private static int _nextId = 1;
        private static int NextId() => _nextId++;

        public static List<CardData> GetBountyCards(int count = 3) => CreateStubs("Bounty", "Wanted bounty • Deliver to planet for credits+fame", count, 500, 2000);
        public static List<CardData> GetCargoCards(int count = 3)   => CreateStubs("Cargo", "Cargo container • Deliver to planet for credits+fame", count, 300, 1500);
        public static List<CardData> GetGearCards(int count = 3)    => CreateStubs("Gear", "Ship gear • Equip for stat bonuses", count, 800, 3000);
        public static List<CardData> GetModsCards(int count = 3)    => CreateStubs("Mod", "Ship mod • Upgrade your ship permanently", count, 1000, 4000);
        public static List<CardData> GetJobCards(int count = 3)     => CreateStubs("Job", "Faction job • Complete for faction rep + rewards", count, 0, 0);
        public static List<CardData> GetLuxuryCards(int count = 3)  => CreateStubs("Luxury", "Luxury item • Sell or trade for high credits", count, 500, 2500);

        public static List<EncounterCardData> GetStubEncounters(int countPerPlanet = 3)
        {
            var list = new List<EncounterCardData>();
            string[] planets = { "Tatooine", "Corellia", "Nal Hutta", "Coruscant", "Ord Mantell", "Mon Cala", "Mustafar" };
            foreach (var planet in planets)
            {
                for (int i = 1; i <= countPerPlanet; i++)
                {
                    var card = ScriptableObject.CreateInstance<EncounterCardData>();
                    card.CardId = NextId();
                    card.CardName = $"Encounter {planet} #{i}";
                    card.FlavorText = $"A {planet} encounter. Choices lead to faction rep or combat.";
                    card.PlanetId = planet;
                    card.EncounterText = $"You encounter a situation on {planet}. Make a choice...";
                    list.Add(card);
                }
            }
            return list;
        }

        private static List<CardData> CreateStubs(string prefix, string desc, int count, int minCost, int maxCost)
        {
            var list = new List<CardData>();
            for (int i = 1; i <= count; i++)
            {
                var card = ScriptableObject.CreateInstance<CardData>();
                card.CardId = NextId();
                card.CardName = $"{prefix} Card #{i}";
                card.FlavorText = desc;
                card.BuyCost = Mathf.RoundToInt(Mathf.Lerp(minCost, maxCost, (float)(i - 1) / Mathf.Max(count - 1, 1)));
                list.Add(card);
            }
            return list;
        }
    }
}
