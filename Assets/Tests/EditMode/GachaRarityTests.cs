using System;
using System.Linq;
using GachaTrainers;
using NUnit.Framework;

namespace P2.Tests
{
    public sealed class GachaRarityTests
    {
        [Test]
        public void EveryCrystalRollMatchesItsWeightAndGradeDistribution()
        {
            var counts = new int[GachaCatalog.Crystals.Count];
            var grades = new int[4];
            Assert.That(GachaCatalog.CrystalWeightTotal, Is.EqualTo(1200));
            for (int roll = 0; roll < GachaCatalog.CrystalWeightTotal; roll++)
            {
                var item = GachaCatalog.GetCrystalForRoll(roll);
                counts[(int)item.Kind]++;
                grades[(int)item.Rarity]++;
            }
            Assert.That(grades, Is.EqualTo(new[] { 600, 360, 180, 60 }));
            foreach (var crystal in GachaCatalog.Crystals)
            {
                int start = GachaCatalog.GetCrystalRollStart(crystal.Kind);
                Assert.That(counts[(int)crystal.Kind], Is.EqualTo(crystal.Weight));
                Assert.That(GachaCatalog.GetCrystalForRoll(start), Is.SameAs(crystal));
                Assert.That(GachaCatalog.GetCrystalForRoll(start + crystal.Weight - 1), Is.SameAs(crystal));
                Assert.That(GachaCatalog.GetCrystalDropPercent(crystal.Kind), Is.EqualTo(crystal.Weight / 12f));
            }
        }

        [Test]
        public void EveryCreatureRollPreservesThreeTwoOneZeroSpeciesAndReservedLegendary()
        {
            var grades = new int[4];
            foreach (int roll in Enumerable.Range(0, 600))
            {
                var result = GachaCatalog.GetCreatureForRoll(roll);
                grades[result == null ? 3 : (int)result.Rarity]++;
            }
            Assert.That(grades, Is.EqualTo(new[] { 300, 180, 90, 30 }));
            Assert.That(Enumerable.Range(0, 4).Select(grade => GachaCatalog.Creatures.Count(item => (int)item.Rarity == grade)),
                Is.EqualTo(new[] { 3, 2, 1, 0 }));
            foreach (var creature in GachaCatalog.Creatures)
            {
                Assert.That(GachaCatalog.GetCreatureForRoll(GachaCatalog.GetCreatureRollStart(creature.Kind)), Is.SameAs(creature));
            }
        }

        [TestCase(-1)]
        [TestCase(1200)]
        public void InvalidCrystalRollRejects(int roll)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => GachaCatalog.GetCrystalForRoll(roll));
        }

        [TestCase(-1)]
        [TestCase(600)]
        public void InvalidCreatureRollRejects(int roll)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => GachaCatalog.GetCreatureForRoll(roll));
        }

        [Test]
        public void LegendaryDrawStoresOneTokenWithoutInventingSpeciesAndNewRunResetsIt()
        {
            var run = new RunDataContainer();
            Assert.That(run.TryPurchase(GachaMachineKind.Creature, _ => 599), Is.True);
            Assert.That(run.Money, Is.EqualTo(500));
            Assert.That(run.LegendaryTokens, Is.EqualTo(1));
            Assert.That(run.Party, Is.Empty);
            Assert.That(run.Owned, Is.Empty);
            Assert.That(new RunDataContainer().LegendaryTokens, Is.Zero);
        }
    }
}
