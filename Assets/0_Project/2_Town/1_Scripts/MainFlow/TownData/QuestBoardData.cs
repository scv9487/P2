using System;

namespace Refactor.Main
{
    public interface IQuestBoardReader
    {
        bool Open { get; }
        string SelectedQuestId { get; }
        string Context { get; }
        string Status { get; }
        int Revision { get; }
    }

    // 의뢰 선택과 입력 문맥만 관리한다. 수락/진척/보고는 공용 진행 저장이 소유한다.
    public sealed class QuestBoardData : IQuestBoardReader
    {
        public bool Open { get; private set; }
        public string SelectedQuestId { get; private set; } = string.Empty;
        public string Context { get; private set; } = string.Empty;
        public string Status { get; private set; } = string.Empty;
        public int Revision { get; private set; }
        internal int SourceRevision { get; private set; }

        internal void Show(string selectedId, int sourceRevision)
        {
            Open = true;
            SelectedQuestId = selectedId ?? string.Empty;
            Advance(sourceRevision, string.Empty);
        }

        internal void Select(string id, int sourceRevision)
        {
            SelectedQuestId = id;
            Advance(sourceRevision, string.Empty);
        }

        internal void Advance(int sourceRevision, string status)
        {
            Context = Guid.NewGuid().ToString("N");
            SourceRevision = sourceRevision;
            Status = status;
            Revision++;
        }

        internal void ShowError(string status)
        {
            Status = status;
            Revision++;
        }

        internal void Close()
        {
            Open = false;
            SelectedQuestId = string.Empty;
            Context = string.Empty;
            Status = string.Empty;
            Revision++;
        }
    }
}
