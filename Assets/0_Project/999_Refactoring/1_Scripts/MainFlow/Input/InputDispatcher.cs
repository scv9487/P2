namespace Refactor.Main.InputModule
{
    public sealed class InputDispatcher
    {
        private readonly InputStream _stream;
        private readonly InputFilter _filter;
        private readonly KeyInputDispatcher _keys;
        private readonly InputBinding _binding;
        private readonly IEventQueue _queue;

        public InputDispatcher(InputStream stream, InputFilter filter, IEventQueue queue, InputProfile profile = InputProfile.FoundationTest)
        {
            _stream = stream;
            _filter = filter;
            _queue = queue;
            _keys = new KeyInputDispatcher(stream, filter, profile);
            _binding = new InputBinding(stream);
        }

        public void Update()
        {
            try
            {
                if (!_filter.Focused || _filter.IsLocked)
                {
                    return;
                }
                _keys.UpdateKeyStream();
                foreach (var request in _binding.ConvertInputStreamToEventData())
                {
                    _queue.AddEvent(request);
                }
            }
            finally
            {
                _stream.ClearInputs();
            }
        }
    }
}
