// DiceFaceDistribution.cs — ScriptableObject defining probability weights for dice faces.
using UnityEngine;

namespace OuterRim
{
    [CreateAssetMenu(fileName = "StandardDie", menuName = "Outer Rim/Dice/Face Distribution")]
    public class DiceFaceDistribution : ScriptableObject
    {
        [Header("Face Weights (default 2B/2F/3H/1C = 8-sided)")]
        [Range(0, 8)] public int BlankFaces = 2;
        [Range(0, 8)] public int FocusFaces = 2;
        [Range(0, 8)] public int HitFaces   = 3;
        [Range(0, 8)] public int CritFaces  = 1;

        /// <summary>Roll a single die using this distribution. Weights sum defines the total sides.</summary>
        public DieFace RollOneDie()
        {
            int total = BlankFaces + FocusFaces + HitFaces + CritFaces;
            if (total <= 0) return DieFace.Blank;

            int roll = Random.Range(0, total);

            if (roll < BlankFaces)                              return DieFace.Blank;
            if (roll < BlankFaces + FocusFaces)                 return DieFace.Focus;
            if (roll < BlankFaces + FocusFaces + HitFaces)      return DieFace.Hit;
            return DieFace.Crit;
        }
    }
}
