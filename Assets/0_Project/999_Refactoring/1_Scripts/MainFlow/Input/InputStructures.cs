namespace Refactor.Main.InputModule
{
    public enum UIInputType
    {
        Increment,
        Decrement,
        ApplyLabel,
        Reset,
        Save,
        Load,
        OpenPopup,
        ClosePopup
    }

    // UI의 가변 편집 상태 대신 수집 시점의 의도와 문자열 값을 보관한다.
    public readonly struct UIInput
    {
        public UIInputType Type { get; }
        public string Value { get; }

        public UIInput(UIInputType type, string value = null)
        {
            Type = type;
            Value = value;
        }
    }
}
