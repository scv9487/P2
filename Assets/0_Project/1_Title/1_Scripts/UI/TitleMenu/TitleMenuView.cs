using System;
using Refactor.Main.InputModule;
using Refactor.Util;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Refactor.UI
{
    // 타이틀은 의도만 기록한다. 씬 변경과 종료는 MainFlow의 요청 처리 단계에서 실행한다.
    public sealed class TitleMenuView : MonoBehaviour
    {
        [SerializeField] private Image Background;
        [SerializeField] private Text LogoText;
        [SerializeField] private Text TaglineText;
        [SerializeField] private Text FooterText;
        [SerializeField] private Text StatusText;
        [SerializeField] private Text StartText;
        [SerializeField] private Text QuitText;
        [SerializeField] private Button StartButton;
        [SerializeField] private Button QuitButton;
        [SerializeField] private CanvasGroup Menu;
        private UnityAction _start;
        private UnityAction _quit;
        private bool _initialized;

        public void Initialize(TownSheet sheet, TownContent content, UIInputStream input)
        {
            UnbindInputs();
            if (sheet == null || content == null || input == null)
            {
                throw new ArgumentNullException("타이틀 초기화 데이터");
            }
            TownView.SetArtwork(Background, content.TownBackground);
            LogoText.text = sheet.Text("title.name");
            TaglineText.text = sheet.Text("title.tagline");
            FooterText.text = sheet.Text("title.footer");
            StartText.text = sheet.Text("title.start");
            QuitText.text = sheet.Text("title.quit");
            _start = () => input.AddInput(UIInputType.StartGame);
            _quit = () => input.AddInput(UIInputType.QuitGame);
            StartButton.onClick.AddListener(_start);
            QuitButton.onClick.AddListener(_quit);
            _initialized = true;
        }

        public void Render(bool locked, string status)
        {
            Menu.interactable = _initialized && !locked;
            StatusText.text = status;
        }

        public void UnbindInputs()
        {
            if (StartButton != null && _start != null)
            {
                StartButton.onClick.RemoveListener(_start);
            }
            if (QuitButton != null && _quit != null)
            {
                QuitButton.onClick.RemoveListener(_quit);
            }
            _start = null;
            _quit = null;
            _initialized = false;
        }

        public void ShowInitializationError(string message)
        {
            UnbindInputs();
            Menu.interactable = false;
            StatusText.text = "초기화 실패: " + message;
        }

        private void OnDestroy()
        {
            UnbindInputs();
        }
    }
}
