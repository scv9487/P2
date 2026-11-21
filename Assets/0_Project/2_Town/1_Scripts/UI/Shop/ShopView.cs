using System;
using System.Collections.Generic;
using System.Globalization;
using Refactor.Main;
using Refactor.Main.InputModule;
using Refactor.Util;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using F = Refactor.UI.AdventureUIFactory;

namespace Refactor.UI
{
    // 소지품과 재고는 Reader의 조회 결과다. 선택/수량/거래 요청만 입력 계층에 기록한다.
    public sealed class ShopView : MonoBehaviour
    {
        private Font _font;
        private ShopSheet _sheet;
        private EquipmentSheet _equipment;
        private UIInputStream _input;
        private CanvasGroup _controls;
        private Text _title;
        private Text _merchant;
        private Text _gold;
        private Text _description;
        private Text _playerHeading;
        private Text _merchantHeading;
        private Text _playerEmpty;
        private Text _merchantEmpty;
        private Text _selection;
        private Text _available;
        private Text _price;
        private Text _quantity;
        private Text _total;
        private Text _balance;
        private Text _status;
        private RectTransform _playerContent;
        private RectTransform _merchantContent;
        private Button _decrease;
        private Button _increase;
        private Button _confirm;
        private Button _close;
        private string _context;
        private bool _wasOpen;
        private readonly List<(Button Button, UnityAction Action)> _bindings = new();
        private readonly Dictionary<string, ItemRow> _playerRows = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ItemRow> _merchantRows = new(StringComparer.Ordinal);

        private sealed class ItemRow
        {
            public Button Button;
            public Text Name;
            public Text Quantity;
            public Text Price;
        }

        public static ShopView Create(Transform canvas, Font font)
        {
            if (canvas == null || font == null)
            {
                throw new ArgumentNullException("상점 화면 부모/폰트");
            }
            var root = F.Stretch("ShopRoot", canvas);
            F.Image(root, new Color(0.01f, 0.015f, 0.02f, 0.91f), true);
            var view = root.gameObject.AddComponent<ShopView>();
            view._font = font;
            view.Build();
            root.gameObject.SetActive(false);
            return view;
        }

        private void Build()
        {
            _controls = gameObject.AddComponent<CanvasGroup>();
            var panel = F.Center("ShopPanel", transform, 1120, 700);
            F.Image(panel, F.Panel, true);
            F.Image(F.Rect("TopRule", panel, 0, 0, 1120, 1), F.Gold);
            _title = F.Text("ShopTitleText", panel, _font, 32, 18, 650, 56, 29, F.PaleGold);
            _merchant = F.Text("ShopMerchantText", panel, _font, 32, 76, 680, 34, 18, F.White);
            _gold = F.Text("ShopGoldText", panel, _font, 740, 20, 348, 52, 23, F.PaleGold);
            _gold.alignment = TextAnchor.MiddleRight;
            _description = F.Text("ShopDescriptionText", panel, _font, 32, 115, 1056, 35, 16, F.Muted);
            _playerHeading = F.Text("ShopPlayerHeading", panel, _font, 32, 157, 516, 34, 19, F.PaleGold);
            _merchantHeading = F.Text("ShopMerchantHeading", panel, _font, 572, 157, 516, 34, 19, F.PaleGold);
            _playerContent = F.ScrollContent("ShopPlayerItems", panel, 32, 197, 516, 210);
            _merchantContent = F.ScrollContent("ShopMerchantItems", panel, 572, 197, 516, 210);
            AddScrollbar(_playerContent);
            AddScrollbar(_merchantContent);
            _playerEmpty = F.Text("ShopPlayerEmptyText", _playerContent, _font, 20, 18, 476, 64, 17, F.Muted);
            _merchantEmpty = F.Text("ShopMerchantEmptyText", _merchantContent, _font, 20, 18, 476, 64, 17, F.Muted);

            var selected = F.Rect("ShopSelectionPanel", panel, 32, 427, 1056, 174);
            F.Image(selected, new Color(0.045f, 0.055f, 0.06f));
            _selection = F.Text("ShopSelectionText", selected, _font, 20, 8, 650, 46, 23, F.PaleGold);
            _available = F.Text("ShopAvailableText", selected, _font, 686, 12, 350, 38, 16, F.Muted);
            _available.alignment = TextAnchor.MiddleRight;
            _price = F.Text("ShopUnitPriceText", selected, _font, 20, 66, 250, 44, 18, F.White);
            _decrease = F.Button("ShopDecreaseButton", selected, _font, 300, 66, 44, 44);
            _quantity = F.Text("ShopQuantityText", selected, _font, 350, 66, 92, 44, 20, F.White);
            _quantity.alignment = TextAnchor.MiddleCenter;
            _increase = F.Button("ShopIncreaseButton", selected, _font, 448, 66, 44, 44);
            _total = F.Text("ShopTotalText", selected, _font, 540, 66, 496, 44, 21, F.PaleGold);
            _total.alignment = TextAnchor.MiddleRight;
            _balance = F.Text("ShopBalanceText", selected, _font, 20, 120, 1016, 44, 20, F.PaleGold);
            _status = F.Text("ShopStatusText", panel, _font, 32, 605, 1056, 38, 14, F.Muted);
            _close = F.Button("ShopCloseButton", panel, _font, 32, 650, 180, 40);
            _confirm = F.Button("ShopConfirmButton", panel, _font, 826, 650, 262, 40, true);
        }

