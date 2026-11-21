using System;
using System.IO;
using Refactor.Main.Event;
using Refactor.Main.InputModule;
using Refactor.UI;
using Refactor.Util;
using Refactor.View;
using UnityEngine;
using EventHandler = Refactor.Main.Event.EventHandler;

namespace Refactor.Main
{
    // 단일 테스트 씬의 조립 및 실행 순서 소유자. 다른 MonoBehaviour는 데이터를 실행하지 않는다.
    public sealed class MainFlow : MonoBehaviour
    {
        [SerializeField] private TextAsset _gameSystemTable;
        [SerializeField] private FoundationView _view;
        private static MainFlow _active;
        private FoundationDataContainer _data;
        private InputStream _stream;
        private InputFilter _filter;
        private InputDispatcher _input;
        private EventQueue _queue;
        private FoundationViewPresenter _presenter;

        public bool IsReady { get; private set; }
        public UIInputStream UIInput { get; private set; }
        public IFoundationDataReader Data => _data;
        public string SavePath { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _active = null;
            DI.Init();
        }

        private void Awake()
        {
            if (_active != null && _active != this)
            {
                Debug.LogError("MainFlow는 씬에 하나만 배치해야 합니다.", this);
                enabled = false;
                return;
            }
            _active = this;
            DI.Init();
            try
            {
                if (_view == null || _gameSystemTable == null)
                {
                    throw new InvalidOperationException("MainFlow의 View와 GameSystemTable 연결을 확인해 주세요.");
                }
                var settings = new GameSystemSheet(new global::Util.Sheet.GameSystemTable(_gameSystemTable.text));
                _data = new FoundationDataContainer(settings);
                DI.PubSub(_data);
                _stream = new InputStream();
                UIInput = new UIInputStream(_stream);
                _filter = new InputFilter(() => _data.PopupOpen) { Focused = Application.isFocused };
                SavePath = Path.Combine(Application.persistentDataPath, "P2Foundation", "save.json");
                _presenter = new FoundationViewPresenter(_view, _data, SavePath);
                var handler = new FoundationEventHandler(_filter, settings, SavePath, _presenter.RequestDraftRefresh);
                _queue = new EventQueue(new EventHandler(handler));
                _input = new InputDispatcher(_stream, _filter, _queue);
                _view.ConfigureInput(settings.LabelMaxLength);
                _view.BindInputs(UIInput);
                _presenter.Refresh();
                IsReady = true;
            }
            catch (Exception exception)
            {
                _view?.ShowInitializationError(exception.Message);
                Debug.LogException(exception, this);
                enabled = false;
            }
        }

        private void Update()
        {
            if (!IsReady)
            {
                return;
            }
            _input.Update();
            _queue.Update();
            // 시간 시뮬레이션/Observer/ObjectLoader는 실제 기능이 생길 때 이 사이에 연결한다.
            _presenter.Refresh();
        }

        private void OnApplicationFocus(bool focused)
        {
            if (_filter == null)
            {
                return;
            }
            _filter.Focused = focused;
            _stream.ClearInputs();
            _queue?.ClearPendingSyncEvents();
        }

        private void OnDisable()
        {
            _stream?.ClearInputs();
            _queue?.ClearPendingSyncEvents();
        }

        private void OnDestroy()
        {
            if (_active != this)
            {
                return;
            }
            IsReady = false;
            _view?.UnbindInputs();
            _stream?.ClearInputs();
            _queue?.ClearPendingSyncEvents();
            DI.Remove<FoundationEventHandler>();
            DI.Remove<FoundationDataContainer>();
            _active = null;
        }
    }
}
