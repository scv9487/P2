using System.Collections;
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

namespace P2.Tests
{
    // 실제 사용 씬의 연결과 프레임 실행을 검사한다. Save/Load 버튼은 눌러 사용자 파일을 변경하지 않는다.
    public sealed class FoundationSceneTests
    {
        private const string ScenePath = "Assets/0_Project/2_Test/0_Scenes/2_Test.unity";
        private MainFlow _flow;
        private FoundationView _view;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return LoadTestScene();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            var scene = SceneManager.GetSceneByPath(ScenePath);
            if (scene.IsValid() && scene.isLoaded)
            {
                // 항상 최소 한 씬을 남겨 두고 테스트 씬의 OnDestroy 정리를 실행한다.
                var empty = SceneManager.CreateScene("P2 Test Cleanup");
                SceneManager.SetActiveScene(empty);
                yield return SceneManager.UnloadSceneAsync(scene);
            }
        }

        [UnityTest]
        public IEnumerator SceneHasSerializedReferencesAndButtonsChangeDataOnTheNextFrame()
        {
            Assert.That(_flow.IsReady, Is.True);
            Assert.That(Object.FindObjectsByType<MainFlow>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
            Assert.That(Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
            Assert.That(_view.GetComponent<GraphicRaycaster>(), Is.Not.Null);
            foreach (var field in typeof(FoundationView).GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
            {
                if (field.IsDefined(typeof(SerializeField), false))
                {
                    Assert.That(field.GetValue(_view) as Object, Is.Not.Null, field.Name + " 직렬화 참조 누락");
                }
            }
            Find<Button>("IncrementButton").onClick.Invoke();
            Assert.That(_flow.Data.Counter, Is.Zero, "버튼 callback에서 직접 상태를 변경하면 안 됩니다.");
            yield return null;
            Assert.That(_flow.Data.Counter, Is.EqualTo(1));
            Assert.That(Find<Text>("CounterText").text, Is.EqualTo("1"));
            Find<Button>("DecrementButton").onClick.Invoke();
            yield return null;
            Assert.That(_flow.Data.Counter, Is.Zero);
            Assert.That(_flow.Data.ProcessedRequestCount, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator LabelDraftStaysLocalUntilAppliedAndResetRefreshesTheDraft()
        {
            var input = Find<InputField>("LabelInput");
            input.text = "테스트 이름";
            yield return null;
            Assert.That(_flow.Data.Label, Is.EqualTo("P2"));
            Assert.That(input.text, Is.EqualTo("테스트 이름"), "View 갱신이 편집 초안을 덮어쓰면 안 됩니다.");
            Find<Button>("ApplyLabelButton").onClick.Invoke();
            yield return null;
            Assert.That(_flow.Data.Label, Is.EqualTo("테스트 이름"));
            StringAssert.Contains("테스트 이름", Find<Text>("LabelText").text);
            input.text = "적용하지 않은 초안";
            Find<Button>("ResetButton").onClick.Invoke();
            yield return null;
            Assert.That(_flow.Data.Label, Is.EqualTo("P2"));
            Assert.That(input.text, Is.EqualTo("P2"));
            Assert.That(_flow.Data.ProcessedRequestCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator PopupBlocksBackgroundInputAndCloseThenClickKeepsFifoOrdering()
        {
            Find<Button>("PopupButton").onClick.Invoke();
            Find<Button>("IncrementButton").onClick.Invoke();
            yield return null;
            Assert.That(_flow.Data.PopupOpen, Is.True);
            Assert.That(_flow.Data.Counter, Is.Zero);
            Assert.That(Find<CanvasGroup>("Content").interactable, Is.False);
            Assert.That(Find<Button>("CloseButton").gameObject.activeInHierarchy, Is.True);
            Find<Button>("CloseButton").onClick.Invoke();
            Find<Button>("IncrementButton").onClick.Invoke();
            yield return null;
            Assert.That(_flow.Data.PopupOpen, Is.False);
            Assert.That(_flow.Data.Counter, Is.EqualTo(1));
            Assert.That(Find<CanvasGroup>("Content").interactable, Is.True);
        }

        [UnityTest]
        public IEnumerator ReloadingSceneReplacesStateAndKeepsOneListenerPerButton()
        {
            Find<Button>("IncrementButton").onClick.Invoke();
            yield return null;
            var previousFlow = _flow;
            var previousData = _flow.Data;
            yield return LoadTestScene();
            Assert.That(previousFlow == null, Is.True, "이전 씬 MainFlow가 파괴되어야 합니다.");
            Assert.That(_flow.Data, Is.Not.SameAs(previousData));
            Assert.That(_flow.Data.Counter, Is.Zero);
            Find<Button>("IncrementButton").onClick.Invoke();
            yield return null;
            Assert.That(_flow.Data.Counter, Is.EqualTo(1));
            Assert.That(_flow.Data.ProcessedRequestCount, Is.EqualTo(1));
            Assert.That(previousData.Counter, Is.EqualTo(1));
            Assert.That(Object.FindObjectsByType<MainFlow>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
        }

        private IEnumerator LoadTestScene()
        {
            Assert.That(Application.CanStreamedLevelBeLoaded(ScenePath), Is.True, "테스트 씬이 Build Settings에 있어야 합니다.");
            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Single);
            _flow = Object.FindFirstObjectByType<MainFlow>();
            _view = Object.FindFirstObjectByType<FoundationView>();
            Assert.That(_flow, Is.Not.Null);
            Assert.That(_view, Is.Not.Null);
            _flow.SendMessage("OnApplicationFocus", true);
            yield return null;
            Assert.That(_flow.IsReady, Is.True);
        }

        private T Find<T>(string objectName) where T : Component
        {
            return _view.GetComponentsInChildren<T>(true).Single(component => component.name == objectName);
        }
    }
}
