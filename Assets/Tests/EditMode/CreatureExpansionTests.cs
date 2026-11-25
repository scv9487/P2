using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GachaTrainers;
using NUnit.Framework;
using UnityEngine;

namespace P2.Tests
{
    public sealed class CreatureExpansionTests
    {
        [Test]
        public void SixCreaturePoolKeepsThreePartySlotsAndTheOriginalTestOpponent()
        {
            Assert.That(GachaCatalog.Creatures.Count, Is.EqualTo(6));
            Assert.That(Enum.GetValues(typeof(CreatureKind)).Length, Is.EqualTo(6));
            foreach (var definition in GachaCatalog.Creatures)
            {
                var purchase = new RunDataContainer();
                Assert.That(purchase.TryPurchase(GachaMachineKind.Creature, count =>
                {
                    Assert.That(count, Is.EqualTo(GachaCatalog.CreatureWeightTotal));
                    return GachaCatalog.GetCreatureRollStart(definition.Kind);
                }), Is.True);
                Assert.That(purchase.Party.Single().Kind, Is.EqualTo(definition.Kind));
                Assert.That(purchase.Money, Is.EqualTo(500));
            }

            var party = new RunDataContainer();
            foreach (var kind in new[] { CreatureKind.FrostOwl, CreatureKind.TideOtter, CreatureKind.StoneBoar })
            {
                Assert.That(party.TryPurchase(GachaMachineKind.Creature, _ => GachaCatalog.GetCreatureRollStart(kind)), Is.True);
            }
            Assert.That(party.TryPurchase(GachaMachineKind.Creature, _ => 0), Is.True);
            Assert.That(party.Owned.Count, Is.EqualTo(4));
            Assert.That(party.Party.Count, Is.EqualTo(3));
            Assert.That(party.Money, Is.EqualTo(200));
            var battle = new BattleDataContainer(party.Party);
            var originalKinds = new[] { CreatureKind.EmberFox, CreatureKind.MossTurtle, CreatureKind.VoltRabbit };
            Assert.That(battle.EnemyTeam.Select(fighter => fighter.Kind), Is.EqualTo(originalKinds));
            Assert.That(RunDataContainer.CreateBattleTestRun().Party.Select(creature => creature.Kind), Is.EqualTo(originalKinds));
        }

        [TestCase(CreatureKind.EmberFox)]
        [TestCase(CreatureKind.MossTurtle)]
        [TestCase(CreatureKind.VoltRabbit)]
        [TestCase(CreatureKind.FrostOwl)]
        [TestCase(CreatureKind.TideOtter)]
        [TestCase(CreatureKind.StoneBoar)]
        public void EveryCreatureCanMoveAttackAndUseItsSkillUnderAutomaticControl(CreatureKind kind)
        {
            var battle = ReadyBattle(kind);
            Vector2 start = new(-2f, -1f);
            Set(battle.Player, nameof(BattleFighter.Position), start);
            Set(battle.Enemy, nameof(BattleFighter.Position), start + Vector2.right * BattleCatalog.Get(kind).BasicRange * 0.7f);
            battle.SetControlMode(BattleControlMode.Auto);
            Advance(battle, 0.05f);
            Assert.That(battle.Player.Position, Is.Not.EqualTo(start));
            Assert.That(battle.Player.BasicCooldown, Is.GreaterThan(0f));
            Assert.That(battle.Player.SpecialCooldown, Is.GreaterThan(0f));
            Assert.That(battle.Projectiles.Any(projectile => projectile.IsPlayer), Is.True);
            Assert.That(battle.TrySpecial(), Is.False, "Automatic mode still rejects manual skill requests.");
        }

