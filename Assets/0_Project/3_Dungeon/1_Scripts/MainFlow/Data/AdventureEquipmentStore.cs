using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using Refactor.Util;

namespace Refactor.Main
{
    public enum ItemActionResult
    {
        Saved,
        AlreadyApplied,
        NotOwned,
        AlreadyEquipped,
        InvalidItem,
        InvalidRequest,
        SaveFailed,
        LoadBlocked
    }

    public sealed partial class AdventureProgressDataContainer
    {
        private readonly EquipmentSheet _equipmentSheet;
        private readonly HashSet<string> _itemActions = new(StringComparer.Ordinal);
        internal bool HasItemAction(string id) => _itemActions.Contains(id);
        internal void RecordItemAction(string id) => _itemActions.Add(id);

        public int GetAvailableItemCount(string itemId)
        {
            if (itemId == null || !_items.TryGetValue(itemId, out int owned))
            {
                return 0;
            }
            bool equipped = _characters.TryGetValue(_sheet.PlayerMemberId, out var hero) && hero.EquippedItemId == itemId;
            return Math.Max(0, owned - (equipped ? 1 : 0));
        }

        private void InitializeEquipment()
        {
            if (_equipmentSheet == null)
            {
                return;
            }
            foreach (string id in new List<string>(_characters.Keys))
            {
                var item = _equipmentSheet.GetStartingWeapon(id);
                SetCharacter(_characters[id].WithEquipment(item.ItemId, item.WeaponId));
                // 동료 장비는 각 인물의 소유이며 플레이어 가방에 넣지 않는다.
                if (id == _sheet.PlayerMemberId && (!_items.TryGetValue(item.ItemId, out int owned) || owned < 1))
                {
                    SetItem(item.ItemId, 1);
                }
            }
        }

        private void CopyEquipmentFrom(AdventureProgressDataContainer source)
        {
            _itemActions.Clear();
            _itemActions.UnionWith(source._itemActions);
        }

        private void WriteEquipmentSave(AdventureSaveData dto)
        {
            var ids = new List<string>();
            var items = new List<string>();
            foreach (string id in dto.CharacterIds)
            {
                var character = _characters[id];
                if (character.EquippedItemId == null)
                {
                    continue;
                }
                ids.Add(id);
                items.Add(character.EquippedItemId);
            }
            dto.EquipmentCharacterIds = ids.ToArray();
            dto.EquippedItemIds = items.ToArray();
            dto.ItemActionIds = new string[_itemActions.Count];
            _itemActions.CopyTo(dto.ItemActionIds);
            Array.Sort(dto.ItemActionIds, StringComparer.Ordinal);
        }

        private void ReadEquipmentSave(AdventureSaveData dto)
        {
            if (dto.EquipmentCharacterIds.Length == 0)
            {
                InitializeEquipment();
                SeedNewWeaponStock();
                return;
            }
            for (int index = 0; index < dto.EquipmentCharacterIds.Length; index++)
            {
                string id = dto.EquipmentCharacterIds[index];
                string itemId = dto.EquippedItemIds[index];
                SetCharacter(_characters[id].WithEquipment(itemId, _equipmentSheet?.GetWeapon(itemId).WeaponId));
            }
            _itemActions.UnionWith(dto.ItemActionIds);
        }

        // v1~5 또는 장비 시트 없이 보존한 미초기화 저장에만 신규 무기 재고를 더한다.
        // 포션, 판매품과 이미 존재하는 무기 수량은 바꾸지 않으며 v6 초기화 후 품절도 유지한다.
        private void SeedNewWeaponStock()
        {
            if (_equipmentSheet == null || _shops == null)
            {
                return;
            }
            foreach (var merchant in _shops.Merchants)
            {
                var stock = GetMerchantStock(merchant.Id);
                foreach (var pair in merchant.InitialStock)
                {
                    if (_equipmentSheet.TryGetWeapon(pair.Key, out _) && !stock.ContainsKey(pair.Key))
                    {
                        SetMerchantItem(merchant.Id, pair.Key, pair.Value);
                    }
                }
            }
        }
    }

    public sealed partial class AdventureProgressStore
    {
        private readonly EquipmentSheet _equipment;

        public ItemActionResult TryEquipItem(string itemId, string requestId, int expectedRevision)
        {
            var validation = ValidateItemRequest(requestId, expectedRevision);
            if (validation != ItemActionResult.Saved)
            {
                return validation;
            }
            if (!_equipment.TryGetWeapon(itemId, out var definition))
            {
                return ItemActionResult.InvalidItem;
            }
            var hero = _progress.GetCharacter(_sheet.PlayerMemberId);
            if (hero.EquippedItemId == itemId)
            {
                return ItemActionResult.AlreadyEquipped;
            }
            if (!_progress.Items.TryGetValue(itemId, out int owned) || owned < 1)
            {
                return ItemActionResult.NotOwned;
            }
            var candidate = _progress.Copy();
            candidate.SetCharacter(hero.WithEquipment(itemId, definition.WeaponId));
            return SaveItemChange(candidate, requestId);
        }

        // 던전 HP는 탐험의 임시 상태다. MainFlow가 회복 가능 여부를 검사하고,
        // 소비 저장이 Saved일 때에만 회복한다. AlreadyApplied는 재회복하지 않는다.
        public ItemActionResult TryConsumeItem(string itemId, string requestId, int expectedRevision)
        {
            var validation = ValidateItemRequest(requestId, expectedRevision);
            if (validation != ItemActionResult.Saved)
            {
                return validation;
            }
            if (!_equipment.TryGetConsumable(itemId, out _))
            {
                return ItemActionResult.InvalidItem;
            }
            if (_progress.GetAvailableItemCount(itemId) < 1)
            {
                return ItemActionResult.NotOwned;
            }
            var candidate = _progress.Copy();
            candidate.SetItem(itemId, candidate.Items[itemId] - 1);
            return SaveItemChange(candidate, requestId);
        }

        private ItemActionResult ValidateItemRequest(string requestId, int expectedRevision)
        {
            if (!CanWrite)
            {
                return ItemActionResult.LoadBlocked;
            }
            if (_equipment == null || !AdventureSave.IsValidSettlementId(requestId))
            {
                return ItemActionResult.InvalidRequest;
            }
            if (_progress.HasItemAction(requestId))
            {
                return ItemActionResult.AlreadyApplied;
            }
            return expectedRevision == _progress.Revision ? ItemActionResult.Saved : ItemActionResult.InvalidRequest;
        }

        private ItemActionResult SaveItemChange(AdventureProgressDataContainer candidate, string requestId)
        {
            candidate.RecordItemAction(requestId);
            try
            {
                AdventureSave.Save(_path, candidate.ToSaveData(), _sheet, _shops, _characters, _quests, _skills, _equipment);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException
                || exception is SecurityException || exception is ArgumentException || exception is NotSupportedException)
            {
                return ItemActionResult.SaveFailed;
            }
            _progress.Replace(candidate);
            return ItemActionResult.Saved;
        }
    }
}
