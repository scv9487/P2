using Refactor.Main;
using Refactor.UI;

namespace Refactor.View
{
    public sealed class FoundationViewPresenter
    {
        private readonly FoundationView _view;
        private readonly IFoundationDataReader _reader;
        private readonly string _savePath;
        private bool _refreshDraft;

        public FoundationViewPresenter(FoundationView view, IFoundationDataReader reader, string savePath)
        {
            _view = view;
            _reader = reader;
            _savePath = savePath;
        }

        // 데이터 복원 후 표시할 일을 예약한다. 화면 적용은 Refresh에서만 수행한다.
        public void RequestDraftRefresh()
        {
            _refreshDraft = true;
        }

        public void Refresh()
        {
            if (_refreshDraft)
            {
                _view.RefreshLabelDraft();
                _refreshDraft = false;
            }
            _view.Render(_reader.Counter, _reader.ProcessedRequestCount, _reader.Label,
                _reader.Status, _reader.PopupOpen, _savePath);
        }
    }
}
