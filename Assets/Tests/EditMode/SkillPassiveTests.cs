using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Refactor.Main;
using Refactor.Util;
using UnityEngine;

namespace P2.Tests
{
    // 조건 조합/효과 수명은 독립된 인물로 검증한다. 저장/실제 입력 통합은 SkillCombatTests가 담당한다.
    public sealed class SkillPassiveTests
    {
        private SkillSheet _skills;
        private CombatStatSheet _combat;
        private CombatStatCalculator _calculator;
        private readonly MajorStats _base = new(10, 10, 10, 10, 10, 10);

        [SetUp]
        public void SetUp()
        {
            _skills = new SkillSheet(Read("SkillDefinitionTable.csv"), Read("SkillClassTable.csv"),
                Read("CharacterClassTable.csv"), Read("SkillWeaponTable.csv"), Read("SkillSettingTable.csv"), Read("SkillTextTable.csv"));
            _combat = new CombatStatSheet(Read("CombatStatTable.csv"), Read("CombatEnemyTable.csv"), Read("CombatAttackTable.csv"));
            _calculator = new CombatStatCalculator(_combat);
        }

        [Test]
        public void CatalogKeepsBasicAttacksOutOfPurchasesAndOnlyIncludesExecutableDsSkills()
        {
            Assert.That(_skills.Skills.Count(skill => skill.Type == SkillType.Active), Is.EqualTo(18));
            Assert.That(_skills.Skills.Count(skill => skill.Type == SkillType.Passive), Is.EqualTo(33));
            Assert.That(_skills.BasicSkills.Count, Is.EqualTo(10));
            Assert.That(_skills.Skills.All(skill => !skill.IsBasicAttack), Is.True);
            Assert.That(_skills.Classes.Count, Is.EqualTo(9));
            foreach (var skill in _skills.Skills.Concat(_skills.BasicSkills))
            {
                Assert.That(skill.Description, Does.Not.Contain("{"), skill.Id);
                Assert.That(skill.Description, Does.Not.Contain("AFFECTER_"), skill.Id);
            }
            foreach (string id in new[] { "SwordFlow", "SaberGale", "DualRequest", "SentinelCry", "WeaponDeflect", "SwordmanTaunt" })
            {
                Assert.Throws<KeyNotFoundException>(() => _skills.GetSkill("SKILL_" + id));
            }
            CollectionAssert.AreEqual(new[] { "SKILL_VerticalSlash" }, _skills.GetSkill("SKILL_ChainSlash").PrerequisiteIds);
        }

        [Test]
        public void PermanentAndTimedPercentagesAreAddedBeforeMultiplicationAndEachDurationExpires()
        {
            var hero = Unit("hero", "SKILL_TwoHandGrip", "SKILL_FastHand", "SKILL_Initiative", "SKILL_FirstAssault", "SKILL_SharpEdge");
            var baseline = _calculator.Calculate(_base);
            CombatSkillPassives.OnBattleStart(hero);
            CombatSkillPassives.OnSkillStart(hero, _skills.GetSkill("SKILL_VerticalSlash"));
            Assert.That(hero.EffectiveStats.AttackSpeed, Is.EqualTo(baseline.AttackSpeed * 1.47f).Within(0.0001f));
            Assert.That(hero.EffectiveStats.PhysicalAttackPower, Is.EqualTo(baseline.PhysicalAttackPower * 1.78f).Within(0.0001f),
                "한손35 + 첫공격35 + 실제 SharpEdge 바인딩8을 더하며, 각각을 곱하지 않습니다.");
            Call(hero, "TickSkills", 8f);
            Assert.That(hero.EffectiveStats.PhysicalAttackPower, Is.EqualTo(baseline.PhysicalAttackPower * 1.70f).Within(0.0001f));
            Call(hero, "TickSkills", 7f);
            Assert.That(hero.EffectiveStats.PhysicalAttackPower, Is.EqualTo(baseline.PhysicalAttackPower * 1.35f).Within(0.0001f));
            Assert.That(hero.EffectiveStats.AttackSpeed, Is.EqualTo(baseline.AttackSpeed * 1.12f).Within(0.0001f));
        }

        [Test]
        public void GreatswordDoesNotReceiveOneHandGripAndSharpShooterCountsEveryReachedMilestone()
        {
            var progress = Progress("bran", 100, "SKILL_TwoHandGrip", "SKILL_SharpShooter", "SKILL_ShieldHandling");
            var actual = CombatSkillPassives.CalculateStats(_calculator, _base, _skills, progress);
            var baseline = _calculator.Calculate(_base);
            Assert.That(actual.PhysicalAttackPower, Is.EqualTo(baseline.PhysicalAttackPower));
            Assert.That(actual.AttackSpeed, Is.EqualTo(baseline.AttackSpeed), "방패를 배우기만 해서는 장착 시 속도 보너스를 얻지 않습니다.");
            Assert.That(actual.GuardChance, Is.EqualTo(Math.Min(80f, baseline.GuardChance + 10f)));
            Assert.That(actual.CriticalChance, Is.EqualTo(baseline.CriticalChance + 21f));
        }

