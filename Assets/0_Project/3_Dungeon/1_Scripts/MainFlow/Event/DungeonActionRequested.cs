using Refactor.Main.InputModule;

namespace Refactor.Main.Event
{
    public sealed class DungeonActionRequested : IEventData
    {
        public UIInputType Action { get; }
        public string TargetId { get; }
        public string Context { get; }

        public DungeonActionRequested(UIInputType action, string targetId = null, string context = null)
        {
            Action = action;
            TargetId = targetId;
            Context = context;
        }
    }
}
