using System;
using System.IO;
using NUnit.Framework;
using Refactor;
using Refactor.Main;
using Refactor.Main.Event;
using Refactor.Main.InputModule;
using Refactor.Util;
using UnityEngine;
using Util.Sheet;
using EventHandler = Refactor.Main.Event.EventHandler;

namespace P2.Tests
{
    // UI 의도부터 실제 Handler 및 디스크까지 검증한다. 사용자 저장 폴더는 사용하지 않는다.
    public sealed class FoundationFlowTests
    {
        private string _directory;
        private string _savePath;
        private GameSystemSheet _settings;
        private FoundationDataContainer _data;
        private InputStream _stream;
        private UIInputStream _ui;
        private InputFilter _filter;
        private EventQueue _queue;

        [SetUp]
        public void SetUp()
        {
            DI.Init();
            _directory = Path.Combine(Path.GetTempPath(), "P2.Foundation.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            _savePath = Path.Combine(_directory, "save.json");
            _settings = Settings();
            _data = new FoundationDataContainer(_settings);
            DI.PubSub(_data);
            _stream = new InputStream();
            _ui = new UIInputStream(_stream);
            _filter = new InputFilter(() => _data.PopupOpen);
            _queue = QueueFor(_savePath);
        }

        [TearDown]
        public void TearDown()
        {
            DI.Remove<FoundationEventHandler>();
            DI.Remove<FoundationDataContainer>();
            DI.Init();
            // 정확히 이 테스트가 만든 GUID 디렉터리만 정리한다.
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, true);
            }
        }

        [Test]
        public void InputIsDeferredAndRepeatedClicksArePreservedInOrder()
        {
            _ui.AddInput(UIInputType.Increment);
            _ui.AddInput(UIInputType.Increment);
            _ui.AddInput(UIInputType.Decrement);
            Assert.That(_data.Counter, Is.Zero);
            TransferInputs();
            Assert.That(_data.Counter, Is.Zero, "입력 변환과 큐 등록만으로 상태가 바뀌면 안 됩니다.");
            _queue.Update();
            Assert.That(_data.Counter, Is.EqualTo(2));
            Assert.That(_data.ProcessedRequestCount, Is.EqualTo(3));
            _queue.Update();
            Assert.That(_data.ProcessedRequestCount, Is.EqualTo(3), "처리한 입력을 재실행하면 안 됩니다.");
        }

        [Test]
        public void SaveResetLoadInOneFrameRestoresTheSavedState()
        {
            _ui.SetLabel("  테스트 이름  ");
            _ui.AddInput(UIInputType.Increment);
            _ui.AddInput(UIInputType.Save);
            _ui.AddInput(UIInputType.Reset);
            _ui.AddInput(UIInputType.Load);
            ExecuteInputs();
            Assert.That(_data.Counter, Is.EqualTo(2));
            Assert.That(_data.Label, Is.EqualTo("테스트 이름"));
            Assert.That(_data.ProcessedRequestCount, Is.EqualTo(2));
            Assert.That(FoundationSave.Load(_savePath, _settings, out var saved), Is.EqualTo(FoundationLoadResult.Loaded));
            Assert.That(saved.Label, Is.EqualTo(_data.Label));
        }

        [Test]
        public void SaveOverwritesExistingFileAndLeavesNoTemporaryFiles()
        {
            Request(UIInputType.Save);
            _ui.SetLabel("두 번째");
            _ui.AddInput(UIInputType.Decrement);
            _ui.AddInput(UIInputType.Save);
            ExecuteInputs();
            Assert.That(FoundationSave.Load(_savePath, _settings, out var dto), Is.EqualTo(FoundationLoadResult.Loaded));
            Assert.That(dto.Counter, Is.EqualTo(-2));
            Assert.That(dto.Label, Is.EqualTo("두 번째"));
            Assert.That(dto.ProcessedRequestCount, Is.EqualTo(2));
            CollectionAssert.AreEquivalent(new[] { _savePath }, Directory.GetFiles(_directory));
        }

        [Test]
        public void PopupBlocksRequestsAndClosingAllowsTheFollowingRequestInTheSameFrame()
        {
            _ui.AddInput(UIInputType.OpenPopup);
            _ui.AddInput(UIInputType.Increment);
            _ui.SetLabel("차단 이름");
            _ui.AddInput(UIInputType.Save);
            ExecuteInputs();
            Assert.That(_data.PopupOpen, Is.True);
            Assert.That(_data.Counter, Is.Zero);
            Assert.That(_data.Label, Is.EqualTo("P2"));
            Assert.That(File.Exists(_savePath), Is.False);
            _ui.AddInput(UIInputType.ClosePopup);
            _ui.AddInput(UIInputType.Increment);
            ExecuteInputs();
            Assert.That(_data.PopupOpen, Is.False);
            Assert.That(_data.Counter, Is.EqualTo(2));
        }

