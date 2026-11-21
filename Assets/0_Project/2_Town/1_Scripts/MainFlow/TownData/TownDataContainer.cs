using System;
using System.Collections.Generic;
using Refactor.Util;

namespace Refactor.Main
{
    public interface ITownDataReader
    {
        string LocationId { get; }
        string CommandId { get; }
        string CharacterId { get; }
        bool DungeonSelectionOpen { get; }
        int SelectedDungeonId { get; }
        bool ShopOpen { get; }
        string ShopId { get; }
        bool QuestBoardOpen { get; }
        int Revision { get; }
    }

    // 현재 단계의 화면 상태만 소유한다. 재화/관계/의뢰 진행이나 저장 데이터는 생성하지 않는다.
    [DIPublisher]
    public sealed class TownDataContainer : ITownDataReader
    {
        private readonly TownSheet _sheet;
        private readonly DungeonSheet _dungeonSheet;
        private readonly ShopSheet _shopSheet;
        private readonly Func<string, bool> _isInParty;
        private readonly QuestSheet _questSheet;
        public string LocationId { get; private set; } = string.Empty;
        public string CommandId { get; private set; } = string.Empty;
        public string CharacterId { get; private set; } = string.Empty;
        public bool DungeonSelectionOpen { get; private set; }
        public int SelectedDungeonId { get; private set; }
        public bool ShopOpen { get; private set; }
        public string ShopId { get; private set; } = string.Empty;
        public bool QuestBoardOpen { get; private set; }
        public int Revision { get; private set; }

        public TownDataContainer(TownSheet sheet, DungeonSheet dungeonSheet = null, ShopSheet shopSheet = null,
            Func<string, bool> isInParty = null, QuestSheet questSheet = null)
        {
            _sheet = sheet ?? throw new ArgumentNullException(nameof(sheet));
            _dungeonSheet = dungeonSheet;
            _shopSheet = shopSheet;
            _isInParty = isInParty;
            _questSheet = questSheet;
        }

        internal bool TryEnterLocation(string id)
        {
            if (LocationId.Length != 0 || string.IsNullOrEmpty(id))
            {
                return false;
            }
            foreach (var location in _sheet.Locations)
            {
                if (location.Id == id)
                {
                    LocationId = id;
                    Revision++;
                    return true;
                }
            }
            return false;
        }

        internal bool TryOpenCommand(string locationId, string commandId)
        {
            if (!CanUseLocation(locationId) || !Contains(_sheet.GetLocation(LocationId).CommandIds, commandId))
            {
                return false;
            }
            if (commandId == "leave")
            {
                return LeaveLocation();
            }
            if (commandId == "enter_dungeon")
            {
                if (_dungeonSheet == null)
                {
                    return false;
                }
                DungeonSelectionOpen = true;
                SelectedDungeonId = 0;
                Revision++;
                return true;
            }
            if (commandId == "trade" && _shopSheet != null)
            {
                var merchant = _shopSheet.ForLocation(LocationId);
                if (merchant == null)
                {
                    return false;
                }
                ShopOpen = true;
                ShopId = merchant.Id;
                Revision++;
                return true;
            }
            if ((commandId == "quest_accept" || commandId == "quest_complete") && _questSheet != null)
            {
                QuestBoardOpen = true;
                Revision++;
                return true;
            }
            CommandId = commandId;
            Revision++;
            return true;
        }

        internal bool TrySelectCharacter(string locationId, string characterId)
        {
            if (!CanUseLocation(locationId) || !Contains(_sheet.GetLocation(LocationId).VisitorIds, characterId)
                || (_isInParty?.Invoke(characterId) ?? false))
            {
                return false;
            }
            CharacterId = characterId;
            Revision++;
            return true;
        }

        internal bool CloseDetails()
        {
            if (CommandId.Length == 0 && CharacterId.Length == 0 && !DungeonSelectionOpen && !ShopOpen && !QuestBoardOpen)
            {
                return false;
            }
            CommandId = string.Empty;
            CharacterId = string.Empty;
            DungeonSelectionOpen = false;
            SelectedDungeonId = 0;
            ShopOpen = false;
            ShopId = string.Empty;
            QuestBoardOpen = false;
            Revision++;
            return true;
        }

        internal bool LeaveLocation()
        {
            if (LocationId.Length == 0 || CommandId.Length != 0 || CharacterId.Length != 0 || DungeonSelectionOpen || ShopOpen || QuestBoardOpen)
            {
                return false;
            }
            LocationId = string.Empty;
            Revision++;
            return true;
        }

        internal bool Back()
        {
            return CloseDetails() || LeaveLocation();
        }

        internal bool TrySelectDungeon(int id)
        {
            if (!DungeonSelectionOpen || _dungeonSheet == null)
            {
                return false;
            }
            foreach (var dungeon in _dungeonSheet.Dungeons)
            {
                if (dungeon.Id == id)
                {
                    if (SelectedDungeonId != id)
                    {
                        SelectedDungeonId = id;
                        Revision++;
                    }
                    return true;
                }
            }
            return false;
        }

        internal bool CanConfirmDungeon => DungeonSelectionOpen && SelectedDungeonId != 0;

        private bool CanUseLocation(string locationId)
        {
            return LocationId.Length != 0 && LocationId == locationId && CommandId.Length == 0 && CharacterId.Length == 0
                && !DungeonSelectionOpen && !ShopOpen && !QuestBoardOpen;
        }

        private static bool Contains(IReadOnlyList<string> values, string value)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] == value)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
