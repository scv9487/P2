using System;
using System.Linq;
using System.Reflection;
using GachaTrainers;
using NUnit.Framework;
using UnityEngine;

namespace P2.Tests
{
    public sealed class CombatProgressionTests
    {
        [Test]
        public void ManualBasicAndSkillFollowMouseAwayFromEnemyAndAutoStillTracksEnemy()
        {
            var battle = Ready();
            Vector2 target = battle.Player.Position + Vector2.up * 3;
            battle.SetAim(target);
            Assert.That(battle.TryBasicAttack(), Is.True);
            Assert.That(battle.Projectiles.Last().Direction, Is.EqualTo(Vector2.up));
            Assert.That(battle.TrySpecial(), Is.True);
            Assert.That(battle.Projectiles.Where(x => x.IsPlayer).All(x => x.Direction.y > 0.9f), Is.True);
            battle.SetMovement(Vector2.left);
            battle.Tick(0.05f);
            Assert.That(Vector2.Dot(battle.Player.Facing, (target - battle.Player.Position).normalized), Is.GreaterThan(0.999f));
            battle.SetControlMode(BattleControlMode.Auto);
            battle.Tick(0.05f);
            Assert.That(battle.AimPoint, Is.Null);
            Assert.That(Vector2.Dot(battle.Player.Facing, (battle.Enemy.Position - battle.Player.Position).normalized), Is.GreaterThan(0.99f));
        }

        [Test]
        public void PauseAndInvalidAimCannotChangeFacingOrConsumeSkillEnergy()
        {
            var battle = Ready();
            var facing = battle.Player.Facing;
            battle.SetAim(new Vector2(float.NaN, 0));
            Assert.That(battle.Player.Facing, Is.EqualTo(facing));
            battle.SetPaused(true);
            battle.SetAim(battle.Player.Position + Vector2.up);
            Assert.That(battle.TrySpecial(), Is.False);
            Assert.That(battle.Player.Energy, Is.EqualTo(100));
            Assert.That(battle.Player.Facing, Is.EqualTo(facing));
        }

        [Test]
        public void ArcaneFormUsesMagicPowerReducedCooldownAndEnergyResource()
        {
            var battle = Ready(null, 4);
            Assert.That(battle.TrySpecial(), Is.True);
            Assert.That(battle.Player.SpecialCooldown, Is.EqualTo(6f / 1.3f).Within(0.0001f));
            Assert.That(battle.Player.Energy, Is.EqualTo(100f));
            Assert.That(Damage(battle.Projectiles[0]), Is.EqualTo(battle.Player.Stats.MagicAttackPower * 1.15f).Within(0.0001f));
            Set(battle.Player, "SpecialCooldown", 0f);
            Set(battle.Player, "Energy", 24f);
            Assert.That(battle.TrySpecial(), Is.False);
            battle.Tick(0.25f);
            Assert.That(battle.Player.Energy, Is.EqualTo(26f).Within(0.001f));
            Assert.That(battle.TrySpecial(), Is.True);
        }

        [Test]
        public void PhysicalAndMagicResistanceUseDSFormulaAndGuardPrecedesCrit()
        {
            var battle = Ready(null, 0, 0f);
            Set(battle.Player.Stats, "CriticalChance", 100f);
            Set(battle.Enemy.Stats, "PhysicalResistance", 100f);
            Set(battle.Enemy.Stats, "GuardChance", 100f);
            float before = battle.Enemy.Health;
            Hit(battle);
            Assert.That(before - battle.Enemy.Health, Is.EqualTo(18f * 0.5f * 0.5f).Within(0.0001f));
            Assert.That(battle.Enemy.LastHitResult, Is.EqualTo("가드"));
        }

        [Test]
        public void MagicSmiteAddsOneSecondaryMagicHitWithoutRecursion()
        {
            var battle = Ready(CrystalKind.MagicSmite);
            Set(battle.Enemy.Stats, "MagicResistance", 100f);
            float before = battle.Enemy.Health;
            Hit(battle);
            Assert.That(before - battle.Enemy.Health, Is.EqualTo(18f + 18f * 0.2f * 0.5f).Within(0.0001f));
        }

