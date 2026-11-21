using Refactor.Main.InputModule;

namespace Refactor.Main.Event
{
    public sealed class UIActionRequested : IEventData
    {
        public UIInputType Action { get; }

        public UIActionRequested(UIInputType action)
        {
            Action = action;
        }
    }

    public sealed class SetLabelRequested : IEventData
    {
        public string Label { get; }

        public SetLabelRequested(string label)
        {
            Label = label;
        }
    }
}
