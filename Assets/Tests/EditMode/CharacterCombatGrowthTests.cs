using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Refactor.Main;
using Refactor.Util;
using UnityEngine;

namespace P2.Tests
{
    public sealed class CharacterCombatGrowthTests
    {
        private CombatStatSheet _combat;
        private CharacterGrowthSheet _growth;
        private CombatStatCalculator _stats;
        private static MajorStats BaseStats => new(10, 10, 10, 10, 10, 10);

        [SetUp]
        public void SetUp()
        {
            _combat = new CombatStatSheet(Read("CombatStatTable.csv"), Read("CombatEnemyTable.csv"), Read("CombatAttackTable.csv"));
            _growth = Growth();
            _stats = new CombatStatCalculator(_combat);
        }

        [Test]
        public void DsDefaultMajorStatsProduceTheOriginalDerivedCombatValues()
        {
            var stats = _stats.Calculate(BaseStats);
            Assert.That(stats.MaxHealth, Is.EqualTo(295));
            Assert.That(stats.PhysicalAttackPower, Is.EqualTo(33.5f).Within(0.0001f));
            Assert.That(stats.MagicAttackPower, Is.EqualTo(33.5f).Within(0.0001f));
            Assert.That(stats.AttackSpeed, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(stats.CriticalChance, Is.EqualTo(11));
            Assert.That(stats.CooldownReduction, Is.EqualTo(3));
            Assert.That(stats.PhysicalResistance, Is.EqualTo(3));
            Assert.That(stats.MagicResistance, Is.EqualTo(3));
            Assert.That(stats.HitChance, Is.EqualTo(100));
            Assert.That(stats.DodgeChance, Is.EqualTo(10));
            Assert.That(stats.CriticalDamageBonus, Is.EqualTo(50));
            Assert.That(stats.GuardDamageReduction, Is.EqualTo(50));
            Assert.That(stats.MovementSpeed, Is.EqualTo(82));
            foreach (CombatStatType type in Enum.GetValues(typeof(CombatStatType)))
            {
                Assert.That(_combat.Get(type).Name, Is.Not.Empty);
                Assert.That(float.IsNaN(stats[type]) || float.IsInfinity(stats[type]), Is.False);
            }
        }

        [Test]
        public void FixedBonusesPrecedePercentageBonusesAndDsCapsRemainStatSpecific()
        {
            var additions = new Dictionary<CombatStatType, float>
            {
                [CombatStatType.PhysicalAttackPower] = 10,
                [CombatStatType.DodgeChance] = 100,
                [CombatStatType.GuardChance] = 100,
                [CombatStatType.GuardDamageReduction] = 100
            };
            var percentages = new Dictionary<CombatStatType, float>
            {
                [CombatStatType.PhysicalAttackPower] = 50,
                [CombatStatType.CriticalChance] = 100,
                [CombatStatType.MovementSpeed] = -200
            };
            var stats = _stats.Calculate(BaseStats, additions, percentages);
            Assert.That(stats.PhysicalAttackPower, Is.EqualTo(65.25f));
            Assert.That(stats.CriticalChance, Is.EqualTo(11), "DS 치명타 확률에는 % 보정 StatType이 없습니다.");
            Assert.That(stats.DodgeChance, Is.EqualTo(80));
            Assert.That(stats.GuardChance, Is.EqualTo(80));
            Assert.That(stats.GuardDamageReduction, Is.EqualTo(80));
            Assert.That(stats.MovementSpeed, Is.EqualTo(8.2f).Within(0.0001f));
        }

        [Test]
        public void GreyRatUsesOriginalUnitDefaultsAndScalingDoesNotChangeItsOtherStats()
        {
            var rat = _stats.CalculateEnemy("grey_rat");
            Assert.That(rat.MaxHealth, Is.EqualTo(500));
            Assert.That(rat.PhysicalAttackPower, Is.EqualTo(30));
            Assert.That(rat.MagicAttackPower, Is.EqualTo(20));
            Assert.That(rat.AttackSpeed, Is.EqualTo(0.3f));
            Assert.That(rat.CriticalChance, Is.EqualTo(5));
            Assert.That(rat.HitChance, Is.EqualTo(100));
            Assert.That(rat.DodgeChance, Is.EqualTo(10));
            Assert.That(rat.PhysicalResistance, Is.Zero);
            Assert.That(_combat.GetEnemy("grey_rat").OnDeathExperience, Is.EqualTo(8));
            var boss = rat.WithMultipliers(2, 3, 1.5f);
            Assert.That(boss.MaxHealth, Is.EqualTo(1000));
            Assert.That(boss.PhysicalAttackPower, Is.EqualTo(90));
            Assert.That(boss.MagicAttackPower, Is.EqualTo(60));
            Assert.That(boss.AttackSpeed, Is.EqualTo(0.45f).Within(0.0001f));
            Assert.That(boss.CriticalChance, Is.EqualTo(rat.CriticalChance));
            Assert.That(rat.MaxHealth, Is.EqualTo(500), "배율 적용은 원형 객체를 변경하지 않습니다.");
        }

        [Test]
        public void HitUsesStrictLessThanAndMissStopsFurtherRandomDraws()
        {
            var random = new SequenceRandom(floats: new[] { 90f });
            var result = CombatCalculator.ResolveAttack(_stats.Calculate(BaseStats), _stats.CalculateEnemy("grey_rat"), random);
            Assert.That(result.Outcome, Is.EqualTo(CombatOutcome.Miss));
            Assert.That(result.Damage, Is.Zero);
            Assert.That(random.FloatCalls, Is.EqualTo(1));
        }

        [Test]
        public void PhysicalGuardTakesPrecedenceOverCriticalAndUsesItsReduction()
        {
            var attacker = _stats.Calculate(BaseStats).WithValue(CombatStatType.CriticalChance, 100);
            var defender = _stats.CalculateEnemy("grey_rat").WithValue(CombatStatType.GuardChance, 80);
            var random = new SequenceRandom(floats: new[] { 0f, 79f });
            var result = CombatCalculator.ResolveAttack(attacker, defender, random);
            Assert.That(result.Outcome, Is.EqualTo(CombatOutcome.Guard));
            Assert.That(result.Damage, Is.EqualTo(16.75f).Within(0.0001f));
            Assert.That(random.FloatCalls, Is.EqualTo(2));
        }

        [Test]
        public void MagicBypassesGuardButStillRollsCriticalAndUsesMagicResistance()
        {
            var attacker = _stats.Calculate(BaseStats);
            var defender = _stats.CalculateEnemy("grey_rat").WithValue(CombatStatType.GuardChance, 80)
                .WithValue(CombatStatType.MagicResistance, 100);
            var random = new SequenceRandom(floats: new[] { 0f, 10f });
            var result = CombatCalculator.ResolveAttack(attacker, defender, random, CombatDamageType.Magic);
            Assert.That(result.Outcome, Is.EqualTo(CombatOutcome.Critical));
            Assert.That(result.Damage, Is.EqualTo(25.125f).Within(0.0001f));
            Assert.That(random.FloatCalls, Is.EqualTo(2));
        }

        [Test]
        public void DamageBonusesAreAddedBeforeResistanceAndLifeStealUsesUncappedDamage()
        {
            var attacker = _stats.Calculate(BaseStats).WithValue(CombatStatType.PhysicalAttackPower, 100)
                .WithValue(CombatStatType.DamageDealtBonus, 20).WithValue(CombatStatType.PhysicalDamageDealtBonus, 30)
                .WithValue(CombatStatType.FinalDamageMultiplier, 2).WithValue(CombatStatType.LifeSteal, 25);
            var defender = _stats.CalculateEnemy("grey_rat").WithValue(CombatStatType.PhysicalResistance, 100)
                .WithValue(CombatStatType.DamageTakenBonus, -10);
            var result = CombatCalculator.ResolveAttack(attacker, defender, new SequenceRandom(floats: new[] { 0f, 99f, 99f }));
            Assert.That(result.Damage, Is.EqualTo(140).Within(0.0001f));
            Assert.That(result.LifeSteal, Is.EqualTo(35).Within(0.0001f), "DS는 오버킬을 포함한 계산 피해를 기준으로 흡혈합니다.");
            defender = defender.WithValue(CombatStatType.DamageTakenBonus, -500).WithValue(CombatStatType.PhysicalResistance, 0);
            attacker = attacker.WithValue(CombatStatType.DamageDealtBonus, 0).WithValue(CombatStatType.PhysicalDamageDealtBonus, 0)
                .WithValue(CombatStatType.FinalDamageMultiplier, 1);
            result = CombatCalculator.ResolveAttack(attacker, defender, new SequenceRandom(floats: new[] { 0f, 99f, 99f }));
            Assert.That(result.Damage, Is.EqualTo(1).Within(0.0001f), "DS 받는 피해 감소는 -99%에서 제한합니다.");
        }

        [Test]
        public void PunchAndRatAttackCyclesAndCooldownsFollowTheDsDivisors()
        {
            var player = _stats.Calculate(BaseStats);
            var rat = _stats.CalculateEnemy("grey_rat");
            Assert.That(_combat.GetAttack("player").Coefficient, Is.EqualTo(0.3f));
            Assert.That(CombatCalculator.AttackCycleSeconds(player, _combat.GetAttack("player")), Is.EqualTo(1.2f).Within(0.0001f));
            Assert.That(CombatCalculator.AttackCycleSeconds(rat, _combat.GetAttack("grey_rat")), Is.EqualTo(10f / 3f).Within(0.0001f));
            Assert.That(CombatCalculator.AdjustCooldown(10, player, true), Is.EqualTo(20));
            Assert.That(CombatCalculator.AdjustCooldown(10, player, false), Is.EqualTo(10f / 1.03f).Within(0.0001f));
            Assert.That(CombatCalculator.AdjustCooldown(10, player.WithValue(CombatStatType.CooldownReduction, -101), false), Is.EqualTo(10));
        }

        [Test]
        public void ExperienceUsesDsLevelThresholdsAndStopsAtMaximumBeforeOneAutomaticGrowth()
        {
            var calculator = new CharacterGrowthCalculator(_growth);
            CollectionAssert.AreEqual(new float[] { 30, 250, 567, 1017, 1600, 2315, 3167, 4153, 5273, 6531, 7926, 9457 },
                Enumerable.Range(1, 12).Select(calculator.RequiredExperience));
            var original = new CharacterGrowthState(1, 29, BaseStats);
            var random = new SequenceRandom(ints: new[] { 2, 3, 4, 2, 3, 4 });
            var after = calculator.GainExperience(original, 10000, random);
            Assert.That(after.Level, Is.EqualTo(2));
            Assert.That(after.Experience, Is.Zero, "초과 경험치는 다음 레벨로 이월하지 않습니다.");
            CollectionAssert.AreEqual(new float[] { 12, 13, 14, 12, 13, 14 }, Enumerable.Range(0, 6).Select(i => after.Stats[(MajorStatType)i]));
            Assert.That(random.IntCalls, Is.EqualTo(6));
            Assert.That(original.Level, Is.EqualTo(1));
            Assert.That(original.Experience, Is.EqualTo(29));
            Assert.That(original.Stats.Strength, Is.EqualTo(10));
        }

        [Test]
        public void ExperienceBonusAndMaximumLevelNeverConsumeUnnecessaryGrowthRandomness()
        {
            var calculator = new CharacterGrowthCalculator(_growth);
            var random = new SequenceRandom();
            var after = calculator.GainExperience(new CharacterGrowthState(1, 0, BaseStats), 8f / 4f, random, 50);
            Assert.That(after.Experience, Is.EqualTo(3));
            Assert.That(after.Level, Is.EqualTo(1));
            after = calculator.GainExperience(new CharacterGrowthState(12, 9456, BaseStats), 999, random);
            Assert.That(after.Level, Is.EqualTo(12));
            Assert.That(after.Experience, Is.EqualTo(9457));
            Assert.That(random.IntCalls, Is.Zero);
        }

        [Test]
        public void GrowthUsesInclusiveIntegerRangesAndUnityBankersRoundingAfterMultiplier()
        {
            var growth = Growth(Read("CharacterGrowthSettingTable.csv").Replace("GrowthMultiplier,1", "GrowthMultiplier,1.25"));
            var random = new SequenceRandom(ints: new[] { 2, 3, 4, 2, 3, 4 });
            var after = new CharacterGrowthCalculator(growth).GainExperience(new CharacterGrowthState(1, 0, BaseStats), 30, random);
            CollectionAssert.AreEqual(new float[] { 12, 14, 15, 12, 14, 15 }, Enumerable.Range(0, 6).Select(i => after.Stats[(MajorStatType)i]));
            Assert.That(random.IntBounds.All(bounds => bounds == (2, 5)), Is.True);
        }

        [Test]
        public void MalformedTuningOrInvalidStateIsRejectedBeforeAnyGrowthMutation()
        {
            Assert.Throws<FormatException>(() => new CharacterGrowthSheet(Read("CharacterGrowthTable.csv").Replace("Strength,2,4", "Strength,5,4"),
                Read("CharacterLevelTable.csv"), Read("CharacterGrowthSettingTable.csv")));
            Assert.Throws<FormatException>(() => new CharacterGrowthSheet(Read("CharacterGrowthTable.csv"),
                Read("CharacterLevelTable.csv").Replace("1,30", "1,NaN"), Read("CharacterGrowthSettingTable.csv")));
            string combat = string.Join("\n", Read("CombatStatTable.csv").Split('\n').Where(line => !line.StartsWith("HitChance,")));
            Assert.Throws<FormatException>(() => new CombatStatSheet(combat, Read("CombatEnemyTable.csv"), Read("CombatAttackTable.csv")));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MajorStats(float.NaN, 10, 10, 10, 10, 10));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CharacterGrowthCalculator(_growth)
                .GainExperience(new CharacterGrowthState(1, 0, BaseStats), -1, new SequenceRandom()));
        }

