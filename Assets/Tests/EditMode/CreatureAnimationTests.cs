using System;
using GachaTrainers;
using NUnit.Framework;
using UnityEngine;

namespace P2.Tests
{
    public sealed class CreatureAnimationTests
    {
        [Test]
        public void EveryClipUsesFourFramesWithLoopingOnlyForIdleAndMovement()
        {
            Assert.That(CreatureAnimationCatalog.FrameCount, Is.EqualTo(4));
            Assert.That(CreatureAnimationCatalog.StateCount, Is.EqualTo(6));
            foreach (CreatureAnimationState state in Enum.GetValues(typeof(CreatureAnimationState)))
            {
                float duration = CreatureAnimationCatalog.Duration(state);
                bool looping = state == CreatureAnimationState.Idle || state == CreatureAnimationState.Move;
                Assert.That(duration, Is.GreaterThan(0f));
                Assert.That(CreatureAnimationCatalog.Loops(state), Is.EqualTo(looping));
                Assert.That(CreatureAnimationCatalog.Frame(state, 0f), Is.EqualTo(0));
                for (int frame = 0; frame < 4; frame++)
                {
                    Assert.That(CreatureAnimationCatalog.Frame(state, duration * (frame + 0.25f) / 4f),
                        Is.EqualTo(frame), state + " frame " + frame);
                }
                Assert.That(CreatureAnimationCatalog.Frame(state, duration * 1.3f),
                    Is.EqualTo(looping ? 1 : 3), state + " after clip duration");
            }
        }

        [Test]
        public void ActualMovementStartsTheWalkCycleAndStoppingReturnsToIdle()
        {
            BattleDataContainer battle = ReadyBattle();
            Assert.That(battle.Player.AnimationState, Is.EqualTo(CreatureAnimationState.Idle));
            Vector2 start = battle.Player.Position;
            battle.SetMovement(Vector2.left);
            Advance(battle, 0.1f);
            Assert.That(battle.Player.Position.x, Is.LessThan(start.x));
            Assert.That(battle.Player.AnimationState, Is.EqualTo(CreatureAnimationState.Move));
            Assert.That(battle.Player.AnimationTime, Is.GreaterThan(0f));
            battle.SetMovement(Vector2.zero);
            battle.Tick(1f / 120f);
            Assert.That(battle.Player.AnimationState, Is.EqualTo(CreatureAnimationState.Idle));
            Assert.That(battle.Player.AnimationTime, Is.LessThanOrEqualTo(1f / 120f));
        }

        [Test]
        public void AcceptedActionsRestartClipsWithSkillPriorityAndRealDamageInterruptsWithHit()
        {
            foreach (CreatureKind kind in Enum.GetValues(typeof(CreatureKind)))
            {
                BattleDataContainer battle = ReadyBattle(kind);
                Assert.That(battle.TryBasicAttack(), Is.True);
                Assert.That(battle.Player.AnimationState, Is.EqualTo(CreatureAnimationState.Attack));
                Assert.That(battle.Player.AnimationTime, Is.EqualTo(0f));
                Advance(battle, 0.1f);
                Assert.That(battle.Player.AnimationTime, Is.GreaterThan(0f));
                Assert.That(battle.TrySpecial(), Is.True);
                Assert.That(battle.Player.AnimationState, Is.EqualTo(CreatureAnimationState.Skill));
                Assert.That(battle.Player.AnimationTime, Is.EqualTo(0f));
                Assert.That(battle.TryDodge(), Is.True);
                Assert.That(battle.Player.AnimationState, Is.EqualTo(CreatureAnimationState.Skill),
                    "A simultaneous dodge must not replace the higher-priority skill clip for " + kind);
            }

            BattleDataContainer hitBattle = ReadyBattle();
            // Place the enemy nearby so its normal AI attack can hit during the player's skill.
            SetFixture(hitBattle.Enemy, nameof(BattleFighter.Position), hitBattle.Player.Position + Vector2.right * 1.3f);
            Assert.That(hitBattle.TrySpecial(), Is.True);
            for (int step = 0; step < 50 && hitBattle.Player.Health == hitBattle.Player.MaxHealth; step++)
            {
                hitBattle.Tick(1f / 120f);
            }
            Assert.That(hitBattle.Player.Health, Is.LessThan(hitBattle.Player.MaxHealth), "The fixture must receive real projectile damage.");
            Assert.That(hitBattle.Player.AnimationState, Is.EqualTo(CreatureAnimationState.Hit));
            Assert.That(hitBattle.Player.AnimationTime, Is.LessThanOrEqualTo(1f / 120f));
        }