        [Test]
        public void FrostSkillSlowsThroughBarriersAndRefreshesButDodgedOrBasicHitsDoNot()
        {
            var battle = ReadyBattle(CreatureKind.FrostOwl, CrystalKind.Ember);
            Assert.That(battle.TryBasicAttack(), Is.True);
            LandShots(battle, true);
            Assert.That(battle.Enemy.SlowTime, Is.Zero);
            Assert.That(battle.Player.BasicHitProgress, Is.EqualTo(1));
            float health = battle.Enemy.Health;
            Set(battle.Enemy, nameof(BattleFighter.ShieldHealth), 1000f);
            Set(battle.Enemy, nameof(BattleFighter.BarrierTime), 10f);
            Assert.That(battle.TrySpecial(), Is.True);
            LandShots(battle, true);
            Assert.That(battle.Enemy.Health, Is.EqualTo(health));
            Assert.That(battle.Enemy.ShieldHealth, Is.EqualTo(1000f - 13f * 1.6f).Within(0.0001f));
            Assert.That(battle.Enemy.SlowTime, Is.EqualTo(2.5f));
            Assert.That(battle.Enemy.MovementSpeedMultiplier, Is.EqualTo(0.6f));
            Assert.That(battle.Player.BasicHitProgress, Is.EqualTo(1), "Skill hits must not increment an engraving's basic-hit counter.");

            Advance(battle, 0.5f);
            Set(battle.Player, nameof(BattleFighter.SpecialCooldown), 0f);
            Assert.That(battle.TrySpecial(), Is.True);
            LandShots(battle, true);
            Assert.That(battle.Enemy.SlowTime, Is.EqualTo(2.5f));
            Assert.That(battle.Enemy.MovementSpeedMultiplier, Is.EqualTo(0.6f));

            Set(battle.Enemy, nameof(BattleFighter.InvulnerableTime), 1f);
            Set(battle.Player, nameof(BattleFighter.SpecialCooldown), 0f);
            Assert.That(battle.TrySpecial(), Is.True);
            LandShots(battle, true);
            Assert.That(battle.Enemy.SlowTime, Is.LessThan(2.5f), "A dodged hit cannot refresh an existing slow.");
            Assert.That(battle.Enemy.ShieldHealth, Is.EqualTo(1000f - 13f * 1.6f * 2f).Within(0.0001f));
        }

        [Test]
        public void SlowAffectsWalkingButNotDodgeAndExpiresOnBenchWithoutAdvancingWhilePaused()
        {
            var battle = ReadyBattle(CreatureKind.MossTurtle);
            var enemyOwl = CreateFighter(CreatureKind.FrostOwl);
            ReplaceEnemy(battle, enemyOwl);
            Set(battle.Player, nameof(BattleFighter.Position), new Vector2(-2f, -1f));
            Set(enemyOwl, nameof(BattleFighter.Position), new Vector2(1f, -1f));
            Set(enemyOwl, nameof(BattleFighter.SpecialCooldown), 0f);
            battle.Tick(1f / 120f);
            Assert.That(battle.Projectiles.Any(projectile => !projectile.IsPlayer), Is.True);
            LandShots(battle, false);
            var slowed = battle.Player;
            Assert.That(slowed.SlowTime, Is.EqualTo(2.5f));
            Set(enemyOwl, nameof(BattleFighter.Position), new Vector2(7f, 2f));
            DisableActions(enemyOwl);

            Vector2 walkingStart = slowed.Position;
            battle.SetMovement(Vector2.down);
            Advance(battle, 0.1f);
            Assert.That(Vector2.Distance(walkingStart, slowed.Position), Is.EqualTo(2.7f * 0.6f * 0.1f).Within(0.0001f));
            Vector2 dodgeStart = slowed.Position;
            battle.SetMovement(Vector2.right);
            Assert.That(battle.TryDodge(), Is.True);
            Advance(battle, 0.05f);
            Assert.That(Vector2.Distance(dodgeStart, slowed.Position), Is.EqualTo(8f * 0.05f).Within(0.0001f));

            battle.SetPaused(true);
            float pausedSlow = slowed.SlowTime;
            Advance(battle, 1f);
            Assert.That(slowed.SlowTime, Is.EqualTo(pausedSlow));
            battle.SetPaused(false);
            Assert.That(battle.TrySwitchPlayer(1), Is.True);
            Advance(battle, 0.5f);
            Assert.That(slowed.SlowTime, Is.EqualTo(pausedSlow - 0.5f).Within(0.0001f));
            Assert.That(battle.TrySwitchPlayer(0), Is.True);
            Assert.That(slowed.MovementSpeedMultiplier, Is.EqualTo(0.6f));
            Advance(battle, 3f);
            Assert.That(slowed.SlowTime, Is.Zero);
            Assert.That(slowed.MovementSpeedMultiplier, Is.EqualTo(1f));
        }

