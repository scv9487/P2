using System;
using System.Collections.Generic;
using System.Globalization;
using Util.Sheet;

namespace Refactor.Util
{
    public sealed class LootItemDefinition
    {
        public string Id { get; }
        public string Name { get; }
        public long Price { get; }

        internal LootItemDefinition(string id, string name, long price)
        {
            Id = id;
            Name = name;
            Price = price;
        }
    }

    public sealed class LootDropDefinition
    {
        public string ItemId { get; }
        public int Quantity { get; }

        internal LootDropDefinition(string itemId, int quantity)
        {
            ItemId = itemId;
            Quantity = quantity;
        }
    }

    public sealed class LootShareDefinition
    {
        public string MemberId { get; }
        public string Name { get; }
        public int Percent { get; }

        internal LootShareDefinition(string memberId, string name, int percent)
        {
            MemberId = memberId;
            Name = name;
            Percent = percent;
        }
    }

    public sealed class LootSheet
    {
        private readonly Dictionary<string, LootItemDefinition> _items = new Dictionary<string, LootItemDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<string, IReadOnlyList<LootDropDefinition>> _drops = new Dictionary<string, IReadOnlyList<LootDropDefinition>>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _texts;
        private static readonly IReadOnlyList<LootDropDefinition> NoDrops = Array.AsReadOnly(Array.Empty<LootDropDefinition>());

        public IReadOnlyList<LootItemDefinition> Items { get; }
        public IReadOnlyList<LootShareDefinition> Shares { get; }
        public string PlayerMemberId { get; }
        public long StartingGold { get; }

        public LootSheet(string items, string drops, string shares, string settings, string texts, DungeonSheet dungeonSheet)
        {
            if (dungeonSheet == null)
            {
                throw new ArgumentNullException(nameof(dungeonSheet));
            }
            var orderedItems = new List<LootItemDefinition>();
            foreach (var row in CsvTable.Read(items, "LootItemTable", "Id", "Name", "Price"))
            {
                string id = Required(row[0], "아이템 ID");
                if (_items.ContainsKey(id))
                {
                    throw new FormatException("전리품 아이템 ID 중복: " + id);
                }
                var item = new LootItemDefinition(id, Required(row[1], "아이템 이름"), Number(row[2], 1, 1000000000, "정가"));
                _items.Add(id, item);
                orderedItems.Add(item);
            }
            if (orderedItems.Count == 0)
            {
                throw new FormatException("전리품 아이템이 없습니다.");
            }
            Items = orderedItems.AsReadOnly();

            var dropGroups = new Dictionary<string, List<LootDropDefinition>>(StringComparer.Ordinal);
            foreach (var row in CsvTable.Read(drops, "LootDropTable", "EnemyId", "ItemId", "Quantity"))
            {
                string enemyId = Required(row[0], "적 ID");
                string itemId = Required(row[1], "드롭 아이템 ID");
                GetItem(itemId);
                if (!dropGroups.TryGetValue(enemyId, out var list))
                {
                    list = new List<LootDropDefinition>();
                    dropGroups.Add(enemyId, list);
                }
                if (list.Exists(item => item.ItemId == itemId))
                {
                    throw new FormatException("동일 적의 드롭 중복: " + enemyId + "/" + itemId);
                }
                list.Add(new LootDropDefinition(itemId, (int)Number(row[2], 1, 100000, "드롭 수량")));
            }
            foreach (var pair in dropGroups)
            {
                _drops.Add(pair.Key, pair.Value.AsReadOnly());
            }

            var party = new Dictionary<string, DungeonPartyDefinition>(StringComparer.Ordinal);
            foreach (var member in dungeonSheet.Party)
            {
                party.Add(member.Id, member);
                if (member.IsPlayerControlled)
                {
                    PlayerMemberId = member.Id;
                }
            }
            var orderedShares = new List<LootShareDefinition>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            int totalPercent = 0;
            foreach (var row in CsvTable.Read(shares, "LootShareTable", "MemberId", "Percent"))
            {
                string memberId = Required(row[0], "분배 인물 ID");
                if (!party.TryGetValue(memberId, out var member) || !seen.Add(memberId))
                {
                    throw new FormatException("분배 인물 ID가 파티에 없거나 중복됩니다: " + memberId);
                }
                int percent = (int)Number(row[1], 0, 100, "분배 비율");
                totalPercent += percent;
                orderedShares.Add(new LootShareDefinition(memberId, member.Name, percent));
            }
            if (seen.Count != party.Count || totalPercent != 100 || string.IsNullOrEmpty(PlayerMemberId))
            {
                throw new FormatException("모든 파티원의 분배 비율이 필요하며 합계는 100이어야 합니다.");
            }
            Shares = orderedShares.AsReadOnly();
            var values = ReadValues(settings, "LootSettingTable");
            if (values.Count != 1 || !values.TryGetValue("StartingGold", out string startingGold))
            {
                throw new FormatException("LootSettingTable에는 StartingGold가 필요합니다.");
            }
            StartingGold = Number(startingGold, 0, 1000000000000, "시작 금화");
            _texts = ReadValues(texts, "LootTextTable");
            string[] requiredTexts =
            {
                "settlement.title", "settlement.committed_title", "settlement.description", "settlement.item",
                "settlement.acquired", "settlement.unit_price", "settlement.purchase", "settlement.external",
                "settlement.item_total", "settlement.quantity_format", "settlement.gold_format", "settlement.decrease",
                "settlement.increase", "settlement.total_format", "settlement.revenue_format", "settlement.shares",
                "settlement.player_name_format", "settlement.share_percent_format", "settlement.share_gold_format",
                "settlement.balance_format", "settlement.purchase_summary_format", "settlement.item_summary_format",
                "settlement.item_separator", "settlement.no_purchase", "settlement.insufficient_format",
                "settlement.ready", "settlement.confirm", "settlement.cancel", "settlement.retry_return",
                "settlement.committed_notice", "settlement.empty", "inventory.gold_format", "inventory.item_format",
                "inventory.item_separator", "inventory.empty", "status.load_failed", "status.save_failed",
                "status.insufficient_gold", "status.invalid_selection", "status.return_failed"
            };
            foreach (string key in requiredTexts)
            {
                Text(key);
            }
        }

