using System;
using System.Collections.Generic;
using System.Globalization;
using Refactor.Main;
using Refactor.Util;
using UnityEngine;
using UnityEngine.UI;
using F = Refactor.UI.AdventureUIFactory;

namespace Refactor.UI
{
    // 마을 전경의 보유 내역. 파일 로드/저장이나 재화 변경은 수행하지 않는다.
    public sealed class AdventureInventoryView : MonoBehaviour
    {
        private LootSheet _sheet;
        private EquipmentSheet _equipment;
        private Text _gold;
        private Text _items;

        public static AdventureInventoryView Create(Transform overviewFrame, Font font)
        {
            var root = F.Rect("AdventureInventoryRoot", overviewFrame, 650, 39, 366, 79);
            F.Image(root, new Color(0.035f, 0.045f, 0.05f, 0.96f));
            var view = root.gameObject.AddComponent<AdventureInventoryView>();
            view._gold = F.Text("InventoryGoldText", root, font, 18, 2, 330, 33, 20, F.PaleGold);
            view._items = F.Text("InventoryItemsText", root, font, 18, 35, 330, 42, 14, F.White);
            return view;
        }

        public void Initialize(LootSheet sheet)
        {
            _sheet = sheet ?? throw new ArgumentNullException(nameof(sheet));
        }

        public void Render(IAdventureProgressReader progress)
        {
            _gold.text = string.Format(CultureInfo.InvariantCulture, _sheet.Text("inventory.gold_format"), progress.PlayerGold);
            var items = new List<string>();
            int weaponTypes = 0;
            foreach (var entry in progress.Items)
            {
                if (entry.Value > 0)
                {
                    if (_equipment != null && _equipment.TryGetWeapon(entry.Key, out _))
                    {
                        weaponTypes++;
                        continue;
                    }
                    items.Add(string.Format(CultureInfo.InvariantCulture, _sheet.Text("inventory.item_format"),
                        _sheet.GetItem(entry.Key).Name, entry.Value));
                }
            }
            if (weaponTypes > 0)
            {
                items.Add(string.Format(CultureInfo.InvariantCulture, _equipment.Text("inventory.weapon_count_format"), weaponTypes));
            }
            _items.text = items.Count == 0 ? _sheet.Text("inventory.empty") : string.Join(_sheet.Text("inventory.item_separator"), items);
            if (_equipment != null && _items.preferredHeight > _items.rectTransform.rect.height)
            {
                _items.text = string.Format(CultureInfo.InvariantCulture, _equipment.Text("inventory.summary_format"),
                    items.Count - (weaponTypes > 0 ? 1 : 0) + weaponTypes);
            }
        }

        public void ConfigureEquipment(EquipmentSheet equipment)
        {
            _equipment = equipment ?? throw new ArgumentNullException(nameof(equipment));
        }
    }
}
