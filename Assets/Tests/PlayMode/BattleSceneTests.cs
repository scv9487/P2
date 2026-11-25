using System.Collections;
using System.Linq;
using System.Reflection;
using GachaTrainers;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace P2.Tests
{
    public sealed class BattleSceneTests
    {
        private const string TitlePath = "Assets/0_Project/GachaTrainers/Scenes/Title.unity";
        private const string ShopPath = "Assets/0_Project/GachaTrainers/Scenes/GachaShop.unity";
        private const string BattlePath = "Assets/0_Project/GachaTrainers/Scenes/Battle.unity";
        private GachaMainFlow _flow;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Assert.That(Application.CanStreamedLevelBeLoaded(BattlePath), Is.True);
            yield return SceneManager.LoadSceneAsync(TitlePath, LoadSceneMode.Single);
            yield return WaitForFlow(GachaSceneKind.Title);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Scene active = SceneManager.GetActiveScene();
            if (active.path == TitlePath || active.path == ShopPath || active.path == BattlePath)
            {
                Scene empty = SceneManager.CreateScene("Battle Test Cleanup");
                SceneManager.SetActiveScene(empty);
                yield return SceneManager.UnloadSceneAsync(active);
            }
        }

        [UnityTest]
        public IEnumerator ShopRequiresACreatureAndCarriesEquipmentIntoTheActualBattleScene()
        {
            yield return StartShop();
            Assert.That(FindButton("BeginBattleButton").interactable, Is.False);
            _flow.Submit(GachaAction.BeginBattle);
            yield return null;
            Assert.That(_flow.SceneKind, Is.EqualTo(GachaSceneKind.Shop));
            Assert.That(_flow.Run.BattleInProgress, Is.False);
            yield return BuyCreatureAndEquipCrystal();
            IReadOnlyRunData previousRun = _flow.Run;
            PartyCreature equipped = previousRun.Party[0];
            int before = previousRun.Money;
            Assert.That(FindButton("BeginBattleButton").interactable, Is.True);
            FindButton("BeginBattleButton").onClick.Invoke();
            Assert.That(_flow.SceneKind, Is.EqualTo(GachaSceneKind.Shop), "버튼 callback은 전환 요청만 기록해야 합니다.");
            yield return WaitForFlow(GachaSceneKind.Battle);
            Assert.That(_flow.Run, Is.SameAs(previousRun));
            Assert.That(_flow.Run.Money, Is.EqualTo(before));
            Assert.That(_flow.Run.BattleInProgress, Is.True);
            Assert.That(_flow.Battle.PlayerTeam.Count, Is.EqualTo(1));
            Assert.That(_flow.Battle.Player.Kind, Is.EqualTo(equipped.Kind));
            Assert.That(_flow.Battle.Player.MaxHealth, Is.EqualTo(equipped.MaxHealth));
            Assert.That(_flow.Battle.Player.Attack, Is.EqualTo(equipped.Attack));
            Assert.That(_flow.Battle.Player.AttackSpeed, Is.EqualTo(equipped.AttackSpeed));
            Assert.That(_flow.Battle.EnemyTeam.Select(fighter => fighter.Kind),
                Is.EqualTo(new[] { CreatureKind.EmberFox, CreatureKind.MossTurtle, CreatureKind.VoltRabbit }));
        }

        [UnityTest]
        public IEnumerator DirectBattleSceneHasBaseTriosAndExactlyTwoSerializedActorViews()
        {
            yield return SceneManager.LoadSceneAsync(BattlePath, LoadSceneMode.Single);
            yield return WaitForFlow(GachaSceneKind.Battle);
            Assert.That(_flow.IsReady, Is.True);
            Assert.That(_flow.Run.Party.Count, Is.EqualTo(3));
            Assert.That(_flow.Run.Money, Is.EqualTo(GachaCatalog.StartingMoney));
            Assert.That(_flow.Battle.PlayerTeam.Count, Is.EqualTo(3));
            Assert.That(_flow.Battle.EnemyTeam.Count, Is.EqualTo(3));
            Assert.That(Object.FindObjectsByType<GachaMainFlow>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
            Assert.That(Object.FindObjectsByType<BattleSceneView>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
            Assert.That(Object.FindObjectsByType<BattleActorView>(FindObjectsSortMode.None).Length, Is.EqualTo(2));
            Assert.That(Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
            Assert.That(Object.FindFirstObjectByType<GraphicRaycaster>(), Is.Not.Null);
            foreach (string name in new[] { "AttackButton", "SpecialButton", "DodgeButton", "PauseBattleButton" })
            {
                Assert.That(FindButton(name), Is.Not.Null, name);
            }
        }

        [UnityTest]
        public IEnumerator PauseAndFocusLossFreezeTheSimulationUntilExplicitResume()
        {
            yield return SceneManager.LoadSceneAsync(BattlePath, LoadSceneMode.Single);
            yield return WaitForFlow(GachaSceneKind.Battle);
            BattleDataContainer battle = GetBattleModel();
            AdvanceSummoning(battle);
            FindButton("PauseBattleButton").onClick.Invoke();
            yield return null;
            Assert.That(battle.Paused, Is.True);
            float time = battle.ElapsedTime;
            Vector2 position = battle.Player.Position;
            float health = battle.Player.Health;
            for (int step = 0; step < 30; step++)
            {
                battle.Tick(0.05f);
            }
            yield return null;
            Assert.That(battle.ElapsedTime, Is.EqualTo(time));
            Assert.That(battle.Player.Position, Is.EqualTo(position));
            Assert.That(battle.Player.Health, Is.EqualTo(health));
            FindButton("ResumeBattleButton").onClick.Invoke();
            yield return null;
            Assert.That(battle.Paused, Is.False);
            _flow.SubmitCombat(Vector2.down, true);
            _flow.SendMessage("OnApplicationFocus", false);
            yield return null;
            Assert.That(battle.Paused, Is.True);
            _flow.SendMessage("OnApplicationFocus", true);
            yield return null;
            Assert.That(battle.Paused, Is.True, "포커스 복귀만으로 전투가 재개되면 안 됩니다.");
            FindButton("ResumeBattleButton").onClick.Invoke();
            yield return null;
            Assert.That(battle.Paused, Is.False);
            Assert.That(battle.Player.Position, Is.EqualTo(position), "포커스 상실 전에 수집된 이동 입력이 남으면 안 됩니다.");
        }

        [UnityTest]
        public IEnumerator VictoryResultFixtureReturnsToShopAndClaimsExactlyOnePrize()
        {
            yield return StartShop();
            yield return BuyCreatureAndEquipCrystal();
            IReadOnlyRunData run = _flow.Run;
            PartyCreature creature = run.Party[0];
            int before = run.Money;
            FindButton("BeginBattleButton").onClick.Invoke();
            yield return WaitForFlow(GachaSceneKind.Battle);
            BattleDataContainer previousBattle = GetBattleModel();
            SetResultFixture(previousBattle, victory: true);
            yield return null;
            Assert.That(_flow.Battle.Phase, Is.EqualTo(BattlePhase.Victory));
            Button finish = FindButton("FinishBattleButton");
            Assert.That(finish.gameObject.activeInHierarchy, Is.True);
            finish.onClick.Invoke();
            finish.onClick.Invoke();
            yield return WaitForFlow(GachaSceneKind.Shop);
            Assert.That(_flow.Run, Is.SameAs(run));
            Assert.That(run.Money, Is.EqualTo(before + RunDataContainer.VictoryPrize));
            Assert.That(run.BattleInProgress, Is.False);
            Assert.That(run.Party[0], Is.SameAs(creature));
            Assert.That(run.Party[0].EquippedCrystal, Is.EqualTo(creature.EquippedCrystal));
            FindButton("BeginBattleButton").onClick.Invoke();
            yield return WaitForFlow(GachaSceneKind.Battle);
            Assert.That(_flow.Battle, Is.Not.SameAs(previousBattle));
            Assert.That(_flow.Battle.Player.Health, Is.EqualTo(creature.MaxHealth));
            Assert.That(_flow.Battle.EnemyTeam.All(fighter => fighter.Health == fighter.MaxHealth), Is.True);
            Assert.That(run.Money, Is.EqualTo(before + RunDataContainer.VictoryPrize));
        }

        [UnityTest]
        public IEnumerator DefeatResultFixtureEndsRunAndNextTitleStartIsEmpty()
        {
            yield return SceneManager.LoadSceneAsync(BattlePath, LoadSceneMode.Single);
            yield return WaitForFlow(GachaSceneKind.Battle);
            IReadOnlyRunData defeatedRun = _flow.Run;
            SetResultFixture(GetBattleModel(), victory: false);
            yield return null;
            Assert.That(_flow.Battle.Phase, Is.EqualTo(BattlePhase.Defeat));
            FindButton("FinishBattleButton").onClick.Invoke();
            yield return WaitForFlow(GachaSceneKind.Title);
            yield return StartShop();
            Assert.That(_flow.Run, Is.Not.SameAs(defeatedRun));
            Assert.That(_flow.Run.Party, Is.Empty);
            Assert.That(_flow.Run.Money, Is.EqualTo(GachaCatalog.StartingMoney));
            Assert.That(_flow.Run.BattleInProgress, Is.False);
        }

        private IEnumerator StartShop()
        {
            FindButton("StartRunButton").onClick.Invoke();
            yield return WaitForFlow(GachaSceneKind.Shop);
        }

        private IEnumerator BuyCreatureAndEquipCrystal()
        {
            yield return MoveTo(ShopDataContainer.CreatureInteractionPoint);
            _flow.Submit(GachaAction.Interact);
            _flow.Submit(GachaAction.Buy);
            _flow.Submit(GachaAction.CloseModal);
            yield return null;
            yield return MoveTo(ShopDataContainer.CrystalInteractionPoint);
            _flow.Submit(GachaAction.Interact);
            _flow.Submit(GachaAction.Buy);
            _flow.Submit(GachaAction.CloseModal);
            yield return null;
            CrystalKind kind = GachaCatalog.Crystals.Single(definition => _flow.Run.GetCrystalCount(definition.Kind) == 1).Kind;
            FindButton("CrystalEquip" + (int)kind).onClick.Invoke();
            yield return null;
            Assert.That(_flow.Run.Party[0].EquippedCrystal, Is.EqualTo(kind));
        }

        private IEnumerator MoveTo(Vector2 target)
        {
            for (int step = 0; step < 150 && Vector2.Distance(_flow.Shop.Position, target) > 0.12f; step++)
            {
                Vector2 delta = target - _flow.Shop.Position;
                _flow.SubmitMove(delta.normalized, Mathf.Min(0.05f, delta.magnitude / ShopDataContainer.MoveSpeed));
                yield return null;
            }
            Assert.That(Vector2.Distance(_flow.Shop.Position, target), Is.LessThanOrEqualTo(0.12f));
        }

        private IEnumerator WaitForFlow(GachaSceneKind kind)
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
            Assert.Fail("씬이 준비되지 않았습니다: " + kind);
        }

        private Button FindButton(string objectName)
        {
            Component view = _flow.SceneKind == GachaSceneKind.Battle
                ? (Component)Object.FindFirstObjectByType<BattleSceneView>()
                : Object.FindFirstObjectByType<GachaSceneView>();
            Assert.That(view, Is.Not.Null);
            return view.GetComponentsInChildren<Button>(true).Single(button => button.name == objectName);
        }

        private BattleDataContainer GetBattleModel()
        {
            return (BattleDataContainer)typeof(GachaMainFlow).GetField("_battle", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(_flow);
        }

        private static void AdvanceSummoning(BattleDataContainer battle)
        {
            for (int step = 0; step < 40 && battle.Phase == BattlePhase.Summoning; step++)
            {
                battle.Tick(0.05f);
            }
            Assert.That(battle.Phase, Is.EqualTo(BattlePhase.Fighting));
        }

        private static void SetResultFixture(BattleDataContainer battle, bool victory)
        {
            // 결과 화면/정산/씬 연결의 fixture만 설정한다. 실제 피해·승패 계산은 BattleDataTests에서 검증한다.
            battle.SetPaused(false);
            AdvanceSummoning(battle);
            PropertyInfo health = typeof(BattleFighter).GetProperty(nameof(BattleFighter.Health));
            foreach (BattleFighter fighter in victory ? battle.EnemyTeam : battle.PlayerTeam)
            {
                health.SetValue(fighter, 0f);
            }
            for (int step = 0; step < 50 && battle.Phase == BattlePhase.Fighting; step++)
            {
                battle.Tick(0.05f);
            }
            Assert.That(battle.Phase, Is.EqualTo(victory ? BattlePhase.Victory : BattlePhase.Defeat));
        }
    }
}
