using System;
using UnityEngine;

namespace GachaTrainers
{
    [CreateAssetMenu(menuName = "Gacha Trainers/Creature Animation Library")]
    public sealed class CreatureAnimationLibrary : ScriptableObject
    {
        [Tooltip("종 → 동작 → 방향(E, NE, N, NW, W, SW, S, SE) → 프레임 순서")]
        [SerializeField] private Sprite[] _frames;

        public int Count => _frames == null ? 0 : _frames.Length;

        public Sprite Get(CreatureKind kind, CreatureFacing facing, CreatureAnimationState state, int frame)
        {
            if ((int)kind < 0 || (int)kind >= GachaCatalog.Creatures.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }
            if ((int)facing < 0 || (int)facing >= CreatureFacingUtility.DirectionCount)
            {
                throw new ArgumentOutOfRangeException(nameof(facing));
            }
            if ((int)state < 0 || (int)state >= CreatureAnimationCatalog.StateCount)
            {
                throw new ArgumentOutOfRangeException(nameof(state));
            }
            if (frame < 0 || frame >= CreatureAnimationCatalog.FrameCount)
            {
                throw new ArgumentOutOfRangeException(nameof(frame));
            }
            int index = (((int)kind * CreatureAnimationCatalog.StateCount + (int)state)
                * CreatureFacingUtility.DirectionCount + (int)facing) * CreatureAnimationCatalog.FrameCount + frame;
            return _frames[index];
        }
    }
}
