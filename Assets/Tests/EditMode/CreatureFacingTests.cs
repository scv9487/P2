using GachaTrainers;
using NUnit.Framework;
using UnityEngine;

namespace P2.Tests
{
    public sealed class CreatureFacingTests
    {
        [Test]
        public void CardinalAndDiagonalVectorsSelectAllEightFacings()
        {
            var directions = new[]
            {
                Vector2.right, new Vector2(1f, 1f), Vector2.up, new Vector2(-1f, 1f),
                Vector2.left, new Vector2(-1f, -1f), Vector2.down, new Vector2(1f, -1f)
            };
            for (int index = 0; index < directions.Length; index++)
            {
                Assert.That(CreatureFacingUtility.FromDirection(directions[index]),
                    Is.EqualTo((CreatureFacing)index), "Facing index " + index);
                Assert.That(CreatureFacingUtility.FromDirection(directions[index] * 17f),
                    Is.EqualTo((CreatureFacing)index), "Direction magnitude must not change the selected art.");
            }
        }

        [Test]
        public void HalfSectorBoundariesChooseTheNearestDirectionAndWrapToEast()
        {
            for (int index = 0; index < 8; index++)
            {
                float boundary = index * 45f + 22.5f;
                Assert.That(CreatureFacingUtility.FromDirection(AtAngle(boundary - 0.1f)),
                    Is.EqualTo((CreatureFacing)index), "Below boundary " + boundary);
                Assert.That(CreatureFacingUtility.FromDirection(AtAngle(boundary + 0.1f)),
                    Is.EqualTo((CreatureFacing)((index + 1) % 8)), "Above boundary " + boundary);
            }
            Assert.That(CreatureFacingUtility.FromDirection(AtAngle(22.5f)), Is.EqualTo(CreatureFacing.NorthEast));
            Assert.That(CreatureFacingUtility.FromDirection(AtAngle(-0.1f)), Is.EqualTo(CreatureFacing.East));
        }

        [Test]
        public void ZeroAndNonFiniteVectorsKeepTheProvidedFacing()
        {
            var invalidDirections = new[]
            {
                Vector2.zero, new Vector2(float.NaN, 1f), new Vector2(1f, float.NaN),
                new Vector2(float.PositiveInfinity, 0f), new Vector2(0f, float.NegativeInfinity)
            };
            foreach (Vector2 direction in invalidDirections)
            {
                Assert.That(CreatureFacingUtility.FromDirection(direction, CreatureFacing.SouthWest),
                    Is.EqualTo(CreatureFacing.SouthWest));
            }
            Assert.That(CreatureFacingUtility.FromDirection(Vector2.zero), Is.EqualTo(CreatureFacing.East));
        }

        private static Vector2 AtAngle(float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        }
    }
}
