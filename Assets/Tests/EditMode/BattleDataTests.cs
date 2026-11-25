using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GachaTrainers;
using NUnit.Framework;
using UnityEngine;

namespace P2.Tests
{
    public sealed class BattleDataTests
    {
        [Test]
        public void BattleSnapshotsEquippedPassiveAndUsesOneOfEachUnmodifiedEnemy()
        {
            var run = CreateRun(0, 1, 2);
            run.TryPurchase(GachaMachineKind.Crystal, _ => 0);
            run.SelectParty(0);
            run.TryEquipCrystal(CrystalKind.Ember);
            var battle = new BattleDataContainer(run.Party);
            Assert.That(battle.Player.MaxHealth, Is.EqualTo(85));
            Assert.That(battle.Player.Health, Is.EqualTo(85));
            Assert.That(battle.Player.Attack, Is.EqualTo(18));
            Assert.That(battle.Player.EquippedCrystal, Is.EqualTo(CrystalKind.Ember));
            Assert.That(battle.EnemyTeam.Select(fighter => fighter.Kind),
                Is.EqualTo(new[] { CreatureKind.EmberFox, CreatureKind.MossTurtle, CreatureKind.VoltRabbit }));
            Assert.That(battle.Enemy.MaxHealth, Is.EqualTo(85));
            Assert.That(battle.EnemyTeam.All(fighter => !fighter.EquippedCrystal.HasValue), Is.True);
            run.UnequipSelectedCrystal();
            run.ReleaseSelectedCreature();
            Assert.That(battle.PlayerTeam.Count, Is.EqualTo(3));
            Assert.That(battle.Player.MaxHealth, Is.EqualTo(85));
            Assert.That(battle.Player.EquippedCrystal, Is.EqualTo(CrystalKind.Ember),
                "Live shop changes must not mutate an existing battle snapshot.");
        }

        [Test]
        public void ProtectionAndReflectionAreCopiedWithoutIncreasingBaseStats()
        {
            var run = CreateRun(0, 2);
            run.TryPurchase(GachaMachineKind.Crystal, _ => GachaCatalog.GetCrystalRollStart(CrystalKind.Protection));
            run.TryPurchase(GachaMachineKind.Crystal, _ => GachaCatalog.GetCrystalRollStart(CrystalKind.Reflection));
            run.SelectParty(0);
            run.TryEquipCrystal(CrystalKind.Protection);
            run.SelectParty(1);
            run.TryEquipCrystal(CrystalKind.Reflection);
            var battle = new BattleDataContainer(run.Party);
            Assert.That(battle.PlayerTeam[0].EquippedCrystal, Is.EqualTo(CrystalKind.Protection));
            Assert.That(battle.PlayerTeam[1].EquippedCrystal, Is.EqualTo(CrystalKind.Reflection));
            Assert.That(battle.PlayerTeam[0].Attack, Is.EqualTo(18));
            Assert.That(battle.PlayerTeam[1].AttackSpeed, Is.EqualTo(1.5f).Within(0.0001f));
            Assert.That(battle.EnemyTeam[0].Attack, Is.EqualTo(18));
        }

        [Test]
        public void EmptyOversizedOrNullPartyIsRejected()
        {
            var run = CreateRun(0);
            Assert.Throws<ArgumentNullException>(() => new BattleDataContainer(null));
            Assert.Throws<ArgumentException>(() => new BattleDataContainer(Array.Empty<PartyCreature>()));
            Assert.Throws<ArgumentException>(() => new BattleDataContainer(new[] { run.Party[0], run.Party[0], run.Party[0], run.Party[0] }));
            Assert.Throws<ArgumentException>(() => new BattleDataContainer(new PartyCreature[] { null }));
        }

