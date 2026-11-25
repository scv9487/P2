using System.Collections;
using System.Linq;
using GachaTrainers;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace P2.Tests
{
    // 실제 게임 씬과 버튼의 구독, 요청 큐, 씬 수명 및 런 초기화를 함께 검증한다.
    public sealed class GachaSceneTests
    {
        private const string TitlePath = "Assets/0_Project/GachaTrainers/Scenes/Title.unity";
        private const string ShopPath = "Assets/0_Project/GachaTrainers/Scenes/GachaShop.unity";
        private GachaMainFlow _flow;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Assert.That(Application.CanStreamedLevelBeLoaded(TitlePath), Is.True);
            Assert.That(Application.CanStreamedLevelBeLoaded(ShopPath), Is.True);
            yield return SceneManager.LoadSceneAsync(TitlePath, LoadSceneMode.Single);
            yield return WaitForFlow(GachaSceneKind.Title);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Scene active = SceneManager.GetActiveScene();
            if (active.path == TitlePath || active.path == ShopPath)
            {
                Scene empty = SceneManager.CreateScene("Gacha Test Cleanup");
                SceneManager.SetActiveScene(empty);
                yield return SceneManager.UnloadSceneAsync(active);
            }
        }

        [UnityTest]
        public IEnumerator TitleButtonStartsFreshRunAndShopHasOneInputAndViewOwner()
        {
            GachaMainFlow title = _flow;
            FindButton("StartRunButton").onClick.Invoke();
            Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo(TitlePath),
                "버튼 callback에서 즉시 씬을 변경하면 안 됩니다.");
            yield return WaitForFlow(GachaSceneKind.Shop);
            Assert.That(title == null, Is.True);
            Assert.That(_flow.Run.Money, Is.EqualTo(GachaCatalog.StartingMoney));
            Assert.That(_flow.Run.Party, Is.Empty);
            Assert.That(_flow.Shop.Position, Is.EqualTo(new Vector2(-2.7f, -1.6f)));
            Assert.That(Object.FindObjectsByType<GachaMainFlow>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
            Assert.That(Object.FindObjectsByType<GachaSceneView>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
            Assert.That(Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
            Assert.That(Object.FindFirstObjectByType<GraphicRaycaster>(), Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator CreatureAndCrystalMachinesBuyThroughRealButtonsAndEquipPassive()
        {
            yield return StartRun();
            yield return MoveTo(ShopDataContainer.CreatureInteractionPoint);
            _flow.Submit(GachaAction.Interact);
            Assert.That(_flow.Shop.Modal, Is.EqualTo(ShopModal.None));
            yield return null;
            Assert.That(_flow.Shop.Modal, Is.EqualTo(ShopModal.Machine));
            int initialMoney = _flow.Run.Money;
            FindButton("BuyButton").onClick.Invoke();
            Assert.That(_flow.Run.Money, Is.EqualTo(initialMoney));
            yield return null;
            Assert.That(_flow.Run.Party.Count, Is.EqualTo(1));
            Assert.That(_flow.Run.Money, Is.EqualTo(initialMoney - GachaCatalog.CreaturePrice));
            Vector2 modalPosition = _flow.Shop.Position;
            _flow.SubmitMove(Vector2.down, 0.05f);
            yield return null;
            Assert.That(_flow.Shop.Position, Is.EqualTo(modalPosition));
            FindButton("CloseModalButton").onClick.Invoke();
            yield return null;
            yield return MoveTo(ShopDataContainer.CrystalInteractionPoint);
            _flow.Submit(GachaAction.Interact);
            yield return null;
            Assert.That(_flow.Shop.ActiveMachine, Is.EqualTo(GachaMachineKind.Crystal));
            FindButton("BuyButton").onClick.Invoke();
            yield return null;
            Assert.That(_flow.Run.Money,
                Is.EqualTo(initialMoney - GachaCatalog.CreaturePrice - GachaCatalog.CrystalPrice));
            CrystalKind acquired = GachaCatalog.Crystals
                .Single(definition => _flow.Run.GetCrystalCount(definition.Kind) == 1).Kind;
            FindButton("CloseModalButton").onClick.Invoke();
            _flow.Submit(GachaAction.EquipCrystal, (int)acquired);
            yield return null;
            Assert.That(_flow.Run.Party[0].EquippedCrystal, Is.EqualTo(acquired));
            Assert.That(_flow.Run.GetCrystalCount(acquired), Is.Zero);
        }

        [UnityTest]
        public IEnumerator ReturnCancelPreservesRunAndConfirmedReturnMakesNextRunFresh()
        {
            yield return StartRun();
            yield return MoveTo(ShopDataContainer.CreatureInteractionPoint);
            _flow.Submit(GachaAction.Interact);
            _flow.Submit(GachaAction.Buy);
            _flow.Submit(GachaAction.CloseModal);
            yield return null;
            IReadOnlyRunData previousRun = _flow.Run;
            Assert.That(previousRun.Party.Count, Is.EqualTo(1));
            FindButton("ReturnTitleButton").onClick.Invoke();
            yield return null;
            Assert.That(_flow.Shop.Modal, Is.EqualTo(ShopModal.ReturnTitle));
            _flow.Submit(GachaAction.CloseModal);
            yield return null;
            Assert.That(_flow.Run, Is.SameAs(previousRun));
            Assert.That(_flow.Run.Party.Count, Is.EqualTo(1));
            FindButton("ReturnTitleButton").onClick.Invoke();
            yield return null;
            FindButton("ConfirmReturnTitleButton").onClick.Invoke();
            yield return WaitForFlow(GachaSceneKind.Title);
            yield return StartRun();
            Assert.That(_flow.Run, Is.Not.SameAs(previousRun));
            Assert.That(_flow.Run.Money, Is.EqualTo(GachaCatalog.StartingMoney));
            Assert.That(_flow.Run.Party, Is.Empty);
            foreach (var definition in GachaCatalog.Crystals)
            {
                Assert.That(_flow.Run.GetCrystalCount(definition.Kind), Is.Zero);
            }
            Assert.That(Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
            yield return MoveTo(ShopDataContainer.CreatureInteractionPoint);
            _flow.Submit(GachaAction.Interact);
            yield return null;
            FindButton("BuyButton").onClick.Invoke();
            yield return null;
            Assert.That(_flow.Run.Party.Count, Is.EqualTo(1), "씬 재진입 후 버튼 listener가 중복되면 안 됩니다.");
            Assert.That(_flow.Run.Money, Is.EqualTo(GachaCatalog.StartingMoney - GachaCatalog.CreaturePrice));
        }

        [UnityTest]
        public IEnumerator FocusLossDiscardsPendingPurchaseAndResumeDoesNotReplayIt()
        {
            yield return StartRun();
            yield return MoveTo(ShopDataContainer.CreatureInteractionPoint);
            _flow.Submit(GachaAction.Interact);
            yield return null;
            FindButton("BuyButton").onClick.Invoke();
            _flow.SendMessage("OnApplicationFocus", false);
            yield return null;
            Assert.That(_flow.Run.Money, Is.EqualTo(GachaCatalog.StartingMoney));
            Assert.That(_flow.Run.Party, Is.Empty);
            _flow.SendMessage("OnApplicationFocus", true);
            yield return null;
            Assert.That(_flow.Run.Party, Is.Empty);
            FindButton("BuyButton").onClick.Invoke();
            yield return null;
            Assert.That(_flow.Run.Party.Count, Is.EqualTo(1));
            Assert.That(_flow.Run.Money, Is.EqualTo(GachaCatalog.StartingMoney - GachaCatalog.CreaturePrice));
        }

        private IEnumerator StartRun()
        {
            // This flow exercises equipment, so replay a non-legendary draw; token draws have their own tests.
            Random.InitState(47);
            for (int attempt = 0; attempt < 100; attempt++)
            {
                var state = Random.state;
                if (Random.Range(0, GachaCatalog.CreatureWeightTotal) < GachaCatalog.LegendaryRollStart)
                {
                    Random.state = state;
                    break;
                }
            }
            FindButton("StartRunButton").onClick.Invoke();
            yield return WaitForFlow(GachaSceneKind.Shop);
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
                    yield break;
                }
            }
            Assert.Fail("씬 MainFlow가 준비되지 않았습니다: " + kind);
        }

        private IEnumerator MoveTo(Vector2 target)
        {
            for (int step = 0; step < 150 && Vector2.Distance(_flow.Shop.Position, target) > 0.12f; step++)
            {
                Vector2 delta = target - _flow.Shop.Position;
                _flow.SubmitMove(delta.normalized, Mathf.Min(0.05f, delta.magnitude / 4.2f));
                yield return null;
            }
            Assert.That(Vector2.Distance(_flow.Shop.Position, target), Is.LessThanOrEqualTo(0.12f));
        }

        private Button FindButton(string objectName)
        {
            GachaSceneView view = Object.FindFirstObjectByType<GachaSceneView>();
            Assert.That(view, Is.Not.Null);
            return view.GetComponentsInChildren<Button>(true).Single(button => button.name == objectName);
        }
    }
}
