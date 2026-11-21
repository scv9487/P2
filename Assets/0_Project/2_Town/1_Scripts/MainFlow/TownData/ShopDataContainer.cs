using System;
using System.Collections.Generic;
using Refactor.Util;

namespace Refactor.Main
{
    public enum ShopTradeResult
    {
        Saved,
        AlreadyCommitted,
        InsufficientGold,
        InsufficientStock,
        InvalidSelection,
        SaveFailed,
        LoadBlocked
    }

    public sealed class ShopItemRow
    {
        public string ItemId { get; }
        public string Name { get; }
        public long Price { get; }
        public int Quantity { get; }
        public int AvailableQuantity { get; }
        public bool IsEquipped { get; }

        internal ShopItemRow(LootItemDefinition item, int quantity, int availableQuantity, bool isEquipped)
        {
            ItemId = item.Id;
            Name = item.Name;
            Price = item.Price;
            Quantity = quantity;
            AvailableQuantity = availableQuantity;
            IsEquipped = isEquipped;
        }
    }

    public interface IShopDataReader
    {
        string MerchantId { get; }
        string MerchantName { get; }
        string Context { get; }
        IReadOnlyList<ShopItemRow> PlayerItems { get; }
        IReadOnlyList<ShopItemRow> MerchantItems { get; }
        string SelectedItemId { get; }
        bool IsBuying { get; }
        int Quantity { get; }
        int AvailableQuantity { get; }
        long UnitPrice { get; }
        long TotalPrice { get; }
        long PlayerGold { get; }
        long FinalPlayerGold { get; }
        bool CanConfirm { get; }
        int Revision { get; }
    }

    // 표시 선택만 소유한다. 실제 돈/재고는 Store의 저장 성공 뒤 DataContainer에서 바뀐다.
    public sealed class ShopDataContainer : IShopDataReader
    {
        private readonly ShopSheet _sheet;
        private readonly IAdventureProgressReader _progress;
        private int _sourceRevision;
        public string MerchantId { get; }
        public string MerchantName { get; }
        public string Context { get; private set; }
        public IReadOnlyList<ShopItemRow> PlayerItems { get; private set; }
        public IReadOnlyList<ShopItemRow> MerchantItems { get; private set; }
        public string SelectedItemId { get; private set; } = string.Empty;
        public bool IsBuying { get; private set; }
        public int Quantity { get; private set; }
        public int AvailableQuantity { get; private set; }
        public long UnitPrice { get; private set; }
        public long TotalPrice => UnitPrice * Quantity;
        public long PlayerGold { get; private set; }
        private decimal FinalGold => (decimal)PlayerGold + (IsBuying ? -TotalPrice : TotalPrice);
        public long FinalPlayerGold => FinalGold > long.MaxValue ? long.MaxValue : (long)FinalGold;
        public bool CanConfirm => ShopTrade.Evaluate(_sheet, _progress, _sourceRevision, MerchantId, SelectedItemId, IsBuying, Quantity) == ShopTradeResult.Saved;
        public int Revision { get; private set; }
        private bool IsCurrent => _progress is AdventureProgressDataContainer data && data.Revision == _sourceRevision;

        public ShopDataContainer(ShopSheet sheet, string merchantId, IAdventureProgressReader progress)
        {
            _sheet = sheet ?? throw new ArgumentNullException(nameof(sheet));
            _progress = progress ?? throw new ArgumentNullException(nameof(progress));
            var merchant = sheet.GetMerchant(merchantId);
            MerchantId = merchant.Id;
            MerchantName = merchant.Name;
            ResetSelection();
        }

        public bool TrySelectItem(string itemId, bool buying)
        {
            if (!IsCurrent || string.IsNullOrEmpty(itemId) || (SelectedItemId == itemId && IsBuying == buying))
            {
                return false;
            }
            IReadOnlyDictionary<string, int> source = buying ? _progress.GetMerchantStock(MerchantId) : _progress.Items;
            if (!source.TryGetValue(itemId, out int available) || available <= 0)
            {
                return false;
            }
            if (!buying)
            {
                available = _progress.GetAvailableItemCount(itemId);
            }
            SelectedItemId = itemId;
            IsBuying = buying;
            Quantity = 1;
            AvailableQuantity = available;
            UnitPrice = _sheet.Items.GetItem(itemId).Price;
            Changed();
            return true;
        }

        public bool TryAdjustQuantity(int delta)
        {
            long next = (long)Quantity + delta;
            return next >= 1 && next <= int.MaxValue && TrySetQuantity((int)next);
        }

        public bool TrySetQuantity(int quantity)
        {
            if (!IsCurrent || SelectedItemId.Length == 0 || quantity < 1 || quantity > AvailableQuantity || quantity == Quantity)
            {
                return false;
            }
            Quantity = quantity;
            Changed();
            return true;
        }

