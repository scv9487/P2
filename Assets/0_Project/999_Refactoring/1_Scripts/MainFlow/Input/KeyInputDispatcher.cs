using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Refactor.Main.InputModule
{
    public sealed class KeyInputDispatcher
    {
        private readonly InputStream _stream;
        private readonly InputFilter _filter;
        private readonly InputProfile _profile;

        public KeyInputDispatcher(InputStream stream, InputFilter filter, InputProfile profile = InputProfile.FoundationTest)
        {
            _stream = stream;
            _filter = filter;
            _profile = profile;
        }

        public void UpdateKeyStream()
        {
            if (!_filter.Focused || _filter.IsLocked)
            {
                return;
            }
            var selected = EventSystem.current?.currentSelectedGameObject;
            if (selected != null && selected.TryGetComponent<InputField>(out var field) && field.isFocused)
            {
                return;
            }
            if (_profile == InputProfile.Title)
            {
                // 선택된 버튼은 EventSystem의 Submit이 실행한다. 종료 버튼의 Enter를 시작으로 바꾸지 않는다.
                if (selected == null || !selected.TryGetComponent<Selectable>(out _))
                {
                    Capture(KeyCode.Return, UIInputType.StartGame);
                }
                return;
            }
            if (_profile == InputProfile.Dungeon)
            {
                Capture(KeyCode.Escape, UIInputType.DungeonBack);
                return;
            }
            if (_profile == InputProfile.Town)
            {
                Capture(KeyCode.Escape, UIInputType.Back);
                return;
            }
            Capture(KeyCode.F1, UIInputType.Increment);
            Capture(KeyCode.F2, UIInputType.Decrement);
            Capture(KeyCode.F5, UIInputType.Save);
            Capture(KeyCode.F9, UIInputType.Load);
            Capture(KeyCode.Escape, _filter.PopupOpen ? UIInputType.ClosePopup : UIInputType.OpenPopup);
        }

        private void Capture(KeyCode key, UIInputType action)
        {
            if (Input.GetKeyDown(key))
            {
                _stream.AddUIInput(new UIInput(action));
            }
        }
    }
}
