using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GachaTrainers;
using NUnit.Framework;
using UnityEngine;

namespace P2.Tests
{
    public sealed class CrystalEffectTests
    {
        [Test]
        public void EmberNeedsThreeBasicHitsAndBurnsWithoutRetriggeringReflection()
        {
            var battle = ReadyBattle(CrystalKind.Ember, CrystalKind.Reflection);
            HitBasic(battle);
            HitBasic(battle);
            Assert.That(battle.Enemy.BurnTime, Is.Zero);
            Assert.That(battle.Player.BasicHitProgress, Is.EqualTo(2));
            HitBasic(battle);
            Assert.That(battle.Player.BasicHitProgress, Is.Zero);
            Assert.That(battle.Enemy.BurnTime, Is.GreaterThan(3.9f));
            float enemyHealth = battle.Enemy.Health;
            float playerHealth = battle.Player.Health;
            Set(battle.Enemy, nameof(BattleFighter.InvulnerableTime), 10f);
            Advance(battle, 4f);
            Assert.That(battle.Enemy.Health, Is.EqualTo(enemyHealth - 12f).Within(0.0001f));
            Assert.That(battle.Player.Health, Is.EqualTo(playerHealth), "Burn must not trigger reflection.");
            Assert.That(battle.Enemy.BurnTime, Is.Zero);
        }

        [Test]
        public void EmberIgnoresDodgedHitsAndSkillHitsButCountsShieldedHits()
        {
            var battle = ReadyBattle(CrystalKind.Ember);
            Set(battle.Enemy, nameof(BattleFighter.InvulnerableTime), 1f);
            HitBasic(battle);
            Assert.That(battle.Player.BasicHitProgress, Is.Zero);
            Set(battle.Enemy, nameof(BattleFighter.InvulnerableTime), 0f);
            Assert.That(battle.TrySpecial(), Is.True);
            LandPlayerShots(battle);
            Assert.That(battle.Player.BasicHitProgress, Is.Zero);
            Set(battle.Enemy, nameof(BattleFighter.ShieldHealth), 20f);
            Set(battle.Enemy, nameof(BattleFighter.BarrierTime), 5f);
            float health = battle.Enemy.Health;
            HitBasic(battle);
            Assert.That(battle.Player.BasicHitProgress, Is.EqualTo(1));
            Assert.That(battle.Enemy.Health, Is.EqualTo(health));
        }