        [TestCase(CrystalKind.Giant, true)]
        [TestCase(CrystalKind.Divine, false)]
        public void MaximumHealthEngravingsAffectTheirAttackType(CrystalKind crystal, bool basic)
        {
            var battle = Ready(crystal);
            if (basic)
            {
                battle.TryBasicAttack();
                Assert.That(Damage(battle.Projectiles[0]), Is.EqualTo(18f + 85f * 0.03f).Within(0.0001f));
            }
            else
            {
                battle.TrySpecial();
                Assert.That(Damage(battle.Projectiles[0]), Is.EqualTo((18f + 85f * 0.03f) * 1.15f).Within(0.0001f));
            }
        }

        [Test]
        public void FlintCriticalBurnsButMissDoesNotCountAsAHit()
        {
            var battle = Ready(CrystalKind.Flint, 0, 0f);
            Hit(battle);
            Assert.That(battle.Enemy.BurnTime, Is.EqualTo(4f));
            Assert.That(battle.Enemy.LastHitResult, Is.EqualTo("치명타"));
            var miss = Ready(CrystalKind.Ember, 0, 99f);
            Set(miss.Enemy.Stats, "DodgeChance", 100f);
            Hit(miss);
            Assert.That(miss.Enemy.Health, Is.EqualTo(miss.Enemy.MaxHealth));
            Assert.That(miss.Player.BasicHitProgress, Is.Zero);
        }

        [Test]
        public void JesterDodgeSlowsAttackerAndElasticityGuardHealsAfterDamage()
        {
            var battle = Ready(CrystalKind.Jester);
            battle.TryDodge();
            EnemyHit(battle);
            Assert.That(battle.Enemy.SlowTime, Is.EqualTo(2.5f));
            Assert.That(battle.Player.Health, Is.EqualTo(battle.Player.MaxHealth));
            var guard = Ready(CrystalKind.Elasticity, 0, 0f);
            Set(guard.Player, "Health", 50f);
            EnemyHit(guard);
            Assert.That(guard.Player.Health, Is.EqualTo(41f + (85f - 41f) * 0.08f).Within(0.001f));
            Assert.That(guard.Player.IronWallTime, Is.EqualTo(1.5f));
        }

        [Test]
        public void LifeStealUsesActualHealthLossAndRegenerationStopsOnBenchAndPause()
        {
            var battle = Ready(null, 3, 99f);
            Set(battle.Player, "Health", 20f);
            Set(battle.Enemy, "Health", 2f);
            Hit(battle);
            Assert.That(battle.Player.Health, Is.EqualTo(20f + 2f * 0.12f).Within(0.001f));
            var regen = Ready(null, 6);
            Set(regen.Player, "Health", 20f);
            regen.Tick(0.2f);
            Assert.That(regen.Player.Health, Is.EqualTo(20f + regen.Player.MaxHealth * 0.015f * 0.2f).Within(0.002f));
            float health = regen.Player.Health;
            regen.SetPaused(true);
            regen.Tick(0.2f);
            Assert.That(regen.Player.Health, Is.EqualTo(health));
        }

        private static BattleDataContainer Ready(CrystalKind? crystal = null, int node = 0, float roll = 99f)
        {
            var creature = (PartyCreature)Activator.CreateInstance(typeof(PartyCreature), BindingFlags.NonPublic | BindingFlags.Instance,
                null, new object[] { CreatureKind.EmberFox, crystal, node, 1 }, null);
            var battle = new BattleDataContainer(new[] { creature }, false, () => roll);
            for (int i = 0; i < 4; i++)
            {
                battle.Tick(0.25f);
            }
            Set(battle.Enemy, "BasicCooldown", 100f);
            Set(battle.Enemy, "SpecialCooldown", 100f);
            return battle;
        }

        private static void Hit(BattleDataContainer battle)
        {
            Assert.That(battle.TryBasicAttack(), Is.True);
            typeof(BattleDataContainer).GetMethod("ApplyDirectHit", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(battle, new object[] { battle.Projectiles.Last(x => x.IsPlayer), battle.Enemy });
        }

        private static void EnemyHit(BattleDataContainer battle)
        {
            typeof(BattleDataContainer).GetMethod("Basic", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(battle, new object[] { battle.Enemy, battle.Player, false });
            typeof(BattleDataContainer).GetMethod("ApplyDirectHit", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(battle, new object[] { battle.Projectiles.Last(x => !x.IsPlayer), battle.Player });
        }

        private static float Damage(BattleProjectile projectile)
        {
            return (float)typeof(BattleProjectile).GetProperty("Damage", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(projectile);
        }

        private static void Set(object target, string property, object value)
        {
            target.GetType().GetProperty(property).SetValue(target, value);
        }
    }
}
