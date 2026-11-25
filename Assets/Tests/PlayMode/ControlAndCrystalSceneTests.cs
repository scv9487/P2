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
    public sealed class ControlAndCrystalSceneTests
    {
        private GachaMainFlow _flow;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return SceneManager.LoadSceneAsync(GachaMainFlow.TitleScenePath);
            yield return WaitFor(GachaSceneKind.Title);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Scene previous = SceneManager.GetActiveScene();
            Scene empty = SceneManager.CreateScene("Control Crystal Cleanup");
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync(previous);
        }

        [UnityTest]
        public IEnumerator ActualModeAndRosterButtonsAllowSwitchingAndKeepRunPreference()
        {
            yield return SceneManager.LoadSceneAsync(GachaMainFlow.BattleScenePath);
            yield return WaitFor(GachaSceneKind.Battle);
            var battle = (BattleDataContainer)_flow.Battle;
            while (battle.Phase == BattlePhase.Summoning)
            {
                battle.Tick(0.1f);
            }
            yield return null;
            Assert.That(Button("PlayerSlot2").interactable, Is.True);
            float health = battle.PlayerTeam[2].Health;
            Button("PlayerSlot2").onClick.Invoke();
            yield return null;
            Assert.That(battle.ActivePlayerIndex, Is.EqualTo(2));
            Assert.That(battle.Player.Health, Is.EqualTo(health));
            Button("ControlModeButton").onClick.Invoke();
            yield return null;
            Assert.That(battle.ControlMode, Is.EqualTo(BattleControlMode.Auto));
            Assert.That(_flow.Run.AutoBattle, Is.True);
            Assert.That(Button("AttackButton").interactable, Is.False);
            Assert.That(Button("PlayerSlot0").interactable, Is.True);
            Button("PauseBattleButton").onClick.Invoke();
            yield return null;
            Button("PauseControlModeButton").onClick.Invoke();
            yield return null;
            Assert.That(battle.Paused, Is.True);
            Assert.That(battle.ControlMode, Is.EqualTo(BattleControlMode.Manual));
            Button("PauseControlModeButton").onClick.Invoke();
            Button("ResumeBattleButton").onClick.Invoke();
            yield return null;
            foreach (var enemy in battle.EnemyTeam)
            {
                typeof(BattleFighter).GetProperty(nameof(BattleFighter.Health)).SetValue(enemy, 0f);
            }
            battle.Tick(0.05f);
            yield return null;
            Button("FinishBattleButton").onClick.Invoke();
            yield return WaitFor(GachaSceneKind.Shop);
            Assert.That(_flow.Run.AutoBattle, Is.True);
            Button("BeginBattleButton").onClick.Invoke();
            yield return WaitFor(GachaSceneKind.Battle);
            Assert.That(_flow.Battle.ControlMode, Is.EqualTo(BattleControlMode.Auto));
        }

        [UnityTest]
        public IEnumerator CombatMouseGateBlocksUIControlsButAllowsTheArena()
        {
            yield return SceneManager.LoadSceneAsync(GachaMainFlow.BattleScenePath);
            yield return WaitFor(GachaSceneKind.Battle);
            Canvas.ForceUpdateCanvases();
            foreach (string name in new[] { "ControlModeButton", "AttackButton", "PlayerSlot0" })
            {
                Button button = Button(name);
                var rect = (RectTransform)button.transform;
                Canvas canvas = button.GetComponentInParent<Canvas>();
                Vector2 point = RectTransformUtility.WorldToScreenPoint(canvas.worldCamera, rect.TransformPoint(rect.rect.center));
                Assert.That(PointerBlocked(point), Is.True, name + " must consume its click even if disabled.");
            }
            Vector2 arena = Camera.main.WorldToScreenPoint(new Vector3(0f, -1f, 0f));
            Assert.That(PointerBlocked(arena), Is.False);
        }

        [UnityTest]
        public IEnumerator ShopHasTwelveRarityItemsAndEachEquipmentButtonUsesTheSelectedCrystal()
        {
            Button("StartRunButton").onClick.Invoke();
            yield return WaitFor(GachaSceneKind.Shop);
            var run = (RunDataContainer)_flow.Run;
            run.TryPurchase(GachaMachineKind.Creature, _ => 0);
            for (int victory = 0; victory < 3; victory++)
            {
                Assert.That(run.TryBeginBattle(), Is.True);
                Assert.That(run.CompleteBattle(true), Is.True);
            }
            foreach (var crystal in GachaCatalog.Crystals)
            {
                Assert.That(run.TryPurchase(GachaMachineKind.Crystal, _ => GachaCatalog.GetCrystalRollStart(crystal.Kind)), Is.True);
            }
            yield return null;
            Assert.That(run.Money, Is.EqualTo(380));
            var scroll = Object.FindObjectsByType<ScrollRect>(FindObjectsSortMode.None)
                .Single(item => item.name == "CrystalInventoryScroll");
            Assert.That(scroll.vertical, Is.True);
            Assert.That(scroll.content.rect.height, Is.GreaterThan(scroll.viewport.rect.height));
            foreach (var crystal in GachaCatalog.Crystals)
            {
                Button equip = Button("CrystalEquip" + (int)crystal.Kind);
                Assert.That(equip.GetComponentInChildren<Text>().text, Does.Contain(GachaCatalog.GetRarityName(crystal.Rarity)));
                Assert.That(equip.interactable, Is.True);
                equip.onClick.Invoke();
                yield return null;
                Assert.That(run.Party[0].EquippedCrystal, Is.EqualTo(crystal.Kind));
            }
            scroll.verticalNormalizedPosition = 0f;
            yield return null;
            Assert.That(run.Party[0].EquippedCrystal, Is.EqualTo(CrystalKind.Elasticity));
            Assert.That(run.GetCrystalCount(CrystalKind.Elasticity), Is.Zero);
            Assert.That(run.GetCrystalCount(CrystalKind.Ember), Is.EqualTo(1));
        }

        private bool PointerBlocked(Vector2 position)
        {
            return (bool)typeof(GachaMainFlow).GetMethod("PointerOverBattleControls", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(_flow, new object[] { position });
        }

        private static Button Button(string name)
        {
            return Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None).Single(button => button.name == name);
        }

        private IEnumerator WaitFor(GachaSceneKind kind)
        {
            float deadline = Time.realtimeSinceStartup + 10f;
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                _flow = Object.FindFirstObjectByType<GachaMainFlow>();
                if (_flow != null && _flow.IsReady && _flow.SceneKind == kind)
                {
                    _flow.SendMessage("OnApplicationFocus", true);
                    if (kind == GachaSceneKind.Battle && _flow.Battle.Paused)
                    {
                        _flow.Submit(GachaAction.ResumeBattle);
                        yield return null;
                    }
                    yield break;
                }
            }
            Assert.Fail("Scene not ready: " + kind);
        }
    }
}