        [Test]
        public void EmberRefreshesDurationWithoutStackingAndBenchBurnDoesNoDamage()
        {
            var battle = ReadyBattle(CrystalKind.Ember);
            for (int index = 0; index < 3; index++)
            {
                HitBasic(battle);
            }
            Advance(battle, 1f);
            for (int index = 0; index < 3; index++)
            {
                HitBasic(battle);
            }
            Assert.That(battle.Enemy.BurnTime, Is.GreaterThan(3.9f));
            float health = battle.Enemy.Health;
            Advance(battle, 1f);
            Assert.That(battle.Enemy.Health, Is.EqualTo(health - 3f).Within(0.0001f));

            // A player projectile uses the same burn application path; transfer the burning fighter
            // into a reserve slot to exercise actual model bench ticking without a second AI harness.
            var burning = battle.Enemy;
            var players = (List<BattleFighter>)typeof(BattleDataContainer)
                .GetField("_playerTeam", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(battle);
            players[1] = burning;
            ReplaceEnemy(battle, CreateFighter(null));
            health = burning.Health;
            float time = burning.BurnTime;
            Advance(battle, 1f);
            Assert.That(burning.Health, Is.EqualTo(health));
            Assert.That(burning.BurnTime, Is.EqualTo(time - 1f).Within(0.0001f));
        }

        [Test]
        public void ProtectionUsesMissingHealthWithoutHealingAndCannotRefreshByFastSwapping()
        {
            var battle = ReadyBattle(CrystalKind.Protection);
            var protectedFighter = battle.Player;
            Assert.That(protectedFighter.PassiveCooldown, Is.Zero, "Full health must not consume the entry passive.");
            Set(protectedFighter, nameof(BattleFighter.Health), 90f);
            Assert.That(battle.TrySwitchPlayer(1), Is.True);
            Assert.That(battle.TrySwitchPlayer(0), Is.True);
            Assert.That(protectedFighter.Health, Is.EqualTo(90f));
            Assert.That(protectedFighter.ShieldHealth, Is.EqualTo(30f));
            Assert.That(protectedFighter.BarrierTime, Is.EqualTo(5f));
            Assert.That(protectedFighter.PassiveCooldown, Is.EqualTo(12f));
            Assert.That(battle.TrySwitchPlayer(1), Is.True);
            Advance(battle, 1f);
            Assert.That(battle.TrySwitchPlayer(0), Is.True);
            Assert.That(protectedFighter.BarrierTime, Is.EqualTo(4f).Within(0.0001f));
            Assert.That(protectedFighter.PassiveCooldown, Is.EqualTo(11f).Within(0.0001f));
            Assert.That(battle.TrySwitchPlayer(1), Is.True);
            Advance(battle, 5f);
            Assert.That(protectedFighter.ShieldHealth, Is.Zero);
            Assert.That(protectedFighter.Health, Is.EqualTo(90f));
            Assert.That(protectedFighter.PassiveCooldown, Is.EqualTo(6f).Within(0.001f));
        }

        [Test]
        public void ReflectionUsesActualHealthLossAfterBarrierAndCannotBounceAgain()
        {
            var battle = ReadyBattle(CrystalKind.Reflection, CrystalKind.Reflection);
            Set(battle.Enemy, nameof(BattleFighter.ShieldHealth), 5f);
            Set(battle.Enemy, nameof(BattleFighter.BarrierTime), 5f);
            HitBasic(battle);
            Assert.That(battle.Enemy.ShieldHealth, Is.Zero);
            Assert.That(battle.Enemy.Health, Is.EqualTo(146f));
            Assert.That(battle.Player.Health, Is.EqualTo(149f), "Only the 4 HP actually lost reflects, once.");
        }

        [Test]
        public void ProjectileReflectionReturnsToOriginalBenchedShooterWithoutEndingActiveCombat()
        {
            var battle = ReadyBattle(CrystalKind.Ember, CrystalKind.Reflection);
            var shooter = battle.Player;
            Set(shooter, nameof(BattleFighter.Health), 1f);
            Assert.That(battle.TryBasicAttack(), Is.True);
            Assert.That(battle.TrySwitchPlayer(1), Is.True);
            var active = battle.Player;
            float activeHealth = active.Health;
            LandPlayerShots(battle);
            Assert.That(shooter.Health, Is.Zero);
            Assert.That(shooter.AnimationState, Is.EqualTo(CreatureAnimationState.Idle));
            Assert.That(shooter.BasicHitProgress, Is.EqualTo(1));
            Assert.That(active.Health, Is.EqualTo(activeHealth));
            Assert.That(battle.ActivePlayerIndex, Is.EqualTo(1));
            Assert.That(battle.Phase, Is.EqualTo(BattlePhase.Fighting));
            Assert.That(battle.TrySwitchPlayer(0), Is.False);
        }

        [Test]
        public void DeathThroesSnapshotsMissingHealthWhenBasicAndSkillAreFired()
        {
            var battle = ReadyBattle(CrystalKind.DeathThroes);
            Set(battle.Player, nameof(BattleFighter.Health), 75f);
            Assert.That(battle.TryBasicAttack(), Is.True);
            Set(battle.Player, nameof(BattleFighter.Health), 150f);
            LandPlayerShots(battle);
            Assert.That(battle.Enemy.Health, Is.EqualTo(150f - 9f * 1.2f).Within(0.0001f));
            Set(battle.Player, nameof(BattleFighter.Health), 75f);
            Assert.That(battle.TrySpecial(), Is.True);
            Set(battle.Player, nameof(BattleFighter.Health), 150f);
            LandPlayerShots(battle);
            Assert.That(battle.Enemy.Health, Is.EqualTo(150f - 27f * 1.2f).Within(0.0001f));
        }

        [Test]
        public void SinkThirdBasicUsesTargetsMissingHealthAndKeepsProgressAcrossSwitches()
        {
            var battle = ReadyBattle(CrystalKind.Sink);
            HitBasic(battle);
            HitBasic(battle);
            Assert.That(battle.TrySwitchPlayer(1), Is.True);
            Assert.That(battle.TrySwitchPlayer(0), Is.True);
            Assert.That(battle.Player.BasicHitProgress, Is.EqualTo(2));
            Set(battle.Enemy, nameof(BattleFighter.Health), 75f);
            HitBasic(battle);
            Assert.That(battle.Enemy.Health, Is.EqualTo(75f - 9f * 1.4f).Within(0.0001f));
            Assert.That(battle.Player.BasicHitProgress, Is.Zero);
        }

        [Test]
        public void CapitalExecutesAfterBasicOrSkillDamageButCannotBypassAFullBarrier()
        {
            var basicBattle = ReadyBattle(CrystalKind.Capital);
            var basicTarget = basicBattle.Enemy;
            Set(basicTarget, nameof(BattleFighter.Health), 30f);
            HitBasic(basicBattle);
            Assert.That(basicTarget.Health, Is.Zero);
            Assert.That(basicBattle.Phase, Is.EqualTo(BattlePhase.Summoning));

            var skillBattle = ReadyBattle(CrystalKind.Capital);
            var skillTarget = skillBattle.Enemy;
            Set(skillTarget, nameof(BattleFighter.Health), 40f);
            Assert.That(skillBattle.TrySpecial(), Is.True);
            LandPlayerShots(skillBattle);
            Assert.That(skillTarget.Health, Is.Zero);

            var shieldBattle = ReadyBattle(CrystalKind.Capital);
            Set(shieldBattle.Enemy, nameof(BattleFighter.Health), 20f);
            Set(shieldBattle.Enemy, nameof(BattleFighter.ShieldHealth), 20f);
            Set(shieldBattle.Enemy, nameof(BattleFighter.BarrierTime), 5f);
            HitBasic(shieldBattle);
            Assert.That(shieldBattle.Enemy.Health, Is.EqualTo(20f));
        }

        private static BattleDataContainer ReadyBattle(CrystalKind playerCrystal, CrystalKind? enemyCrystal = null)
        {
            var run = new RunDataContainer();
            Assert.That(run.TryPurchase(GachaMachineKind.Creature, _ => GachaCatalog.GetCreatureRollStart(CreatureKind.MossTurtle)), Is.True);
            Assert.That(run.TryPurchase(GachaMachineKind.Crystal, _ => GachaCatalog.GetCrystalRollStart(playerCrystal)), Is.True);
            Assert.That(run.TryEquipCrystal(playerCrystal), Is.True);
            Assert.That(run.TryPurchase(GachaMachineKind.Creature, _ => GachaCatalog.GetCreatureRollStart(CreatureKind.EmberFox)), Is.True);
            var battle = new BattleDataContainer(run.Party);
            ReplaceEnemy(battle, CreateFighter(enemyCrystal));
            Advance(battle, 1f);
            return battle;
        }

        private static BattleFighter CreateFighter(CrystalKind? crystal)
        {
            var run = new RunDataContainer();
            run.TryPurchase(GachaMachineKind.Creature, _ => GachaCatalog.GetCreatureRollStart(CreatureKind.MossTurtle));
            if (crystal.HasValue)
            {
                run.TryPurchase(GachaMachineKind.Crystal, _ => GachaCatalog.GetCrystalRollStart(crystal.Value));
                run.TryEquipCrystal(crystal.Value);
            }
            return new BattleDataContainer(run.Party).Player;
        }

        private static void ReplaceEnemy(BattleDataContainer battle, BattleFighter enemy)
        {
            var enemies = (List<BattleFighter>)typeof(BattleDataContainer)
                .GetField("_enemyTeam", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(battle);
            enemies[0] = enemy;
            Set(enemy, nameof(BattleFighter.Position), new Vector2(4.7f, -0.6f));
            Set(enemy, nameof(BattleFighter.BasicCooldown), 100f);
            Set(enemy, nameof(BattleFighter.SpecialCooldown), 100f);
            Set(enemy, nameof(BattleFighter.DodgeCooldown), 100f);
        }

        private static void HitBasic(BattleDataContainer battle)
        {
            Set(battle.Player, nameof(BattleFighter.BasicCooldown), 0f);
            Assert.That(battle.TryBasicAttack(), Is.True);
            LandPlayerShots(battle);
        }

        private static void LandPlayerShots(BattleDataContainer battle)
        {
            // Place existing shots at impact; the production collision/damage/passive pipeline still runs.
            foreach (var projectile in battle.Projectiles.Where(projectile => projectile.IsPlayer))
            {
                typeof(BattleProjectile).GetProperty(nameof(BattleProjectile.Position)).SetValue(projectile, battle.Enemy.Position);
            }
            battle.Tick(1f / 120f);
        }

        private static void Advance(BattleDataContainer battle, float seconds)
        {
            for (int index = 0; index < Mathf.RoundToInt(seconds * 120f); index++)
            {
                battle.Tick(1f / 120f);
            }
        }

        private static void Set(BattleFighter fighter, string property, object value)
        {
            typeof(BattleFighter).GetProperty(property, BindingFlags.Instance | BindingFlags.Public).SetValue(fighter, value);
        }
    }
}