        [Test]
        public void MissingHealthAndBurningDamageMultipliersCombineWhileStunGuaranteesMeleeHitOnly()
        {
            var hero = Unit("hero", "SKILL_FatalVerdict", "SKILL_Ignition", "SKILL_InevitableBlow");
            var enemy = Enemy();
            Call(enemy, "TakeHealthDamage", enemy.MaxHp * 0.5f);
            Effect(enemy, "AFFECTER_Burning", 10, 2, 50);
            Effect(enemy, "AFFECTER_Stun", 2);
            var attack = hero.EffectiveStats.WithValue(CombatStatType.DamageDealtBonus, 20);
            var defense = enemy.EffectiveStats.WithValue(CombatStatType.DamageTakenBonus, 20);
            float coefficient = 1f;
            bool hit = false;
            CombatSkillPassives.BeforeDamage(hero, enemy, _skills.GetSkill("SKILL_BasicSwordAttack"), ref attack, ref defense, ref coefficient, ref hit);
            Assert.That(coefficient, Is.EqualTo(1f));
            Assert.That(attack.DamageDealtBonus, Is.EqualTo(41f).Within(0.0001f));
            Assert.That(hit, Is.True);
            var actual = CombatCalculator.ResolveAttack(attack, defense, new NormalRandom(), CombatDamageType.Physical, coefficient, hit);
            Assert.That(actual.Damage, Is.EqualTo(attack.PhysicalAttackPower * 1.61f * 100f / (100f + defense.PhysicalResistance)).Within(0.0001f),
                "기존 가피증20+받피증20+잃은HP15+연소6은 한 가산항이며 계수 곱의 교차항이 없어야 합니다.");
            attack = hero.EffectiveStats;
            defense = enemy.EffectiveStats;
            coefficient = 1f;
            hit = false;
            CombatSkillPassives.BeforeDamage(hero, enemy, _skills.GetSkill("SKILL_BasicMagicMissile"), ref attack, ref defense, ref coefficient, ref hit);
            Assert.That(coefficient, Is.EqualTo(1f));
            Assert.That(attack.DamageDealtBonus, Is.EqualTo(hero.EffectiveStats.DamageDealtBonus + 6f).Within(0.0001f));
            Assert.That(hit, Is.False);
            attack = hero.EffectiveStats;
            defense = enemy.EffectiveStats;
            coefficient = 1f;
            hit = false;
            CombatSkillPassives.BeforeDamage(hero, enemy, _skills.GetSkill("SKILL_BattleCadence"), ref attack, ref defense, ref coefficient, ref hit);
            Assert.That(attack.DamageDealtBonus, Is.EqualTo(hero.EffectiveStats.DamageDealtBonus + 6f).Within(0.0001f));
            Assert.That(hit, Is.False, "패시브 발행 피해는 근접/물리로 취급하지 않습니다.");
        }

        [Test]
        public void FlowSeekerTriggersGuardBarrierOnceAndFortifiedUsesIntegerStacks()
        {
            var hero = Unit("hero", "SKILL_FlowSeeker", "SKILL_GuardBarrier", "SKILL_Fortified");
            var enemy = Enemy();
            CombatSkillPassives.OnSkillEnd(hero, _skills.GetSkill("SKILL_VerticalSlash"), enemy);
            Assert.That(Stack(enemy, "AFFECTER_ExposedFlaw"), Is.EqualTo(1));
            Assert.That(hero.Barrier, Is.EqualTo(17), "295×5%×1.2=17.7을 DS는 정수절삭합니다.");
            CombatSkillPassives.OnSkillEnd(hero, _skills.GetSkill("SKILL_BasicSwordAttack"), enemy);
            Assert.That(Stack(enemy, "AFFECTER_ExposedFlaw"), Is.EqualTo(1));
            Assert.That(hero.Barrier, Is.EqualTo(17));
        }

        [Test]
        public void SavageBarrierIsOncePerBattleAndCannotReviveADeadUnit()
        {
            var hero = Unit("hero", "SKILL_SavageBarrier", "SKILL_Fortified");
            Call(hero, "BeginSkillBattle");
            Call(hero, "TakeHealthDamage", 250f);
            CombatSkillPassives.CheckHealth(hero);
            Assert.That(hero.Barrier, Is.EqualTo(106));
            CombatSkillPassives.CheckHealth(hero);
            Assert.That(hero.Barrier, Is.EqualTo(106));
            Call(hero, "TakeHealthDamage", 1000f);
            CombatSkillPassives.CheckHealth(hero);
            Assert.That(hero.Hp, Is.Zero);
            Assert.That(hero.Barrier, Is.EqualTo(106));
        }