        public void Initialize(ShopSheet sheet, UIInputStream input)
        {
            UnbindInputs();
            _sheet = sheet ?? throw new ArgumentNullException(nameof(sheet));
            _input = input ?? throw new ArgumentNullException(nameof(input));
            _title.text = sheet.Text("shop.title");
            _description.text = sheet.Text("shop.description");
            _playerHeading.text = sheet.Text("shop.player_inventory");
            _merchantHeading.text = sheet.Text("shop.merchant_inventory");
            _playerEmpty.text = sheet.Text("shop.player_empty");
            _merchantEmpty.text = sheet.Text("shop.merchant_empty");
            _decrease.GetComponentInChildren<Text>().text = sheet.Text("shop.decrease");
            _increase.GetComponentInChildren<Text>().text = sheet.Text("shop.increase");
            _close.GetComponentInChildren<Text>().text = sheet.Text("shop.close");
            Bind(_decrease, () => _input.AddInput(UIInputType.ShopDecrease, null, _context));
            Bind(_increase, () => _input.AddInput(UIInputType.ShopIncrease, null, _context));
            Bind(_confirm, () => _input.AddInput(UIInputType.ShopConfirm, null, _context));
            Bind(_close, () => _input.AddInput(UIInputType.ShopClose, null, _context));
        }

        private static void AddScrollbar(RectTransform content)
        {
            var scroll = content.GetComponentInParent<ScrollRect>();
            var track = F.Rect("Scrollbar", scroll.transform, 506, 4, 8, 202);
            F.Image(track, new Color(0.12f, 0.13f, 0.135f));
            var handle = F.Stretch("Handle", track);
            var graphic = F.Image(handle, F.Gold, true);
            var bar = track.gameObject.AddComponent<Scrollbar>();
            bar.handleRect = handle;
            bar.targetGraphic = graphic;
            bar.direction = Scrollbar.Direction.BottomToTop;
            scroll.verticalScrollbar = bar;
        }