        [Test]
        public void OtterSkillHealsOnceWithinMaximumHealthAndStillFiresARealWave()
        {
            var battle = ReadyBattle(CreatureKind.TideOtter);
            var otter = battle.Player;
            Set(otter, nameof(BattleFighter.Health), 50f);
            float enemyHealth = battle.Enemy.Health;
            Assert.That(battle.TrySpecial(), Is.True);
            Assert.That(otter.Health, Is.EqualTo(50f + 105f * 0.18f).Within(0.0001f));
            Assert.That(battle.TrySpecial(), Is.False);
            Assert.That(otter.Health, Is.EqualTo(50f + 105f * 0.18f).Within(0.0001f));
            LandShots(battle, true);
            Assert.That(battle.Enemy.Health, Is.EqualTo(enemyHealth - 12f * 1.4f).Within(0.0001f));
            Assert.That(battle.TrySwitchPlayer(1), Is.True);
            Advance(battle, 0.1f);
            Assert.That(battle.TrySwitchPlayer(0), Is.True);
            Assert.That(battle.TrySpecial(), Is.False, "Switching cannot reset healing's cooldown.");

            Set(otter, nameof(BattleFighter.SpecialCooldown), 0f);
            Set(otter, nameof(BattleFighter.Health), 100f);
            Assert.That(battle.TrySpecial(), Is.True);
            Assert.That(otter.Health, Is.EqualTo(105f));
            Set(otter, nameof(BattleFighter.SpecialCooldown), 0f);
            Set(otter, nameof(BattleFighter.Health), 0f);
            Assert.That(battle.TrySpecial(), Is.False);
            Assert.That(otter.Health, Is.Zero, "Healing cannot revive a defeated fighter.");
        }

        [Test]
        public void InjuredAutomaticOtterCanHealOutsideAttackRange()
        {
            var battle = ReadyBattle(CreatureKind.TideOtter);
            Set(battle.Player, nameof(BattleFighter.Health), 70f);
            Set(battle.Player, nameof(BattleFighter.Position), new Vector2(-7f, -1f));
            Set(battle.Enemy, nameof(BattleFighter.Position), new Vector2(7f, -1f));
            battle.SetControlMode(BattleControlMode.Auto);
            battle.Tick(1f / 120f);
            Assert.That(battle.Player.Health, Is.EqualTo(70f + 105f * 0.18f).Within(0.0001f));
            Assert.That(battle.Player.SpecialCooldown, Is.EqualTo(9f));
            Assert.That(battle.Player.BasicCooldown, Is.Zero);
        }

        [Test]
        public void BoarSkillKnockbackWorksAfterSwitchingAndThroughShieldButStaysInsideArena()
        {
            var battle = ReadyBattle(CreatureKind.StoneBoar);
            Set(battle.Player, nameof(BattleFighter.Position), new Vector2(-3f, -0.6f));
            Set(battle.Enemy, nameof(BattleFighter.Position), new Vector2(2f, -0.6f));
            Set(battle.Enemy, nameof(BattleFighter.ShieldHealth), 1000f);
            Set(battle.Enemy, nameof(BattleFighter.BarrierTime), 10f);
            Assert.That(battle.TrySpecial(), Is.True);
            Assert.That(battle.TrySwitchPlayer(1), Is.True);
            Vector2 before = battle.Enemy.Position;
            LandShots(battle, true);
            Assert.That(battle.Enemy.Position.x - before.x, Is.InRange(1.45f, 1.55f));
            Assert.That(battle.Enemy.Health, Is.EqualTo(battle.Enemy.MaxHealth));
            Assert.That(battle.Enemy.ShieldHealth, Is.EqualTo(1000f - 16f * 2.2f).Within(0.0001f));

            Assert.That(battle.TrySwitchPlayer(0), Is.True);
            Set(battle.Player, nameof(BattleFighter.SpecialCooldown), 0f);
            Set(battle.Enemy, nameof(BattleFighter.Position), new Vector2(BattleCatalog.MaxX - 0.05f, -0.6f));
            Assert.That(battle.TrySpecial(), Is.True);
            LandShots(battle, true);
            Assert.That(battle.Enemy.Position.x, Is.EqualTo(BattleCatalog.MaxX));
        }

