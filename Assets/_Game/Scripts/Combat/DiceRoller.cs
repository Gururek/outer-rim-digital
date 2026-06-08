// DiceRoller.cs — V2: uses DiceFaceDistribution SO for die rolls
using System.Collections.Generic;
using UnityEngine;

namespace OuterRim
{
    public struct DiceRollResult
    {
        public List<DieFace> Faces;
        public int Blanks => Faces.FindAll(f => f == DieFace.Blank).Count;
        public int Focuses => Faces.FindAll(f => f == DieFace.Focus).Count;
        public int Hits => Faces.FindAll(f => f == DieFace.Hit).Count;
        public int Crits => Faces.FindAll(f => f == DieFace.Crit).Count;
        // V2: Hit = 1 damage, Crit = 2 damage
        public int Damage => Hits * 1 + Crits * 2;

        public int[] ToIntArray()
        {
            var arr = new int[Faces.Count];
            for (int i = 0; i < Faces.Count; i++) arr[i] = (int)Faces[i];
            return arr;
        }
    }

    public static class DiceRoller
    {
        public static DiceRollResult Roll(int numDice, DiceFaceDistribution dist)
        {
            var faces = new List<DieFace>();
            for (int i = 0; i < numDice; i++)
                faces.Add(dist.RollOneDie());
            return new DiceRollResult { Faces = faces };
        }
    }
}
