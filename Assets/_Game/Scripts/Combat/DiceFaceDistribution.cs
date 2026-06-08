// DiceFaceDistribution.cs — V2 ScriptableObject per Outer Rim rulebook
// Standard Outer Rim die: 8-sided (2 Blank, 2 Focus, 3 Hit, 1 Crit)
using UnityEngine;

namespace OuterRim
{
    [CreateAssetMenu(fileName = "StandardDie", menuName = "Outer Rim/Dice/Face Distribution")]
    public class DiceFaceDistribution : ScriptableObject
    {
        [Header("V2 Standard Die (8-sided): 2B / 2F / 3H / 1C")]
        [Range(0, 8)] public int BlankFaces = 2;
        [Range(0, 8)] public int FocusFaces = 2;
        [Range(0, 8)] public int HitFaces   = 3;
        [Range(0, 8)] public int CritFaces  = 1;

        public DieFace RollOneDie()
        {
            int total = BlankFaces + FocusFaces + HitFaces + CritFaces;
            if (total <= 0) return DieFace.Blank;
            int roll = Random.Range(0, total);
            if (roll < BlankFaces) return DieFace.Blank;
            if (roll < BlankFaces + FocusFaces) return DieFace.Focus;
            if (roll < BlankFaces + FocusFaces + HitFaces) return DieFace.Hit;
            return DieFace.Crit;
        }
    }
}
