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
    // 모든 금액/구매 수량의 원본은 정산 모델에 있다. View는 읽은 내역과 입력 의도만 연결한다.
    public sealed class LootSettlementView : MonoBehaviour
    {
        private Font _font;
        private LootSheet _sheet;
        private UIInputStream _input;
        private CanvasGroup _controls;
        private Text _title;
        private Text _description;
        private readonly List<(Text Text, string Key)> _labels = new();
        private Text _total;
        private Text _revenue;
        private Text _balance;
        private Text _purchaseSummary;
        private Text _error;
        private Text _emptyLoot;
        private RectTransform _itemsContent;
        private RectTransform _sharesContent;
        private Button _confirm;
        private Button _cancel;
        private readonly List<(Button Button, UnityAction Action)> _bindings = new();
        private readonly List<ItemRow> _items = new();
        private readonly List<ShareRow> _shares = new();
        private string _context;
        private bool _wasOpen;

        private sealed class ItemRow
        {
            public RectTransform Root;
            public Text Name;
            public Text Acquired;
            public Text Price;
            public Text Purchased;
            public Text External;
            public Text Total;
            public Button Decrease;
            public Button Increase;
        }

        private sealed class ShareRow
        {
            public RectTransform Root;
            public Text Name;
            public Text Percent;
            public Text Gold;
        }

        public static LootSettlementView Create(Transform canvas, Font font)
        {
            if (canvas == null || font == null)
            {
                throw new ArgumentNullException("정산 화면 부모/폰트");
            }
            var root = F.Stretch("LootSettlementRoot", canvas);
            F.Image(root, new Color(0.01f, 0.015f, 0.02f, 0.90f), true);
            var view = root.gameObject.AddComponent<LootSettlementView>();
            view._font = font;
            view.Build();
            root.gameObject.SetActive(false);
            return view;
        }

        private void Build()
        {
            _controls = gameObject.AddComponent<CanvasGroup>();
            var panel = F.Center("LootSettlementPanel", transform, 1080, 700);
            F.Image(panel, F.Panel, true);
            F.Image(F.Rect("TopRule", panel, 0, 0, 1080, 1), F.Gold);
            _title = F.Text("LootSettlementTitle", panel, _font, 32, 20, 1016, 65, 29, F.PaleGold);
            _description = F.Text("LootSettlementDescription", panel, _font, 32, 91, 1016, 48, 16, F.Muted);
            Label(panel, "ItemHeading", "settlement.item", 44, 149, 170, 27);
            Label(panel, "AcquiredHeading", "settlement.acquired", 214, 149, 98, 27);
            Label(panel, "UnitPriceHeading", "settlement.unit_price", 322, 149, 100, 27);
            Label(panel, "PurchaseHeading", "settlement.purchase", 446, 149, 186, 27);
            Label(panel, "ExternalHeading", "settlement.external", 660, 149, 138, 27);
            Label(panel, "ItemTotalHeading", "settlement.item_total", 842, 149, 194, 27);
            _itemsContent = F.ScrollContent("LootItems", panel, 32, 183, 1016, 94);
            _emptyLoot = F.Text("LootEmptyText", _itemsContent, _font, 12, 9, 992, 45, 18, F.Muted);

            var revenuePanel = F.Rect("RevenuePanel", panel, 32, 302, 500, 194);
            F.Image(revenuePanel, new Color(0.045f, 0.055f, 0.06f));
            _total = F.Text("LootTotalText", revenuePanel, _font, 20, 12, 460, 59, 23, F.PaleGold);
            _revenue = F.Text("LootRevenueText", revenuePanel, _font, 20, 83, 460, 94, 16, F.White);
            _revenue.alignment = TextAnchor.UpperLeft;
            var sharesPanel = F.Rect("SharesPanel", panel, 556, 302, 492, 194);
            F.Image(sharesPanel, new Color(0.045f, 0.055f, 0.06f));
            var sharesHeading = F.Text("SharesHeading", sharesPanel, _font, 18, 12, 456, 38, 18, F.PaleGold);
            _labels.Add((sharesHeading, "settlement.shares"));
            _sharesContent = F.ScrollContent("LootShares", sharesPanel, 18, 54, 456, 126);
            _balance = F.Text("LootBalanceText", panel, _font, 32, 515, 1016, 48, 21, F.PaleGold);
            _purchaseSummary = F.Text("LootPurchaseSummaryText", panel, _font, 32, 565, 1016, 29, 15, F.White);
            _error = F.Text("LootErrorText", panel, _font, 32, 600, 1016, 40, 14, F.Muted);
            _confirm = F.Button("LootConfirmButton", panel, _font, 816, 648, 232, 42, true);
            _cancel = F.Button("LootCancelButton", panel, _font, 598, 648, 194, 42);
        }

        private void Label(Transform parent, string name, string key, float x, float y, float width, float height)
        {
            _labels.Add((F.Text(name, parent, _font, x, y, width, height, 13, F.Muted), key));
        }

        public void Initialize(LootSheet sheet, UIInputStream input)
        {
            UnbindInputs();
            _sheet = sheet ?? throw new ArgumentNullException(nameof(sheet));
            _input = input ?? throw new ArgumentNullException(nameof(input));
            _description.text = sheet.Text("settlement.description");
            _emptyLoot.text = sheet.Text("settlement.empty");
            foreach (var label in _labels)
            {
                label.Text.text = sheet.Text(label.Key);
            }
            _cancel.GetComponentInChildren<Text>().text = sheet.Text("settlement.cancel");
            Bind(_confirm, () => _input.AddInput(UIInputType.DungeonConfirmSettlement, null, _context));
            Bind(_cancel, () => _input.AddInput(UIInputType.DungeonCancelSettlement, null, _context));
            _context = null;
        }

        public void Render(ILootSettlementReader settlement, bool open, bool locked, bool canWrite, string error)
        {
            bool visible = open && settlement != null;
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
            if (_context != settlement.Id)
            {
                RebuildRows(settlement);
                _context = settlement.Id;
            }
            _controls.interactable = !locked;
            _controls.blocksRaycasts = true;
            _emptyLoot.gameObject.SetActive(settlement.Items.Count == 0);
            _title.text = _sheet.Text(settlement.IsCommitted ? "settlement.committed_title" : "settlement.title");
            for (int i = 0; i < _items.Count; i++)
            {
                var item = settlement.Items[i];
                var row = _items[i];
                row.Name.text = item.Name;
                row.Acquired.text = Format("settlement.quantity_format", item.Acquired);
                row.Price.text = Format("settlement.gold_format", item.Price);
                row.Purchased.text = Format("settlement.quantity_format", item.Purchased);
                row.External.text = Format("settlement.quantity_format", item.ExternalQuantity);
                row.Total.text = Format("settlement.gold_format", item.Price * item.Acquired);
                // 구매 시도는 예산 부족 때도 가능하다. 확정만 모델의 유효성 결과로 차단한다.
                row.Decrease.interactable = !locked && !settlement.IsCommitted && item.Purchased > 0;
                row.Increase.interactable = !locked && !settlement.IsCommitted && item.Purchased < item.Acquired;
            }
            for (int i = 0; i < _shares.Count; i++)
            {
                var share = settlement.Shares[i];
                var row = _shares[i];
                row.Name.text = share.MemberId == _sheet.PlayerMemberId
                    ? Format("settlement.player_name_format", share.Name) : share.Name;
                row.Percent.text = Format("settlement.share_percent_format", share.Percent);
                row.Gold.text = Format("settlement.share_gold_format", share.Gold);
            }
            _total.text = Format("settlement.total_format", settlement.TotalValue);
            _revenue.text = Format("settlement.revenue_format", settlement.TotalValue - settlement.PurchaseCost, settlement.PurchaseCost);
            _balance.text = Format("settlement.balance_format", settlement.PlayerGold, settlement.PlayerShare,
                settlement.PurchaseCost, settlement.FinalPlayerGold);
            _balance.color = settlement.FinalPlayerGold < 0 ? F.Error : F.PaleGold;
            var purchased = new List<string>();
            foreach (var item in settlement.Items)
            {
                if (item.Purchased > 0)
                {
                    purchased.Add(Format("settlement.item_summary_format", item.Name, item.Purchased));
                }
            }
            _purchaseSummary.text = purchased.Count == 0 ? _sheet.Text("settlement.no_purchase")
                : Format("settlement.purchase_summary_format", string.Join(_sheet.Text("settlement.item_separator"), purchased));
            bool invalid = !canWrite || !settlement.CanConfirm;
            if (!string.IsNullOrEmpty(error))
            {
                _error.text = error;
                _error.color = F.Error;
            }
            else if (!canWrite && !settlement.IsCommitted)
            {
                _error.text = _sheet.Text("status.load_failed");
                _error.color = F.Error;
            }
            else if (settlement.FinalPlayerGold < 0)
            {
                _error.text = Format("settlement.insufficient_format", -settlement.FinalPlayerGold);
                _error.color = F.Error;
            }
            else if (invalid && !settlement.IsCommitted)
            {
                _error.text = _sheet.Text("status.invalid_selection");
                _error.color = F.Error;
            }
            else
            {
                _error.text = _sheet.Text(settlement.IsCommitted ? "settlement.committed_notice" : "settlement.ready");
                _error.color = F.Muted;
            }
            _confirm.GetComponentInChildren<Text>().text = _sheet.Text(settlement.IsCommitted ? "settlement.retry_return" : "settlement.confirm");
            _confirm.interactable = !locked && (settlement.IsCommitted || (canWrite && settlement.CanConfirm));
            _cancel.interactable = !locked && !settlement.IsCommitted;
            if (!_wasOpen)
            {
                EventSystem.current?.SetSelectedGameObject(_cancel.interactable ? _cancel.gameObject : _confirm.gameObject);
            }
            _wasOpen = true;
        }

        private void RebuildRows(ILootSettlementReader settlement)
        {
            ClearRows();
            for (int i = 0; i < settlement.Items.Count; i++)
            {
                var item = settlement.Items[i];
                var root = F.Rect("LootItem_" + item.ItemId, _itemsContent, 0, i * 68, 1016, 68);
                var row = new ItemRow
                {
                    Root = root,
                    Name = F.Text("LootItemText", root, _font, 12, 9, 170, 45, 20, F.White),
                    Acquired = F.Text("LootAcquiredText", root, _font, 182, 9, 98, 45, 18, F.White),
                    Price = F.Text("LootUnitPriceText", root, _font, 290, 9, 100, 45, 18, F.White),
                    Purchased = F.Text("LootPurchaseQuantityText", root, _font, 464, 9, 84, 45, 19, F.PaleGold),
                    External = F.Text("LootExternalQuantityText", root, _font, 628, 9, 138, 45, 18, F.White),
                    Total = F.Text("LootItemTotalText", root, _font, 810, 9, 194, 45, 18, F.White),
                    Decrease = F.Button("LootDecreaseButton", root, _font, 414, 11, 42, 42),
                    Increase = F.Button("LootIncreaseButton", root, _font, 554, 11, 42, 42)
                };
                row.Purchased.alignment = TextAnchor.MiddleCenter;
                row.Decrease.GetComponentInChildren<Text>().text = _sheet.Text("settlement.decrease");
                row.Increase.GetComponentInChildren<Text>().text = _sheet.Text("settlement.increase");
                string itemId = item.ItemId;
                row.Decrease.onClick.AddListener(() => _input.AddInput(UIInputType.DungeonLootDecrease, itemId, _context));
                row.Increase.onClick.AddListener(() => _input.AddInput(UIInputType.DungeonLootIncrease, itemId, _context));
                _items.Add(row);
            }
            _itemsContent.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(94, settlement.Items.Count * 68));
            _itemsContent.anchoredPosition = Vector2.zero;
            for (int i = 0; i < settlement.Shares.Count; i++)
            {
                var share = settlement.Shares[i];
                var root = F.Rect("Share_" + share.MemberId, _sharesContent, 0, i * 43, 456, 43);
                var row = new ShareRow
                {
                    Root = root,
                    Name = F.Text("Name", root, _font, 0, 0, 212, 40, 17, F.White),
                    Percent = F.Text("Ratio", root, _font, 218, 0, 74, 40, 17, F.Muted),
                    Gold = F.Text("Amount", root, _font, 302, 0, 154, 40, 17, F.PaleGold)
                };
                row.Gold.alignment = TextAnchor.MiddleRight;
                _shares.Add(row);
            }
            _sharesContent.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(126, settlement.Shares.Count * 43));
            _sharesContent.anchoredPosition = Vector2.zero;
        }

        private string Format(string key, params object[] values)
        {
            return string.Format(CultureInfo.InvariantCulture, _sheet.Text(key), values);
        }

        private void Bind(Button button, UnityAction action)
        {
            button.onClick.AddListener(action);
            _bindings.Add((button, action));
        }

        private void ClearRows()
        {
            foreach (var row in _items)
            {
                if (row.Decrease != null)
                {
                    row.Decrease.onClick.RemoveAllListeners();
                }
                if (row.Increase != null)
                {
                    row.Increase.onClick.RemoveAllListeners();
                }
                if (row.Root != null)
                {
                    row.Root.gameObject.SetActive(false);
                    Destroy(row.Root.gameObject);
                }
            }
            _items.Clear();
            foreach (var row in _shares)
            {
                if (row.Root != null)
                {
                    row.Root.gameObject.SetActive(false);
                    Destroy(row.Root.gameObject);
                }
            }
            _shares.Clear();
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
            ClearRows();
            _input = null;
            _context = null;
            _wasOpen = false;
            // 씬 해제 순서에 따라 부모의 정리 호출보다 이 View가 먼저 파괴될 수 있다.
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
