using Refactor.Main;
using Refactor.UI;

namespace Refactor.View
{
    public sealed class DungeonViewPresenter
    {
        private readonly DungeonView _view;
        private readonly IDungeonDataReader _reader;

        public DungeonViewPresenter(DungeonView view, IDungeonDataReader reader)
        {
            _view = view;
            _reader = reader;
        }

        public void Refresh(bool locked, string status)
        {
            _view.Render(_reader, locked, status);
        }
    }
}