        [Test]
        public void LifeCycleObservesOtherMembersKillsAndUsesCeilingBeforeApplyingHealthBoost()
        {
            var owner = Unit("hero", "SKILL_LifeCycle");
            var killer = Unit("mira");
            CombatSkillPassives.OnKill(killer, new[] { owner, killer });
            float expected = (float)Math.Ceiling(owner.Stats.MagicAttackPower * 0.01f);
            Assert.That(Stack(owner, "AFFECTER_HealthBoost"), Is.EqualTo(expected));
            Assert.That(Stack(killer, "AFFECTER_HealthBoost"), Is.EqualTo(expected));
            Call(killer, "TakeHealthDamage", 1000f);
            CombatSkillPassives.OnKill(owner, new[] { owner, killer });
            Assert.That(killer.Hp, Is.Zero);
        }

        [Test]
        public void BattleCadencePreservesDsRemovalThenIncrementOrderAndCounterRequiresMeleeGuard()
        {
            var hero = Unit("hero", "SKILL_BattleCadence");
            var enemy = Enemy();
            var result = Result(CombatOutcome.Normal, 0);
            var steps = new List<int>();
            for (int attack = 1; attack <= 7; attack++)
            {
                int current = attack;
                CombatSkillPassives.AfterDamage(hero, enemy, _skills.GetSkill("SKILL_BasicSwordAttack"), result,
                    (source, target, skill, coefficient) =>
                    {
                        Assert.That(coefficient, Is.EqualTo(0.3f).Within(0.0001f));
                        steps.Add(current);
                    });
            }
            CollectionAssert.AreEqual(new[] { 3, 5, 7 }, steps);
            Call(enemy, "TakeHealthDamage", 10000f);
            CombatSkillPassives.AfterDamage(hero, enemy, _skills.GetSkill("SKILL_BasicSwordAttack"), result);
            Assert.That(Stack(hero, "AFFECTER_BattleCadence"), Is.EqualTo(2), "처치한 기본 공격도 원본 스택 증가 사건에 포함됩니다.");
            enemy = Enemy();
            var defender = Unit("hero", "SKILL_SwordCounter");
            Effect(defender, "AFFECTER_SwordCounter", 12);
            int counters = 0;
            Action<CombatantData, CombatantData, SkillDefinition, float> counter = (source, target, skill, coefficient) =>
            {
                Assert.That(source, Is.SameAs(defender));
                Assert.That(target, Is.SameAs(enemy));
                Assert.That(coefficient, Is.EqualTo(2));
                counters++;
            };
            CombatSkillPassives.AfterDamage(enemy, defender, null, Result(CombatOutcome.Guard, 1), counter);
            CombatSkillPassives.AfterDamage(enemy, defender, _skills.GetSkill("SKILL_BasicBowAttack"), Result(CombatOutcome.Guard, 1), counter);
            CombatSkillPassives.AfterDamage(enemy, defender, null, Result(CombatOutcome.Normal, 1), counter);
            Assert.That(counters, Is.EqualTo(1));
        }

        [Test]
        public void BarrierUsesDsIntegerAbsorptionAndWaveCycleReducesOnlyActualSkillCooldowns()
        {
            var hero = Unit("hero", "SKILL_WaveCicle");
            Call(hero, "SpendSkill", _skills.GetSkill("SKILL_VerticalSlash"));
            float cooldown = hero.GetCooldown("SKILL_VerticalSlash");
            float global = hero.GlobalCooldown;
            Call(hero, "AddBarrier", 10.9f, 5f);
            Assert.That(hero.Barrier, Is.EqualTo(10));
            Assert.That(hero.GetCooldown("SKILL_VerticalSlash"), Is.EqualTo(cooldown * 0.9f).Within(0.0001f));
            Assert.That(hero.GlobalCooldown, Is.EqualTo(global));
            float hp = hero.Hp;
            Call(hero, "TakeDamage", 3.9f);
            Assert.That(hero.Barrier, Is.EqualTo(7));
            Assert.That(hero.Hp, Is.EqualTo(hp));
            Call(hero, "TakeDamage", 8f);
            Assert.That(hero.Barrier, Is.Zero);
            Assert.That(hero.Hp, Is.EqualTo(hp - 1f));
        }

