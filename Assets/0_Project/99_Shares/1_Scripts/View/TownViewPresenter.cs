using Refactor.Main;
using Refactor.UI;

namespace Refactor.View
{
    public sealed class TownViewPresenter
    {
        private readonly TownView _view;
        private readonly ITownDataReader _reader;

        public TownViewPresenter(TownView view, ITownDataReader reader)
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
