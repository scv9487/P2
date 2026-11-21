using System;
using System.Collections.Generic;
using Refactor.Util;

namespace Refactor
{
    public static partial class AdventureSave
    {
        private static bool HasValidEquipmentData(AdventureSaveData dto, LootSheet items, EquipmentSheet equipment)
        {
            if (dto.EquipmentCharacterIds == null || dto.EquippedItemIds == null || dto.ItemActionIds == null
                || dto.EquipmentCharacterIds.Length != dto.EquippedItemIds.Length)
            {
                return false;
            }
            if (dto.EquipmentCharacterIds.Length == 0)
            {
                return dto.ItemActionIds.Length == 0;
            }
            if (dto.EquipmentCharacterIds.Length != dto.CharacterIds.Length)
            {
                return false;
            }
            var characters = new HashSet<string>(dto.CharacterIds, StringComparer.Ordinal);
            var found = new HashSet<string>(StringComparer.Ordinal);
            var knownItems = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in items.Items)
            {
                knownItems.Add(item.Id);
            }
            for (int index = 0; index < dto.EquipmentCharacterIds.Length; index++)
            {
                string character = dto.EquipmentCharacterIds[index];
                string item = dto.EquippedItemIds[index];
                if (!IsValidSettlementId(character) || !characters.Contains(character) || !found.Add(character)
                    || !IsValidSettlementId(item) || !knownItems.Contains(item)
                    || equipment != null && !equipment.TryGetWeapon(item, out _))
                {
                    return false;
                }
                if (character == items.PlayerMemberId)
                {
                    int ownedIndex = Array.IndexOf(dto.ItemIds, item);
                    if (ownedIndex < 0 || dto.ItemCounts[ownedIndex] < 1)
                    {
                        return false;
                    }
                }
            }
            var actions = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in dto.ItemActionIds)
            {
                if (!IsValidSettlementId(id) || !actions.Add(id))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