        [Test]
        public void UntypedPassiveDamageSkipsHitGuardCriticalResistanceAndTypedBonuses()
        {
            var attack = _calculator.Calculate(_base).WithValue(CombatStatType.CriticalChance, 100)
                .WithValue(CombatStatType.HitChance, -100).WithValue(CombatStatType.PhysicalDamageDealtBonus, 200);
            var defense = _calculator.Calculate(_base).WithValue(CombatStatType.GuardChance, 80)
                .WithValue(CombatStatType.PhysicalResistance, 500).WithValue(CombatStatType.MagicResistance, 500);
            var result = CombatCalculator.ResolveAttack(attack, defense, new NoRollRandom(), CombatDamageType.None,
                0.3f, true, true, attack.PhysicalAttackPower);
            Assert.That(result.Outcome, Is.EqualTo(CombatOutcome.Normal));
            Assert.That(result.Damage, Is.EqualTo(attack.PhysicalAttackPower * 0.3f).Within(0.0001f));
        }

        [Test]
        public void BarrierResurgeRestoresConsumedStacksAfterHealthDamageAndExpiredStacks()
        {
            var hero = Unit("hero", "SKILL_BarrierResurge");
            float hp = hero.Hp;
            Call(hero, "AddBarrier", 10f, 5f);
            Call(hero, "TakeDamage", 20f);
            Assert.That(hero.Hp, Is.EqualTo(hp - 8f).Within(0.0001f), "HP를 먼저 차감한 뒤 소모 스택 10의 20%를 회복합니다.");
            Call(hero, "AddBarrier", 10f, 1f);
            Call(hero, "TickSkills", 5f);
            Assert.That(hero.Hp, Is.EqualTo(hp - 6f).Within(0.0001f));
        }

        private sealed class NoRollRandom : IRandomSource
        {
            public int NextInt(int minimum, int maximum) => throw new AssertionException("무속성 피해는 난수를 뽑지 않습니다.");
            public float NextFloat(float minimum, float maximum) => throw new AssertionException("무속성 피해는 난수를 뽑지 않습니다.");
        }

        private CharacterProgress Progress(string id, int kills, params string[] learned)
        {
            var skillProgressType = typeof(CharacterProgress).Assembly.GetType("Refactor.Main.CharacterSkillProgress", true);
            var skillProgress = Activator.CreateInstance(skillProgressType,
                new object[] { 100, 100, learned, kills });
            return (CharacterProgress)Activator.CreateInstance(typeof(CharacterProgress), BindingFlags.Instance | BindingFlags.NonPublic,
                null, new object[] { id, 0, 12, 0f, new MajorStats(0, 0, 0, 0, 0, 0), _base,
                    skillProgress }, null);
        }

        private CombatantData Unit(string id, params string[] learned)
        {
            var progress = Progress(id, 0, learned);
            var stats = CombatSkillPassives.CalculateStats(_calculator, _base, _skills, progress);
            var unit = NewUnit(id, stats);
            Call(unit, "ConfigureSkills", _skills, progress);
            return unit;
        }

        private CombatantData Enemy()
        {
            var unit = NewUnit("test_rat", _calculator.CalculateEnemy("grey_rat"));
            Call(unit, "ConfigureEnemySkills", _skills);
            return unit;
        }

        private CombatantData NewUnit(string id, CombatStats stats)
        {
            return (CombatantData)Activator.CreateInstance(typeof(CombatantData), BindingFlags.Instance | BindingFlags.NonPublic,
                null, new object[] { id, id, stats, _combat.GetAttack("player"), 0, false, id == "hero" }, null);
        }

        private static void Effect(CombatantData unit, string key, float duration, float stacks = 1, float max = 1)
        {
            Call(unit, "AddEffect", key, duration, stacks, max, null);
        }

        private static float Stack(CombatantData unit, string key) => (float)Call(unit, "EffectStacks", key);

        private static CombatAttackResult Result(CombatOutcome outcome, float damage)
        {
            return (CombatAttackResult)Activator.CreateInstance(typeof(CombatAttackResult), BindingFlags.Instance | BindingFlags.NonPublic,
                null, new object[] { outcome, damage, 0f }, null);
        }

        private static object Call(CombatantData unit, string method, params object[] args)
        {
            var info = typeof(CombatantData).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(info, Is.Not.Null, method);
            return info.Invoke(unit, args);
        }

        private static string Read(string file) => File.ReadAllText(Path.Combine(Application.dataPath,
            "1_InternalTools/Util/DataSheetLoader/Table", file));

        private sealed class NormalRandom : IRandomSource
        {
            public int NextInt(int minInclusive, int maxExclusive) => minInclusive;
            public float NextFloat(float minInclusive, float maxInclusive) => 99f;
        }
    }
}
