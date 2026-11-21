using System.Globalization;
using Refactor.Main.Event;
using Refactor.Main.InputModule;
using Refactor.Util;

namespace Refactor.Main
{
    public sealed partial class MainFlow
    {
        private EquipmentSheet _equipmentSheet;
        public EquipmentSheet EquipmentSheet => _equipmentSheet;

        private void RefreshInventoryState()
        {
            if (_characterSheet == null || _adventureStore == null)
            {
                return;
            }
            if (_dungeonData != null)
            {
                var player = _dungeonData.PlayerUnit;
                _heroMenu.SetItemState(player?.Hp ?? 0f, player?.MaxHp ?? 0f,
                    _dungeonData.CanActWithInventory, _dungeonData.InCombat);
                return;
            }
            var hero = _adventureStore.Progress.GetCharacter(_characterSheet.PlayerId);
            float maxHealth = CombatSkillPassives.CalculateStats(new CombatStatCalculator(_characterSheet.Combat),
                hero.Stats, _skillSheet, hero).MaxHealth;
            // 기존 마을/새 탐험의 완전 회복 정책은 유지한다. 가득 찬 상태에서 포션은 소비하지 않는다.
            _heroMenu.SetItemState(maxHealth, maxHealth, true, false);
        }

        private void HandleInventoryAction(HeroMenuActionRequested request)
        {
            if (_equipmentSheet == null)
            {
                return;
            }
            var progress = _adventureStore.Progress;
            if (request.Action == UIInputType.InventorySelectItem)
            {
                if (request.TargetId != null && progress.Items.TryGetValue(request.TargetId, out int count) && count > 0)
                {
                    _heroMenu.SelectItem(request.TargetId, progress.Revision);
                }
                return;
            }
            if ((request.Action != UIInputType.InventoryEquipItem && request.Action != UIInputType.InventoryUseItem)
                || string.IsNullOrEmpty(request.TargetId) || request.TargetId != _heroMenu.SelectedItemId)
            {
                return;
            }
            RefreshInventoryState();
            if (!_heroMenu.CanActWithItems)
            {
                _heroMenu.ShowError(_equipmentSheet.Text(_heroMenu.PlayerHealth <= 0f
                    ? "status.not_alive" : "status.turn_required"));
                return;
            }
            float healing = 0f;
            bool equipping = request.Action == UIInputType.InventoryEquipItem;
            if (!equipping)
            {
                if (!_equipmentSheet.TryGetConsumable(request.TargetId, out var consumable))
                {
                    _heroMenu.ShowError(_equipmentSheet.Text("status.invalid_item"));
                    return;
                }
                if (_heroMenu.PlayerHealth >= _heroMenu.PlayerMaxHealth)
                {
                    _heroMenu.ShowError(_equipmentSheet.Text("status.full_health"));
                    return;
                }
                healing = consumable.Amount;
            }
            var result = equipping
                ? _adventureStore.TryEquipItem(request.TargetId, request.Context, _heroMenu.SourceRevision)
                : _adventureStore.TryConsumeItem(request.TargetId, request.Context, _heroMenu.SourceRevision);
            if (result == ItemActionResult.Saved)
            {
                bool combat = _dungeonData?.InCombat ?? false;
                float before = _heroMenu.PlayerHealth;
                if (equipping)
                {
                    _dungeonData?.SynchronizeParty();
                }
                _dungeonData?.CompleteInventoryAction(healing);
                RefreshInventoryState();
                string itemName = _lootSheet.GetItem(request.TargetId).Name;
                string status = equipping ? string.Format(_equipmentSheet.Text("status.equipped_format"), itemName)
                    : string.Format(CultureInfo.InvariantCulture, _equipmentSheet.Text("status.used_format"), itemName,
                        _heroMenu.PlayerHealth - before);
                _heroMenu.Advance(progress.Revision, status);
                if (combat)
                {
                    _heroMenu.Close();
                }
                return;
            }
            if (result == ItemActionResult.AlreadyApplied)
            {
                // 영속 요청의 재전송은 회복이나 행동 게이지 차감을 다시 실행하지 않는다.
                _heroMenu.Advance(progress.Revision, string.Empty);
                return;
            }
            string key = result switch
            {
                ItemActionResult.NotOwned => "status.not_owned",
                ItemActionResult.AlreadyEquipped => "status.already_equipped",
                ItemActionResult.InvalidItem => "status.invalid_item",
                ItemActionResult.SaveFailed => "status.save_failed",
                ItemActionResult.LoadBlocked => "status.load_failed",
                _ => "status.invalid_request"
            };
            _heroMenu.ShowError(_equipmentSheet.Text(key));
        }
    }
}
