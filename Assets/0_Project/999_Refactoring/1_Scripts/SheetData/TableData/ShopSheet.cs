using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using Util.Sheet;

namespace Refactor.Util
{
    public sealed class ShopMerchantDefinition
    {
        public string Id { get; }
        public string LocationId { get; }
        public string Name { get; }
        public IReadOnlyDictionary<string, int> InitialStock { get; }

        internal ShopMerchantDefinition(string id, string locationId, string name, Dictionary<string, int> stock)
        {
            Id = id;
            LocationId = locationId;
            Name = name;
            InitialStock = new ReadOnlyDictionary<string, int>(stock);
        }
    }

    public sealed class ShopSheet
    {
        private readonly Dictionary<string, ShopMerchantDefinition> _merchants = new Dictionary<string, ShopMerchantDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<string, ShopMerchantDefinition> _locations = new Dictionary<string, ShopMerchantDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _texts = new Dictionary<string, string>(StringComparer.Ordinal);
        public LootSheet Items { get; }
        public IReadOnlyList<ShopMerchantDefinition> Merchants { get; }

        public ShopSheet(string merchants, string stock, string texts, LootSheet items, TownSheet town)
        {
            Items = items ?? throw new ArgumentNullException(nameof(items));
            if (town == null)
            {
                throw new ArgumentNullException(nameof(town));
            }
            var stocks = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
            var list = new List<ShopMerchantDefinition>();
            foreach (var row in CsvTable.Read(merchants, "ShopMerchantTable", "Id", "LocationId", "Name"))
            {
                string id = Required(row[0]);
                string locationId = Required(row[1]);
                var location = town.GetLocation(locationId);
                bool hasTrade = false;
                foreach (string command in location.CommandIds)
                {
                    hasTrade |= command == "trade";
                }
                if (!hasTrade || _merchants.ContainsKey(id) || _locations.ContainsKey(locationId))
                {
                    throw new FormatException("상인/장소가 중복되었거나 거래 장소가 아닙니다: " + id);
                }
                var inventory = new Dictionary<string, int>(StringComparer.Ordinal);
                var merchant = new ShopMerchantDefinition(id, locationId, Required(row[2]), inventory);
                list.Add(merchant);
                _merchants.Add(id, merchant);
                _locations.Add(locationId, merchant);
                stocks.Add(id, inventory);
            }
            if (list.Count == 0)
            {
                throw new FormatException("상인 시트에 상인이 없습니다.");
            }
            foreach (var location in town.Locations)
            {
                foreach (string command in location.CommandIds)
                {
                    if (command == "trade" && !_locations.ContainsKey(location.Id))
                    {
                        throw new FormatException("거래 장소의 상인이 없습니다: " + location.Id);
                    }
                }
            }
            Merchants = list.AsReadOnly();
            foreach (var row in CsvTable.Read(stock, "ShopStockTable", "MerchantId", "ItemId", "Quantity"))
            {
                string merchantId = Required(row[0]);
                string itemId = Required(row[1]);
                items.GetItem(itemId);
                if (!stocks.TryGetValue(merchantId, out var inventory) || inventory.ContainsKey(itemId)
                    || !int.TryParse(row[2], NumberStyles.None, CultureInfo.InvariantCulture, out int quantity)
                    || quantity < 1 || quantity > 100000)
                {
                    throw new FormatException("상인 초기 재고의 참조/중복/수량이 올바르지 않습니다.");
                }
                inventory.Add(itemId, quantity);
            }
            foreach (var row in CsvTable.Read(texts, "ShopTextTable", "Key", "Value"))
            {
                string key = Required(row[0]);
                if (_texts.ContainsKey(key) || string.IsNullOrWhiteSpace(row[1]))
                {
                    throw new FormatException("상점 문구가 중복되거나 비어 있습니다: " + key);
                }
                _texts.Add(key, row[1]);
            }
            string[] requiredTexts =
            {
                "shop.title", "shop.description", "shop.player_inventory", "shop.merchant_inventory",
                "shop.player_empty", "shop.merchant_empty", "shop.gold_format", "shop.row_quantity_format",
                "shop.unit_price_format", "shop.quantity_format", "shop.total_format", "shop.choose_item",
                "shop.selected_buy_format", "shop.selected_sell_format", "shop.available_buy_format", "shop.available_sell_format",
                "shop.buy_balance_format", "shop.sell_balance_format", "shop.insufficient_format", "shop.choose_hint",
                "shop.ready", "shop.decrease", "shop.increase", "shop.confirm", "shop.confirm_buy", "shop.confirm_sell",
                "shop.close", "status.load_failed", "status.save_failed", "status.invalid_selection",
                "status.insufficient_gold", "status.no_stock", "status.not_owned", "status.buy_success_format", "status.sell_success_format"
            };
            foreach (string key in requiredTexts)
            {
                Text(key);
            }
        }

        public ShopMerchantDefinition GetMerchant(string id)
        {
            return id != null && _merchants.TryGetValue(id, out var merchant) ? merchant
                : throw new KeyNotFoundException("상인을 찾을 수 없습니다: " + id);
        }

        public ShopMerchantDefinition ForLocation(string locationId)
        {
            return locationId != null && _locations.TryGetValue(locationId, out var merchant) ? merchant : null;
        }

        public string Text(string key)
        {
            return _texts.TryGetValue(key, out string value) ? value : throw new KeyNotFoundException("상점 문구 누락: " + key);
        }

        private static string Required(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value != value.Trim())
            {
                throw new FormatException("상점 시트의 ID/이름은 빈 값과 양끝 공백을 허용하지 않습니다.");
            }
            return value;
        }
    }
}
