using System;
using System.Linq;
using System.Reflection;
using GachaTrainers;
using NUnit.Framework;
using UnityEngine;

namespace P2.Tests
{
    public sealed class BattleControlModelTests
    {
        [Test]
        public void ModeChangesClearInputAndAutoRejectsManualCombatCommands()
        {
            var battle = ReadyBattle();
            Vector2 start = battle.Player.Position;
            battle.SetMovement(Vector2.left);
            battle.SetBasicAttack(true);
            battle.SetControlMode(BattleControlMode.Auto);
            Assert.That(battle.TryBasicAttack(), Is.False);
            Assert.That(battle.TrySpecial(), Is.False);
            Assert.That(battle.TryDodge(), Is.False);
            battle.SetMovement(Vector2.left);
            battle.SetBasicAttack(true);
            battle.SetControlMode(BattleControlMode.Manual);
            Advance(battle, 0.1f);
            Assert.That(battle.Player.Position, Is.EqualTo(start));
            Assert.That(battle.Projectiles.Any(projectile => projectile.IsPlayer), Is.False);

            battle.SetControlMode(BattleControlMode.Auto);
            Advance(battle, 0.1f);
            Assert.That(battle.Player.Position.x, Is.GreaterThan(start.x));
            battle.SetPaused(true);
            battle.SetControlMode(BattleControlMode.Manual);
            Assert.That(battle.ControlMode, Is.EqualTo(BattleControlMode.Manual));
            Assert.That(battle.Paused, Is.True);
            Assert.Throws<ArgumentOutOfRangeException>(() => battle.SetControlMode((BattleControlMode)99));
        }

        [Test]
        public void SwitchingPreservesHealthCooldownsEnemyAndProjectiles()
        {
            var battle = ReadyBattle();
            var previous = battle.Player;
            Set(previous, nameof(BattleFighter.Health), 42f);
            Assert.That(battle.TrySpecial(), Is.True);
            Assert.That(battle.TryBasicAttack(), Is.True);
            Assert.That(battle.TryDodge(), Is.True);
            var shots = battle.Projectiles.ToArray();
            var enemy = battle.Enemy;
            Vector2 enemyPosition = enemy.Position;
            Vector2 playerPosition = previous.Position;
            float specialCooldown = previous.SpecialCooldown;
            battle.SetMovement(Vector2.left);
            battle.SetBasicAttack(true);

            Assert.That(battle.TrySwitchPlayer(1), Is.True);
            Assert.That(battle.ActivePlayerIndex, Is.EqualTo(1));
            Assert.That(battle.Player.Position, Is.EqualTo(playerPosition));
            Assert.That(battle.Player.InvulnerableTime, Is.Zero);
            Assert.That(previous.InvulnerableTime, Is.Zero);
            Assert.That(previous.AnimationState, Is.EqualTo(CreatureAnimationState.Idle));
            Assert.That(previous.Health, Is.EqualTo(42f));
            Assert.That(previous.SpecialCooldown, Is.EqualTo(specialCooldown));
            Assert.That(battle.Enemy, Is.SameAs(enemy));
            Assert.That(enemy.Position, Is.EqualTo(enemyPosition));
            Assert.That(enemy.Health, Is.EqualTo(enemy.MaxHealth));
            Assert.That(battle.Projectiles, Is.EqualTo(shots));

            Advance(battle, 0.1f);
            Assert.That(previous.SpecialCooldown, Is.EqualTo(specialCooldown - 0.1f).Within(0.0001f));
            Assert.That(battle.Player.Position, Is.EqualTo(playerPosition), "A swap clears held movement and attack input.");
            Assert.That(battle.TrySwitchPlayer(0), Is.True);
            Assert.That(battle.Player.Health, Is.EqualTo(42f));
            Assert.That(battle.Player.SpecialCooldown, Is.EqualTo(specialCooldown - 0.1f).Within(0.0001f));
        }

        [Test]
        public void SwitchingRejectsUnavailableSlotsButWorksDuringAutoCombat()
        {
            var battle = new BattleDataContainer(RunDataContainer.CreateBattleTestRun().Party);
            Assert.That(battle.TrySwitchPlayer(1), Is.False);
            Advance(battle, 1f);
            Assert.That(battle.TrySwitchPlayer(-1), Is.False);
            Assert.That(battle.TrySwitchPlayer(3), Is.False);
            Assert.That(battle.TrySwitchPlayer(0), Is.False);
            Set(battle.PlayerTeam[1], nameof(BattleFighter.Health), 0f);
            Assert.That(battle.TrySwitchPlayer(1), Is.False);
            battle.SetPaused(true);
            Assert.That(battle.TrySwitchPlayer(2), Is.False);
            battle.SetControlMode(BattleControlMode.Auto);
            battle.SetPaused(false);
            Assert.That(battle.TrySwitchPlayer(2), Is.True);
            Assert.That(battle.Player, Is.SameAs(battle.PlayerTeam[2]));
            Assert.That(battle.Phase, Is.EqualTo(BattlePhase.Fighting));
        }

        private static BattleDataContainer ReadyBattle()
        {
            var battle = new BattleDataContainer(RunDataContainer.CreateBattleTestRun().Party);
            Advance(battle, 1f);
            return battle;
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
