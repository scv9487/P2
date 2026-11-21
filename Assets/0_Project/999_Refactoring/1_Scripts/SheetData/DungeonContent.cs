using System;
using UnityEngine;

namespace Refactor.Util
{
    [CreateAssetMenu(menuName = "P2/Dungeon Content", fileName = "DungeonContent")]
    public sealed class DungeonContent : ScriptableObject
    {
        public TextAsset Dungeons;
        public TextAsset Nodes;
        public TextAsset Party;
        public TextAsset Settings;
        public TextAsset Texts;
        public TownContent Town;
        public LootContent Loot;
        public ShopContent Shop;
        public CharacterContent Characters;
        public QuestContent Quests;
        public SkillContent Skills;
        public EquipmentContent Equipment;
        public Sprite Background;

        public EquipmentSheet LoadEquipment()
        {
            if (Equipment == null)
            {
                throw new InvalidOperationException("DungeonContent의 EquipmentContent 연결을 확인하세요.");
            }
            return Equipment.Load(LoadLoot(), LoadSkills(), LoadCharacters());
        }

        public SkillSheet LoadSkills()
        {
            if (Skills == null)
            {
                throw new InvalidOperationException("DungeonContent의 SkillContent 연결을 확인하세요.");
            }
            var sheet = Skills.Load();
            foreach (var character in LoadCharacters().Characters)
            {
                sheet.GetClassForCharacter(character.Id);
            }
            return sheet;
        }

        public QuestSheet LoadQuests()
        {
            if (Quests == null)
            {
                throw new InvalidOperationException("DungeonContent의 QuestContent 연결을 확인하세요.");
            }
            var dungeon = Load();
            if (Loot == null)
            {
                throw new InvalidOperationException("DungeonContent의 LootContent 연결을 확인하세요.");
            }
            return Quests.Load(dungeon, Loot.Load(dungeon));
        }

        public CharacterSheet LoadCharacters()
        {
            if (Characters == null)
            {
                throw new InvalidOperationException("DungeonContent의 CharacterContent 연결을 확인하세요.");
            }
            var sheet = Characters.Load(Town.Load());
            foreach (var character in sheet.Characters)
            {
                if (Town.Portraits == null || character.PortraitIndex >= Town.Portraits.Length || Town.Portraits[character.PortraitIndex] == null)
                {
                    throw new InvalidOperationException("인물 초상화 연결 누락: " + character.Id);
                }
            }
            return sheet;
        }

        public ShopSheet LoadShop()
        {
            if (Shop == null)
            {
                throw new InvalidOperationException("DungeonContent의 ShopContent 연결을 확인하세요.");
            }
            return Shop.Load(LoadLoot(), Town.Load());
        }

        public LootSheet LoadLoot()
        {
            if (Loot == null)
            {
                throw new InvalidOperationException("DungeonContent의 LootContent 연결을 확인하세요.");
            }
            return Loot.Load(Load());
        }

        public DungeonSheet Load()
        {
            if (Dungeons == null || Nodes == null || Party == null || Settings == null || Texts == null || Town == null)
            {
                throw new InvalidOperationException("DungeonContent의 다섯 시트와 TownContent 연결을 확인하세요.");
            }
            var sheet = new DungeonSheet(Dungeons.text, Nodes.text, Party.text, Settings.text, Texts.text, Town.Load());
            foreach (var member in sheet.Party)
            {
                if (Town.Portraits == null || member.PortraitIndex >= Town.Portraits.Length || Town.Portraits[member.PortraitIndex] == null)
                {
                    throw new InvalidOperationException("동료 초상화 연결 누락: " + member.Id);
                }
            }
            return sheet;
        }
    }
}
