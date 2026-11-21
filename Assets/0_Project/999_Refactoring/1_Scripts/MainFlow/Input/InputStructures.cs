namespace Refactor.Main.InputModule
{
    public enum UIInputType
    {
        Increment,
        Decrement,
        ApplyLabel,
        Reset,
        Save,
        Load,
        OpenPopup,
        ClosePopup,
        StartGame = 100,
        QuitGame,
        ReturnTitle,
        EnterLocation,
        LocationCommand,
        SelectCharacter,
        CloseDetails,
        LeaveLocation,
        Back,
        SelectDungeon,
        ConfirmDungeon,
        CancelDungeon,
        ShopSelectBuy,
        ShopSelectSell,
        ShopIncrease,
        ShopDecrease,
        ShopConfirm,
        ShopClose,
        DungeonMove = 200,
        DungeonAttack,
        DungeonRest,
        DungeonNextFloor,
        DungeonTalk,
        DungeonCloseDetails,
        DungeonReturnTown,
        DungeonBack,
        DungeonLootIncrease,
        DungeonLootDecrease,
        DungeonConfirmSettlement,
        DungeonCancelSettlement,
        DungeonSelectSkill,
        DungeonUseSkill,
        CharacterOpen = 300,
        PartyCharacterOpen,
        CharacterTalk,
        CharacterRecruit,
        CharacterDismiss,
        CharacterClose,
        QuestSelect = 400,
        QuestAccept,
        QuestComplete,
        QuestClose,
        InventoryOpen = 500,
        SkillsOpen,
        SkillSelect,
        SkillLearn,
        HeroMenuClose,
        InventorySelectItem,
        InventoryEquipItem,
        InventoryUseItem
    }

    public enum InputProfile
    {
        FoundationTest,
        Title,
        Town,
        Dungeon
    }

    // UI의 가변 편집 상태 대신 수집 시점의 의도와 문자열 값을 보관한다.
    public readonly struct UIInput
    {
        public UIInputType Type { get; }
        public string Value { get; }
        public string Context { get; }

        public UIInput(UIInputType type, string value = null, string context = null)
        {
            Type = type;
            Value = value;
            Context = context;
        }
    }
}
