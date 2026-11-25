using GachaTrainers;
using NUnit.Framework;
using Refactor.Main;
using UnityEngine;
using EventHandler = Refactor.Main.Event.EventHandler;

namespace P2.Tests
{
    public sealed class BattleControlFlowTests
    {
        [Test]
        public void ModeRequestsPersistWithinRunAndPausedModeChangeDoesNotResumeCombat()
        {
            var run = RunDataContainer.CreateBattleTestRun();
            var battle = new BattleDataContainer(run.Party);
            var queue = new EventQueue(new EventHandler(new GachaEventHandler(GachaSceneKind.Battle,
                run, null, () => true, _ => { }, _ => 0, battle)));
            queue.AddEvent(new GachaActionRequested(GachaAction.ToggleBattleControl));
            Assert.That(run.AutoBattle, Is.False, "Enqueueing alone must not modify the run.");
            queue.Update();
            Assert.That(battle.ControlMode, Is.EqualTo(BattleControlMode.Auto));
            Assert.That(run.AutoBattle, Is.True);
            Assert.That(new BattleDataContainer(run.Party, run.AutoBattle).ControlMode, Is.EqualTo(BattleControlMode.Auto));
            queue.AddEvent(new GachaActionRequested(GachaAction.BattlePause));
            queue.AddEvent(new GachaActionRequested(GachaAction.ToggleBattleControl));
            queue.Update();
            Assert.That(battle.Paused, Is.True);
            Assert.That(battle.ControlMode, Is.EqualTo(BattleControlMode.Manual));
            Assert.That(run.AutoBattle, Is.False);
            Assert.That(new RunDataContainer().AutoBattle, Is.False);
        }

        [Test]
        public void SwitchRequestsRespectFocusPauseAndCurrentModeThroughTheQueue()
        {
            var run = RunDataContainer.CreateBattleTestRun();
            var battle = new BattleDataContainer(run.Party);
            while (battle.Phase == BattlePhase.Summoning)
            {
                battle.Tick(0.1f);
            }
            bool focused = true;
            var queue = new EventQueue(new EventHandler(new GachaEventHandler(GachaSceneKind.Battle,
                run, null, () => focused, _ => { }, _ => 0, battle)));
            queue.AddEvent(new GachaActionRequested(GachaAction.SwitchBattleCreature, 2));
            queue.Update();
            Assert.That(battle.ActivePlayerIndex, Is.EqualTo(2));
            queue.AddEvent(new GachaActionRequested(GachaAction.ToggleBattleControl));
            queue.AddEvent(new GachaActionRequested(GachaAction.SwitchBattleCreature, 1));
            queue.Update();
            Assert.That(battle.ControlMode, Is.EqualTo(BattleControlMode.Auto));
            Assert.That(battle.ActivePlayerIndex, Is.EqualTo(1));
            Vector2 before = battle.Player.Position;
            queue.AddEvent(new BattleCombatRequested(Vector2.up, true));
            queue.AddEvent(new GachaActionRequested(GachaAction.BattlePause));
            queue.AddEvent(new GachaActionRequested(GachaAction.SwitchBattleCreature, 0));
            queue.Update();
            Assert.That(battle.ActivePlayerIndex, Is.EqualTo(1));
            Assert.That(battle.Player.Position, Is.EqualTo(before));
            focused = false;
            queue.AddEvent(new GachaActionRequested(GachaAction.ResumeBattle));
            queue.AddEvent(new GachaActionRequested(GachaAction.ToggleBattleControl));
            queue.Update();
            Assert.That(battle.Paused, Is.True);
            Assert.That(battle.ControlMode, Is.EqualTo(BattleControlMode.Auto));
        }
    }
}