        public void Render(IShopDataReader shop, bool open, bool locked, bool canWrite, string status)
        {
            bool visible = open && shop != null;
            gameObject.SetActive(visible);
            if (!visible)
            {
                if (_wasOpen)
                {
                    EventSystem.current?.SetSelectedGameObject(null);
                }
                _wasOpen = false;
                return;
            }
            transform.SetAsLastSibling();
            _context = shop.Context;
            _controls.interactable = !locked;
            _controls.blocksRaycasts = true;
            _merchant.text = shop.MerchantName;
            _gold.text = Format("shop.gold_format", shop.PlayerGold);
            RenderRows(shop.PlayerItems, _playerRows, _playerContent, false, shop, locked);
            RenderRows(shop.MerchantItems, _merchantRows, _merchantContent, true, shop, locked);
            _playerEmpty.gameObject.SetActive(shop.PlayerItems.Count == 0);
            _merchantEmpty.gameObject.SetActive(shop.MerchantItems.Count == 0);
            bool selected = !string.IsNullOrEmpty(shop.SelectedItemId);
            string selectedName = string.Empty;
            var selectedRows = shop.IsBuying ? shop.MerchantItems : shop.PlayerItems;
            foreach (var item in selectedRows)
            {
                if (item.ItemId == shop.SelectedItemId)
                {
                    selectedName = item.Name;
                    break;
                }
            }
            _selection.text = !selected ? _sheet.Text("shop.choose_item") : selectedName.Length == 0
                ? _sheet.Text(shop.IsBuying ? "status.no_stock" : "status.not_owned")
                : Format(shop.IsBuying ? "shop.selected_buy_format" : "shop.selected_sell_format", selectedName);
            _available.text = selected ? !shop.IsBuying && _equipment != null
                ? EquipmentFormat("shop.available_sell_format", shop.AvailableQuantity)
                : Format(shop.IsBuying ? "shop.available_buy_format" : "shop.available_sell_format", shop.AvailableQuantity)
                : string.Empty;
            _price.text = Format("shop.unit_price_format", shop.UnitPrice);
            _quantity.text = Format("shop.quantity_format", shop.Quantity);
            _total.text = Format("shop.total_format", shop.TotalPrice);
            _balance.text = selected ? Format(shop.IsBuying ? "shop.buy_balance_format" : "shop.sell_balance_format",
                shop.PlayerGold, shop.TotalPrice, shop.FinalPlayerGold) : Format("shop.gold_format", shop.PlayerGold);
            _balance.color = shop.FinalPlayerGold < 0 ? F.Error : F.PaleGold;
            _decrease.interactable = !locked && selected && shop.Quantity > 1;
            _increase.interactable = !locked && selected && shop.Quantity < shop.AvailableQuantity;
            _confirm.interactable = !locked && canWrite && shop.CanConfirm;
            _close.interactable = !locked;
            _confirm.GetComponentInChildren<Text>().text = _sheet.Text(!selected ? "shop.confirm"
                : shop.IsBuying ? "shop.confirm_buy" : "shop.confirm_sell");
            RenderStatus(shop, canWrite, status, selected);
            if (!_wasOpen)
            {
                _playerContent.anchoredPosition = Vector2.zero;
                _merchantContent.anchoredPosition = Vector2.zero;
                EventSystem.current?.SetSelectedGameObject(_close.gameObject);
            }
            _wasOpen = true;
        }

        private void RenderStatus(IShopDataReader shop, bool canWrite, string status, bool selected)
        {
            bool invalid = !canWrite || (selected && !shop.CanConfirm);
            bool storageError = status == _sheet.Text("status.save_failed") || status == _sheet.Text("status.load_failed");
            _status.color = invalid || storageError ? F.Error : F.Muted;
            if (!string.IsNullOrEmpty(status))
            {
                _status.text = status;
            }
            else if (!canWrite)
            {
                _status.text = _sheet.Text("status.load_failed");
            }
            else if (!selected)
            {
                _status.text = _sheet.Text("shop.choose_hint");
            }
            else if (shop.AvailableQuantity == 0)
            {
                bool equipped = !shop.IsBuying && _equipment != null;
                if (equipped)
                {
                    equipped = false;
                    foreach (var row in shop.PlayerItems)
                    {
                        if (row.ItemId == shop.SelectedItemId && row.IsEquipped)
                        {
                            equipped = true;
                            break;
                        }
                    }
                }
                _status.text = equipped ? _equipment.Text("shop.reserved_notice")
                    : _sheet.Text(shop.IsBuying ? "status.no_stock" : "status.not_owned");
            }
            else if (shop.FinalPlayerGold < 0)
            {
                _status.text = Format("shop.insufficient_format", -shop.FinalPlayerGold);
            }
            else if (!shop.CanConfirm)
            {
                _status.text = _sheet.Text("status.invalid_selection");
            }
            else
            {
                _status.text = _sheet.Text("shop.ready");
            }
        }

