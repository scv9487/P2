using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using Util.Sheet;

namespace Refactor.Util
{
    public enum ConsumableEffect
    {
        RestoreHealth
    }

    public sealed class EquipmentDefinition
    {
        public string ItemId { get; }
        public string Name { get; }
        public long Price { get; }
        public string WeaponId { get; }
        public string WeaponName { get; }

        internal EquipmentDefinition(LootItemDefinition item, SkillWeaponDefinition weapon)
        {
            ItemId = item.Id;
            Name = item.Name;
            Price = item.Price;
            WeaponId = weapon.Id;
            WeaponName = weapon.Name;
        }
    }

    public sealed class ConsumableDefinition
    {
        public string ItemId { get; }
        public string Name { get; }
        public long Price { get; }
        public ConsumableEffect Effect { get; }
        public float Amount { get; }

        internal ConsumableDefinition(LootItemDefinition item, ConsumableEffect effect, float amount)
        {
            ItemId = item.Id;
            Name = item.Name;
            Price = item.Price;
            Effect = effect;
            Amount = amount;
        }
    }

    // 거래 아이템과 무기 종류를 분리한다. 같은 종류의 다른 아이템도 같은 직업/기본기를 사용할 수 있다.
    public sealed class EquipmentSheet
    {
        private readonly Dictionary<string, EquipmentDefinition> _weapons = new Dictionary<string, EquipmentDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<string, ConsumableDefinition> _consumables = new Dictionary<string, ConsumableDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _starters = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _texts = new Dictionary<string, string>(StringComparer.Ordinal);

        public IReadOnlyList<EquipmentDefinition> Weapons { get; }
        public IReadOnlyList<ConsumableDefinition> Consumables { get; }
        public IReadOnlyDictionary<string, string> StartingWeapons { get; }
        public string PlayerId { get; }
        public LootSheet Items { get; }
        public SkillSheet Skills { get; }

        public EquipmentSheet(string equipment, string consumables, string starters, string texts,
            LootSheet items, SkillSheet skills, CharacterSheet characters)
        {
            Items = items ?? throw new ArgumentNullException(nameof(items));
            Skills = skills ?? throw new ArgumentNullException(nameof(skills));
            if (characters == null)
            {
                throw new ArgumentNullException(nameof(characters));
            }
            PlayerId = characters.PlayerId;
            var weaponList = new List<EquipmentDefinition>();
            foreach (var row in CsvTable.Read(equipment, "EquipmentDefinitionTable", "ItemId", "WeaponId"))
            {
                var item = items.GetItem(Required(row[0]));
                var weapon = skills.GetWeapon(Required(row[1]));
                skills.GetClassForWeapon(weapon.Id);
                var definition = new EquipmentDefinition(item, weapon);
                if (!_weapons.TryAdd(item.Id, definition))
                {
                    throw new FormatException("장비 아이템 ID가 중복됩니다: " + item.Id);
                }
                weaponList.Add(definition);
            }
            Weapons = weaponList.AsReadOnly();
            var consumableList = new List<ConsumableDefinition>();
            foreach (var row in CsvTable.Read(consumables, "ConsumableDefinitionTable", "ItemId", "Effect", "Amount"))
            {
                var item = items.GetItem(Required(row[0]));
                if (!Enum.TryParse(row[1], false, out ConsumableEffect effect)
                    || !Enum.IsDefined(typeof(ConsumableEffect), effect) || row[1] != effect.ToString())
                {
                    throw new FormatException("소모품 효과가 올바르지 않습니다: " + row[1]);
                }
                if (!float.TryParse(row[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float amount)
                    || float.IsNaN(amount) || float.IsInfinity(amount) || amount <= 0f || amount > 1000000f)
                {
                    throw new FormatException("소모품 효과량은 0보다 크고 1000000 이하여야 합니다: " + row[2]);
                }
                var definition = new ConsumableDefinition(item, effect, amount);
                if (_weapons.ContainsKey(item.Id) || !_consumables.TryAdd(item.Id, definition))
                {
                    throw new FormatException("아이템은 장비/소모품 중 하나로만 한 번 정의할 수 있습니다: " + item.Id);
                }
                consumableList.Add(definition);
            }
            Consumables = consumableList.AsReadOnly();
            foreach (var row in CsvTable.Read(starters, "CharacterEquipmentTable", "CharacterId", "ItemId"))
            {
                string characterId = Required(row[0]);
                characters.GetCharacter(characterId);
                var starter = GetWeapon(Required(row[1]));
                if (skills.GetClassForCharacter(characterId).WeaponId != starter.WeaponId)
                {
                    throw new FormatException("인물의 시작 무기와 시작 직업이 일치하지 않습니다: " + characterId);
                }
                if (!_starters.TryAdd(characterId, starter.ItemId))
                {
                    throw new FormatException("인물의 시작 장비가 중복됩니다: " + characterId);
                }
            }
            foreach (var character in characters.Characters)
            {
                if (!_starters.ContainsKey(character.Id))
                {
                    throw new FormatException("인물의 시작 장비가 없습니다: " + character.Id);
                }
            }
            StartingWeapons = new ReadOnlyDictionary<string, string>(_starters);
            foreach (var row in CsvTable.Read(texts, "EquipmentTextTable", "Key", "Value"))
            {
                if (string.IsNullOrWhiteSpace(row[1]) || !_texts.TryAdd(Required(row[0]), row[1]))
                {
                    throw new FormatException("장비 문구가 비어 있거나 중복됩니다: " + row[0]);
                }
            }
        }

        public EquipmentDefinition GetWeapon(string itemId)
        {
            return TryGetWeapon(itemId, out var definition) ? definition
                : throw new KeyNotFoundException("장비 아이템 정의 누락: " + itemId);
        }

        public bool TryGetWeapon(string itemId, out EquipmentDefinition definition)
        {
            definition = null;
            return itemId != null && _weapons.TryGetValue(itemId, out definition);
        }

        public ConsumableDefinition GetConsumable(string itemId)
        {
            return TryGetConsumable(itemId, out var definition) ? definition
                : throw new KeyNotFoundException("소모품 정의 누락: " + itemId);
        }

        public bool TryGetConsumable(string itemId, out ConsumableDefinition definition)
        {
            definition = null;
            return itemId != null && _consumables.TryGetValue(itemId, out definition);
        }

        public EquipmentDefinition GetStartingWeapon(string characterId)
        {
            return GetWeapon(GetStartingWeaponId(characterId));
        }

        public string GetStartingWeaponId(string characterId)
        {
            return characterId != null && _starters.TryGetValue(characterId, out string itemId) ? itemId
                : throw new KeyNotFoundException("인물의 시작 장비 정의 누락: " + characterId);
        }

        public string Text(string key)
        {
            return key != null && _texts.TryGetValue(key, out string value) ? value
                : throw new KeyNotFoundException("장비 문구 누락: " + key);
        }

        private static string Required(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value != value.Trim())
            {
                throw new FormatException("장비 시트의 필수 문자열이 비어 있거나 양끝에 공백이 있습니다.");
            }
            return value;
        }
    }
}
