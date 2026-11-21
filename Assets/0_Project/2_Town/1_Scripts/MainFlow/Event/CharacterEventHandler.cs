using System;
using UnityEngine.Scripting;
using Refactor.Main.InputModule;

namespace Refactor.Main.Event
{
    [Preserve]
    public sealed class CharacterEventHandler : IEventHandler
    {
        private readonly InputFilter _filter;
        private readonly Action<CharacterActionRequested> _handle;

        public CharacterEventHandler(InputFilter filter, Action<CharacterActionRequested> handle)
        {
            _filter = filter ?? throw new ArgumentNullException(nameof(filter));
            _handle = handle ?? throw new ArgumentNullException(nameof(handle));
        }

        [Preserve]
        public void OnEvent(CharacterActionRequested request)
        {
            if (_filter.IsInputAllowed(request.Action))
            {
                _handle(request);
            }
        }
    }
}
