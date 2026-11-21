using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Refactor.Main.InputModule
{
    public sealed class KeyInputDispatcher
    {
        private readonly InputStream _stream;
        private readonly InputFilter _filter;

        public KeyInputDispatcher(InputStream stream, InputFilter filter)
        {
            _stream = stream;
            _filter = filter;
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
