namespace Refactor.Main.InputModule
{
    // View에 주는 권한은 입력 기록뿐이다. 큐 실행이나 상태 변경은 노출하지 않는다.
    public sealed class UIInputStream
    {
        private readonly InputStream _inputStream;

        public UIInputStream(InputStream inputStream)
        {
            _inputStream = inputStream;
        }

        public void AddInput(UIInputType action, string value = null, string context = null)
        {
            _inputStream.AddUIInput(new UIInput(action, value, context));
        }

        public void SetLabel(string label)
        {
            _inputStream.AddUIInput(new UIInput(UIInputType.ApplyLabel, label));
        }
    }
}
