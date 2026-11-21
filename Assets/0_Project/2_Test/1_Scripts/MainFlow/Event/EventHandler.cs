using Refactor.Util;

namespace Refactor.Main.Event
{
    public interface IEventHandler
    {
    }

    // DS/IZ와 같이 보유한 구체 Handler에서 요청 타입을 찾는다.
    public sealed class EventHandler
    {
        public readonly FoundationEventHandler Foundation;

        public EventHandler(FoundationEventHandler foundation)
        {
            Foundation = foundation;
            DI.PubSub(Foundation);
        }
    }
}
