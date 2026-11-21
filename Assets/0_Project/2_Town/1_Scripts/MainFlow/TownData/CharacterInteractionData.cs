using System;

namespace Refactor.Main
{
    public interface ICharacterInteractionReader
    {
        bool Open { get; }
        string CharacterId { get; }
        bool FromParty { get; }
        string Context { get; }
        string Status { get; }
        bool DialogueOpen { get; }
    }

    // 화면 선택과 입력 문맥만 소유하며 인물의 영속 상태는 ProgressStore에 둔다.
    public sealed class CharacterInteractionData : ICharacterInteractionReader
    {
        public bool Open { get; private set; }
        public string CharacterId { get; private set; } = string.Empty;
        public bool FromParty { get; private set; }
        public string Context { get; private set; } = string.Empty;
        public string Status { get; private set; } = string.Empty;
        public bool DialogueOpen { get; private set; }
        internal int SourceRevision { get; private set; }
        internal int? RecruitmentRoll { get; set; }

        internal void Select(string id, bool fromParty, int revision)
        {
            Open = true;
            CharacterId = id;
            FromParty = fromParty;
            DialogueOpen = false;
            Advance(revision, string.Empty);
        }

        internal void Advance(int revision, string status, bool dialogue = false)
        {
            Context = Guid.NewGuid().ToString("N");
            SourceRevision = revision;
            RecruitmentRoll = null;
            Status = status;
            DialogueOpen = dialogue;
        }

        internal void ShowError(string status)
        {
            Status = status;
        }

        internal void Close()
        {
            Open = false;
            CharacterId = string.Empty;
            Context = string.Empty;
            Status = string.Empty;
            DialogueOpen = false;
            RecruitmentRoll = null;
        }
    }
}
