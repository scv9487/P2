using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
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
    // 사용자에게 제공되는 타이틀/마을 씬을 직접 실행한다. 영구 상태는 테스트별 임시 경로로 격리한다.
    public sealed class TownSceneTests
    {
        private const string TitlePath = "Assets/0_Project/1_Title/0_Scenes/1_Title.unity";
        private const string TownPath = "Assets/0_Project/2_Town/0_Scenes/2_Town.unity";
        private MainFlow _flow;
        private AdventureSaveScope _save;

        [SetUp]
        public void SetUp()
        {
            _save = new AdventureSaveScope();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.IsValid() && (scene.path == TitlePath || scene.path == TownPath))
            {
                var empty = SceneManager.CreateScene("P2 Town Test Cleanup");
                SceneManager.SetActiveScene(empty);
                yield return SceneManager.UnloadSceneAsync(scene);
            }
            _save?.Dispose();
        }

        [UnityTest]
        public IEnumerator DirectTitleStartupAndStartButtonLoadAReadyTownScene()
        {
            yield return LoadScene(SceneType.Title);
            var title = Object.FindFirstObjectByType<TitleMenuView>();
            Assert.That(title, Is.Not.Null);
            AssertSerializedReferences(title);
            Assert.That(Reference<Text>(title, "LogoText").text, Is.EqualTo(_flow.TownSheet.Text("title.name")));
            Assert.That(Reference<Text>(title, "StartText").text, Is.EqualTo(_flow.TownSheet.Text("title.start")));
            Assert.That(Reference<Image>(title, "Background").sprite, Is.Not.Null);
            Assert.That(Reference<CanvasGroup>(title, "Menu").interactable, Is.True);
            AssertTextFits(title, "LogoText");
            var originalFlow = _flow;

            // 한 프레임의 이중 클릭도 MainFlow의 전환 잠금으로 한 씬 전환만 수행해야 한다.
            Reference<Button>(title, "StartButton").onClick.Invoke();
            Reference<Button>(title, "StartButton").onClick.Invoke();
            Assert.That(_flow.CurrentScene, Is.EqualTo(SceneType.Title));
            Assert.That(_flow.IsTransitioning, Is.False, "버튼 callback은 요청만 기록합니다.");
            yield return WaitForScene(SceneType.Town);

            Assert.That(originalFlow == null, Is.True);
            Assert.That(_flow.TownData.LocationId, Is.Empty);
            Assert.That(_flow.TownData.Revision, Is.Zero);
            AssertSingleRuntime();
            var town = Object.FindFirstObjectByType<TownView>();
            AssertSerializedReferences(town);
            Assert.That(Reference<GameObject>(town, "OverviewRoot").activeSelf, Is.True);
            Assert.That(Reference<GameObject>(town, "LocationRoot").activeSelf, Is.False);
            CollectionAssert.AreEquivalent(new[] { "Location_tavern", "Location_market", "Location_inn", "Location_guild", "Location_dungeon" },
                ActiveButtons(town, "Location_").Select(button => button.name));
            AssertTextFits(town, "TownNameText", "LocationsHeadingText");
        }

        [UnityTest]
        public IEnumerator AllFourLocationsShowLeftCommandsTopVisitorsAndWorkingDetailPanels()
        {
            yield return LoadScene(SceneType.Town);
            var town = Object.FindFirstObjectByType<TownView>();
            AssertTextFits(town, "TownNameText", "LocationsHeadingText");
            foreach (string id in new[] { "tavern", "market", "inn", "guild" })
            {
                var location = _flow.TownSheet.GetLocation(id);
                ButtonNamed(town, "Location_" + id).onClick.Invoke();
                yield return null;
                Assert.That(_flow.TownData.LocationId, Is.EqualTo(id));
                Assert.That(Reference<GameObject>(town, "LocationRoot").activeSelf, Is.True);
                Assert.That(Reference<Text>(town, "LocationNameText").text, Is.EqualTo(location.Name));
                Assert.That(Reference<Image>(town, "LocationBackground").sprite, Is.Not.Null);

                var commandButtons = ActiveButtons(town, "Command_");
                var visitorButtons = ActiveButtons(town, "Character_");
                CollectionAssert.AreEqual(location.CommandIds.Select(command => "Command_" + command), commandButtons.Select(button => button.name));
                CollectionAssert.AreEqual(location.VisitorIds.Where(visitor => !_flow.AdventureProgress.IsInParty(visitor))
                    .Select(visitor => "Character_" + visitor), visitorButtons.Select(button => button.name));
                foreach (var button in visitorButtons)
                {
                    Assert.That(button.transform.Find("PortraitClip/Portrait").GetComponent<Image>().sprite, Is.Not.Null, button.name);
                }
                Canvas.ForceUpdateCanvases();
                Assert.That(commandButtons[0].transform.position.x, Is.LessThan(visitorButtons[0].transform.position.x), "명령 목록은 인물 행 왼쪽에 배치합니다.");
                Assert.That(visitorButtons[0].transform.position.y, Is.GreaterThan(commandButtons[0].transform.position.y), "방문 인물은 명령보다 위쪽에 표시합니다.");
                AssertTextFits(town, "LocationNameText", "VisitorsHeadingText", "CommandsHeadingText");

                foreach (string commandId in location.CommandIds.Where(command => command != "leave"))
                {
                    ButtonNamed(town, "Command_" + commandId).onClick.Invoke();
                    yield return null;
                    if (commandId == "trade")
                    {
                        Assert.That(_flow.TownData.CommandId, Is.Empty);
                        Assert.That(_flow.TownData.ShopOpen, Is.True);
                        Assert.That(_flow.TownData.ShopId, Is.EqualTo(id));
                        Assert.That(_flow.Shop.MerchantId, Is.EqualTo(id));
                        Assert.That(town.GetComponentsInChildren<Transform>(true).Single(item => item.name == "ShopRoot").gameObject.activeSelf, Is.True);
                        Assert.That(Reference<GameObject>(town, "DetailsRoot").activeSelf, Is.False);
                        Assert.That(Reference<CanvasGroup>(town, "LocationControls").interactable, Is.False);
                        AssertTextFits(town);
                        Reference<Button>(town, "LeaveLocationButton").onClick.Invoke();
                        yield return null;
                        Assert.That(_flow.TownData.LocationId, Is.EqualTo(id));
                        Assert.That(_flow.TownData.ShopOpen, Is.True);
                        ButtonNamed(town, "ShopCloseButton").onClick.Invoke();
                        yield return null;
                        Assert.That(_flow.TownData.ShopOpen, Is.False);
                        continue;
                    }
                    if (commandId == "quest_accept" || commandId == "quest_complete")
                    {
                        Assert.That(_flow.TownData.CommandId, Is.Empty);
                        Assert.That(_flow.TownData.QuestBoardOpen && _flow.QuestBoard.Open, Is.True);
                        Assert.That(town.GetComponentsInChildren<Transform>(true).Single(item => item.name == "QuestBoardRoot").gameObject.activeSelf, Is.True);
                        Assert.That(ActiveButtons(town, "QuestCard_").Count(), Is.EqualTo(4));
                        Assert.That(Reference<GameObject>(town, "DetailsRoot").activeSelf, Is.False);
                        Assert.That(Reference<CanvasGroup>(town, "LocationControls").interactable, Is.False);
                        AssertTextFits(town);
                        Reference<Button>(town, "LeaveLocationButton").onClick.Invoke();
                        yield return null;
                        Assert.That(_flow.TownData.LocationId, Is.EqualTo(id));
                        Assert.That(_flow.QuestBoard.Open, Is.True);
                        ButtonNamed(town, "QuestCloseButton").onClick.Invoke();
                        yield return null;
                        Assert.That(_flow.TownData.QuestBoardOpen || _flow.QuestBoard.Open, Is.False);
                        continue;
                    }
                    Assert.That(_flow.TownData.CommandId, Is.EqualTo(commandId));
                    Assert.That(Reference<GameObject>(town, "DetailsRoot").activeSelf, Is.True);
                    Assert.That(Reference<GameObject>(town, "CommandDetails").activeSelf, Is.True);
                    Assert.That(Reference<Text>(town, "CommandTitleText").text, Is.EqualTo(_flow.TownSheet.GetCommand(commandId).Title));
                    Assert.That(Reference<Text>(town, "CommandBodyText").text, Is.EqualTo(_flow.TownSheet.GetCommand(commandId).Body));
                    Assert.That(Reference<CanvasGroup>(town, "LocationControls").interactable, Is.False);
                    AssertTextFits(town, "CommandTitleText");

                    Reference<Button>(town, "LeaveLocationButton").onClick.Invoke();
                    yield return null;
                    Assert.That(_flow.TownData.LocationId, Is.EqualTo(id), "모달 뒤쪽의 입력은 직접 callback을 호출해도 차단합니다.");
                    Reference<Button>(town, "CommandCloseButton").onClick.Invoke();
                    yield return null;
                    Assert.That(_flow.TownData.CommandId, Is.Empty);
                    Assert.That(Reference<GameObject>(town, "DetailsRoot").activeSelf, Is.False);
                }

                if (location.CommandIds.Contains("leave"))
                {
                    ButtonNamed(town, "Command_leave").onClick.Invoke();
                }
                else
                {
                    Reference<Button>(town, "LeaveLocationButton").onClick.Invoke();
                }
                yield return null;
                Assert.That(_flow.TownData.LocationId, Is.Empty);
                Assert.That(Reference<GameObject>(town, "OverviewRoot").activeSelf, Is.True);
            }
        }

        [UnityTest]
        public IEnumerator ConversationClosesAndTitleRoundTripCreatesFreshStateWithoutDuplicateListeners()
        {
            yield return LoadScene(SceneType.Town);
            var town = Object.FindFirstObjectByType<TownView>();
            ButtonNamed(town, "Location_tavern").onClick.Invoke();
            yield return null;
            string visitorId = _flow.TownSheet.GetLocation("tavern").VisitorIds[0];
            var visitor = _flow.TownSheet.GetCharacter(visitorId);
            ButtonNamed(town, "Character_" + visitorId).onClick.Invoke();
            yield return null;
            Assert.That(_flow.TownData.CharacterId, Is.EqualTo(visitorId));
            Assert.That(_flow.CharacterInteraction.Open, Is.True);
            Assert.That(_flow.CharacterInteraction.DialogueOpen, Is.False, "인물 카드는 먼저 상호작용 목록을 엽니다.");
            Assert.That(Reference<GameObject>(town, "CharacterDetails").activeSelf, Is.False);
            var interaction = Object.FindFirstObjectByType<CharacterPartyView>();
            Assert.That(interaction.GetComponentsInChildren<Text>().Single(text => text.name == "CharacterNameText").text, Is.EqualTo(visitor.Name));
            Assert.That(interaction.GetComponentsInChildren<Image>().Single(image => image.name == "CharacterPortrait").sprite, Is.Not.Null);
            ButtonNamed(town, "CharacterTalkButton").onClick.Invoke();
            yield return null;
            Assert.That(_flow.CharacterInteraction.DialogueOpen, Is.True);
            Assert.That(interaction.GetComponentsInChildren<Text>().Single(text => text.name == "CharacterDialogueText").text, Is.EqualTo(visitor.Dialogue));
            Assert.That(_flow.AdventureProgress.GetCharacter(visitorId).Affinity, Is.EqualTo(_flow.CharacterSheet.TalkAffinityGain));
            AssertTextFits(town);
            ButtonNamed(town, "CharacterCloseButton").onClick.Invoke();
            yield return null;
            Assert.That(_flow.CharacterInteraction.Open, Is.False);
            Assert.That(_flow.TownData.CharacterId, Is.Empty);
            Assert.That(Reference<GameObject>(town, "DetailsRoot").activeSelf, Is.False);
            Reference<Button>(town, "LeaveLocationButton").onClick.Invoke();
            yield return null;

            var previousFlow = _flow;
            var previousData = _flow.TownData;
            int previousRevision = previousData.Revision;
            Reference<Button>(town, "ReturnTitleButton").onClick.Invoke();
            yield return WaitForScene(SceneType.Title);
            Assert.That(previousFlow == null, Is.True);
            var title = Object.FindFirstObjectByType<TitleMenuView>();
            AssertTextFits(title, "LogoText");
            Reference<Button>(title, "StartButton").onClick.Invoke();
            yield return WaitForScene(SceneType.Town);
            Assert.That(_flow.TownData, Is.Not.SameAs(previousData));
            Assert.That(_flow.TownData.LocationId, Is.Empty);
            Assert.That(_flow.TownData.Revision, Is.Zero);
            AssertSingleRuntime();

            town = Object.FindFirstObjectByType<TownView>();
            AssertTextFits(town, "TownNameText", "LocationsHeadingText");
            ButtonNamed(town, "Location_inn").onClick.Invoke();
            yield return null;
            ButtonNamed(town, "Command_rest").onClick.Invoke();
            yield return null;
            Reference<Button>(town, "CommandCloseButton").onClick.Invoke();
            yield return null;
            Assert.That(_flow.TownData.LocationId, Is.EqualTo("inn"));
            Assert.That(_flow.TownData.CommandId, Is.Empty);
            Assert.That(_flow.TownData.Revision, Is.EqualTo(3));
            Assert.That(previousData.Revision, Is.EqualTo(previousRevision), "종료된 씬의 상태에 입력이 전달되면 안 됩니다.");
        }

        private IEnumerator LoadScene(SceneType scene)
        {
            string path = scene == SceneType.Title ? TitlePath : TownPath;
            Assert.That(Application.CanStreamedLevelBeLoaded(path), Is.True, path + " Build Settings 누락");
            var operation = SceneManager.LoadSceneAsync(path, LoadSceneMode.Single);
            Assert.That(operation, Is.Not.Null);
            float deadline = Time.realtimeSinceStartup + 30f;
            for (int frame = 0; frame < 1800 && !operation.isDone && Time.realtimeSinceStartup < deadline; frame++)
            {
                yield return null;
            }
            Assert.That(operation.isDone, Is.True, path + " 로딩 시간 초과");
            yield return WaitForScene(scene);
        }

        private IEnumerator WaitForScene(SceneType scene)
        {
            string name = SceneLoader.SceneName(scene);
            float deadline = Time.realtimeSinceStartup + 30f;
            for (int frame = 0; frame < 1800 && Time.realtimeSinceStartup < deadline; frame++)
            {
                var flow = Object.FindFirstObjectByType<MainFlow>();
                if (SceneManager.GetActiveScene().name == name && flow != null && flow.IsReady && flow.CurrentScene == scene)
                {
                    _flow = flow;
                    if (scene != SceneType.Title)
                    {
                        Assert.That(_flow.AdventureSavePath, Is.EqualTo(_save.SavePath));
                    }
                    _flow.SendMessage("OnApplicationFocus", true);
                    yield return null;
                    yield break;
                }
                yield return null;
            }
            Assert.Fail(name + " 씬이 준비되지 않았습니다.");
        }

        private static Button[] ActiveButtons(TownView view, string prefix)
        {
            return view.GetComponentsInChildren<Button>().Where(button => button.name.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
        }

        private static Button ButtonNamed(TownView view, string name)
        {
            return view.GetComponentsInChildren<Button>().Single(button => button.name == name);
        }

        private static T Reference<T>(Component view, string fieldName) where T : Object
        {
            var field = view.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, fieldName);
            var value = field.GetValue(view) as T;
            Assert.That(value, Is.Not.Null, fieldName + " 직렬화 연결 누락");
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

        // 현재 CSV 문구와 고정 크기 폰트의 필요 높이를 실제 Rect와 비교한다.
        // 화면 가림/색 대비의 육안 확인을 대신하지 않으며 명시적인 Overflow 설정은 제외한다.
        private static void AssertTextFits(Component view, params string[] singleLineHeadingFields)
        {
            Canvas.ForceUpdateCanvases();
            const float tolerance = 2f;
            var failures = new List<string>();
            foreach (var text in view.GetComponentsInChildren<Text>())
            {
                if (!text.isActiveAndEnabled || string.IsNullOrWhiteSpace(text.text)
                    || text.verticalOverflow == VerticalWrapMode.Overflow)
                {
                    continue;
                }
                Rect rect = text.rectTransform.rect;
                string context = SceneManager.GetActiveScene().name + "/" + HierarchyPath(text.transform)
                    + $" (font={text.fontSize}, rect={rect.width:F1}x{rect.height:F1}, preferredHeight={text.preferredHeight:F1})";
                if (text.font == null || rect.width <= 0f || rect.height <= 0f)
                {
                    failures.Add("텍스트의 폰트 또는 표시 영역이 없습니다: " + context);
                }
                else if (text.preferredHeight > rect.height + tolerance)
                {
                    failures.Add("텍스트가 세로로 잘립니다: " + context);
                }
            }

            // 주요 제목은 의도하지 않은 줄바꿈으로 높이 검사만 통과하지 않도록 가로 폭도 확인한다.
            foreach (string field in singleLineHeadingFields)
            {
                var text = Reference<Text>(view, field);
                if (text.isActiveAndEnabled && !string.IsNullOrWhiteSpace(text.text)
                    && text.horizontalOverflow != HorizontalWrapMode.Overflow
                    && text.preferredWidth > text.rectTransform.rect.width + tolerance)
                {
                    failures.Add("제목의 가로 공간이 부족합니다: " + SceneManager.GetActiveScene().name + "/" + HierarchyPath(text.transform));
                }
            }
            Assert.That(failures, Is.Empty, string.Join("\n", failures));
        }

        private static string HierarchyPath(Transform transform)
        {
            string path = transform.name;
            while (transform.parent != null)
            {
                transform = transform.parent;
                path = transform.name + "/" + path;
            }
            return path;
        }

        private static void AssertSingleRuntime()
        {
            Assert.That(Object.FindObjectsByType<MainFlow>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
            Assert.That(Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
        }
    }
}
