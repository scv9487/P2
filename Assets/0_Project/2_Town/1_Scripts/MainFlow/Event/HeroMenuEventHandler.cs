using System;
using Refactor.Main.InputModule;
using UnityEngine.Scripting;

namespace Refactor.Main.Event
{
    [Preserve]
    public sealed class HeroMenuEventHandler : IEventHandler
    {
        private readonly InputFilter _filter;
        private readonly Action<HeroMenuActionRequested> _handle;

        public HeroMenuEventHandler(InputFilter filter, Action<HeroMenuActionRequested> handle)
        {
            _filter = filter ?? throw new ArgumentNullException(nameof(filter));
            _handle = handle ?? throw new ArgumentNullException(nameof(handle));
        }

        [Preserve]
        public void OnEvent(HeroMenuActionRequested request)
        {
            if (_filter.IsInputAllowed(request.Action))
            {
                _handle(request);
            }
        }
    }
}
