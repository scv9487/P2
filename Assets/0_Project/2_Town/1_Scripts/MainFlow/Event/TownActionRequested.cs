using Refactor.Main.InputModule;

namespace Refactor.Main.Event
{
    // 이름/표시 문구 대신 시트 ID와 입력이 발생한 장소를 복사한다.
    public sealed class TownActionRequested : IEventData
    {
        public UIInputType Action { get; }
        public string TargetId { get; }
        public string LocationId { get; }

        public TownActionRequested(UIInputType action, string targetId = null, string locationId = null)
        {
            Action = action;
            TargetId = targetId;
            LocationId = locationId;
        }
    }
}