        private void RenderRows(IReadOnlyList<ShopItemRow> items, Dictionary<string, ItemRow> rows,
            RectTransform content, bool buying, IShopDataReader shop, bool locked)
        {
            var remaining = new HashSet<string>(rows.Keys, StringComparer.Ordinal);
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                remaining.Remove(item.ItemId);
                if (!rows.TryGetValue(item.ItemId, out var row))
                {
                    row = CreateRow(item.ItemId, content, buying);
                    rows.Add(item.ItemId, row);
                }
                ((RectTransform)row.Button.transform).anchoredPosition = new Vector2(8, -(8 + i * 76));
                row.Name.text = item.Name;
                row.Quantity.text = Format("shop.row_quantity_format", item.Quantity);
                row.Price.text = !buying && item.IsEquipped && _equipment != null
                    ? EquipmentFormat("shop.equipped_price_format", item.AvailableQuantity, item.Price)
                    : Format("shop.unit_price_format", item.Price);
                row.Button.interactable = !locked;
                row.Button.GetComponent<Image>().color = shop.SelectedItemId == item.ItemId && shop.IsBuying == buying
                    ? new Color(0.32f, 0.26f, 0.17f) : new Color(0.12f, 0.13f, 0.135f);
            }
            foreach (string id in remaining)
            {
                DestroyRow(rows[id]);
                rows.Remove(id);
            }
            content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(210, 16 + items.Count * 76));
        }

        private ItemRow CreateRow(string itemId, Transform content, bool buying)
        {
            var button = F.Button((buying ? "ShopBuy_" : "ShopSell_") + itemId, content, _font, 8, 0, 500, 70);
            var name = button.GetComponentInChildren<Text>();
            name.name = "ItemNameText";
            name.rectTransform.anchoredPosition = new Vector2(12, -2);
            name.rectTransform.sizeDelta = new Vector2(295, 38);
            name.alignment = TextAnchor.MiddleLeft;
            var quantity = F.Text("ItemQuantityText", button.transform, _font, 320, 2, 168, 38, 17, F.White);
            quantity.alignment = TextAnchor.MiddleRight;
            var price = F.Text("ItemPriceText", button.transform, _font, 12, 36, 476, 28, 14, F.Muted);
            button.onClick.AddListener(() => _input.AddInput(buying ? UIInputType.ShopSelectBuy : UIInputType.ShopSelectSell,
                itemId, _context));
            return new ItemRow { Button = button, Name = name, Quantity = quantity, Price = price };
        }

        private string Format(string key, params object[] values)
        {
            return string.Format(CultureInfo.InvariantCulture, _sheet.Text(key), values);
        }

        public void ConfigureEquipment(EquipmentSheet equipment)
        {
            _equipment = equipment ?? throw new ArgumentNullException(nameof(equipment));
        }

        private string EquipmentFormat(string key, params object[] values)
        {
            return string.Format(CultureInfo.InvariantCulture, _equipment.Text(key), values);
        }

        private void Bind(Button button, UnityAction action)
        {
            button.onClick.AddListener(action);
            _bindings.Add((button, action));
        }

        private static void DestroyRow(ItemRow row)
        {
            if (row.Button != null)
            {
                row.Button.onClick.RemoveAllListeners();
                row.Button.gameObject.SetActive(false);
                Destroy(row.Button.gameObject);
            }
        }

        public void UnbindInputs()
        {
            foreach (var binding in _bindings)
            {
                if (binding.Button != null)
                {
                    binding.Button.onClick.RemoveListener(binding.Action);
                }
            }
            _bindings.Clear();
            foreach (var row in _playerRows.Values)
            {
                DestroyRow(row);
            }
            foreach (var row in _merchantRows.Values)
            {
                DestroyRow(row);
            }
            _playerRows.Clear();
            _merchantRows.Clear();
            _input = null;
            _context = null;
            _wasOpen = false;
            if (this != null)
            {
                gameObject.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            UnbindInputs();
        }
    }
}
