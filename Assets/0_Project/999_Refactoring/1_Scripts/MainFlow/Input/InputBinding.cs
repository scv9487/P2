using System.Collections.Generic;
using Refactor.Main.Event;

namespace Refactor.Main.InputModule
{
    public sealed class InputBinding
    {
        private readonly InputStream _stream;

        public InputBinding(InputStream stream)
        {
            _stream = stream;
        }

        // 모달 여부는 Handler가 각 요청 실행 직전에 검사한다.
        // 같은 프레임의 ClosePopup → Increment도 FIFO 결과를 따른다.
        public IEnumerable<IEventData> ConvertInputStreamToEventData()
        {
            foreach (var input in _stream.UIInputs)
            {
                yield return input.Type == UIInputType.ApplyLabel
                    ? (IEventData)new SetLabelRequested(input.Value)
                    : new UIActionRequested(input.Type);
            }
        }
    }
}