        [Test]
        public void SummoningBlocksMovementAndCombatUntilTheDelayHasElapsed()
        {
            var battle = new BattleDataContainer(CreateRun(0).Party);
            Vector2 initial = battle.Player.Position;
            battle.SetMovement(Vector2.right);
            battle.SetBasicAttack(true);
            Assert.That(battle.TryBasicAttack(), Is.False);
            Assert.That(battle.TrySpecial(), Is.False);
            Assert.That(battle.TryDodge(), Is.False);
            Advance(battle, 0.5f);
            Assert.That(battle.Phase, Is.EqualTo(BattlePhase.Summoning));
            Assert.That(battle.Player.Position, Is.EqualTo(initial));
            Assert.That(battle.Projectiles, Is.Empty);
            Advance(battle, 0.6f);
            Assert.That(battle.Phase, Is.EqualTo(BattlePhase.Fighting));
            Assert.That(battle.Player.Position.x, Is.GreaterThan(initial.x));
            Assert.That(battle.Projectiles.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DiagonalMovementHasTheSameSpeedAndCannotLeaveTheArena()
        {
            var straight = ReadyBattle();
            var diagonal = ReadyBattle();
            Vector2 start = straight.Player.Position;
            straight.SetMovement(Vector2.left);
            diagonal.SetMovement(new Vector2(-1f, -1f));
            Advance(straight, 0.1f);
            Advance(diagonal, 0.1f);
            Assert.That(Vector2.Distance(diagonal.Player.Position, start),
                Is.EqualTo(Vector2.Distance(straight.Player.Position, start)).Within(0.0001f));
            Advance(diagonal, 2f);
            Assert.That(diagonal.Player.Position.x, Is.EqualTo(BattleCatalog.MinX).Within(0.0001f));
            Assert.That(diagonal.Player.Position.y, Is.EqualTo(BattleCatalog.MinY).Within(0.0001f));
            diagonal.SetMovement(new Vector2(float.NaN, float.PositiveInfinity));
            Vector2 before = diagonal.Player.Position;
            diagonal.Tick(float.NaN);
            diagonal.Tick(float.PositiveInfinity);
            diagonal.Tick(-1f);
            Assert.That(diagonal.Player.Position, Is.EqualTo(before));
        }

        [Test]
        public void AttackSpecialAndDodgeRespectTheirCooldowns()
        {
            var battle = ReadyBattle();
            Assert.That(battle.TryBasicAttack(), Is.True);
            int firstProjectileCount = battle.Projectiles.Count;
            Assert.That(battle.TryBasicAttack(), Is.False);
            Assert.That(battle.Projectiles.Count, Is.EqualTo(firstProjectileCount));
            Assert.That(battle.Player.BasicCooldown, Is.EqualTo(1f / battle.Player.AttackSpeed).Within(0.0001f));
            Assert.That(battle.TrySpecial(), Is.True);
            Assert.That(battle.TrySpecial(), Is.False);
            Assert.That(battle.Player.SpecialCooldown, Is.EqualTo(BattleCatalog.Get(battle.Player.Kind).SpecialCooldown));
            Assert.That(battle.TryDodge(), Is.True);
            Assert.That(battle.TryDodge(), Is.False);
            Assert.That(battle.Player.InvulnerableTime, Is.GreaterThan(0f));
            Assert.That(battle.Player.DodgeCooldown, Is.EqualTo(BattleCatalog.DodgeCooldown));
        }

        [Test]
        public void PauseFreezesAllCombatClocksAndDiscardsCommandsOnResume()
        {
            var battle = ReadyBattle();
            battle.TryBasicAttack();
            battle.TryDodge();
            battle.SetPaused(true);
            float clock = battle.ElapsedTime;
            float cooldown = battle.Player.DodgeCooldown;
            float health = battle.Player.Health;
            Vector2 position = battle.Player.Position;
            Vector2 projectile = battle.Projectiles[0].Position;
            battle.SetMovement(Vector2.right);
            battle.SetBasicAttack(true);
            Advance(battle, 2f);
            Assert.That(battle.TrySpecial(), Is.False);
            Assert.That(battle.ElapsedTime, Is.EqualTo(clock));
            Assert.That(battle.Player.DodgeCooldown, Is.EqualTo(cooldown));
            Assert.That(battle.Player.Health, Is.EqualTo(health));
            Assert.That(battle.Player.Position, Is.EqualTo(position));
            Assert.That(battle.Projectiles[0].Position, Is.EqualTo(projectile));
            battle.SetPaused(false);
            Advance(battle, 0.3f);
            float resumed = battle.ElapsedTime;
            Vector2 afterDodge = battle.Player.Position;
            Advance(battle, 0.1f);
            Assert.That(battle.ElapsedTime, Is.GreaterThan(resumed));
            Assert.That(battle.Player.Position, Is.EqualTo(afterDodge), "Movement buffered while paused must not replay.");
        }

        [Test]
        public void RealTimeAIProducesProjectilesAndChangesHealthWithoutTurns()
        {
            var battle = new BattleDataContainer(CreateRun(0, 1, 2).Party, true);
            bool sawProjectile = false;
            bool sawDamage = false;
            for (int frame = 0; frame < 8 * 120; frame++)
            {
                battle.Tick(1f / 120f);
                sawProjectile |= battle.Projectiles.Count > 0;
                sawDamage |= battle.PlayerTeam.Any(fighter => fighter.Health < fighter.MaxHealth)
                    || battle.EnemyTeam.Any(fighter => fighter.Health < fighter.MaxHealth);
            }
            Assert.That(sawProjectile, Is.True);
            Assert.That(sawDamage, Is.True);
            Assert.That(battle.ElapsedTime, Is.GreaterThan(1f));
        }

        [Test]
        public void FaintedFighterIsReplacedWithoutHealingTheSurvivorOrLeakingProjectiles()
        {
            var battle = ReadyBattle(0, 1, 2);
            var previous = battle.Player;
            var survivor = battle.Enemy;
            SetHealth(survivor, 37f);
            battle.TryBasicAttack();
            SetHealth(previous, 0f);
            battle.Tick(1f / 60f);
            Assert.That(battle.Phase, Is.EqualTo(BattlePhase.Summoning));
            Assert.That(battle.Player, Is.SameAs(battle.PlayerTeam[1]));
            Assert.That(battle.Player, Is.Not.SameAs(previous));
            Assert.That(battle.Enemy, Is.SameAs(survivor));
            Assert.That(battle.Enemy.Health, Is.EqualTo(37f));
            Assert.That(battle.Projectiles, Is.Empty);
            Assert.That(battle.Player.Health, Is.EqualTo(battle.Player.MaxHealth));
            Advance(battle, 0.5f);
            Assert.That(battle.Phase, Is.EqualTo(BattlePhase.Summoning));
            Assert.That(battle.Player.Health, Is.EqualTo(battle.Player.MaxHealth));
            Assert.That(battle.TryBasicAttack(), Is.False);
        }

        [TestCase(BattlePhase.Victory)]
        [TestCase(BattlePhase.Defeat)]
        [TestCase(BattlePhase.Draw)]
        public void ExhaustedTeamsResolveOnceAndFinishedBattleCannotKeepSimulating(BattlePhase expected)
        {
            var battle = ReadyBattle(0, 1, 2);
            if (expected != BattlePhase.Victory)
            {
                foreach (var fighter in battle.PlayerTeam)
                {
                    SetHealth(fighter, 0f);
                }
            }
            if (expected != BattlePhase.Defeat)
            {
                foreach (var fighter in battle.EnemyTeam)
                {
                    SetHealth(fighter, 0f);
                }
            }
            battle.Tick(1f / 60f);
            Assert.That(battle.Phase, Is.EqualTo(expected));
            float finishedTime = battle.ElapsedTime;
            int revision = battle.Revision;
            Advance(battle, 5f);
            Assert.That(battle.ElapsedTime, Is.EqualTo(finishedTime));
            Assert.That(battle.Revision, Is.EqualTo(revision));
            Assert.That(battle.Projectiles, Is.Empty);
            Assert.That(battle.TryBasicAttack(), Is.False);
            Assert.That(battle.TrySpecial(), Is.False);
            Assert.That(battle.TryDodge(), Is.False);
        }

        [Test]
        public void IdenticalInputsSimulateTheSameBattleAtThirtySixtyAndOneHundredTwentyFps()
        {
            var low = new BattleDataContainer(CreateRun(0, 1, 2).Party, true);
            var medium = new BattleDataContainer(CreateRun(0, 1, 2).Party, true);
            var high = new BattleDataContainer(CreateRun(0, 1, 2).Party, true);
            Advance(low, 20f, 30);
            Advance(medium, 20f, 60);
            Advance(high, 20f, 120);
            AssertEquivalent(low, medium);
            AssertEquivalent(medium, high);
        }

        [Test]
        public void AutomaticThreeCreatureBattleReachesAResultAndDoesNotMutateTheRun()
        {
            var run = CreateRun(0, 1, 2);
            int money = run.Money;
            var battle = new BattleDataContainer(run.Party, true);
            Advance(battle, 180f);
            Assert.That(battle.Phase, Is.EqualTo(BattlePhase.Victory).Or.EqualTo(BattlePhase.Defeat).Or.EqualTo(BattlePhase.Draw));
            Assert.That(run.Money, Is.EqualTo(money));
            Assert.That(run.Party.Count, Is.EqualTo(3));
            Assert.That(run.Party[0].MaxHealth, Is.EqualTo(85));
            Assert.That(battle.PlayerTeam as IList<BattleFighter>, Is.Not.Null);
            Assert.Throws<NotSupportedException>(() => ((IList<BattleFighter>)battle.PlayerTeam).Clear());
        }

        private static BattleDataContainer ReadyBattle(params int[] creatures)
        {
            var battle = new BattleDataContainer(CreateRun(creatures.Length == 0 ? new[] { 0 } : creatures).Party);
            Advance(battle, 1f);
            Assert.That(battle.Phase, Is.EqualTo(BattlePhase.Fighting));
            return battle;
        }

        private static RunDataContainer CreateRun(params int[] creatures)
        {
            var run = new RunDataContainer();
            foreach (int creature in creatures)
            {
                Assert.That(run.TryPurchase(GachaMachineKind.Creature, _ => GachaCatalog.GetCreatureRollStart((CreatureKind)creature)), Is.True);
            }
            return run;
        }

        private static void Advance(BattleDataContainer battle, float seconds, int framesPerSecond = 120)
        {
            for (int index = 0; index < Mathf.RoundToInt(seconds * framesPerSecond); index++)
            {
                battle.Tick(1f / framesPerSecond);
            }
        }

        private static void SetHealth(BattleFighter fighter, float health)
        {
            typeof(BattleFighter).GetProperty(nameof(BattleFighter.Health), BindingFlags.Public | BindingFlags.Instance)
                .SetValue(fighter, health);
        }

        private static void AssertEquivalent(IReadOnlyBattleData left, IReadOnlyBattleData right)
        {
            Assert.That(left.Phase, Is.EqualTo(right.Phase));
            Assert.That(left.ElapsedTime, Is.EqualTo(right.ElapsedTime).Within(0.0001f));
            Assert.That(left.Player.Position.x, Is.EqualTo(right.Player.Position.x).Within(0.0001f));
            Assert.That(left.Player.Position.y, Is.EqualTo(right.Player.Position.y).Within(0.0001f));
            Assert.That(left.Enemy.Position.x, Is.EqualTo(right.Enemy.Position.x).Within(0.0001f));
            Assert.That(left.Enemy.Position.y, Is.EqualTo(right.Enemy.Position.y).Within(0.0001f));
            for (int index = 0; index < left.PlayerTeam.Count; index++)
            {
                Assert.That(left.PlayerTeam[index].Health, Is.EqualTo(right.PlayerTeam[index].Health).Within(0.0001f));
            }
            for (int index = 0; index < left.EnemyTeam.Count; index++)
            {
                Assert.That(left.EnemyTeam[index].Health, Is.EqualTo(right.EnemyTeam[index].Health).Within(0.0001f));
            }
            Assert.That(left.Projectiles.Count, Is.EqualTo(right.Projectiles.Count));
        }
    }
}