        private static CharacterGrowthSheet Growth(string settings = null)
        {
            return new CharacterGrowthSheet(Read("CharacterGrowthTable.csv"), Read("CharacterLevelTable.csv"), settings ?? Read("CharacterGrowthSettingTable.csv"));
        }
        private static string Read(string name) => File.ReadAllText(Path.Combine(Application.dataPath, "1_InternalTools/Util/DataSheetLoader/Table", name));

        private sealed class SequenceRandom : IRandomSource
        {
            private readonly Queue<int> _integers;
            private readonly Queue<float> _floats;
            public int IntCalls { get; private set; }
            public int FloatCalls { get; private set; }
            public List<(int min, int max)> IntBounds { get; } = new();
            public SequenceRandom(int[] ints = null, float[] floats = null)
            {
                _integers = new Queue<int>(ints ?? Array.Empty<int>());
                _floats = new Queue<float>(floats ?? Array.Empty<float>());
            }
            public int NextInt(int minInclusive, int maxExclusive)
            {
                IntCalls++;
                IntBounds.Add((minInclusive, maxExclusive));
                Assert.That(_integers.Count, Is.GreaterThan(0), "예상하지 않은 성장 난수 호출");
                return _integers.Dequeue();
            }
            public float NextFloat(float minInclusive, float maxInclusive)
            {
                FloatCalls++;
                Assert.That(minInclusive, Is.Zero);
                Assert.That(maxInclusive, Is.EqualTo(100));
                Assert.That(_floats.Count, Is.GreaterThan(0), "예상하지 않은 전투 난수 호출");
                return _floats.Dequeue();
            }
        }
    }
}