        [Test]
        public void FocusAndLockAreCheckedAtExecutionTime()
        {
            _ui.AddInput(UIInputType.Increment);
            TransferInputs();
            _filter.Focused = false;
            _queue.Update();
            Assert.That(_data.Counter, Is.Zero);
            _filter.Focused = true;
            _filter.IsLocked = true;
            Request(UIInputType.Increment);
            Assert.That(_data.Counter, Is.Zero);
            _filter.IsLocked = false;
            Request(UIInputType.Increment);
            Assert.That(_data.Counter, Is.EqualTo(2));
        }

        [Test]
        public void CounterBoundsRejectMutationWithoutChangingRequestCount()
        {
            Request(UIInputType.Increment);
            Request(UIInputType.Increment);
            Request(UIInputType.Increment);
            Assert.That(_data.Counter, Is.EqualTo(4));
            Assert.That(_data.ProcessedRequestCount, Is.EqualTo(2));
            for (int i = 0; i < 5; i++)
            {
                Request(UIInputType.Decrement);
            }
            Assert.That(_data.Counter, Is.EqualTo(-4));
            Assert.That(_data.ProcessedRequestCount, Is.EqualTo(6));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("12345678901234567")]
        public void InvalidLabelPreservesCurrentData(string invalid)
        {
            _ui.SetLabel(invalid);
            ExecuteInputs();
            Assert.That(_data.Label, Is.EqualTo("P2"));
            Assert.That(_data.ProcessedRequestCount, Is.Zero);
        }

        [Test]
        public void SameLabelIsNotCountedAsAnotherMutation()
        {
            _ui.SetLabel(" P2 ");
            ExecuteInputs();
            Assert.That(_data.ProcessedRequestCount, Is.Zero);
        }

        [Test]
        public void RequestCountOverflowDoesNotPartiallyChangeData()
        {
            WriteDto(0, int.MaxValue, "기존 이름");
            Request(UIInputType.Load);
            Request(UIInputType.Increment);
            _ui.SetLabel("다른 이름");
            ExecuteInputs();
            Assert.That(_data.Counter, Is.Zero);
            Assert.That(_data.Label, Is.EqualTo("기존 이름"));
            Assert.That(_data.ProcessedRequestCount, Is.EqualTo(int.MaxValue));
        }

        [Test]
        public void MissingSaveLeavesCurrentDataUnchanged()
        {
            AssertLoadFailure(FoundationLoadResult.Missing);
        }

        [TestCase("{")]
        [TestCase("null")]
        [TestCase("{}")]
        [TestCase("{\"Format\":\"IZ.Foundation\",\"Version\":1,\"Counter\":0,\"ProcessedRequestCount\":0,\"Label\":\"P2\"}")]
        [TestCase("{\"Format\":\"P2.Foundation\",\"Version\":1,\"ProcessedRequestCount\":0,\"Label\":\"P2\"}")]
        [TestCase("{\"Format\":\"P2.Foundation\",\"Version\":1,\"Counter\":0,\"Label\":\"P2\"}")]
        [TestCase("{\"Format\":\"P2.Foundation\",\"Version\":1,\"Counter\":0,\"ProcessedRequestCount\":0}")]
        [TestCase("{\"Format\":\"P2.Foundation\",\"Counter\":0,\"ProcessedRequestCount\":0,\"Label\":\"P2\"}")]
        public void CorruptOrIncompleteSaveLeavesCurrentDataUnchanged(string contents)
        {
            File.WriteAllText(_savePath, contents);
            AssertLoadFailure(FoundationLoadResult.Corrupt);
            Assert.That(File.ReadAllText(_savePath), Is.EqualTo(contents), "읽기 실패로 파일을 덮어쓰면 안 됩니다.");
        }

        [TestCase(5, 0, "P2")]
        [TestCase(-5, 0, "P2")]
        [TestCase(0, -1, "P2")]
        [TestCase(0, 0, "  ")]
        [TestCase(0, 0, " P2")]
        [TestCase(0, 0, "12345678901234567")]
        public void InvalidSavedValuesAreRejected(int counter, int count, string label)
        {
            WriteDto(counter, count, label);
            AssertLoadFailure(FoundationLoadResult.Corrupt);
        }

        [Test]
        public void FutureVersionLeavesCurrentDataUnchanged()
        {
            WriteDto(0, 0, "P2", 2);
            AssertLoadFailure(FoundationLoadResult.UnsupportedVersion);
        }

        [Test]
        public void ReadIOErrorLeavesCurrentDataUnchanged()
        {
            // 디렉터리를 파일처럼 읽는 시도는 테스트 플랫폼에서 접근/I/O 실패가 된다.
            _savePath = _directory;
            _queue = QueueFor(_savePath);
            AssertLoadFailure(FoundationLoadResult.IOError);
        }

