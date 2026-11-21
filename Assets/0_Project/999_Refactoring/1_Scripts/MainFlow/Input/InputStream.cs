using System.Collections.Generic;

namespace Refactor.Main.InputModule
{
    public sealed class InputStream
    {
        private readonly List<UIInput> _uiInputs = new();
        public IReadOnlyList<UIInput> UIInputs => _uiInputs;

        // IZ의 HashSet 대신 수집 순서를 보존한다. Save/Reset/Load 순서도 동일하다.
        public void AddUIInput(UIInput input)
        {
            _uiInputs.Add(input);
        }

        public void ClearInputs()
        {
            _uiInputs.Clear();
        }
    }
}
