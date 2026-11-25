using System.Collections;
using System.Linq;
using System.Reflection;
using GachaTrainers;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace P2.Tests
{
    public sealed class StageSceneTests
    {
        private GachaMainFlow _flow;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return SceneManager.LoadSceneAsync(GachaMainFlow.TitleScenePath);
            yield return WaitFor(GachaSceneKind.Title);
            Button("StartRunButton").onClick.Invoke();
            yield return WaitFor(GachaSceneKind.Shop);
            ((RunDataContainer)_flow.Run).TryPurchase(GachaMachineKind.Creature, _ => 0);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            var previous = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(SceneManager.CreateScene("Stage Cleanup"));
            yield return SceneManager.UnloadSceneAsync(previous);
        }

        [UnityTest]
        public IEnumerator FirstVictoryAdvancesToSecondSheetRosterAndKeepsPartyAndPrize()
        {
            var run = _flow.Run;
            var party = run.Party[0];
            int money = run.Money;
            Assert.That(Button("BeginBattleButton").GetComponentInChildren<Text>().text, Does.Contain("1/10"));
            Button("BeginBattleButton").onClick.Invoke();
            yield return WaitFor(GachaSceneKind.Battle);
            Assert.That(_flow.Battle.StageNumber, Is.EqualTo(1));
            Assert.That(_flow.Battle.Enemy.Stats.PhysicalAttackPower, Is.EqualTo(9f));
            Assert.That(_flow.Battle.Enemy.Stats.MagicAttackPower, Is.EqualTo(9f));
            Assert.That(_flow.Battle.Enemy.MaxHealth, Is.EqualTo(42));
            Assert.That(_flow.Battle.Player.MaxHealth, Is.EqualTo(85));
            Assert.That(BattleText("_timerText").text, Does.Contain("STAGE 1/10"));
            SetVictory((BattleDataContainer)_flow.Battle);
            yield return null;
            Assert.That(BattleText("_resultDescription").text, Does.Contain("스테이지 2"));
            Button("FinishBattleButton").onClick.Invoke();
            Button("FinishBattleButton").onClick.Invoke();
            yield return WaitFor(GachaSceneKind.Shop);
            Assert.That(_flow.Run, Is.SameAs(run));
            Assert.That(run.ClearedStages, Is.EqualTo(1));
            Assert.That(run.StageNumber, Is.EqualTo(2));
            Assert.That(run.Money, Is.EqualTo(money + RunDataContainer.VictoryPrize));
            Assert.That(run.Party[0], Is.SameAs(party));
            Assert.That(Button("BeginBattleButton").GetComponentInChildren<Text>().text, Does.Contain("2/10"));
            Button("BeginBattleButton").onClick.Invoke();
            yield return WaitFor(GachaSceneKind.Battle);
            Assert.That(_flow.Battle.StageNumber, Is.EqualTo(2));
            Assert.That(_flow.Battle.EnemyTeam.Select(enemy => enemy.Kind),
                Is.EqualTo(new[] { CreatureKind.MossTurtle, CreatureKind.TideOtter, CreatureKind.FrostOwl }));
            Assert.That(_flow.Battle.Enemy.MaxHealth, Is.EqualTo(105));
            Assert.That(_flow.Battle.Enemy.Stats.PhysicalAttackPower, Is.EqualTo(6.3f).Within(0.0001f));
            Assert.That(BattleText("_timerText").text, Does.Contain("STAGE 2/10"));
        }

        [UnityTest]
        public IEnumerator TenthVictoryShowsCompletionReturnsToTitleAndResetsStageOnNewRun()
        {
            var run = (RunDataContainer)_flow.Run;
            // Seed completed stages; the final battle uses the real CSV, result processing, buttons and scenes.
            for (int i = 0; i < 9; i++)
            {
                Assert.That(run.TryBeginBattle(), Is.True);
                Assert.That(run.CompleteBattle(true), Is.True);
            }
            yield return null;
            Assert.That(Button("BeginBattleButton").GetComponentInChildren<Text>().text, Does.Contain("10/10"));
            Button("BeginBattleButton").onClick.Invoke();
            yield return WaitFor(GachaSceneKind.Battle);
            Assert.That(_flow.Battle.StageNumber, Is.EqualTo(10));
            Assert.That(_flow.Battle.Enemy.Kind, Is.EqualTo(CreatureKind.StoneBoar));
            Assert.That(_flow.Battle.Enemy.MaxHealth, Is.EqualTo(299));
            Assert.That(_flow.Battle.Enemy.Stats.MagicAttackPower, Is.EqualTo(36.8f).Within(0.0001f));
            SetVictory((BattleDataContainer)_flow.Battle);
            yield return null;
            Assert.That(BattleText("_resultTitle").text, Does.Contain("10개 스테이지 완료"));
            Assert.That(BattleText("_resultDescription").text, Does.Contain("모험 성공"));
            Assert.That(BattleText("_finishLabel").text, Does.Contain("타이틀"));
            int money = run.Money;
            Button("FinishBattleButton").onClick.Invoke();
            Button("FinishBattleButton").onClick.Invoke();
            yield return WaitFor(GachaSceneKind.Title);
            Assert.That(run.RunCompleted, Is.True);
            Assert.That(run.ClearedStages, Is.EqualTo(10));
            Assert.That(run.Money, Is.EqualTo(money + RunDataContainer.VictoryPrize));
            Assert.That(run.TryBeginBattle(), Is.False);
            Button("StartRunButton").onClick.Invoke();
            yield return WaitFor(GachaSceneKind.Shop);
            Assert.That(_flow.Run, Is.Not.SameAs(run));
            Assert.That(_flow.Run.StageNumber, Is.EqualTo(1));
            Assert.That(_flow.Run.ClearedStages, Is.Zero);
            Assert.That(_flow.Run.RunCompleted, Is.False);
            Assert.That(_flow.Run.Owned, Is.Empty);
            Assert.That(_flow.Run.Money, Is.EqualTo(GachaCatalog.StartingMoney));
        }

        private static void SetVictory(BattleDataContainer battle)
        {
            battle.SetPaused(false);
            for (int i = 0; i < 12 && battle.Phase == BattlePhase.Summoning; i++)
            {
                battle.Tick(0.1f);
            }
            Assert.That(battle.Phase, Is.EqualTo(BattlePhase.Fighting));
            foreach (var enemy in battle.EnemyTeam)
            {
                typeof(BattleFighter).GetProperty("Health").SetValue(enemy, 0f);
            }
            battle.Tick(0.1f);
            Assert.That(battle.Phase, Is.EqualTo(BattlePhase.Victory));
        }

        private static Text BattleText(string field)
        {
            return (Text)typeof(BattleSceneView).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(Object.FindFirstObjectByType<BattleSceneView>());
        }

        private static Button Button(string name)
        {
            return Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None).Single(button => button.name == name);
        }

        private IEnumerator WaitFor(GachaSceneKind kind)
        {
            float deadline = Time.realtimeSinceStartup + 10f;
            do
            {
                yield return null;
                _flow = Object.FindFirstObjectByType<GachaMainFlow>();
                if (_flow != null && _flow.IsReady && _flow.SceneKind == kind)
                {
                    _flow.SendMessage("OnApplicationFocus", true);
                    if (_flow.Battle != null && _flow.Battle.Paused)
                    {
                        _flow.Submit(GachaAction.ResumeBattle);
                        yield return null;
                    }
                    yield break;
                }
            }
            while (Time.realtimeSinceStartup < deadline);
            Assert.Fail("Stage scene did not become ready: " + kind);
        }
    }
}
