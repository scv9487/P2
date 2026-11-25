using System;
using UnityEngine;

namespace GachaTrainers
{
    public enum CreatureAnimationState
    {
        Idle,
        Move,
        Dodge,
        Hit,
        Attack,
        Skill
    }

    public static class CreatureAnimationCatalog
    {
        public const int FrameCount = 4;
        public const int StateCount = 6;

        public static float Duration(CreatureAnimationState state)
        {
            return state switch
            {
                CreatureAnimationState.Idle => 1.2f,
                CreatureAnimationState.Move => 0.4f,
                CreatureAnimationState.Dodge => 0.28f,
                CreatureAnimationState.Hit => 0.16f,
                CreatureAnimationState.Attack => 0.28f,
                CreatureAnimationState.Skill => 0.5f,
                _ => throw new ArgumentOutOfRangeException(nameof(state))
            };
        }

        public static bool Loops(CreatureAnimationState state)
        {
            return state == CreatureAnimationState.Idle || state == CreatureAnimationState.Move;
        }

        public static int Frame(CreatureAnimationState state, float time)
        {
            float duration = Duration(state);
            if (!float.IsFinite(time) || time <= 0f)
            {
                return 0;
            }
            if (Loops(state))
            {
                time %= duration;
            }
            else if (time >= duration)
            {
                return FrameCount - 1;
            }
            return Mathf.Min(FrameCount - 1, Mathf.FloorToInt(time / duration * FrameCount));
        }
    }
}
