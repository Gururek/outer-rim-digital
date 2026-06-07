// DiceRoller.cs — Static utility. All rolls happen server-side.
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OuterRim
{
    /// <summary>Immutable result of a single dice roll. Crits count as Hits AND crits.</summary>
    public readonly struct DiceRollResult
    {
        public readonly List<DieFace> Faces;

        public DiceRollResult(List<DieFace> faces) => Faces = faces;

        public int Hits    => Faces.Count(f => f == DieFace.Hit || f == DieFace.Crit);
        public int Crits   => Faces.Count(f => f == DieFace.Crit);
        public int Focuses => Faces.Count(f => f == DieFace.Focus);
        public int Blanks  => Faces.Count(f => f == DieFace.Blank);

        public int[] ToIntArray() => Faces.ConvertAll(f => (int)f).ToArray();

        public override string ToString() =>
            $"[Hits:{Hits} Crits:{Crits} Focus:{Focuses} Blank:{Blanks}]";
    }

    public static class DiceRoller
    {
        /// <summary>Rolls numDice dice using the given distribution. Server-only.</summary>
        public static DiceRollResult Roll(int numDice, DiceFaceDistribution distribution)
        {
            numDice = Mathf.Max(1, numDice);
            var faces = new List<DieFace>(numDice);
            for (int i = 0; i < numDice; i++)
                faces.Add(distribution.RollOneDie());
            return new DiceRollResult(faces);
        }
    }
}
