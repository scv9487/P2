using System;

namespace Refactor.Main.InputModule
{
    public sealed class InputFilter
    {
        private readonly Func<bool> _isPopupOpen;
        public bool Focused { get; set; } = true;
        public bool IsLocked { get; set; }
        public bool PopupOpen => _isPopupOpen();

        public InputFilter(Func<bool> isPopupOpen)
        {
            _isPopupOpen = isPopupOpen;
        }

        public bool IsInputAllowed(UIInputType action)
        {
            return Focused && !IsLocked && (!PopupOpen || action == UIInputType.ClosePopup);
        }
    }
}
