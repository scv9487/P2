using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Refactor;
using Refactor.Main;
using Refactor.Main.InputModule;
using Refactor.Util;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace P2.Tests
{
    public sealed class QuestSceneTests
    {
        private AdventureSaveScope _save;
        private MainFlow _flow;
        private float _timeScale;
        private float _maximumDelta;

        [SetUp]
        public void SetUp()
        {
            _save = new AdventureSaveScope();
            _timeScale = Time.timeScale;
            _maximumDelta = Time.maximumDeltaTime;
            Time.timeScale = 4f;
            Time.maximumDeltaTime = 0.1f;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = _timeScale;
            Time.maximumDeltaTime = _maximumDelta;
            var scene = SceneManager.GetActiveScene();
            if (scene.IsValid() && (scene.name == "2_Town" || scene.name == "3_Dungeon"))
            {
                var empty = SceneManager.CreateScene("P2 Quest Test Cleanup");
                SceneManager.SetActiveScene(empty);
                yield return SceneManager.UnloadSceneAsync(scene);
            }
            _save?.Dispose();
        }

        [UnityTest]
        public IEnumerator GuildShowsFourCardsBlocksBackgroundAndPreservesOneActiveQuestAcrossReload()
        {
            yield return Load(SceneType.Town);
            yield return OpenBoard("quest_accept");
            Assert.That(Buttons("QuestCard_").Length, Is.EqualTo(4));
            Assert.That(_flow.QuestBoard.SelectedQuestId, Is.Empty);
            Assert.That(ButtonNamed("QuestAcceptButton").IsInteractable(), Is.False);
            foreach (var quest in _flow.QuestSheet.Quests)
            {
                yield return Click("QuestCard_" + quest.Id);
                Assert.That(TextNamed("QuestTitleText").text, Is.EqualTo(quest.Name));
                Assert.That(TextNamed("QuestDescriptionText").text, Is.EqualTo(quest.Description));
                Assert.That(ButtonNamed("QuestAcceptButton").IsInteractable(), Is.True);
                Assert.That(ButtonNamed("QuestCompleteButton").IsInteractable(), Is.False);
                AssertTextsFit();
            }
            var leave = ButtonNamed("LeaveLocationButton");
            Assert.That(RaycastAt(leave)?.GetComponentInParent<Button>(), Is.Not.SameAs(leave));
            leave.onClick.Invoke();
            yield return null;
            Assert.That(_flow.TownData.LocationId, Is.EqualTo("guild"));
            Assert.That(_flow.QuestBoard.Open, Is.True);

            var selected = Quest(QuestKind.ClearDungeon);
            yield return Click("QuestCard_" + selected.Id);
            yield return Click("QuestAcceptButton", 2);
            Assert.That(_flow.AdventureProgress.Quest.ActiveQuestId, Is.EqualTo(selected.Id));
            string acceptance = _flow.AdventureProgress.Quest.AcceptanceId;
            string oldContext = _flow.QuestBoard.Context;
            var other = Quest(QuestKind.KillMonster);
            yield return Click("QuestCard_" + other.Id);
            Assert.That(ButtonNamed("QuestAcceptButton").IsInteractable(), Is.False);
            _flow.UIInput.AddInput(UIInputType.QuestAccept, selected.Id, oldContext);
            _flow.UIInput.AddInput(UIInputType.QuestClose, selected.Id, oldContext);
            yield return null;
            Assert.That(_flow.QuestBoard.Open, Is.True);
            Assert.That(_flow.AdventureProgress.Quest.AcceptanceId, Is.EqualTo(acceptance));
            _flow.UIInput.AddInput(UIInputType.Back);
            yield return null;
            Assert.That(_flow.QuestBoard.Open, Is.False);
            Assert.That(_flow.TownData.LocationId, Is.EqualTo("guild"));

            yield return Load(SceneType.Town);
            Assert.That(_flow.AdventureProgress.Quest.ActiveQuestId, Is.EqualTo(selected.Id));
            yield return OpenBoard("quest_complete");
            Assert.That(_flow.QuestBoard.SelectedQuestId, Is.EqualTo(selected.Id), "보고 창은 활성 의뢰를 자동 선택합니다.");
            Assert.That(ButtonNamed("QuestCompleteButton").IsInteractable(), Is.False);
            AssertTextsFit();
        }

        [UnityTest]
        public IEnumerator DeliveryReportConsumesOwnedItemsOnlyAfterSavingAndCannotRewardTwice()
        {
            File.WriteAllText(_save.SavePath,
                "{\"Format\":\"P2.Adventure\",\"Version\":1,\"MemberIds\":[\"hero\",\"mira\"],\"MemberGold\":[7,0],"
                + "\"ItemIds\":[\"rat_tail\"],\"ItemCounts\":[3],\"SettlementIds\":[]}");
            yield return Load(SceneType.Town);
            yield return OpenBoard("quest_accept");
            var delivery = Quest(QuestKind.DeliverItems);
            yield return Click("QuestCard_" + delivery.Id);
            Assert.That(TextNamed("QuestStatusText").text, Does.Contain("정산").And.Contain("소지품"));
            Assert.That(TextNamed("QuestDeliveryText").text, Does.Contain(delivery.TargetName).And.Contain("3"));
            Assert.That(ButtonNamed("QuestCompleteButton").IsInteractable(), Is.False);
            yield return Click("QuestAcceptButton");
            Assert.That(ButtonNamed("QuestCompleteButton").IsInteractable(), Is.True);
            Assert.That(ItemCount(delivery.TargetId), Is.EqualTo(3));
            AssertTextsFit();

            string before = File.ReadAllText(_save.SavePath);
            string blocker = _save.SavePath + ".tmp";
            Directory.CreateDirectory(blocker);
            yield return Click("QuestCompleteButton");
            Assert.That(_flow.AdventureProgress.Quest.ActiveQuestId, Is.EqualTo(delivery.Id));
            Assert.That(ItemCount(delivery.TargetId), Is.EqualTo(3));
            Assert.That(_flow.AdventureProgress.PlayerGold, Is.EqualTo(7));
            Assert.That(TextNamed("QuestStatusText").text, Is.EqualTo(_flow.QuestSheet.Text("status.save_failed")));
            Assert.That(File.ReadAllText(_save.SavePath), Is.EqualTo(before));
            Directory.Delete(blocker);
            yield return Click("QuestCompleteButton", 2);
            Assert.That(_flow.AdventureProgress.Quest.ActiveQuestId, Is.Empty);
            Assert.That(_flow.AdventureProgress.Quest.IsCompleted(delivery.Id), Is.True);
            Assert.That(ItemCount(delivery.TargetId), Is.Zero);
            Assert.That(_flow.AdventureProgress.PlayerGold, Is.EqualTo(7 + delivery.RewardGold));
            Assert.That(ButtonNamed("QuestAcceptButton").IsInteractable(), Is.False);
            Assert.That(ButtonNamed("QuestCompleteButton").IsInteractable(), Is.False);
            AssertTextsFit();

            yield return Load(SceneType.Town);
            Assert.That(_flow.AdventureProgress.Quest.IsCompleted(delivery.Id), Is.True);
            Assert.That(ItemCount(delivery.TargetId), Is.Zero);
            Assert.That(_flow.AdventureProgress.PlayerGold, Is.EqualTo(7 + delivery.RewardGold));
            yield return OpenBoard("quest_complete");
            yield return Click("QuestCard_" + delivery.Id);
            Assert.That(ButtonNamed("QuestAcceptButton").IsInteractable(), Is.False);
            Assert.That(TextNamed("QuestStatusText").text, Is.EqualTo(_flow.QuestSheet.Text("board.completed_hint")));
        }

        [UnityTest]
        public IEnumerator AcceptedRescueAddsAReadableInstanceNodeAndOnlyItsFirstVictoryAllowsReporting()
        {
            SeedStrongParty();
            yield return Load(SceneType.Town);
            yield return OpenBoard("quest_accept");
            var rescue = Quest(QuestKind.RescueAdventurers);
            yield return Click("QuestCard_" + rescue.Id);
            yield return Click("QuestAcceptButton");
            yield return Click("QuestCloseButton");
            yield return Click("LeaveLocationButton");
            yield return EnterDungeon();
            var node = _flow.DungeonData.Nodes.Single(item => item.Type == DungeonNodeType.Rescue);
            Assert.That(node.Icon, Is.Not.Empty);
            Assert.That(ButtonNamed("Node_" + node.Id).transform.Find("Icon").GetComponent<Text>().text, Is.EqualTo(node.Icon));
            Assert.That(_flow.DungeonSheet.GetNodes(1).Any(item => item.Type == DungeonNodeType.Rescue), Is.False);
            yield return WalkTo(node.Id);
            Assert.That(_flow.DungeonData.InCombat, Is.True);
            yield return BlockLastRescueKillAndRetry();
            Assert.That(_flow.AdventureProgress.Quest.ProgressCount, Is.EqualTo(1));
            Assert.That(_flow.DungeonData.IsNodeResolved(node.Id), Is.True);
            AssertTextsFit();
            if (_flow.DungeonData.DetailsOpen)
            {
                yield return Click("DungeonDetailsCloseButton");
            }
            string neighbor = node.NeighborIds[0];
            yield return WalkTo(neighbor);
            yield return WalkTo(node.Id);
            Assert.That(_flow.DungeonData.InCombat, Is.False);
            Assert.That(_flow.AdventureProgress.Quest.ProgressCount, Is.EqualTo(1));
            yield return Click("DungeonReturnTownButton");
            yield return Click("LootConfirmButton");
            yield return WaitForScene(SceneType.Town);
            long gold = _flow.AdventureProgress.PlayerGold;
            yield return OpenBoard("quest_complete");
            Assert.That(_flow.QuestBoard.SelectedQuestId, Is.EqualTo(rescue.Id));
            Assert.That(ButtonNamed("QuestCompleteButton").IsInteractable(), Is.True);
            yield return Click("QuestCompleteButton", 2);
            Assert.That(_flow.AdventureProgress.PlayerGold, Is.EqualTo(gold + rescue.RewardGold));
            Assert.That(_flow.AdventureProgress.Quest.IsCompleted(rescue.Id), Is.True);
            yield return Load(SceneType.Town);
            Assert.That(_flow.AdventureProgress.Quest.IsCompleted(rescue.Id), Is.True);
        }

        [UnityTest]
        public IEnumerator InvalidSaveIsPreservedAndQuestMutationIsDisabled()
        {
            const string invalid = "{invalid user progress";
            File.WriteAllText(_save.SavePath, invalid);
            yield return Load(SceneType.Town);
            yield return OpenBoard("quest_accept");
            yield return Click("QuestCard_" + Quest(QuestKind.DeliverItems).Id);
            Assert.That(ButtonNamed("QuestAcceptButton").IsInteractable(), Is.False);
            Assert.That(ButtonNamed("QuestCompleteButton").IsInteractable(), Is.False);
            Assert.That(TextNamed("QuestStatusText").text, Is.EqualTo(_flow.QuestSheet.Text("status.load_failed")));
            Assert.That(File.ReadAllText(_save.SavePath), Is.EqualTo(invalid));
            yield return Click("QuestCloseButton");
            Assert.That(_flow.QuestBoard.Open, Is.False);
        }

        private IEnumerator BlockLastRescueKillAndRetry()
        {
            var data = (DungeonDataContainer)_flow.DungeonData;
            float deadline = Time.realtimeSinceStartup + 20f;
            while ((!data.IsAwaitingCommand || data.Enemies.Count(enemy => enemy.IsAlive) > 1)
                && Time.realtimeSinceStartup < deadline)
            {
                _flow.SendMessage("OnApplicationFocus", true);
                if (data.IsAwaitingCommand)
                {
                    yield return Click("AttackButton");
                    yield return Click("Enemy_" + data.Enemies.First(enemy => enemy.IsAlive).Id);
                }
                yield return null;
            }
            Assert.That(data.IsAwaitingCommand, Is.True);
            Assert.That(data.Enemies.Count(enemy => enemy.IsAlive), Is.EqualTo(1));
            long gold = _flow.AdventureProgress.PlayerGold;
            string saved = File.ReadAllText(_save.SavePath);
            string blocker = _save.SavePath + ".tmp";
            Directory.CreateDirectory(blocker);
            while (!data.ProgressSaveBlocked && Time.realtimeSinceStartup < deadline)
            {
                _flow.SendMessage("OnApplicationFocus", true);
                if (data.IsAwaitingCommand)
                {
                    yield return Click("AttackButton");
                    yield return Click("Enemy_" + data.Enemies.First(enemy => enemy.IsAlive).Id);
                }
                yield return null;
            }
            Assert.That(data.ProgressSaveBlocked, Is.True);
            Assert.That(data.InCombat, Is.False, "마지막 구조 적 처치 이후 저장 실패를 재시도합니다.");
            Assert.That(data.DetailsOpen, Is.True);
            Assert.That(_flow.AdventureProgress.Quest.ProgressCount, Is.Zero);
            Assert.That(_flow.AdventureProgress.PlayerGold, Is.EqualTo(gold));
            Assert.That(File.ReadAllText(_save.SavePath), Is.EqualTo(saved));
            string position = data.NodeId;
            float[] gauges = data.Allies.Select(unit => unit.Gauge).ToArray();
            _flow.UIInput.AddInput(UIInputType.DungeonMove, data.Nodes.First().Id, data.Context);
            _flow.UIInput.AddInput(UIInputType.DungeonAttack, data.Enemies.First().Id, "hero");
            yield return new WaitForSecondsRealtime(0.1f);
            Assert.That(data.NodeId, Is.EqualTo(position));
            Assert.That(data.Allies.Select(unit => unit.Gauge), Is.EqualTo(gauges));
            yield return Click("DungeonDetailsCloseButton");
            Assert.That(data.ProgressSaveBlocked && data.DetailsOpen, Is.True);
            Assert.That(_flow.AdventureProgress.Quest.ProgressCount, Is.Zero);
            Directory.Delete(blocker);
            yield return Click("DungeonDetailsCloseButton");
            Assert.That(data.ProgressSaveBlocked, Is.False);
            Assert.That(data.PendingExperience, Is.Null);
            Assert.That(data.PendingQuestFact, Is.Null);
            Assert.That(_flow.AdventureProgress.Quest.ProgressCount, Is.EqualTo(1));
            Assert.That(_flow.AdventureProgress.PlayerGold, Is.EqualTo(gold), "진척 저장은 길드 보고 보상을 지급하지 않습니다.");
        }

        private void SeedStrongParty()
        {
            string[] ids = { "hero", "mira", "orin", "bran", "sera", "neri", "cass" };
            var data = new AdventureSaveData
            {
                Format = "P2.Adventure", Version = 3, MemberIds = ids, MemberGold = ids.Select(id => id == "hero" ? 100L : 0L).ToArray(),
                ItemIds = Array.Empty<string>(), ItemCounts = Array.Empty<int>(), SettlementIds = Array.Empty<string>(),
                MerchantIds = new[] { "market", "tavern" }, StockMerchantIds = new[] { "market", "tavern" },
                StockItemIds = new[] { "health_potion", "health_potion" }, StockCounts = new[] { 10, 10 }, TradeIds = Array.Empty<string>(),
                CharacterIds = ids, CharacterLevels = Enumerable.Repeat(1, ids.Length).ToArray(), CharacterExperience = new float[ids.Length],
                CharacterAffinity = ids.Select(id => id == "hero" ? 0 : 100).ToArray(),
                CharacterStrengthGrowth = Enumerable.Repeat(2000f, ids.Length).ToArray(), CharacterConstitutionGrowth = Enumerable.Repeat(90f, ids.Length).ToArray(),
                CharacterWillPowerGrowth = new float[ids.Length], CharacterIntelligenceGrowth = new float[ids.Length],
                CharacterAgilityGrowth = new float[ids.Length], CharacterPerceptionGrowth = new float[ids.Length],
                PartyIds = new[] { "hero", "mira" }, CharacterActionIds = Array.Empty<string>()
            };
            File.WriteAllText(_save.SavePath, JsonUtility.ToJson(data));
        }

        private QuestDefinition Quest(QuestKind kind) => _flow.QuestSheet.Quests.Single(quest => quest.Kind == kind);
        private int ItemCount(string id) => _flow.AdventureProgress.Items.TryGetValue(id, out int count) ? count : 0;

        private IEnumerator OpenBoard(string command)
        {
            yield return Click("Location_guild");
            yield return Click("Command_" + command);
            Assert.That(_flow.TownData.QuestBoardOpen && _flow.QuestBoard.Open, Is.True);
        }

        private IEnumerator EnterDungeon()
        {
            yield return Click("Location_dungeon");
            yield return Click("Command_enter_dungeon");
            yield return Click("DungeonSelect_1");
            yield return Click("DungeonConfirmButton");
            yield return WaitForScene(SceneType.Dungeon);
        }

        private IEnumerator WalkTo(string target)
        {
            var previous = new Dictionary<string, string> { [_flow.DungeonData.NodeId] = null };
            var queue = new Queue<string>();
            queue.Enqueue(_flow.DungeonData.NodeId);
            while (queue.Count > 0 && !previous.ContainsKey(target))
            {
                string id = queue.Dequeue();
                foreach (string neighbor in _flow.DungeonData.Nodes.Single(node => node.Id == id).NeighborIds)
                {
                    if (previous.ContainsKey(neighbor)) continue;
                    previous.Add(neighbor, id);
                    queue.Enqueue(neighbor);
                }
            }
            Assert.That(previous.ContainsKey(target), Is.True);
            var path = new Stack<string>();
            for (string id = target; previous[id] != null; id = previous[id]) path.Push(id);
            while (path.Count > 0)
            {
                yield return Click("Node_" + path.Pop());
                if (path.Count > 0)
                {
                    if (_flow.DungeonData.InCombat)
                    {
                        yield return Fight();
                    }
                    if (_flow.DungeonData.DetailsOpen)
                    {
                        yield return Click("DungeonDetailsCloseButton");
                    }
                }
            }
        }

        private IEnumerator Fight()
        {
            float deadline = Time.realtimeSinceStartup + 20f;
            while (_flow.DungeonData.InCombat && Time.realtimeSinceStartup < deadline)
            {
                _flow.SendMessage("OnApplicationFocus", true);
                if (!_flow.DungeonData.IsAwaitingCommand)
                {
                    yield return null;
                    continue;
                }
                string target = _flow.DungeonData.Enemies.First(enemy => enemy.IsAlive).Id;
                yield return Click("AttackButton");
                yield return Click("Enemy_" + target);
            }
            Assert.That(_flow.DungeonData.InCombat, Is.False, "전투 시간 초과");
            Assert.That(_flow.DungeonData.IsDefeated, Is.False);
            yield return null;
            yield return null;
        }

        private IEnumerator Load(SceneType scene)
        {
            yield return SceneManager.LoadSceneAsync(SceneLoader.SceneName(scene), LoadSceneMode.Single);
            yield return WaitForScene(scene);
        }

        private IEnumerator WaitForScene(SceneType scene)
        {
            float deadline = Time.realtimeSinceStartup + 30f;
            while (Time.realtimeSinceStartup < deadline)
            {
                var flow = Object.FindFirstObjectByType<MainFlow>();
                if (flow != null && flow.IsReady && flow.CurrentScene == scene)
                {
                    _flow = flow;
                    Assert.That(_flow.AdventureSavePath, Is.EqualTo(_save.SavePath));
                    if (scene == SceneType.Dungeon)
                    {
                        typeof(DungeonDataContainer).GetField("_random", BindingFlags.Instance | BindingFlags.NonPublic)
                            .SetValue(_flow.DungeonData, new PredictableRandom());
                    }
                    _flow.SendMessage("OnApplicationFocus", true);
                    yield return null;
                    yield return null;
                    yield break;
                }
                yield return null;
            }
            Assert.Fail(scene + " 씬 초기화 시간 초과");
        }

        private IEnumerator Click(string name, int count = 1)
        {
            var button = ButtonNamed(name);
            GameObject hit = null;
            float deadline = Time.realtimeSinceStartup + 3f;
            for (int frame = 0; frame < 120 && Time.realtimeSinceStartup < deadline; frame++)
            {
                _flow.SendMessage("OnApplicationFocus", true);
                hit = RaycastAt(button);
                if (button.IsInteractable() && hit != null && hit.GetComponentInParent<Button>() == button) break;
                yield return null;
            }
            Assert.That(button.IsInteractable(), Is.True, name);
            Assert.That(hit, Is.Not.Null, name);
            Assert.That(hit.GetComponentInParent<Button>(), Is.SameAs(button), name + " 가림");
            var pointer = new PointerEventData(EventSystem.current) { position = ScreenCenter(button), button = PointerEventData.InputButton.Left };
            for (int i = 0; i < count; i++)
            {
                ExecuteEvents.ExecuteHierarchy(hit, pointer, ExecuteEvents.pointerDownHandler);
                ExecuteEvents.ExecuteHierarchy(hit, pointer, ExecuteEvents.pointerUpHandler);
                ExecuteEvents.ExecuteHierarchy(hit, pointer, ExecuteEvents.pointerClickHandler);
            }
            yield return null;
            yield return null;
        }

        private static Button ButtonNamed(string name) => Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Single(button => button.name == name);
        private static Text TextNamed(string name) => Object.FindObjectsByType<Text>(FindObjectsSortMode.None).Single(text => text.name == name);
        private static Button[] Buttons(string prefix) => Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Where(button => button.name.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
        private static GameObject RaycastAt(Button button)
        {
            Canvas.ForceUpdateCanvases();
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = ScreenCenter(button) }, hits);
            return hits.Count == 0 ? null : hits[0].gameObject;
        }
        private static Vector2 ScreenCenter(Button button)
        {
            var rect = (RectTransform)button.transform;
            var canvas = button.GetComponentInParent<Canvas>();
            return RectTransformUtility.WorldToScreenPoint(canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera, rect.TransformPoint(rect.rect.center));
        }
        private static void AssertTextsFit()
        {
            Canvas.ForceUpdateCanvases();
            var failures = new List<string>();
            foreach (var text in Object.FindObjectsByType<Text>(FindObjectsSortMode.None))
            {
                if (string.IsNullOrWhiteSpace(text.text) || text.verticalOverflow == VerticalWrapMode.Overflow) continue;
                if (text.font == null || text.rectTransform.rect.width <= 0 || text.preferredHeight > text.rectTransform.rect.height + 2f)
                {
                    failures.Add(text.name + $" 높이 {text.preferredHeight:F1}>{text.rectTransform.rect.height:F1}: {text.text}");
                }
            }
            Assert.That(failures, Is.Empty, string.Join("\n", failures));
        }

        private sealed class PredictableRandom : IRandomSource
        {
            public int NextInt(int minInclusive, int maxExclusive) => minInclusive;
            public float NextFloat(float minInclusive, float maxInclusive) => (minInclusive + maxInclusive) / 2f;
        }
    }
}
