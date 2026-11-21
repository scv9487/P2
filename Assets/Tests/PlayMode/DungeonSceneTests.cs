using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Refactor;
using Refactor.Main;
using Refactor.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace P2.Tests
{
    // 실제 씬과 EventSystem 레이캐스트를 사용한다. 영구 상태는 테스트별 임시 경로로 격리한다.
    public sealed class DungeonSceneTests
    {
        private const string TownPath = "Assets/0_Project/2_Town/0_Scenes/2_Town.unity";
        private const string DungeonPath = "Assets/0_Project/3_Dungeon/0_Scenes/3_Dungeon.unity";
        private MainFlow _flow;
        private float _previousTimeScale;
        private float _previousMaximumDeltaTime;
        private AdventureSaveScope _save;

        [SetUp]
        public void SetUp()
        {
            _save = new AdventureSaveScope();
            SeedEstablishedParty();
            _previousTimeScale = Time.timeScale;
            _previousMaximumDeltaTime = Time.maximumDeltaTime;
            Time.timeScale = 4f;
            // 현재 미라/플레이어 준비 경계를 서로 다른 렌더 프레임에서 관찰할 수 있도록 제한한다.
            Time.maximumDeltaTime = 0.1f;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = _previousTimeScale;
            Time.maximumDeltaTime = _previousMaximumDeltaTime;
            var scene = SceneManager.GetActiveScene();
            if (scene.IsValid() && (scene.path == TownPath || scene.path == DungeonPath))
            {
                var empty = SceneManager.CreateScene("P2 Dungeon Test Cleanup");
                SceneManager.SetActiveScene(empty);
                yield return SceneManager.UnloadSceneAsync(scene);
            }
            _save?.Dispose();
        }

        [UnityTest]
        public IEnumerator TownEntranceRequiresSelectionAndConfirmationAndReentryCreatesFreshState()
        {
            yield return LoadScene(SceneType.Town);
            var town = Object.FindFirstObjectByType<TownView>();
            yield return Click(ButtonNamed(town, "Location_dungeon"));
            yield return null;
            Assert.That(_flow.TownData.LocationId, Is.EqualTo("dungeon"));
            CollectionAssert.AreEqual(new[] { "Command_enter_dungeon", "Command_leave" },
                Buttons(town, "Command_").Select(button => button.name));
            Assert.That(Buttons(town, "Character_"), Is.Empty);
            AssertTextFits(town, "LocationNameText");
            yield return Click(ButtonNamed(town, "Command_enter_dungeon"));
            yield return null;
            Assert.That(_flow.TownData.DungeonSelectionOpen, Is.True);
            Assert.That(ButtonNamed(town, "DungeonConfirmButton").IsInteractable(), Is.False);
            CollectionAssert.AreEqual(Enumerable.Range(1, 6).Select(id => "DungeonSelect_" + id),
                Buttons(town, "DungeonSelect_").Select(button => button.name));
            AssertTextFits(town, "DungeonSelectionTitle");
            Assert.That(TopButtonAt(ButtonNamed(town, "LeaveLocationButton")), Is.Not.SameAs(ButtonNamed(town, "LeaveLocationButton")));
            foreach (int id in Enumerable.Range(1, 6))
            {
                yield return Click(ButtonNamed(town, "DungeonSelect_" + id));
                yield return null;
                Assert.That(_flow.TownData.SelectedDungeonId, Is.EqualTo(id));
                Assert.That(_flow.CurrentScene, Is.EqualTo(SceneType.Town));
                Assert.That(ButtonNamed(town, "DungeonConfirmButton").IsInteractable(), Is.True);
            }
            yield return Click(ButtonNamed(town, "DungeonCancelButton"));
            yield return null;
            Assert.That(_flow.TownData.DungeonSelectionOpen, Is.False);
            Assert.That(_flow.TownData.SelectedDungeonId, Is.Zero);
            yield return SelectAndEnter(town, 6);
            var firstFlow = _flow;
            var previousData = _flow.DungeonData;
            Assert.That(previousData.DungeonId, Is.EqualTo(6));
            var view = Object.FindFirstObjectByType<DungeonView>();
            AssertSerializedReferences(view);
            AssertSingleRuntime();
            AssertTextFits(view, "DungeonNameText", "FloorText", "NodeNameText");
            yield return Click(ButtonNamed(view, "DungeonReturnTownButton"));
            yield return ConfirmSettlementAndReturn(view);
            Assert.That(firstFlow == null, Is.True);
            Assert.That(_flow.TownData.LocationId, Is.Empty);
            int previousRevision = previousData.Revision;
            town = Object.FindFirstObjectByType<TownView>();
            yield return Click(ButtonNamed(town, "Location_dungeon"));
            yield return null;
            yield return SelectAndEnter(town, 1);
            Assert.That(_flow.DungeonData, Is.Not.SameAs(previousData));
            Assert.That(_flow.DungeonData.DungeonId, Is.EqualTo(1));
            Assert.That(_flow.DungeonData.Revision, Is.Zero);
            Assert.That(_flow.DungeonData.Floor, Is.EqualTo(1));
            Assert.That(_flow.DungeonData.Allies.All(unit => unit.Hp == unit.MaxHp), Is.True);
            Assert.That(previousData.Revision, Is.EqualTo(previousRevision));
            AssertSingleRuntime();
            view = Object.FindFirstObjectByType<DungeonView>();
            Assert.That(Buttons(view, "Node_").Length, Is.EqualTo(_flow.DungeonData.Nodes.Count));
        }

        [UnityTest]
        public IEnumerator DirectDungeonPlaySupportsRestVisitorAndEventWithBlockingReadableModals()
        {
            yield return LoadScene(SceneType.Dungeon);
            var view = Object.FindFirstObjectByType<DungeonView>();
            AssertSerializedReferences(view);
            AssertSingleRuntime();
            Assert.That(_flow.DungeonData.NodeId, Is.EqualTo("camp"));
            Assert.That(ButtonNamed(view, "Node_cache").IsInteractable(), Is.False);
            Assert.That(ButtonNamed(view, "Node_camp").IsInteractable(), Is.False);
            Assert.That(ButtonNamed(view, "Node_visitor").IsInteractable(), Is.True);
            AssertTextFits(view, "DungeonNameText", "MapHeadingText", "NodeNameText");
            var hintCorners = new Vector3[4];
            var partyCorners = new Vector3[4];
            Reference<Text>(view, "MapHintText").rectTransform.GetWorldCorners(hintCorners);
            view.GetComponentsInChildren<RectTransform>().Single(rect => rect.name == "PartyBar").GetWorldCorners(partyCorners);
            Assert.That(hintCorners[0].y, Is.GreaterThan(partyCorners[1].y), "지도 안내문은 파티 카드 위에 온전히 표시해야 합니다.");
            yield return Click(ButtonNamed(view, "RestButton"));
            yield return null;
            AssertModalBlocksMap(view);
            AssertTextFits(view, "DetailsTitleText");
            yield return Click(ButtonNamed(view, "DungeonDetailsCloseButton"));
            yield return null;
            Assert.That(_flow.DungeonData.DetailsOpen, Is.False);
            Assert.That(ButtonNamed(view, "RestButton").IsInteractable(), Is.False);
            yield return Move(view, "visitor");
            string visitorId = _flow.DungeonSheet.GetNode(1, "visitor").CharacterId;
            var visitor = ButtonNamed(view, "Visitor_" + visitorId);
            Assert.That(visitor.transform.Find("PortraitClip/Portrait").GetComponent<Image>().sprite, Is.Not.Null);
            yield return Click(visitor);
            yield return null;
            Assert.That(_flow.CharacterInteraction.Open, Is.True);
            Assert.That(_flow.CharacterInteraction.CharacterId, Is.EqualTo(visitorId));
            Assert.That(Reference<CanvasGroup>(view, "MapControls").interactable, Is.False);
            yield return Click(ButtonNamed(view, "CharacterTalkButton"));
            Assert.That(_flow.CharacterInteraction.DialogueOpen, Is.True);
            Assert.That(_flow.AdventureProgress.GetCharacter(visitorId).Affinity, Is.EqualTo(10));
            yield return Click(ButtonNamed(view, "CharacterCloseButton"));
            yield return null;
            yield return Move(view, "cache");
            Assert.That(_flow.DungeonData.IsNodeResolved("cache"), Is.True);
            AssertModalBlocksMap(view);
            AssertTextFits(view, "DetailsTitleText");
            yield return Click(ButtonNamed(view, "DungeonDetailsCloseButton"));
            yield return null;
            yield return Move(view, "visitor");
            yield return Move(view, "cache");
            Assert.That(_flow.DungeonData.DetailsOpen, Is.False);
            Assert.That(Buttons(view, "Node_").Length, Is.EqualTo(_flow.DungeonData.Nodes.Count));
            yield return Click(ButtonNamed(view, "DungeonReturnTownButton"));
            yield return ConfirmSettlementAndReturn(view);
            AssertSingleRuntime();
        }

        [UnityTest]
        public IEnumerator ThreeFloorCombatUsesGaugesTargetSelectionHpCarryAndResultReturnButton()
        {
            yield return LoadScene(SceneType.Dungeon);
            var view = Object.FindFirstObjectByType<DungeonView>();
            for (int floor = 1; floor <= 3; floor++)
            {
                Assert.That(_flow.DungeonData.Floor, Is.EqualTo(floor));
                yield return Click(ButtonNamed(view, "RestButton"));
                yield return null;
                AssertTextFits(view, "DetailsTitleText");
                yield return Click(ButtonNamed(view, "DungeonDetailsCloseButton"));
                yield return null;
                yield return Move(view, "visitor");
                yield return Move(view, "cache");
                yield return Click(ButtonNamed(view, "DungeonDetailsCloseButton"));
                yield return null;
                yield return Move(view, floor == 3 ? "boss" : "passage");
                Assert.That(_flow.DungeonData.InCombat, Is.True);
                Assert.That(Reference<GameObject>(view, "BattleRoot").activeSelf, Is.True);
                Assert.That(Buttons(view, "Ally_").Length, Is.EqualTo(_flow.DungeonData.Allies.Count));
                Assert.That(Buttons(view, "Enemy_").Length, Is.EqualTo(_flow.DungeonData.Enemies.Count));
                AssertTextFits(view, "BattleTitleText", "BattleReadyText");
                if (floor == 1)
                {
                    yield return ObserveAutomaticCompanionBeforePlayerTurn(view);
                }
                yield return WinThroughButtons(view);
                Assert.That(_flow.DungeonData.IsDefeated, Is.False);
                Assert.That(_flow.DungeonData.DetailsOpen, Is.True);
                AssertTextFits(view, "DetailsTitleText");
                if (floor == 3)
                {
                    break;
                }
                yield return Click(ButtonNamed(view, "DungeonDetailsCloseButton"));
                yield return null;
                yield return Move(view, "stairs");
                Assert.That(_flow.DungeonData.Floor, Is.EqualTo(floor));
                var hp = _flow.DungeonData.Allies.Select(unit => unit.Hp).ToArray();
                yield return Click(ButtonNamed(view, "NextFloorButton"));
                yield return null;
                CollectionAssert.AreEqual(hp, _flow.DungeonData.Allies.Select(unit => unit.Hp));
                Assert.That(_flow.DungeonData.Floor, Is.EqualTo(floor + 1));
                Assert.That(Buttons(view, "Node_").Length, Is.EqualTo(_flow.DungeonData.Nodes.Count));
                AssertTextFits(view, "FloorText", "NodeNameText");
            }
            Assert.That(_flow.DungeonData.IsCleared, Is.True);
            var returnButton = ButtonNamed(view, "DungeonDetailsCloseButton");
            Assert.That(returnButton.GetComponentInChildren<Text>().text, Is.EqualTo(_flow.DungeonSheet.Text("map.return_town")));
            yield return Click(returnButton);
            yield return ConfirmSettlementAndReturn(view);
            Assert.That(_flow.TownData.LocationId, Is.Empty);
            AssertSingleRuntime();
        }

        [UnityTest]
        public IEnumerator LootPurchaseCancelAndDoubleConfirmPersistAcrossFreshSceneInstances()
        {
            yield return LoadScene(SceneType.Dungeon);
            var view = Object.FindFirstObjectByType<DungeonView>();
            yield return WinGuardBattle(view);
            int acquired = _flow.DungeonData.Loot["rat_tail"];
            Assert.That(acquired, Is.EqualTo(_flow.DungeonSheet.Settings.NormalEnemyCount));
            string node = _flow.DungeonData.NodeId;
            var hp = _flow.DungeonData.Allies.Select(unit => unit.Hp).ToArray();
            long originalGold = _flow.AdventureProgress.PlayerGold;
            var originalItems = _flow.AdventureProgress.Items.ToDictionary(pair => pair.Key, pair => pair.Value);
            string savedBefore = File.Exists(_save.SavePath) ? File.ReadAllText(_save.SavePath) : null;
            yield return Click(ButtonNamed(view, "DungeonReturnTownButton"));
            AssertSettlementVisible(view);
            long total = _flow.Settlement.TotalValue;
            Assert.That(_flow.Settlement.Items.Single(item => item.ItemId == "rat_tail").Purchased, Is.Zero);
            yield return Click(ButtonNamed(view, "LootIncreaseButton"));
            Assert.That(_flow.Settlement.Items.Single(item => item.ItemId == "rat_tail").Purchased, Is.EqualTo(1));
            Assert.That(_flow.Settlement.TotalValue, Is.EqualTo(total), "플레이어 구매대금도 분배 대상 판매대금에 포함합니다.");
            Assert.That(_flow.AdventureProgress.PlayerGold, Is.EqualTo(originalGold));
            CollectionAssert.AreEquivalent(originalItems, _flow.AdventureProgress.Items);
            AssertTextFits(view);
            yield return Click(ButtonNamed(view, "LootIncreaseButton"));
            Assert.That(_flow.Settlement.CanConfirm, Is.False, "현재 예시의 초기 금화와 분배금으로는 두 개를 살 수 없습니다.");
            Assert.That(ButtonNamed(view, "LootConfirmButton").IsInteractable(), Is.False);
            Assert.That(ButtonNamed(view, "LootIncreaseButton").IsInteractable(), Is.False, "획득 수량을 초과해서 구매할 수 없습니다.");
            Assert.That(TextNamed(view, "LootErrorText").text,
                Is.EqualTo(string.Format(_flow.LootSheet.Text("settlement.insufficient_format"), -_flow.Settlement.FinalPlayerGold)));
            AssertTextFits(view);
            yield return Click(ButtonNamed(view, "LootDecreaseButton"));
            Assert.That(_flow.Settlement.CanConfirm, Is.True);
            yield return Click(ButtonNamed(view, "LootCancelButton"));
            Assert.That(_flow.DungeonData.SettlementOpen, Is.False);
            Assert.That(_flow.Settlement, Is.Null);
            Assert.That(_flow.DungeonData.NodeId, Is.EqualTo(node));
            CollectionAssert.AreEqual(hp, _flow.DungeonData.Allies.Select(unit => unit.Hp));
            Assert.That(_flow.DungeonData.Loot["rat_tail"], Is.EqualTo(acquired));
            Assert.That(_flow.DungeonData.IsNodeResolved("guard"), Is.True);
            Assert.That(File.Exists(_save.SavePath) ? File.ReadAllText(_save.SavePath) : null, Is.EqualTo(savedBefore));

            // 첫 탐험은 모두 판매한다. 다음 탐험은 그때 받은 금화까지 사용해 구매한다.
            yield return Click(ButtonNamed(view, "DungeonReturnTownButton"));
            Assert.That(_flow.Settlement.Items.Single(item => item.ItemId == "rat_tail").Purchased, Is.Zero);
            long goldAfterSale = _flow.Settlement.FinalPlayerGold;
            string firstSettlement = _flow.Settlement.Id;
            yield return ConfirmSettlementAndReturn(view);
            Assert.That(_flow.AdventureProgress.PlayerGold, Is.EqualTo(goldAfterSale));
            Assert.That(_flow.AdventureProgress.HasSettlement(firstSettlement), Is.True);
            CollectionAssert.AreEquivalent(originalItems, _flow.AdventureProgress.Items);

            yield return LoadScene(SceneType.Dungeon);
            view = Object.FindFirstObjectByType<DungeonView>();
            Assert.That(_flow.AdventureProgress.PlayerGold, Is.EqualTo(goldAfterSale));
            yield return WinGuardBattle(view);
            yield return Click(ButtonNamed(view, "DungeonReturnTownButton"));
            var itemRow = _flow.Settlement.Items.Single(item => item.ItemId == "rat_tail");
            int purchaseCount = (int)Math.Min(itemRow.Acquired,
                (_flow.Settlement.PlayerGold + _flow.Settlement.PlayerShare) / itemRow.Price);
            Assert.That(purchaseCount, Is.GreaterThan(0));
            long totalBeforePurchase = _flow.Settlement.TotalValue;
            long[] sharesBeforePurchase = _flow.Settlement.Shares.Select(share => share.Gold).ToArray();
            for (int i = 0; i < purchaseCount; i++)
            {
                yield return Click(ButtonNamed(view, "LootIncreaseButton"));
            }
            Assert.That(_flow.Settlement.PurchaseCost, Is.GreaterThan(_flow.Settlement.PlayerShare),
                "현재 예시에서는 기존 금화와 이번 분배금을 함께 사용해야 전량 구매할 수 있습니다.");
            Assert.That(_flow.Settlement.TotalValue, Is.EqualTo(totalBeforePurchase));
            CollectionAssert.AreEqual(sharesBeforePurchase, _flow.Settlement.Shares.Select(share => share.Gold));
            long expectedGold = _flow.Settlement.FinalPlayerGold;
            var expectedMemberGold = _flow.Settlement.Shares.ToDictionary(share => share.MemberId,
                share => _flow.AdventureProgress.GetGold(share.MemberId) + share.Gold);
            expectedMemberGold[_flow.LootSheet.PlayerMemberId] -= _flow.Settlement.PurchaseCost;
            string settlementId = _flow.Settlement.Id;
            AssertTextFits(view);
            yield return Click(ButtonNamed(view, "LootConfirmButton"), 2);
            yield return WaitForScene(SceneType.Town);
            AssertProgressAndInventory(expectedGold, purchaseCount, settlementId, expectedMemberGold);
            var previousProgress = _flow.AdventureProgress;
            yield return LoadScene(SceneType.Town);
            Assert.That(_flow.AdventureProgress, Is.Not.SameAs(previousProgress), "새 MainFlow가 파일에서 진행 상태를 다시 읽어야 합니다.");
            AssertProgressAndInventory(expectedGold, purchaseCount, settlementId, expectedMemberGold);
        }

        [UnityTest]
        public IEnumerator FailedSettlementSaveStaysInDungeonAndRetryCommitsOnlyOnce()
        {
            yield return LoadScene(SceneType.Dungeon);
            var view = Object.FindFirstObjectByType<DungeonView>();
            yield return WinGuardBattle(view);
            yield return Click(ButtonNamed(view, "DungeonReturnTownButton"));
            long beforeGold = _flow.AdventureProgress.PlayerGold;
            long expectedGold = _flow.Settlement.FinalPlayerGold;
            string id = _flow.Settlement.Id;
            string originalSave = File.ReadAllText(_save.SavePath);
            // 처치 경험치 저장은 이미 존재한다. 임시 쓰기 위치만 막아 원본 보존을 검사한다.
            Directory.CreateDirectory(_save.SavePath + ".tmp");
            yield return Click(ButtonNamed(view, "LootConfirmButton"));
            Assert.That(_flow.CurrentScene, Is.EqualTo(SceneType.Dungeon));
            Assert.That(_flow.IsTransitioning, Is.False);
            Assert.That(_flow.DungeonData.SettlementOpen, Is.True);
            Assert.That(_flow.DungeonData.SettlementCommitted, Is.False);
            Assert.That(_flow.AdventureProgress.PlayerGold, Is.EqualTo(beforeGold));
            Assert.That(_flow.AdventureProgress.HasSettlement(id), Is.False);
            Assert.That(TextNamed(view, "LootErrorText").text, Is.EqualTo(_flow.LootSheet.Text("status.save_failed")));
            AssertTextFits(view);
            Assert.That(File.ReadAllText(_save.SavePath), Is.EqualTo(originalSave));
            Directory.Delete(_save.SavePath + ".tmp");
            yield return Click(ButtonNamed(view, "LootConfirmButton"), 2);
            yield return WaitForScene(SceneType.Town);
            Assert.That(_flow.AdventureProgress.PlayerGold, Is.EqualTo(expectedGold));
            Assert.That(_flow.AdventureProgress.HasSettlement(id), Is.True);
            Assert.That(File.Exists(_save.SavePath), Is.True);
        }

        private IEnumerator WinGuardBattle(DungeonView view)
        {
            yield return Move(view, "guard");
            yield return WinThroughButtons(view);
            Assert.That(_flow.DungeonData.IsDefeated, Is.False);
            yield return Click(ButtonNamed(view, "DungeonDetailsCloseButton"));
            Assert.That(_flow.DungeonData.DetailsOpen, Is.False);
        }

        private IEnumerator ConfirmSettlementAndReturn(DungeonView view)
        {
            AssertSettlementVisible(view);
            AssertTextFits(view);
            yield return Click(ButtonNamed(view, "LootConfirmButton"));
            yield return WaitForScene(SceneType.Town);
        }

        private void AssertSettlementVisible(DungeonView view)
        {
            Assert.That(_flow.CurrentScene, Is.EqualTo(SceneType.Dungeon));
            Assert.That(_flow.IsTransitioning, Is.False);
            Assert.That(_flow.DungeonData.SettlementOpen, Is.True);
            Assert.That(_flow.Settlement, Is.Not.Null);
            var returnButton = ButtonNamed(view, "DungeonReturnTownButton");
            Assert.That(TopButtonAt(returnButton), Is.Not.SameAs(returnButton));
            foreach (var node in Buttons(view, "Node_"))
            {
                Assert.That(node.IsInteractable(), Is.False, "정산 뒤쪽의 노드를 조작할 수 없어야 합니다.");
            }
        }

        private void AssertProgressAndInventory(long gold, int tails, string settlementId, Dictionary<string, long> memberGold)
        {
            Assert.That(_flow.AdventureProgress.PlayerGold, Is.EqualTo(gold));
            Assert.That(_flow.AdventureProgress.Items["rat_tail"], Is.EqualTo(tails));
            Assert.That(_flow.AdventureProgress.HasSettlement(settlementId), Is.True);
            foreach (var member in memberGold)
            {
                Assert.That(_flow.AdventureProgress.GetGold(member.Key), Is.EqualTo(member.Value));
            }
            var town = Object.FindFirstObjectByType<TownView>();
            Assert.That(TextNamed(town, "InventoryGoldText").text,
                Is.EqualTo(string.Format(_flow.LootSheet.Text("inventory.gold_format"), gold)));
            Assert.That(TextNamed(town, "InventoryItemsText").text,
                Does.Contain(string.Format(_flow.LootSheet.Text("inventory.item_format"), _flow.LootSheet.GetItem("rat_tail").Name, tails)));
            AssertTextFits(town);
            AssertSingleRuntime();
        }

        private IEnumerator WinThroughButtons(DungeonView view)
        {
            for (int attack = 0; attack < 100 && _flow.DungeonData.InCombat; attack++)
            {
                float deadline = Time.realtimeSinceStartup + 15f;
                while (_flow.DungeonData.InCombat && !_flow.DungeonData.IsAwaitingCommand && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }
                if (!_flow.DungeonData.InCombat)
                {
                    break;
                }
                Assert.That(_flow.DungeonData.IsAwaitingCommand, Is.True, "플레이어 게이지가 준비되지 않았습니다.");
                var ally = _flow.DungeonData.Allies.Single(unit => unit.Id == _flow.DungeonData.ReadyAllyId);
                Assert.That(ally.IsPlayerControlled, Is.True, "동료의 행동을 플레이어에게 선택시키면 안 됩니다.");
                Assert.That(Reference<Text>(view, "BattleReadyText").text,
                    Is.EqualTo(string.Format(_flow.DungeonSheet.Text("battle.ready_format"), ally.Name)));
                var enemy = _flow.DungeonData.Enemies.First(unit => unit.IsAlive);
                var card = ButtonNamed(view, "Enemy_" + enemy.Id);
                Assert.That(card.IsInteractable(), Is.False, "공격 명령 전에 대상 카드를 선택하지 않습니다.");
                var allUnits = _flow.DungeonData.Allies.Concat(_flow.DungeonData.Enemies).ToArray();
                var gauges = allUnits.Select(unit => unit.Gauge).ToArray();
                var hp = allUnits.Select(unit => unit.Hp).ToArray();
                for (int frame = 0; frame < 3; frame++)
                {
                    yield return null;
                }
                CollectionAssert.AreEqual(gauges, allUnits.Select(unit => unit.Gauge));
                CollectionAssert.AreEqual(hp, allUnits.Select(unit => unit.Hp));
                var allyCard = ButtonNamed(view, "Ally_" + ally.Id);
                Assert.That(allyCard.transform.Find("GaugeBar/GaugeFill").GetComponent<RectTransform>().anchorMax.x,
                    Is.EqualTo(1f).Within(0.001f));
                Assert.That(allyCard.transform.Find("HpText").GetComponent<Text>().text, Is.Not.Empty);
                Assert.That(allyCard.transform.Find("SpeedText").GetComponent<Text>().text, Is.Not.Empty);
                AssertTextFits(view, "BattleTitleText", "BattleReadyText");
                float expectedDamage = CombatCalculator.ResolveAttack(ally.Stats, enemy.Stats,
                    new PredictableRandom(), coefficient: ally.AttackCoefficient).Damage;
                float expectedHp = Math.Max(0, enemy.Hp - expectedDamage);
                yield return Click(ButtonNamed(view, "AttackButton"));
                yield return null;
                Assert.That(card.IsInteractable(), Is.True);
                yield return Click(card);
                yield return null;
                Assert.That(enemy.Hp, Is.LessThanOrEqualTo(expectedHp + 0.001f), "플레이어 공격 후에는 동료의 자동 피해도 추가될 수 있습니다.");
                Assert.That(ally.Gauge, Is.LessThan(1f), "플레이어의 공격이 준비 게이지를 사용해야 합니다.");
            }
            Assert.That(_flow.DungeonData.InCombat, Is.False, "전투가 제한된 행동 수 안에 끝나지 않았습니다.");
        }

        private IEnumerator ObserveAutomaticCompanionBeforePlayerTurn(DungeonView view)
        {
            var player = _flow.DungeonData.Allies.Single(unit => unit.IsPlayerControlled);
            var companion = _flow.DungeonData.Allies.Single(unit => !unit.IsPlayerControlled);
            Assert.That(companion.Speed, Is.GreaterThan(player.Speed), "현재 예시 파티에서는 미라가 먼저 행동합니다.");
            foreach (var ally in _flow.DungeonData.Allies)
            {
                var card = ButtonNamed(view, "Ally_" + ally.Id);
                string key = ally.IsPlayerControlled ? "battle.player_name_format" : "battle.companion_name_format";
                Assert.That(card.transform.Find("Name").GetComponent<Text>().text,
                    Is.EqualTo(string.Format(_flow.DungeonSheet.Text(key), ally.Name)));
            }
            float initialEnemyHp = _flow.DungeonData.Enemies.Sum(unit => unit.MaxHp);
            float expectedDamage = CombatCalculator.ResolveAttack(companion.Stats, _flow.DungeonData.Enemies[0].Stats,
                new PredictableRandom(), coefficient: companion.AttackCoefficient).Damage;
            float deadline = Time.realtimeSinceStartup + 15f;
            while (_flow.DungeonData.Enemies.Sum(unit => unit.Hp) == initialEnemyHp && Time.realtimeSinceStartup < deadline)
            {
                Assert.That(_flow.DungeonData.IsAwaitingCommand, Is.False);
                Assert.That(ButtonNamed(view, "AttackButton").IsInteractable(), Is.False);
                yield return null;
            }
            Assert.That(_flow.DungeonData.Enemies.Sum(unit => unit.Hp), Is.EqualTo(initialEnemyHp - expectedDamage).Within(0.001f),
                "어떤 공격 입력도 보내지 않은 동안 미라가 자동으로 한 번 공격해야 합니다.");
            Assert.That(player.Gauge, Is.LessThan(1f));
            Assert.That(companion.Gauge, Is.LessThan(1f));
            Assert.That(_flow.DungeonData.ReadyAllyId, Is.Empty);
            Assert.That(ButtonNamed(view, "AttackButton").IsInteractable(), Is.False);
            Assert.That(Reference<Text>(view, "BattleReadyText").text, Is.EqualTo(_flow.DungeonSheet.Text("battle.waiting")));
            AssertTextFits(view, "BattleTitleText", "BattleReadyText");
        }

        private IEnumerator SelectAndEnter(TownView town, int id)
        {
            yield return Click(ButtonNamed(town, "Command_enter_dungeon"));
            yield return null;
            yield return Click(ButtonNamed(town, "DungeonSelect_" + id));
            yield return null;
            yield return Click(ButtonNamed(town, "DungeonConfirmButton"));
            yield return WaitForScene(SceneType.Dungeon);
        }

        private IEnumerator Move(DungeonView view, string id)
        {
            yield return Click(ButtonNamed(view, "Node_" + id));
            yield return null;
            Assert.That(_flow.DungeonData.NodeId, Is.EqualTo(id));
        }

        private void AssertModalBlocksMap(DungeonView view)
        {
            Assert.That(_flow.DungeonData.DetailsOpen, Is.True);
            Assert.That(Reference<GameObject>(view, "DungeonDetailsRoot").activeSelf, Is.True);
            Assert.That(Reference<CanvasGroup>(view, "MapControls").interactable, Is.False);
            var node = Buttons(view, "Node_").First(button => button.name != "Node_" + _flow.DungeonData.NodeId);
            Assert.That(node.IsInteractable(), Is.False);
            Assert.That(TopButtonAt(node), Is.Not.SameAs(node), "모달 뒤의 지도 노드에 레이캐스트가 도달하면 안 됩니다.");
            var returnButton = ButtonNamed(view, "DungeonReturnTownButton");
            Assert.That(TopButtonAt(returnButton), Is.Not.SameAs(returnButton));
        }

        private IEnumerator LoadScene(SceneType scene)
        {
            string path = scene == SceneType.Town ? TownPath : DungeonPath;
            Assert.That(Application.CanStreamedLevelBeLoaded(path), Is.True, path + " Build Settings 누락");
            var operation = SceneManager.LoadSceneAsync(path, LoadSceneMode.Single);
            Assert.That(operation, Is.Not.Null);
            float deadline = Time.realtimeSinceStartup + 30f;
            while (!operation.isDone && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
            Assert.That(operation.isDone, Is.True, path + " 로딩 시간 초과");
            yield return WaitForScene(scene);
        }

        private IEnumerator WaitForScene(SceneType scene)
        {
            float deadline = Time.realtimeSinceStartup + 30f;
            while (Time.realtimeSinceStartup < deadline)
            {
                var flow = Object.FindFirstObjectByType<MainFlow>();
                if (flow != null && flow.IsReady && flow.CurrentScene == scene && SceneManager.GetActiveScene().name == SceneLoader.SceneName(scene))
                {
                    _flow = flow;
                    if (scene == SceneType.Dungeon)
                    {
                        typeof(DungeonDataContainer).GetField("_random", BindingFlags.Instance | BindingFlags.NonPublic)
                            .SetValue(_flow.DungeonData, new PredictableRandom());
                    }
                    Assert.That(_flow.AdventureSavePath, Is.EqualTo(_save.SavePath));
                    _flow.SendMessage("OnApplicationFocus", true);
                    yield return null;
                    yield break;
                }
                yield return null;
            }
            Assert.Fail(scene + " 씬이 준비되지 않았습니다.");
        }

        private static IEnumerator Click(Button button, int count = 1)
        {
            // 활성화 직후에는 CanvasRenderer의 가림 정보가 아직 이전 프레임일 수 있다.
            // 실제 레이캐스트가 준비될 때까지만 기다리고, 가려진 UI를 우회하지 않는다.
            GameObject hit = null;
            float deadline = Time.realtimeSinceStartup + 2f;
            for (int frame = 0; frame < 60 && Time.realtimeSinceStartup < deadline; frame++)
            {
                hit = RaycastAt(button);
                if (button.IsInteractable() && hit != null && hit.GetComponentInParent<Button>() == button)
                {
                    break;
                }
                yield return null;
            }
            Assert.That(button.IsInteractable(), Is.True, button.name + " 비활성 버튼");
            Assert.That(hit, Is.Not.Null, button.name + " 레이캐스트 결과 없음");
            Assert.That(hit.GetComponentInParent<Button>(), Is.SameAs(button), button.name + " 다른 UI가 버튼을 가립니다.");
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

        private static Button ButtonNamed(Component view, string name) => view.GetComponentsInChildren<Button>().Single(button => button.name == name);
        private static Text TextNamed(Component view, string name) => view.GetComponentsInChildren<Text>(true).Single(text => text.name == name);
        private static Button[] Buttons(Component view, string prefix) => view.GetComponentsInChildren<Button>()
            .Where(button => button.name.StartsWith(prefix, StringComparison.Ordinal)).ToArray();

        private static T Reference<T>(Component view, string name) where T : Object
        {
            var field = view.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            var value = field.GetValue(view) as T;
            Assert.That(value, Is.Not.Null, name + " 직렬화 연결 누락");
            return value;
        }

        private static void AssertSerializedReferences(Component view)
        {
            Assert.That(view, Is.Not.Null);
            foreach (var field in view.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
            {
                if (field.IsDefined(typeof(SerializeField), false))
                {
                    Assert.That(field.GetValue(view) as Object, Is.Not.Null, field.Name + " 직렬화 연결 누락");
                }
            }
        }

        private static void AssertTextFits(Component view, params string[] headings)
        {
            Canvas.ForceUpdateCanvases();
            var failures = new List<string>();
            foreach (var text in view.GetComponentsInChildren<Text>())
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
            foreach (string heading in headings)
            {
                var text = Reference<Text>(view, heading);
                if (text.isActiveAndEnabled && text.horizontalOverflow != HorizontalWrapMode.Overflow
                    && text.preferredWidth > text.rectTransform.rect.width + 2f)
                {
                    failures.Add(heading + " 제목 가로 폭 부족: " + text.text);
                }
            }
            Assert.That(failures, Is.Empty, string.Join("\n", failures));
        }

        private static void AssertSingleRuntime()
        {
            Assert.That(Object.FindObjectsByType<MainFlow>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
            Assert.That(Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
        }

        // 경로/UI 회귀는 명시적인 기존 파티와 성장 수치를 시드한다. 새 게임/영입은 CharacterSceneTests에서 검증한다.
        private void SeedEstablishedParty()
        {
            string[] ids = { "hero", "mira", "orin", "bran", "sera", "neri", "cass" };
            float[] normal = new float[ids.Length];
            var dto = new AdventureSaveData
            {
                Format = AdventureSave.CurrentFormat,
                Version = AdventureSave.CurrentVersion,
                MemberIds = ids,
                MemberGold = new long[ids.Length],
                ItemIds = Array.Empty<string>(),
                ItemCounts = Array.Empty<int>(),
                SettlementIds = Array.Empty<string>(),
                CharacterIds = ids,
                CharacterLevels = Enumerable.Repeat(1, ids.Length).ToArray(),
                CharacterExperience = new float[ids.Length],
                CharacterAffinity = new int[ids.Length],
                CharacterStrengthGrowth = new[] { 150f, 150f, 0f, 0f, 0f, 0f, 0f },
                CharacterConstitutionGrowth = new[] { 90f, 90f, 0f, 0f, 0f, 0f, 0f },
                CharacterWillPowerGrowth = normal,
                CharacterIntelligenceGrowth = normal,
                CharacterAgilityGrowth = new[] { 0f, 6f, 0f, 0f, 0f, 0f, 0f },
                CharacterPerceptionGrowth = normal,
                PartyIds = new[] { "hero", "mira" }
            };
            File.WriteAllText(_save.SavePath, JsonUtility.ToJson(dto));
        }

        private sealed class PredictableRandom : IRandomSource
        {
            public int NextInt(int minInclusive, int maxExclusive) => minInclusive;
            public float NextFloat(float minInclusive, float maxInclusive) => (minInclusive + maxInclusive) / 2f;
        }
    }
}