        [Test]
        public void PauseAndFinishedBattleFreezeAnimationTimeUntilSimulationCanRun()
        {
            BattleDataContainer battle = ReadyBattle();
            battle.TrySpecial();
            Advance(battle, 0.1f);
            battle.SetPaused(true);
            float playerTime = battle.Player.AnimationTime;
            float enemyTime = battle.Enemy.AnimationTime;
            CreatureAnimationState state = battle.Player.AnimationState;
            Advance(battle, 0.3f);
            Assert.That(battle.Player.AnimationTime, Is.EqualTo(playerTime));
            Assert.That(battle.Enemy.AnimationTime, Is.EqualTo(enemyTime));
            Assert.That(battle.Player.AnimationState, Is.EqualTo(state));
            battle.SetPaused(false);
            Advance(battle, 0.05f);
            Assert.That(battle.Player.AnimationTime, Is.GreaterThan(playerTime));

            foreach (BattleFighter enemy in battle.EnemyTeam)
            {
                SetFixture(enemy, nameof(BattleFighter.Health), 0f);
            }
            battle.Tick(1f / 120f);
            Assert.That(battle.Phase, Is.EqualTo(BattlePhase.Victory));
            playerTime = battle.Player.AnimationTime;
            enemyTime = battle.Enemy.AnimationTime;
            Advance(battle, 0.5f);
            Assert.That(battle.Player.AnimationTime, Is.EqualTo(playerTime));
            Assert.That(battle.Enemy.AnimationTime, Is.EqualTo(enemyTime));
        }

        [Test]
        public void RepeatedActionsAndReplacementDoNotReuseThePreviousAnimationClock()
        {
            var run = RunDataContainer.CreateBattleTestRun();
            var battle = new BattleDataContainer(run.Party);
            Advance(battle, 1f);
            battle.TryBasicAttack();
            Advance(battle, 0.35f);
            // Make another accepted request without waiting for unrelated enemy combat.
            SetFixture(battle.Player, nameof(BattleFighter.BasicCooldown), 0f);
            Assert.That(battle.TryBasicAttack(), Is.True);
            Assert.That(battle.Player.AnimationState, Is.EqualTo(CreatureAnimationState.Attack));
            Assert.That(battle.Player.AnimationTime, Is.EqualTo(0f));
            SetFixture(battle.Player, nameof(BattleFighter.Health), 0f);
            battle.Tick(1f / 120f);
            Assert.That(battle.Phase, Is.EqualTo(BattlePhase.Summoning));
            Assert.That(battle.Player.Kind, Is.EqualTo(CreatureKind.MossTurtle));
            Assert.That(battle.Player.AnimationState, Is.EqualTo(CreatureAnimationState.Idle));
            Assert.That(battle.Player.AnimationTime, Is.EqualTo(0f));
            Assert.That(battle.Enemy.AnimationState, Is.EqualTo(CreatureAnimationState.Idle));
            Assert.That(battle.Enemy.AnimationTime, Is.EqualTo(0f));
            var nextBattle = new BattleDataContainer(run.Party);
            Assert.That(nextBattle.Player.AnimationState, Is.EqualTo(CreatureAnimationState.Idle));
            Assert.That(nextBattle.Player.AnimationTime, Is.EqualTo(0f));
        }

        private static BattleDataContainer ReadyBattle(CreatureKind kind = CreatureKind.EmberFox)
        {
            var run = new RunDataContainer();
            run.TryPurchase(GachaMachineKind.Creature, _ => GachaCatalog.GetCreatureRollStart(kind));
            var battle = new BattleDataContainer(run.Party);
            Advance(battle, 1f);
            Assert.That(battle.Phase, Is.EqualTo(BattlePhase.Fighting));
            return battle;
        }

        private static void Advance(BattleDataContainer battle, float seconds)
        {
            for (int step = 0; step < Mathf.RoundToInt(seconds * 120f); step++)
            {
                battle.Tick(1f / 120f);
            }
        }

        private static void SetFixture<T>(BattleFighter fighter, string property, T value)
        {
            typeof(BattleFighter).GetProperty(property).SetValue(fighter, value);
        }
    }
}
