using UnityEngine;

namespace GachaTrainers
{
    // Counterclockwise in the arena's XY plane. North shows the creature's back.
    public enum CreatureFacing
    {
        East,
        NorthEast,
        North,
        NorthWest,
        West,
        SouthWest,
        South,
        SouthEast
    }

    public static class CreatureFacingUtility
    {
        public const int DirectionCount = 8;

        public static CreatureFacing FromDirection(Vector2 direction, CreatureFacing fallback = CreatureFacing.East)
        {
            if (!float.IsFinite(direction.x) || !float.IsFinite(direction.y) || direction.sqrMagnitude < 0.000001f)
            {
                return fallback;
            }

            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            int sector = Mathf.FloorToInt((angle + 22.5f + 360f) / 45f) % DirectionCount;
            return (CreatureFacing)sector;
        }
    }
}
