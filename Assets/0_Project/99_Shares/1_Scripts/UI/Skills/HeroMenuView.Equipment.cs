using System;
using System.Globalization;
using Refactor.Main;
using Refactor.Util;
using UnityEngine;
using UnityEngine.UI;
using F = Refactor.UI.AdventureUIFactory;

namespace Refactor.UI
{
    public sealed partial class HeroMenuView
    {
        private EquipmentSheet _equipment;
        private string _selectedItemId;
        private Text _bagHealth;
        private Text _itemTitle;
        private Text _itemQuantity;
        private Text _itemDescription;
        private Text _itemConditions;
        private Button _bagEquip;
        private Button _bagUse;

        private void BuildItemDetails()
        {
            _bagHealth = F.Text("BagHealthText", _inventoryPanel, _font, 670, 105, 386, 28, 16, F.White);
            _bagHealth.alignment = TextAnchor.MiddleRight;
            var detail = F.Rect("BagItemDetailPanel", _inventoryPanel, 632, 158, 424, 360);
            F.Image(detail, new Color(0.035f, 0.047f, 0.05f));
            _itemTitle = F.Text("BagSelectedItemText", detail, _font, 20, 12, 384, 58, 24, F.PaleGold);
            _itemQuantity = F.Text("BagSelectedQuantityText", detail, _font, 20, 78, 384, 48, 16, F.White);
            _itemDescription = F.Text("BagItemDescriptionText", detail, _font, 20, 142, 384, 104, 17, F.White);
            _itemDescription.alignment = TextAnchor.UpperLeft;
            _itemConditions = F.Text("BagItemConditionsText", detail, _font, 20, 248, 384, 96, 14, F.Muted);
            _itemConditions.alignment = TextAnchor.UpperLeft;
            _bagEquip = F.Button("BagEquipButton", _inventoryPanel, _font, 576, 570, 232, 38, true);
            _bagUse = F.Button("BagUseButton", _inventoryPanel, _font, 576, 570, 232, 38, true);
            _bagEquip.gameObject.SetActive(false);
            _bagUse.gameObject.SetActive(false);
        }

        public void ConfigureEquipment(EquipmentSheet equipment)
        {
            _equipment = equipment ?? throw new ArgumentNullException(nameof(equipment));
            _bagEquip.GetComponentInChildren<Text>().text = equipment.Text("bag.equip");
            _bagUse.GetComponentInChildren<Text>().text = equipment.Text("bag.use");
        }

        private void RenderItemDetails(IAdventureProgressReader progress, IHeroMenuReader menu, bool locked, bool canWrite)
        {
            if (_equipment == null)
            {
                _bagHealth.text = _itemTitle.text = _itemQuantity.text = _itemDescription.text = _itemConditions.text = string.Empty;
                _bagEquip.gameObject.SetActive(false);
                _bagUse.gameObject.SetActive(false);
                return;
            }
            var hero = progress.GetCharacter(_characters.PlayerId);
            bool selected = !string.IsNullOrEmpty(_selectedItemId);
            int quantity = selected && progress.Items.TryGetValue(_selectedItemId, out int count) ? count : 0;
            bool weapon = _equipment.TryGetWeapon(_selectedItemId, out var equipment);
            bool consumable = _equipment.TryGetConsumable(_selectedItemId, out var potion);
            bool equipped = selected && hero.EquippedItemId == _selectedItemId;
            bool alive = menu.PlayerHealth > 0;
            bool injured = menu.PlayerHealth < menu.PlayerMaxHealth;
            _bagHealth.text = EquipmentFormat("bag.health_format", menu.PlayerHealth, menu.PlayerMaxHealth);
            _itemTitle.text = selected ? _loot.GetItem(_selectedItemId).Name : _equipment.Text("bag.choose_item");
            _itemQuantity.text = selected ? EquipmentFormat("bag.owned_price_format", quantity, _loot.GetItem(_selectedItemId).Price)
                : string.Empty;
            _bagEquip.gameObject.SetActive(weapon);
            _bagUse.gameObject.SetActive(consumable);
            _bagEquip.interactable = !locked && canWrite && quantity > 0 && alive && menu.CanActWithItems && !equipped;
            _bagUse.interactable = !locked && canWrite && quantity > 0 && alive && injured && menu.CanActWithItems;
            _bagEquip.GetComponentInChildren<Text>().text = _equipment.Text(equipped ? "bag.equipped" : "bag.equip");
            if (weapon)
            {
                var job = _sheet.GetClassForWeapon(equipment.WeaponId);
                _itemDescription.text = EquipmentFormat("bag.weapon_description_format", equipment.WeaponName, job.Name);
                _itemConditions.text = _equipment.Text("bag.weapon_retained") + "\n"
                    + _equipment.Text(menu.InCombat ? "bag.combat_action" : "bag.equip_free");
            }
            else if (consumable)
            {
                float healing = potion.Amount;
                if (hero.HasSkill("SKILL_InnerGrove"))
                {
                    healing *= 1f + _sheet.GetSkill("SKILL_InnerGrove").Values[0] / 100f;
                }
                float restored = Mathf.Min(healing, Mathf.Max(0, menu.PlayerMaxHealth - menu.PlayerHealth));
                _itemDescription.text = EquipmentFormat("bag.potion_description_format", potion.Amount, restored);
                _itemConditions.text = _equipment.Text("bag.potion_self_only") + "\n"
                    + _equipment.Text(menu.InCombat ? "bag.combat_action" : "bag.potion_free");
            }
            else
            {
                _itemDescription.text = _equipment.Text(selected ? "bag.material_description" : "bag.choose_hint");
                _itemConditions.text = string.Empty;
            }
            string reason = !canWrite ? "status.load_failed" : !selected ? "bag.choose_hint"
                : quantity <= 0 ? "status.not_owned" : !weapon && !consumable ? "bag.material_description"
                : !alive ? "status.not_alive" : !menu.CanActWithItems ? "status.turn_required"
                : equipped ? "status.already_equipped" : consumable && !injured ? "status.full_health"
                : weapon ? "bag.equip_ready" : "bag.use_ready";
            _bagNote.text = !string.IsNullOrEmpty(menu.Status) ? menu.Status : _equipment.Text(reason);
            _bagNote.color = !canWrite || menu.Status == _equipment.Text("status.save_failed")
                || menu.Status == _equipment.Text("status.invalid_request") ? F.Error : F.Muted;
        }

        private string EquipmentFormat(string key, params object[] values)
        {
            return string.Format(CultureInfo.InvariantCulture, _equipment.Text(key), values);
        }

        private void ClearSkillLists()
        {
            foreach (var row in _skillRows)
            {
                for (int i = _bindings.Count - 1; i >= 0; i--)
                {
                    if (_bindings[i].Button == row.Button)
                    {
                        if (row.Button != null)
                        {
                            row.Button.onClick.RemoveListener(_bindings[i].Action);
                        }
                        _bindings.RemoveAt(i);
                    }
                }
            }
            _skillRows.Clear();
            foreach (var item in _skillGenerated)
            {
                _generated.Remove(item);
                if (item != null)
                {
                    item.SetActive(false);
                    Destroy(item);
                }
            }
            _skillGenerated.Clear();
        }
    }
}