        public ShopTrade CreateTrade()
        {
            return IsCurrent && SelectedItemId.Length > 0 && Quantity > 0
                ? new ShopTrade(this, _sheet, _progress, _sourceRevision, Context, MerchantId, SelectedItemId, IsBuying, Quantity) : null;
        }

        public void ResetSelection()
        {
            SelectedItemId = string.Empty;
            IsBuying = false;
            Quantity = 0;
            AvailableQuantity = 0;
            UnitPrice = 0;
            PlayerGold = _progress.PlayerGold;
            _sourceRevision = _progress is AdventureProgressDataContainer data ? data.Revision : -1;
            PlayerItems = Rows(_progress.Items, true);
            MerchantItems = Rows(_progress.GetMerchantStock(MerchantId), false);
            Changed();
        }

        private IReadOnlyList<ShopItemRow> Rows(IReadOnlyDictionary<string, int> stock, bool player)
        {
            var rows = new List<ShopItemRow>();
            foreach (var item in _sheet.Items.Items)
            {
                if (stock.TryGetValue(item.Id, out int quantity) && quantity > 0)
                {
                    int available = player ? _progress.GetAvailableItemCount(item.Id) : quantity;
                    rows.Add(new ShopItemRow(item, quantity, available, player && available < quantity));
                }
            }
            return rows.AsReadOnly();
        }

        private void Changed()
        {
            Context = Guid.NewGuid().ToString("N");
            Revision++;
        }
    }

    public sealed class ShopTrade
    {
        internal ShopSheet Sheet { get; }
        internal IAdventureProgressReader Progress { get; }
        private readonly int _sourceRevision;
        private readonly ShopDataContainer _source;
        public string Id { get; }
        public string MerchantId { get; }
        public string ItemId { get; }
        public bool IsBuying { get; }
        public int Quantity { get; }

        internal ShopTrade(ShopDataContainer source, ShopSheet sheet, IAdventureProgressReader progress, int revision, string id,
            string merchantId, string itemId, bool buying, int quantity)
        {
            Sheet = sheet;
            _source = source;
            Progress = progress;
            _sourceRevision = revision;
            Id = id;
            MerchantId = merchantId;
            ItemId = itemId;
            IsBuying = buying;
            Quantity = quantity;
        }

        internal static ShopTradeResult Evaluate(ShopSheet sheet, IAdventureProgressReader progress, int revision,
            string merchantId, string itemId, bool buying, int quantity)
        {
            if (!(progress is AdventureProgressDataContainer data) || revision != data.Revision || string.IsNullOrEmpty(itemId) || quantity <= 0)
            {
                return ShopTradeResult.InvalidSelection;
            }
            var stock = progress.GetMerchantStock(merchantId);
            stock.TryGetValue(itemId, out int merchantQuantity);
            progress.Items.TryGetValue(itemId, out int playerQuantity);
            if (quantity > (buying ? merchantQuantity : progress.GetAvailableItemCount(itemId)))
            {
                return ShopTradeResult.InsufficientStock;
            }
            long total = sheet.Items.GetItem(itemId).Price * quantity;
            if (buying && total > progress.PlayerGold)
            {
                return ShopTradeResult.InsufficientGold;
            }
            if ((!buying && (decimal)progress.PlayerGold + total > long.MaxValue)
                || (long)(buying ? playerQuantity : merchantQuantity) + quantity > int.MaxValue)
            {
                return ShopTradeResult.InvalidSelection;
            }
            return ShopTradeResult.Saved;
        }

        internal ShopTradeResult TryCreateCandidate(AdventureProgressDataContainer source, out AdventureProgressDataContainer candidate)
        {
            candidate = null;
            if (!ReferenceEquals(Progress, source) || _source.Context != Id)
            {
                return ShopTradeResult.InvalidSelection;
            }
            var result = Evaluate(Sheet, source, _sourceRevision, MerchantId, ItemId, IsBuying, Quantity);
            if (result != ShopTradeResult.Saved)
            {
                return result;
            }
            source.Items.TryGetValue(ItemId, out int owned);
            source.GetMerchantStock(MerchantId).TryGetValue(ItemId, out int stock);
            long total = Sheet.Items.GetItem(ItemId).Price * Quantity;
            candidate = source.Copy();
            candidate.SetGold(Sheet.Items.PlayerMemberId, checked(source.PlayerGold + (IsBuying ? -total : total)));
            candidate.SetItem(ItemId, IsBuying ? checked(owned + Quantity) : owned - Quantity);
            candidate.SetMerchantItem(MerchantId, ItemId, IsBuying ? stock - Quantity : checked(stock + Quantity));
            candidate.RecordTrade(Id);
            return ShopTradeResult.Saved;
        }
    }
}
