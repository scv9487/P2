using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Refactor.Main;
using Refactor.Main.InputModule;
using Refactor.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace P2.Tests
{
    // 기존 진행 저장과 실제 상점 UI를 함께 사용한다. 가격/저장 형식의 세부 단위 검증은 별도 테스트가 맡는다.
    public sealed class ShopSceneTests
    {
        private const string TownPath = "Assets/0_Project/2_Town/0_Scenes/2_Town.unity";
        private AdventureSaveScope _save;
        private MainFlow _flow;
        private TownView _view;

        [SetUp]
        public void SetUp()
        {
            _save = new AdventureSaveScope();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.IsValid() && scene.path == TownPath)
            {
                var empty = SceneManager.CreateScene("P2 Shop Test Cleanup");
                SceneManager.SetActiveScene(empty);
                yield return SceneManager.UnloadSceneAsync(scene);
            }
            _save?.Dispose();
        }

        [UnityTest]
        public IEnumerator ExistingInventoryCanBeSoldBoughtBackAndSpentWithIndependentMerchantStocks()
        {
            WriteVersionOneSave(50, 3);
            string legacySave = File.ReadAllText(_save.SavePath);
            yield return LoadTown();
            Assert.That(_flow.AdventureProgress.PlayerGold, Is.EqualTo(50));
            Assert.That(ItemCount("rat_tail"), Is.EqualTo(3));
            Assert.That(File.ReadAllText(_save.SavePath), Is.EqualTo(legacySave), "불러오기만으로 기존 저장 파일을 덮어쓰지 않습니다.");
            yield return OpenShop("tavern");
            Assert.That(Stock("tavern", "health_potion"), Is.EqualTo(10));
            Assert.That(Stock("market", "health_potion"), Is.EqualTo(10));
            Assert.That(ButtonNamed("ShopSell_rat_tail"), Is.Not.Null);
            Assert.That(ButtonNamed("ShopBuy_health_potion"), Is.Not.Null);
            Assert.That(_flow.Shop.MerchantId, Is.EqualTo("tavern"));
            AssertTextFits();

            yield return Click(ButtonNamed("ShopSell_rat_tail"));
            Assert.That(_flow.Shop.IsBuying, Is.False);
            yield return Click(ButtonNamed("ShopIncreaseButton"));
            Assert.That(_flow.Shop.Quantity, Is.EqualTo(2));
            yield return Click(ButtonNamed("ShopConfirmButton"), 2);
            Assert.That(_flow.AdventureProgress.PlayerGold, Is.EqualTo(70));
            Assert.That(ItemCount("rat_tail"), Is.EqualTo(1));
            Assert.That(Stock("tavern", "rat_tail"), Is.EqualTo(2));
            Assert.That(_flow.Shop.SelectedItemId, Is.Empty);

            yield return Click(ButtonNamed("ShopBuy_rat_tail"));
            Assert.That(_flow.Shop.IsBuying, Is.True);
            yield return Click(ButtonNamed("ShopConfirmButton"));
            Assert.That(_flow.AdventureProgress.PlayerGold, Is.EqualTo(60));
            Assert.That(ItemCount("rat_tail"), Is.EqualTo(2));
            Assert.That(Stock("tavern", "rat_tail"), Is.EqualTo(1));

            yield return Click(ButtonNamed("ShopBuy_health_potion"));
            Assert.That(_flow.Shop.UnitPrice, Is.EqualTo(20));
            yield return Click(ButtonNamed("ShopIncreaseButton"));
            Assert.That(_flow.Shop.Quantity, Is.EqualTo(2));
            yield return Click(ButtonNamed("ShopConfirmButton"));
            Assert.That(_flow.AdventureProgress.PlayerGold, Is.EqualTo(20));
            Assert.That(ItemCount("health_potion"), Is.EqualTo(2));
            Assert.That(Stock("tavern", "health_potion"), Is.EqualTo(8));
            Assert.That(Stock("market", "health_potion"), Is.EqualTo(10));
            Assert.That(Stock("market", "rat_tail"), Is.Zero);
            AssertTextFits();
            yield return CloseAndLeaveShop();
            yield return OpenShop("market");
            Assert.That(_flow.Shop.MerchantId, Is.EqualTo("market"));
            Assert.That(_flow.Shop.MerchantItems.Single(item => item.ItemId == "health_potion").Quantity, Is.EqualTo(10));
            Assert.That(_flow.Shop.MerchantItems.Any(item => item.ItemId == "rat_tail"), Is.False);
            yield return CloseAndLeaveShop();

            var oldProgress = _flow.AdventureProgress;
            yield return LoadTown();
            Assert.That(_flow.AdventureProgress, Is.Not.SameAs(oldProgress));
            Assert.That(_flow.AdventureProgress.PlayerGold, Is.EqualTo(20));
            Assert.That(_flow.AdventureProgress.GetGold("mira"), Is.EqualTo(10), "개인 거래는 동료 금화를 바꾸지 않습니다.");
            Assert.That(ItemCount("rat_tail"), Is.EqualTo(2));
            Assert.That(ItemCount("health_potion"), Is.EqualTo(2));
            Assert.That(Stock("tavern", "health_potion"), Is.EqualTo(8));
            Assert.That(Stock("tavern", "rat_tail"), Is.EqualTo(1));
            Assert.That(Stock("market", "health_potion"), Is.EqualTo(10));
            Assert.That(_flow.AdventureProgress.HasSettlement("shop-legacy-fixture"), Is.True);
            AssertTextFits();
            yield return OpenShop("tavern");
            Assert.That(_flow.Shop.MerchantItems.Single(item => item.ItemId == "health_potion").Quantity, Is.EqualTo(8));
            Assert.That(_flow.Shop.PlayerItems.Single(item => item.ItemId == "health_potion").Quantity, Is.EqualTo(2));
            AssertSingleRuntime();
        }

        [UnityTest]
        public IEnumerator ShopBlocksHiddenControlsUnaffordableAmountsAndRequestsFromPreviousSelections()
        {
            WriteVersionOneSave(25, 1);
            yield return LoadTown();
            yield return OpenShop("market");
            var leave = ButtonNamed("LeaveLocationButton");
            Assert.That(TopButtonAt(leave), Is.Not.SameAs(leave));
            leave.onClick.Invoke();
            yield return null;
            Assert.That(_flow.TownData.LocationId, Is.EqualTo("market"));
            Assert.That(_flow.TownData.ShopOpen, Is.True);
            yield return Click(ButtonNamed("ShopBuy_health_potion"));
            string firstSelection = _flow.Shop.Context;
            yield return Click(ButtonNamed("ShopIncreaseButton"));
            Assert.That(_flow.Shop.Quantity, Is.EqualTo(2));
            Assert.That(_flow.Shop.CanConfirm, Is.False);
            Assert.That(ButtonNamed("ShopConfirmButton").IsInteractable(), Is.False);
            Assert.That(TextNamed("ShopStatusText").text,
                Is.EqualTo(string.Format(_flow.ShopSheet.Text("shop.insufficient_format"), 15)));
            _flow.UIInput.AddInput(UIInputType.ShopConfirm, null, firstSelection);
            yield return null;
            Assert.That(_flow.AdventureProgress.PlayerGold, Is.EqualTo(25));
            Assert.That(ItemCount("health_potion"), Is.Zero);
            Assert.That(Stock("market", "health_potion"), Is.EqualTo(10));
            AssertTextFits();

            yield return Click(ButtonNamed("ShopDecreaseButton"));
            Assert.That(_flow.Shop.Quantity, Is.EqualTo(1));
            Assert.That(ButtonNamed("ShopDecreaseButton").IsInteractable(), Is.False);
            Assert.That(_flow.Shop.CanConfirm, Is.True);
            string confirmedContext = _flow.Shop.Context;
            yield return Click(ButtonNamed("ShopConfirmButton"), 2);
            Assert.That(_flow.AdventureProgress.PlayerGold, Is.EqualTo(5));
            Assert.That(ItemCount("health_potion"), Is.EqualTo(1));
            Assert.That(Stock("market", "health_potion"), Is.EqualTo(9));
            Assert.That(_flow.Shop.SelectedItemId, Is.Empty);
            yield return Click(ButtonNamed("ShopCloseButton"));
            Assert.That(_flow.TownData.ShopOpen, Is.False);
            Assert.That(_flow.TownData.LocationId, Is.EqualTo("market"));
            yield return Click(ButtonNamed("Command_trade"));
            yield return Click(ButtonNamed("ShopSell_rat_tail"));
            string currentContext = _flow.Shop.Context;
            Assert.That(currentContext, Is.Not.EqualTo(confirmedContext));
            Assert.That(ButtonNamed("ShopIncreaseButton").IsInteractable(), Is.False, "내가 가진 한 개보다 많이 팔 수 없습니다.");
            _flow.UIInput.AddInput(UIInputType.ShopConfirm, null, confirmedContext);
            _flow.UIInput.AddInput(UIInputType.ShopClose, null, confirmedContext);
            yield return null;
            Assert.That(_flow.TownData.ShopOpen, Is.True);
            Assert.That(_flow.Shop.Context, Is.EqualTo(currentContext));
            Assert.That(_flow.AdventureProgress.PlayerGold, Is.EqualTo(5));
            Assert.That(ItemCount("rat_tail"), Is.EqualTo(1));
            _flow.UIInput.AddInput(UIInputType.Back);
            yield return null;
            Assert.That(_flow.TownData.ShopOpen, Is.False);
            Assert.That(_flow.TownData.LocationId, Is.EqualTo("market"));
            AssertTextFits();
        }

        [UnityTest]
        public IEnumerator TradeSaveFailurePreservesExistingFileGoldInventoryAndStockUntilRetry()
        {
            WriteVersionOneSave(50, 1);
            yield return LoadTown();
            yield return OpenShop("tavern");
            yield return Click(ButtonNamed("ShopBuy_health_potion"));
            string before = File.ReadAllText(_save.SavePath);
            string blockedTemporaryPath = _save.SavePath + ".tmp";
            Directory.CreateDirectory(blockedTemporaryPath);
            yield return Click(ButtonNamed("ShopConfirmButton"));
            Assert.That(_flow.CurrentScene, Is.EqualTo(SceneType.Town));
            Assert.That(_flow.TownData.ShopOpen, Is.True);
            Assert.That(_flow.AdventureProgress.PlayerGold, Is.EqualTo(50));
            Assert.That(ItemCount("rat_tail"), Is.EqualTo(1));
            Assert.That(ItemCount("health_potion"), Is.Zero);
            Assert.That(Stock("tavern", "health_potion"), Is.EqualTo(10));
            Assert.That(_flow.Shop.SelectedItemId, Is.EqualTo("health_potion"));
            Assert.That(_flow.Shop.Quantity, Is.EqualTo(1));
            Assert.That(File.ReadAllText(_save.SavePath), Is.EqualTo(before));
            Assert.That(TextNamed("ShopStatusText").text, Is.EqualTo(_flow.ShopSheet.Text("status.save_failed")));
            AssertTextFits();
            Directory.Delete(blockedTemporaryPath);
            yield return Click(ButtonNamed("ShopConfirmButton"), 2);
            Assert.That(_flow.AdventureProgress.PlayerGold, Is.EqualTo(30));
            Assert.That(ItemCount("health_potion"), Is.EqualTo(1));
            Assert.That(Stock("tavern", "health_potion"), Is.EqualTo(9));
            yield return LoadTown();
            Assert.That(_flow.AdventureProgress.PlayerGold, Is.EqualTo(30));
            Assert.That(ItemCount("health_potion"), Is.EqualTo(1));
            Assert.That(Stock("tavern", "health_potion"), Is.EqualTo(9));
        }

        private void WriteVersionOneSave(long gold, int tails)
        {
            // 실제로 배포된 v1의 필드만 사용한다. v2 상인 재고는 씬 초기화의 마이그레이션 경로가 만든다.
            string json = "{\"Format\":\"P2.Adventure\",\"Version\":1,\"MemberIds\":[\"hero\",\"mira\"],"
                + "\"MemberGold\":[" + gold + ",10],\"ItemIds\":[\"rat_tail\"],\"ItemCounts\":[" + tails
                + "],\"SettlementIds\":[\"shop-legacy-fixture\"]}";
            File.WriteAllText(_save.SavePath, json);
        }

        private IEnumerator LoadTown()
        {
            Assert.That(Application.CanStreamedLevelBeLoaded(TownPath), Is.True);
            var load = SceneManager.LoadSceneAsync(TownPath, LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null);
            float deadline = Time.realtimeSinceStartup + 30f;
            while (!load.isDone && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
            Assert.That(load.isDone, Is.True);
            while (Time.realtimeSinceStartup < deadline)
            {
                var flow = Object.FindFirstObjectByType<MainFlow>();
                if (flow != null && flow.IsReady && flow.CurrentScene == SceneType.Town)
                {
                    _flow = flow;
                    _view = Object.FindFirstObjectByType<TownView>();
                    Assert.That(_flow.AdventureSavePath, Is.EqualTo(_save.SavePath));
                    _flow.SendMessage("OnApplicationFocus", true);
                    yield return null;
                    yield return null;
                    AssertSingleRuntime();
                    yield break;
                }
                yield return null;
            }
            Assert.Fail("상점 테스트의 마을 씬 초기화 시간이 초과되었습니다.");
        }

        private IEnumerator OpenShop(string id)
        {
            yield return Click(ButtonNamed("Location_" + id));
            yield return Click(ButtonNamed("Command_trade"));
            Assert.That(_flow.TownData.ShopOpen, Is.True);
            Assert.That(_flow.TownData.ShopId, Is.EqualTo(id));
            Assert.That(_flow.TownData.CommandId, Is.Empty);
            Assert.That(_flow.Shop, Is.Not.Null);
            Assert.That(ButtonNamed("ShopCloseButton").IsInteractable(), Is.True);
        }

        private IEnumerator CloseAndLeaveShop()
        {
            yield return Click(ButtonNamed("ShopCloseButton"));
            Assert.That(_flow.TownData.ShopOpen, Is.False);
            yield return Click(ButtonNamed("LeaveLocationButton"));
            Assert.That(_flow.TownData.LocationId, Is.Empty);
        }

        private int ItemCount(string id) => _flow.AdventureProgress.Items.TryGetValue(id, out int quantity) ? quantity : 0;
        private int Stock(string merchant, string id) => _flow.AdventureProgress.GetMerchantStock(merchant).TryGetValue(id, out int quantity) ? quantity : 0;
        private Button ButtonNamed(string name) => _view.GetComponentsInChildren<Button>().Single(button => button.name == name);
        private Text TextNamed(string name) => _view.GetComponentsInChildren<Text>(true).Single(text => text.name == name);

        private static IEnumerator Click(Button button, int count = 1)
        {
            GameObject hit = null;
            float deadline = Time.realtimeSinceStartup + 2f;
            for (int frame = 0; frame < 60 && Time.realtimeSinceStartup < deadline; frame++)
            {
                hit = RaycastAt(button);
                if (button.IsInteractable() && hit != null && hit.GetComponentInParent<Button>() == button)
                {
                    break;
                }
                var scroll = button.GetComponentInParent<ScrollRect>();
                if (scroll != null)
                {
                    Vector2 center = ScreenCenter(button);
                    var corners = new Vector3[4];
                    scroll.viewport.GetWorldCorners(corners);
                    float direction = center.y < corners[0].y ? -1 : center.y > corners[1].y ? 1 : 0;
                    if (direction != 0)
                    {
                        ExecuteEvents.Execute(scroll.gameObject, new PointerEventData(EventSystem.current)
                        {
                            position = center,
                            scrollDelta = new Vector2(0, direction * 3)
                        }, ExecuteEvents.scrollHandler);
                    }
                }
                yield return null;
            }
            Assert.That(button.IsInteractable(), Is.True, button.name);
            Assert.That(hit, Is.Not.Null, button.name + " 레이캐스트 결과 없음");
            Assert.That(hit.GetComponentInParent<Button>(), Is.SameAs(button), button.name + " 다른 UI가 가립니다.");
            var pointer = new PointerEventData(EventSystem.current)
            {
                button = PointerEventData.InputButton.Left,
                position = ScreenCenter(button)
            };
            for (int i = 0; i < count; i++)
            {
                ExecuteEvents.ExecuteHierarchy(hit, pointer, ExecuteEvents.pointerClickHandler);
            }
            yield return null;
            yield return null;
        }

        private static Button TopButtonAt(Button button) => RaycastAt(button)?.GetComponentInParent<Button>();

        private static GameObject RaycastAt(Button button)
        {
            Canvas.ForceUpdateCanvases();
            var pointer = new PointerEventData(EventSystem.current) { position = ScreenCenter(button) };
            var results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, results);
            return results.Count == 0 ? null : results[0].gameObject;
        }

        private static Vector2 ScreenCenter(Button button)
        {
            var rect = (RectTransform)button.transform;
            var canvas = button.GetComponentInParent<Canvas>();
            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            return RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(rect.rect.center));
        }

        private void AssertTextFits()
        {
            Canvas.ForceUpdateCanvases();
            var failures = new List<string>();
            foreach (var text in _view.GetComponentsInChildren<Text>())
            {
                if (!text.isActiveAndEnabled || string.IsNullOrWhiteSpace(text.text) || text.verticalOverflow == VerticalWrapMode.Overflow)
                {
                    continue;
                }
                Rect rect = text.rectTransform.rect;
                if (text.font == null || rect.width <= 0 || rect.height <= 0 || text.preferredHeight > rect.height + 2f)
                {
                    failures.Add(text.transform.parent.name + "/" + text.name + $" 높이 {text.preferredHeight:F1} > {rect.height:F1}: {text.text}");
                }
            }
            Assert.That(failures, Is.Empty, string.Join("\n", failures));
        }

        private static void AssertSingleRuntime()
        {
            Assert.That(Object.FindObjectsByType<MainFlow>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
            Assert.That(Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
        }
    }
}