        public LootItemDefinition GetItem(string id)
        {
            if (id == null || !_items.TryGetValue(id, out var item))
            {
                throw new KeyNotFoundException("전리품 아이템을 찾을 수 없습니다: " + id);
            }
            return item;
        }

        public IReadOnlyList<LootDropDefinition> GetDrops(string enemyId)
        {
            return enemyId != null && _drops.TryGetValue(enemyId, out var drops) ? drops : NoDrops;
        }

        public string Text(string key)
        {
            return _texts.TryGetValue(key, out string value) ? value : throw new KeyNotFoundException("전리품 문구 누락: " + key);
        }

        private static Dictionary<string, string> ReadValues(string csv, string name)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var row in CsvTable.Read(csv, name, "Key", "Value"))
            {
                string key = Required(row[0], name + " 키");
                if (result.ContainsKey(key))
                {
                    throw new FormatException(name + " 키 중복: " + key);
                }
                if (string.IsNullOrWhiteSpace(row[1]))
                {
                    throw new FormatException(name + " 값이 비어 있습니다: " + key);
                }
                result.Add(key, row[1]);
            }
            return result;
        }

        private static string Required(string value, string label)
        {
            if (string.IsNullOrWhiteSpace(value) || value != value.Trim())
            {
                throw new FormatException(label + " 값이 비어 있거나 양끝 공백이 있습니다.");
            }
            return value;
        }

        private static long Number(string value, long minimum, long maximum, string label)
        {
            if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long parsed)
                || parsed < minimum || parsed > maximum)
            {
                throw new FormatException(label + " 값이 허용 범위의 정수가 아닙니다.");
            }
            return parsed;
        }
    }
}
