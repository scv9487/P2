using System;

namespace Refactor.Main
{
    public enum HeroMenuMode
    {
        None,
        Inventory,
        Skills
    }

    public interface IHeroMenuReader
    {
        HeroMenuMode Mode { get; }
        bool Open { get; }
        string SelectedSkillId { get; }
        string SelectedItemId { get; }
        float PlayerHealth { get; }
        float PlayerMaxHealth { get; }
        bool CanActWithItems { get; }
        bool InCombat { get; }
        string Context { get; }
        string Status { get; }
        int Revision { get; }
    }

    // 개인 메뉴의 선택 문맥만 관리한다. 영속적인 스킬 변경은 Store가 소유한다.
    public sealed class HeroMenuData : IHeroMenuReader
    {
        public HeroMenuMode Mode { get; private set; }
        public bool Open => Mode != HeroMenuMode.None;
        public string SelectedSkillId { get; private set; } = string.Empty;
        public string SelectedItemId { get; private set; } = string.Empty;
        public float PlayerHealth { get; private set; }
        public float PlayerMaxHealth { get; private set; }
        public bool CanActWithItems { get; private set; }
        public bool InCombat { get; private set; }
        public string Context { get; private set; } = string.Empty;
        public string Status { get; private set; } = string.Empty;
        public int Revision { get; private set; }
        internal int SourceRevision { get; private set; }

        internal void Show(HeroMenuMode mode, int sourceRevision)
        {
            Mode = mode;
            SelectedSkillId = string.Empty;
            SelectedItemId = string.Empty;
            Advance(sourceRevision, string.Empty);
        }

        internal void Select(string id, int sourceRevision)
        {
            SelectedSkillId = id;
            Advance(sourceRevision, string.Empty);
        }

        internal void SelectItem(string id, int sourceRevision)
        {
            SelectedItemId = id;
            Advance(sourceRevision, string.Empty);
        }

        internal void SetItemState(float health, float maxHealth, bool canAct, bool inCombat)
        {
            if (PlayerHealth == health && PlayerMaxHealth == maxHealth && CanActWithItems == canAct && InCombat == inCombat)
            {
                return;
            }
            PlayerHealth = health;
            PlayerMaxHealth = maxHealth;
            CanActWithItems = canAct;
            InCombat = inCombat;
            Revision++;
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
            Mode = HeroMenuMode.None;
            SelectedSkillId = SelectedItemId = Context = Status = string.Empty;
            Revision++;
        }
    }
}