        [Test]
        public void FailedSavePreservesStateAndCleansItsTemporaryFile()
        {
            string blockedParent = Path.Combine(_directory, "parent-is-a-file");
            File.WriteAllText(blockedParent, "keep");
            _queue = QueueFor(Path.Combine(blockedParent, "save.json"));
            Request(UIInputType.Increment);
            Request(UIInputType.Save);
            Assert.That(_data.Counter, Is.EqualTo(2));
            Assert.That(_data.ProcessedRequestCount, Is.EqualTo(1));
            StringAssert.Contains("실패", _data.Status);
            Assert.That(File.ReadAllText(blockedParent), Is.EqualTo("keep"));
        }

        [Test]
        public void QueueRejectsReentrantEnqueueAndRecoversForTheNextUpdate()
        {
            _queue = QueueFor(_savePath, () => _queue.AddEvent(new UIActionRequested(UIInputType.Increment)));
            _queue.AddEvent(new UIActionRequested(UIInputType.Reset));
            Assert.Throws<InvalidOperationException>(() => _queue.Update());
            _queue.AddEvent(new UIActionRequested(UIInputType.Increment));
            Assert.DoesNotThrow(() => _queue.Update());
            Assert.That(_data.Counter, Is.EqualTo(2));
        }

        [Test]
        public void QueueRejectsReentrantUpdateAndUnknownRequest()
        {
            _queue = QueueFor(_savePath, () => _queue.Update());
            _queue.AddEvent(new UIActionRequested(UIInputType.Reset));
            Assert.Throws<InvalidOperationException>(() => _queue.Update());
            Assert.Throws<InvalidOperationException>(() => _queue.AddEvent(new UnknownRequest()));
            Assert.Throws<ArgumentNullException>(() => _queue.AddEvent(null));
        }

        [Test]
        public void DependencyReplacementAndRemovalClearTheOldLifetime()
        {
            var probe = new DataProbe();
            DI.PubSub(probe);
            Assert.That(probe.Data, Is.SameAs(_data));
            var replacement = new FoundationDataContainer(_settings);
            DI.PubSub(replacement);
            Request(UIInputType.Increment);
            Assert.That(_data.Counter, Is.Zero);
            Assert.That(replacement.Counter, Is.EqualTo(2));
            Assert.That(probe.Data, Is.SameAs(replacement));
            DI.Remove<FoundationDataContainer>();
            Assert.That(probe.Data, Is.Null);
            DI.Init();
            DI.PubSub(_data);
            Assert.That(probe.Data, Is.Null, "초기화 전에 등록한 Subscriber는 다음 실행에 남으면 안 됩니다.");
        }

        [TestCase("0", "4", "16")]
        [TestCase("5", "4", "16")]
        [TestCase("1.5", "4", "16")]
        [TestCase("1", "4", "1")]
        public void InvalidSheetSettingsFailBeforeRuntimeStarts(string step, string limit, string labelLimit)
        {
            Assert.Throws<FormatException>(() => new GameSystemSheet(new GameSystemTable(Csv(step, limit, labelLimit))));
        }

        private void AssertLoadFailure(FoundationLoadResult expected)
        {
            Request(UIInputType.Increment);
            _ui.SetLabel("현재 데이터");
            ExecuteInputs();
            Assert.That(FoundationSave.Load(_savePath, _settings, out var dto), Is.EqualTo(expected));
            Assert.That(dto, Is.Null);
            Request(UIInputType.Load);
            Assert.That(_data.Counter, Is.EqualTo(2));
            Assert.That(_data.Label, Is.EqualTo("현재 데이터"));
            Assert.That(_data.ProcessedRequestCount, Is.EqualTo(2));
        }

        private EventQueue QueueFor(string path, Action onRefresh = null)
        {
            return new EventQueue(new EventHandler(new FoundationEventHandler(_filter, _settings, path, onRefresh)));
        }

        private void Request(UIInputType action)
        {
            _ui.AddInput(action);
            ExecuteInputs();
        }

        private void ExecuteInputs()
        {
            TransferInputs();
            _queue.Update();
        }

        private void TransferInputs()
        {
            foreach (var request in new InputBinding(_stream).ConvertInputStreamToEventData())
            {
                _queue.AddEvent(request);
            }
            _stream.ClearInputs();
        }

        private void WriteDto(int counter, int count, string label, int version = 1)
        {
            File.WriteAllText(_savePath, JsonUtility.ToJson(new FoundationSaveData
            {
                Format = FoundationSave.CurrentFormat,
                Version = version,
                Counter = counter,
                ProcessedRequestCount = count,
                Label = label
            }));
        }

        private static GameSystemSheet Settings()
        {
            return new GameSystemSheet(new GameSystemTable(Csv("2", "4", "16")));
        }

        private static string Csv(string step, string limit, string labelLimit)
        {
            return "Key,Value,Description\n"
                + $"GAMESYSTEM_Foundation_CounterStep,{step},step\n"
                + $"GAMESYSTEM_Foundation_CounterLimit,{limit},limit\n"
                + $"GAMESYSTEM_Foundation_LabelMaxLength,{labelLimit},label\n";
        }

        private sealed class UnknownRequest : IEventData
        {
        }

        [DISubscriber]
        private sealed class DataProbe
        {
            [Inject] public FoundationDataContainer Data;
        }
    }
}