        [Test]
        public void BoarBasicAndDodgedOrLethalSkillHitsDoNotKnockBack()
        {
            var battle = ReadyBattle(CreatureKind.StoneBoar);
            Assert.That(battle.TryBasicAttack(), Is.True);
            Vector2 before = battle.Enemy.Position;
            LandShots(battle, true);
            Assert.That(Vector2.Distance(before, battle.Enemy.Position), Is.LessThan(0.04f));

            Set(battle.Enemy, nameof(BattleFighter.InvulnerableTime), 1f);
            float health = battle.Enemy.Health;
            Assert.That(battle.TrySpecial(), Is.True);
            before = battle.Enemy.Position;
            LandShots(battle, true);
            Assert.That(Vector2.Distance(before, battle.Enemy.Position), Is.LessThan(0.04f));
            Assert.That(battle.Enemy.Health, Is.EqualTo(health));

            Set(battle.Enemy, nameof(BattleFighter.InvulnerableTime), 0f);
            Set(battle.Enemy, nameof(BattleFighter.Health), 1f);
            Set(battle.Player, nameof(BattleFighter.SpecialCooldown), 0f);
            var defeated = battle.Enemy;
            before = defeated.Position;
            Assert.That(battle.TrySpecial(), Is.True);
            LandShots(battle, true);
            Assert.That(defeated.Health, Is.Zero);
            Assert.That(Vector2.Distance(before, defeated.Position), Is.LessThan(0.04f));
            Assert.That(battle.Phase, Is.EqualTo(BattlePhase.Summoning));
        }

        private static BattleDataContainer ReadyBattle(CreatureKind kind, CrystalKind? crystal = null)
        {
            var run = new RunDataContainer();
            Assert.That(run.TryPurchase(GachaMachineKind.Creature, _ => GachaCatalog.GetCreatureRollStart(kind)), Is.True);
            if (crystal.HasValue)
            {
                Assert.That(run.TryPurchase(GachaMachineKind.Crystal, _ => GachaCatalog.GetCrystalRollStart(crystal.Value)), Is.True);
                Assert.That(run.TryEquipCrystal(crystal.Value), Is.True);
            }
            Assert.That(run.TryPurchase(GachaMachineKind.Creature, _ => GachaCatalog.GetCreatureRollStart(CreatureKind.MossTurtle)), Is.True);
            var battle = new BattleDataContainer(run.Party);
            DisableActions(battle.Enemy);
            Advance(battle, 1f);
            return battle;
        }

        private static BattleFighter CreateFighter(CreatureKind kind)
        {
            var run = new RunDataContainer();
            run.TryPurchase(GachaMachineKind.Creature, _ => GachaCatalog.GetCreatureRollStart(kind));
            return new BattleDataContainer(run.Party).Player;
        }

        private static void ReplaceEnemy(BattleDataContainer battle, BattleFighter enemy)
        {
            var enemies = (List<BattleFighter>)typeof(BattleDataContainer)
                .GetField("_enemyTeam", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(battle);
            enemies[0] = enemy;
            DisableActions(enemy);
        }

        private static void DisableActions(BattleFighter fighter)
        {
            Set(fighter, nameof(BattleFighter.BasicCooldown), 100f);
            Set(fighter, nameof(BattleFighter.SpecialCooldown), 100f);
            Set(fighter, nameof(BattleFighter.DodgeCooldown), 100f);
        }

        private static void LandShots(BattleDataContainer battle, bool player)
        {
            // Position actual fired projectiles at impact; production collision, damage and status logic runs.
            foreach (var projectile in battle.Projectiles.Where(projectile => projectile.IsPlayer == player))
            {
                typeof(BattleProjectile).GetProperty(nameof(BattleProjectile.Position))
                    .SetValue(projectile, player ? battle.Enemy.Position : battle.Player.Position);
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
