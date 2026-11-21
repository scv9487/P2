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
    // 실제 씬/최상단 UI 레이캐스트로 관계와 파티를 검증한다. 진행 저장은 항상 임시 경로다.
    public sealed class CharacterSceneTests
    {
        private AdventureSaveScope _save;
        private MainFlow _flow;

        [SetUp]
        public void SetUp()
        {
            _save = new AdventureSaveScope();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.IsValid() && (scene.name == "2_Town" || scene.name == "3_Dungeon"))
            {
                var empty = SceneManager.CreateScene("P2 Character Test Cleanup");
                SceneManager.SetActiveScene(empty);
                yield return SceneManager.UnloadSceneAsync(scene);
            }
            _save?.Dispose();
        }

        [UnityTest]
        public IEnumerator TavernInteractionsRecruitThreeCompanionsRespectCapacityAndPersistDismissal()
        {
            yield return Load(SceneType.Town);
            CollectionAssert.AreEqual(new[] { "hero" }, _flow.AdventureProgress.PartyIds);
            yield return Click("Location_tavern");
            Assert.That(Buttons("Character_").Length, Is.EqualTo(6));
            Assert.That(Buttons("PartyCharacter_").Length, Is.EqualTo(1));
            AssertTextsFit();

            yield return Click("Character_mira");
            Assert.That(_flow.CharacterInteraction.Open, Is.True);
            Assert.That(_flow.CharacterInteraction.DialogueOpen, Is.False);
            Assert.That(TextNamed("CharacterIdentityText").text, Does.Contain("여성").And.Contain("25"));
            Assert.That(TextNamed("CharacterProgressText").text, Does.Contain("Lv. 1").And.Contain("EXP"));
            Assert.That(Object.FindFirstObjectByType<CharacterPartyView>().GetComponentsInChildren<Transform>()
                .Count(item => item.name.StartsWith("Combat_", StringComparison.Ordinal)), Is.EqualTo(21));
            Assert.That(_flow.AdventureProgress.GetCharacter("mira").Affinity, Is.Zero);
            yield return Click("CharacterRecruitButton");
            Assert.That(_flow.AdventureProgress.IsInParty("mira"), Is.False, "친밀도 0의 영입은 반드시 실패합니다.");
            Assert.That(TextNamed("CharacterStatusText").text, Is.EqualTo(_flow.CharacterSheet.Text("status.recruit_failed")));

            // 모달이 보일 때 실제 레이캐스트와 Handler 양쪽에서 배경을 차단한다.
            var leave = ButtonNamed("LeaveLocationButton");
            Assert.That(TopButtonAt(leave), Is.Not.SameAs(leave));
            leave.onClick.Invoke();
            yield return null;
            Assert.That(_flow.TownData.LocationId, Is.EqualTo("tavern"));
            Assert.That(_flow.CharacterInteraction.CharacterId, Is.EqualTo("mira"));

            yield return TalkToMaximumAndRecruit("mira");
            Assert.That(_flow.CharacterInteraction.FromParty, Is.True);
            Assert.That(Buttons("Character_").Any(button => button.name == "Character_mira"), Is.False);
            yield return Click("CharacterCloseButton");
            Assert.That(Buttons("PartyCharacter_").Length, Is.EqualTo(2));
            foreach (string id in new[] { "orin", "bran" })
            {
                yield return Click("Character_" + id);
                yield return TalkToMaximumAndRecruit(id);
                yield return Click("CharacterCloseButton");
            }
            CollectionAssert.AreEqual(new[] { "hero", "mira", "orin", "bran" }, _flow.AdventureProgress.PartyIds);
            Assert.That(Buttons("PartyCharacter_").Length, Is.EqualTo(4));
            Assert.That(Buttons("Character_").Length, Is.EqualTo(3));

            yield return Click("Character_sera");
            Assert.That(ButtonNamed("CharacterRecruitButton").IsInteractable(), Is.False);
            Assert.That(TextNamed("CharacterStatusText").text, Is.EqualTo(_flow.CharacterSheet.Text("status.party_full")));
            _flow.UIInput.AddInput(UIInputType.CharacterRecruit, "sera", _flow.CharacterInteraction.Context);
            yield return null;
            Assert.That(_flow.AdventureProgress.PartyIds.Count, Is.EqualTo(4));
            Assert.That(_flow.AdventureProgress.IsInParty("sera"), Is.False);
            yield return Click("CharacterCloseButton");

            yield return Click("PartyCharacter_mira");
            Assert.That(_flow.CharacterInteraction.FromParty, Is.True);
            Assert.That(ButtonNamed("CharacterDismissButton").IsInteractable(), Is.True);
            yield return Click("CharacterTalkButton");
            Assert.That(_flow.CharacterInteraction.DialogueOpen, Is.True);
            Assert.That(TextNamed("CharacterDialogueText").text, Is.EqualTo(_flow.TownSheet.GetCharacter("mira").Dialogue));
            AssertTextsFit();
            yield return Click("CharacterDismissButton");
            Assert.That(_flow.CharacterInteraction.Open, Is.False);
            Assert.That(_flow.AdventureProgress.IsInParty("mira"), Is.False);
            Assert.That(ButtonNamed("Character_mira"), Is.Not.Null, "추방한 동료는 원래 방문 장소에 다시 표시합니다.");
            Assert.That(Buttons("PartyCharacter_").Length, Is.EqualTo(3));
            Assert.That(File.Exists(_save.SavePath), Is.True);

            yield return Load(SceneType.Town);
            CollectionAssert.AreEqual(new[] { "hero", "orin", "bran" }, _flow.AdventureProgress.PartyIds);
            Assert.That(_flow.AdventureProgress.GetCharacter("mira").Affinity, Is.EqualTo(100));
            Assert.That(_flow.AdventureProgress.GetCharacter("orin").Affinity, Is.EqualTo(100));
            yield return Click("PartyCharacter_hero");
            Assert.That(ButtonNamed("CharacterTalkButton").IsInteractable(), Is.False);
            Assert.That(ButtonNamed("CharacterDismissButton").IsInteractable(), Is.False);
            Assert.That(TextNamed("CharacterNameText").text, Does.Contain("모험가"));
            AssertTextsFit();
            yield return Click("CharacterCloseButton");
            yield return Click("Location_tavern");
            CollectionAssert.AreEquivalent(new[] { "Character_mira", "Character_sera", "Character_neri", "Character_cass" },
                Buttons("Character_").Select(button => button.name));
            AssertSingleRuntime();
        }

        [UnityTest]
        public IEnumerator DungeonVisitorsAndBattlePartyCardsOpenTheSameInteractionAndPauseCombat()
        {
            yield return Load(SceneType.Town);
            yield return Click("Location_tavern");
            yield return Click("Character_mira");
            yield return TalkToMaximumAndRecruit("mira");
            yield return Click("CharacterCloseButton");
            yield return Click("LeaveLocationButton");
            yield return Click("Location_dungeon");
            yield return Click("Command_enter_dungeon");
            yield return Click("DungeonSelect_1");
            yield return Click("DungeonConfirmButton");
            yield return WaitForScene(SceneType.Dungeon);
            Assert.That(ButtonNamed("PartyCharacter_mira"), Is.Not.Null);

            yield return Click("Node_visitor");
            Assert.That(_flow.DungeonData.VisitorId, Is.EqualTo("sera"));
            yield return Click("Visitor_sera");
            Assert.That(_flow.CharacterInteraction.CharacterId, Is.EqualTo("sera"));
            Assert.That(_flow.CharacterInteraction.DialogueOpen, Is.False);
            yield return Click("CharacterTalkButton");
            Assert.That(_flow.AdventureProgress.GetCharacter("sera").Affinity, Is.EqualTo(10));
            Assert.That(_flow.CharacterInteraction.DialogueOpen, Is.True);
            AssertTextsFit();
            yield return Click("CharacterCloseButton");
            yield return Click("Node_camp");
            yield return Click("Node_guard");
            Assert.That(_flow.DungeonData.InCombat, Is.True);
            CollectionAssert.AreEqual(new[] { "PartyCharacter_hero" }, Buttons("PartyCharacter_").Select(button => button.name),
                "전투 중에는 좌하단 플레이어 메뉴 도크를 유지하고 동료 상세는 아군 카드를 사용합니다.");
            Assert.That(ButtonNamed("InventoryOpenButton").IsInteractable(), Is.True);
            Assert.That(ButtonNamed("SkillsOpenButton").IsInteractable(), Is.True);
            yield return Click("Ally_mira");
            Assert.That(_flow.CharacterInteraction.Open, Is.True);
            Assert.That(_flow.CharacterInteraction.FromParty, Is.True);
            var hp = _flow.DungeonData.Allies.Select(unit => unit.Hp).ToArray();
            var gauges = _flow.DungeonData.Allies.Select(unit => unit.Gauge).ToArray();
            var enemyHp = _flow.DungeonData.Enemies.Select(unit => unit.Hp).ToArray();
            yield return new WaitForSecondsRealtime(0.15f);
            CollectionAssert.AreEqual(hp, _flow.DungeonData.Allies.Select(unit => unit.Hp));
            CollectionAssert.AreEqual(gauges, _flow.DungeonData.Allies.Select(unit => unit.Gauge));
            CollectionAssert.AreEqual(enemyHp, _flow.DungeonData.Enemies.Select(unit => unit.Hp));
            AssertTextsFit();
            yield return Click("CharacterDismissButton");
            Assert.That(_flow.CharacterInteraction.Open, Is.False);
            Assert.That(_flow.DungeonData.Allies.Any(unit => unit.Id == "mira"), Is.False);
            Assert.That(_flow.AdventureProgress.IsInParty("mira"), Is.False);
            Assert.That(Buttons("Ally_").Select(button => button.name), Is.EquivalentTo(new[] { "Ally_hero" }));

            // 탐험 화면 상태와 별개로 확정된 관계/파티는 다음 씬 초기화에도 유지된다.
            yield return Load(SceneType.Town);
            CollectionAssert.AreEqual(new[] { "hero" }, _flow.AdventureProgress.PartyIds);
            Assert.That(_flow.AdventureProgress.GetCharacter("mira").Affinity, Is.EqualTo(100));
            Assert.That(_flow.AdventureProgress.GetCharacter("sera").Affinity, Is.EqualTo(10));
            yield return Click("Location_tavern");
            Assert.That(Buttons("Character_").Length, Is.EqualTo(6));
            AssertSingleRuntime();
        }

        private IEnumerator TalkToMaximumAndRecruit(string id)
        {
            Assert.That(_flow.CharacterInteraction.CharacterId, Is.EqualTo(id));
            Assert.That(_flow.CharacterSheet.TalkAffinityGain, Is.EqualTo(10));
            for (int i = 0; i < 10; i++)
            {
                yield return Click("CharacterTalkButton");
                Assert.That(_flow.AdventureProgress.GetCharacter(id).Affinity, Is.EqualTo((i + 1) * 10));
            }
            Assert.That(_flow.CharacterInteraction.DialogueOpen, Is.True);
            yield return Click("CharacterRecruitButton");
            Assert.That(_flow.AdventureProgress.IsInParty(id), Is.True, "친밀도 100의 영입은 반드시 성공합니다.");
            Assert.That(_flow.CharacterInteraction.CharacterId, Is.EqualTo(id));
        }

        private IEnumerator Load(SceneType scene)
        {
            var operation = SceneManager.LoadSceneAsync(SceneLoader.SceneName(scene), LoadSceneMode.Single);
            Assert.That(operation, Is.Not.Null);
            yield return operation;
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
                    _flow.SendMessage("OnApplicationFocus", true);
                    yield return null;
                    yield return null;
                    yield break;
                }
                yield return null;
            }
            Assert.Fail(scene + " 씬 초기화 시간 초과");
        }

        private IEnumerator Click(string name)
        {
            var button = ButtonNamed(name);
            GameObject hit = null;
            float deadline = Time.realtimeSinceStartup + 3f;
            for (int frame = 0; frame < 120 && Time.realtimeSinceStartup < deadline; frame++)
            {
                _flow.SendMessage("OnApplicationFocus", true);
                hit = RaycastAt(button);
                if (button.IsInteractable() && hit != null && hit.GetComponentInParent<Button>() == button)
                {
                    break;
                }
                yield return null;
            }
            Assert.That(button.IsInteractable(), Is.True, name + " 비활성 버튼");
            Assert.That(hit, Is.Not.Null, name + " 레이캐스트 누락");
            Assert.That(hit.GetComponentInParent<Button>(), Is.SameAs(button), name + " 다른 UI에 가려짐");
            var pointer = new PointerEventData(EventSystem.current)
            {
                button = PointerEventData.InputButton.Left,
                position = ScreenCenter(button)
            };
            ExecuteEvents.ExecuteHierarchy(hit, pointer, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.ExecuteHierarchy(hit, pointer, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.ExecuteHierarchy(hit, pointer, ExecuteEvents.pointerClickHandler);
            yield return null;
            yield return null;
            if (_flow != null)
            {
                _flow.SendMessage("OnApplicationFocus", true);
            }
        }

        private static Button ButtonNamed(string name) => Object.FindObjectsByType<Button>(FindObjectsSortMode.None).Single(button => button.name == name);
        private static Text TextNamed(string name) => Object.FindObjectsByType<Text>(FindObjectsSortMode.None).Single(text => text.name == name);
        private static Button[] Buttons(string prefix) => Object.FindObjectsByType<Button>(FindObjectsSortMode.None)
            .Where(button => button.name.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
        private static Button TopButtonAt(Button button) => RaycastAt(button)?.GetComponentInParent<Button>();

        private static GameObject RaycastAt(Button button)
        {
            Canvas.ForceUpdateCanvases();
            var results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = ScreenCenter(button) }, results);
            return results.Count == 0 ? null : results[0].gameObject;
        }

        private static Vector2 ScreenCenter(Button button)
        {
            var rect = (RectTransform)button.transform;
            var canvas = button.GetComponentInParent<Canvas>();
            return RectTransformUtility.WorldToScreenPoint(canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null : canvas.worldCamera, rect.TransformPoint(rect.rect.center));
        }

        private static void AssertTextsFit()
        {
            Canvas.ForceUpdateCanvases();
            var failures = new List<string>();
            foreach (var text in Object.FindObjectsByType<Text>(FindObjectsSortMode.None))
            {
                if (string.IsNullOrWhiteSpace(text.text) || text.verticalOverflow == VerticalWrapMode.Overflow)
                {
                    continue;
                }
                Rect rect = text.rectTransform.rect;
                if (text.font == null || rect.width <= 0 || rect.height <= 0 || text.preferredHeight > rect.height + 2f)
                {
                    failures.Add(text.transform.parent.name + "/" + text.name + $" {text.preferredHeight:F1}>{rect.height:F1}: {text.text}");
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
