using System;
using System.Collections.Generic;
using Refactor.Main.InputModule;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Refactor.UI
{
    // IZ FoundationView의 입력 전달/표시 책임만 유지한다. 실행 상태는 DataContainer가 소유한다.
    [RequireComponent(typeof(Canvas), typeof(GraphicRaycaster))]
    public sealed class FoundationView : MonoBehaviour
    {
        [SerializeField] private Text CounterText;
        [SerializeField] private Text RequestCountText;
        [SerializeField] private Text LabelText;
        [SerializeField] private Text StatusText;
        [SerializeField] private Text SavePathText;
        [SerializeField] private Button IncrementButton;
        [SerializeField] private Button DecrementButton;
        [SerializeField] private InputField LabelInput;
        [SerializeField] private Button ApplyLabelButton;
        [SerializeField] private Button ResetButton;
        [SerializeField] private Button SaveButton;
        [SerializeField] private Button LoadButton;
        [SerializeField] private Button PopupButton;
        [SerializeField] private Button CloseButton;
        [SerializeField] private GameObject Popup;
        [SerializeField] private CanvasGroup Content;

        private readonly List<(Button Button, UnityAction Action)> _bindings = new();
        private bool _hasRenderedLabel;
        private string _renderedLabel;
        private bool _popupWasOpen;
        private bool _initialized;

        // 버튼은 의도만 기록한다. 파일 I/O나 데이터 변경은 MainFlow의 요청 처리 단계에 속한다.
        public void BindInputs(UIInputStream stream)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }
            UnbindInputs();
            Bind(IncrementButton, () => stream.AddInput(UIInputType.Increment));
            Bind(DecrementButton, () => stream.AddInput(UIInputType.Decrement));
            Bind(ApplyLabelButton, () => stream.SetLabel(LabelInput.text));
            Bind(ResetButton, () => stream.AddInput(UIInputType.Reset));
            Bind(SaveButton, () => stream.AddInput(UIInputType.Save));
            Bind(LoadButton, () => stream.AddInput(UIInputType.Load));
            Bind(PopupButton, () => stream.AddInput(UIInputType.OpenPopup));
            Bind(CloseButton, () => stream.AddInput(UIInputType.ClosePopup));
            _initialized = true;
        }

        public void ConfigureInput(int labelMaxLength)
        {
            LabelInput.characterLimit = labelMaxLength;
        }

        private void Bind(Button button, UnityAction callback)
        {
            if (button == null)
            {
                throw new InvalidOperationException("FoundationView의 버튼 참조가 연결되지 않았습니다.");
            }
            button.onClick.AddListener(callback);
            _bindings.Add((button, callback));
        }

        public void UnbindInputs()
        {
            foreach (var binding in _bindings)
            {
                if (binding.Button != null)
                {
                    binding.Button.onClick.RemoveListener(binding.Action);
                }
            }
            _bindings.Clear();
            _initialized = false;
        }

        // 복원/초기화가 성공했을 때 Presenter가 마지막 View 단계에서 호출한다.
        public void RefreshLabelDraft()
        {
            _hasRenderedLabel = false;
        }

        public void Render(int counter, int processedRequestCount, string label, string status, bool popupOpen, string savePath)
        {
            CounterText.text = counter.ToString("N0");
            RequestCountText.text = "처리한 데이터 변경  " + processedRequestCount.ToString("N0") + "회";
            LabelText.text = "현재 이름  ·  " + label;
            StatusText.text = status;
            SavePathText.text = "저장 위치  " + savePath;

            // 매 프레임 새로 표시하더라도 사용자가 쓰고 있는 초안을 덮어쓰지 않는다.
            if (!_hasRenderedLabel || _renderedLabel != label)
            {
                LabelInput.SetTextWithoutNotify(label);
                _renderedLabel = label;
                _hasRenderedLabel = true;
            }

            Content.interactable = _initialized && !popupOpen;
            Content.blocksRaycasts = !popupOpen;
            Popup.SetActive(popupOpen);
            if (popupOpen != _popupWasOpen)
            {
                EventSystem.current?.SetSelectedGameObject(popupOpen ? CloseButton.gameObject : PopupButton.gameObject);
                _popupWasOpen = popupOpen;
            }
        }

        public void ShowInitializationError(string message)
        {
            UnbindInputs();
            StatusText.text = "초기화 실패: " + message;
            Content.interactable = false;
            Popup.SetActive(false);
        }

        private void OnDestroy()
        {
            UnbindInputs();
        }
    }
}
