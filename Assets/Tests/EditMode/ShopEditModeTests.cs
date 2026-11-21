using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Refactor;
using Refactor.Main;
using Refactor.Util;
using UnityEngine;

namespace P2.Tests
{
    public sealed class ShopEditModeTests
    {
        private TownSheet _town;
        private LootSheet _loot;
        private ShopSheet _shops;
        private string _directory;
        private string _path;

        [SetUp]
        public void SetUp()
        {
            _town = new TownSheet(Read("TownLocationTable.csv"), Read("TownCommandTable.csv"), Read("TownCharacterTable.csv"), Read("TownTextTable.csv"));
            var dungeon = new DungeonSheet(Read("DungeonDefinitionTable.csv"), Read("DungeonNodeTable.csv"), Read("DungeonPartyTable.csv"),
                Read("DungeonSettingTable.csv"), Read("DungeonTextTable.csv"), _town);
            _loot = new LootSheet(Read("LootItemTable.csv"), Read("LootDropTable.csv"), Read("LootShareTable.csv"),
                Read("LootSettingTable.csv"), Read("LootTextTable.csv"), dungeon);
            _shops = CreateSheet();
            _directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "ShopEditModeTests", Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(_directory);
            _path = Path.Combine(_directory, "progress.json");
        }

        [TearDown]
        public void TearDown()
        {
            string allowed = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "ShopEditModeTests")) + Path.DirectorySeparatorChar;
            Assert.That(Path.GetFullPath(_directory).StartsWith(allowed, StringComparison.OrdinalIgnoreCase), Is.True);
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, true);
            }
        }

        [Test]
        public void DesignerShopsShareItemPricesAndOwnSeparateStartingStock()
        {
            Assert.That(_loot.GetItem("health_potion").Name, Is.EqualTo("생명력 포션"));
            Assert.That(_loot.GetItem("health_potion").Price, Is.EqualTo(20));
            Assert.That(_shops.ForLocation("market").Id, Is.EqualTo("market"));
            Assert.That(_shops.ForLocation("tavern").Id, Is.EqualTo("tavern"));
            Assert.That(_shops.ForLocation("guild"), Is.Null);
            var store = new AdventureProgressStore(_loot, _path, _shops);
            Assert.That(store.Load(), Is.EqualTo(AdventureLoadResult.Missing));
            Assert.That(store.Progress.PlayerGold, Is.Zero);
            Assert.That(store.Progress.GetMerchantStock("market")["health_potion"], Is.EqualTo(10));
            Assert.That(store.Progress.GetMerchantStock("tavern")["health_potion"], Is.EqualTo(10));
            Assert.Throws<NotSupportedException>(() => ((IDictionary<string, int>)store.Progress.GetMerchantStock("market")).Add("rat_tail", 2));
            Assert.That(File.Exists(_path), Is.False);
        }

        [Test]
        public void ShopConfigRejectsMissingTradeMerchantsDuplicateRowsAndInvalidItems()
        {
            Assert.Throws<FormatException>(() => CreateSheet(merchants: "Id,LocationId,Name\nmarket,market,상인"));
            Assert.Throws<FormatException>(() => CreateSheet(stock: "MerchantId,ItemId,Quantity\nmarket,health_potion,1\nmarket,health_potion,2"));
            Assert.Throws<FormatException>(() => CreateSheet(stock: "MerchantId,ItemId,Quantity\nmissing,health_potion,1"));
            Assert.Throws<FormatException>(() => CreateSheet(stock: "MerchantId,ItemId,Quantity\nmarket,health_potion,0"));
            Assert.Throws<KeyNotFoundException>(() => CreateSheet(stock: "MerchantId,ItemId,Quantity\nmarket,missing,1"));
            Assert.Throws<KeyNotFoundException>(() => CreateSheet(texts: "Key,Value\nshop.title,상점"));
        }

        [Test]
        public void BuyingAndSellingMoveOnlyPlayerGoldAndTheChosenMerchantsStock()
        {
            var store = Seed(100, 0);
            var market = Shop(store);
            Assert.That(market.TrySelectItem("health_potion", true), Is.True);
            market.TrySetQuantity(2);
            Assert.That(market.TotalPrice, Is.EqualTo(40));
            Assert.That(market.FinalPlayerGold, Is.EqualTo(60));
            Assert.That(store.TryTrade(market.CreateTrade()), Is.EqualTo(ShopTradeResult.Saved));
            Assert.That(store.Progress.Items["health_potion"], Is.EqualTo(2));
            Assert.That(store.Progress.GetMerchantStock("market")["health_potion"], Is.EqualTo(8));
            Assert.That(store.Progress.GetMerchantStock("tavern")["health_potion"], Is.EqualTo(10));
            Assert.That(store.Progress.GetGold("mira"), Is.EqualTo(7));
            var tavern = Shop(store, "tavern");
            tavern.TrySelectItem("health_potion", false);
            Assert.That(store.TryTrade(tavern.CreateTrade()), Is.EqualTo(ShopTradeResult.Saved));
            Assert.That(store.Progress.PlayerGold, Is.EqualTo(80));
            Assert.That(store.Progress.Items["health_potion"], Is.EqualTo(1));
            Assert.That(store.Progress.GetMerchantStock("tavern")["health_potion"], Is.EqualTo(11));
            Assert.That(store.Progress.GetMerchantStock("market")["health_potion"], Is.EqualTo(8));
            Assert.That(store.Progress.GetGold("mira"), Is.EqualTo(7));
        }

        [Test]
        public void SoldLootBecomesMerchantStockAndCanBeBoughtBackAtTheSamePrice()
        {
            var store = Seed(0, 3);
            var shop = Shop(store);
            shop.TrySelectItem("rat_tail", false);
            shop.TrySetQuantity(2);
            Assert.That(store.TryTrade(shop.CreateTrade()), Is.EqualTo(ShopTradeResult.Saved));
            shop.ResetSelection();
            Assert.That(shop.MerchantItems.Single(row => row.ItemId == "rat_tail").Quantity, Is.EqualTo(2));
            shop.TrySelectItem("rat_tail", true);
            Assert.That(store.TryTrade(shop.CreateTrade()), Is.EqualTo(ShopTradeResult.Saved));
            Assert.That(store.Progress.PlayerGold, Is.EqualTo(10));
            Assert.That(store.Progress.Items["rat_tail"], Is.EqualTo(2));
            Assert.That(store.Progress.GetMerchantStock("market")["rat_tail"], Is.EqualTo(1));
        }

        [Test]
        public void InsufficientGoldAndInvalidQuantityNeverChangeMoneyStockOrTheFile()
        {
            var store = Seed(0, 0);
            string saved = File.ReadAllText(_path);
            var shop = Shop(store);
            Assert.That(shop.CreateTrade(), Is.Null);
            Assert.That(store.TryTrade(shop.CreateTrade()), Is.EqualTo(ShopTradeResult.InvalidSelection));
            shop.TrySelectItem("health_potion", true);
            string context = shop.Context;
            Assert.That(shop.TrySetQuantity(0), Is.False);
            Assert.That(shop.TrySetQuantity(11), Is.False);
            Assert.That(shop.TryAdjustQuantity(int.MaxValue), Is.False);
            Assert.That(shop.TrySelectItem("rat_tail", false), Is.False);
            Assert.That(shop.Context, Is.EqualTo(context));
            Assert.That(shop.FinalPlayerGold, Is.EqualTo(-20));
            Assert.That(shop.CanConfirm, Is.False);
            Assert.That(store.TryTrade(shop.CreateTrade()), Is.EqualTo(ShopTradeResult.InsufficientGold));
            Assert.That(store.Progress.Items, Is.Empty);
            Assert.That(store.Progress.GetMerchantStock("market")["health_potion"], Is.EqualTo(10));
            Assert.That(File.ReadAllText(_path), Is.EqualTo(saved));
        }

        [Test]
        public void ExhaustedMerchantStockStaysEmptyAfterReloadInsteadOfBeingRestocked()
        {
            var store = Seed(200, 0);
            var shop = Shop(store);
            shop.TrySelectItem("health_potion", true);
            shop.TrySetQuantity(10);
            Assert.That(store.TryTrade(shop.CreateTrade()), Is.EqualTo(ShopTradeResult.Saved));
            shop.ResetSelection();
            Assert.That(shop.MerchantItems.All(item => item.ItemId != "health_potion"), Is.True);
            Assert.That(shop.TrySelectItem("health_potion", true), Is.False);
            var reloaded = new AdventureProgressStore(_loot, _path, _shops);
            Assert.That(reloaded.Load(), Is.EqualTo(AdventureLoadResult.Loaded));
            Assert.That(reloaded.Progress.GetMerchantStock("market").ContainsKey("health_potion"), Is.False);
            Assert.That(reloaded.Progress.GetMerchantStock("tavern")["health_potion"], Is.EqualTo(10));
        }

        [Test]
        public void SameTradeIsAppliedOnceAndChangingSelectionInvalidatesPreparedRequests()
        {
            var store = Seed(100, 0);
            var shop = Shop(store);
            string opened = shop.Context;
            shop.TrySelectItem("health_potion", true);
            Assert.That(shop.Context, Is.Not.EqualTo(opened));
            ShopTrade old = shop.CreateTrade();
            shop.TrySetQuantity(2);
            Assert.That(store.TryTrade(old), Is.EqualTo(ShopTradeResult.InvalidSelection));
            ShopTrade current = shop.CreateTrade();
            Assert.That(store.TryTrade(current), Is.EqualTo(ShopTradeResult.Saved));
            string saved = File.ReadAllText(_path);
            shop.ResetSelection();
            Assert.That(store.TryTrade(current), Is.EqualTo(ShopTradeResult.AlreadyCommitted));
            Assert.That(store.Progress.PlayerGold, Is.EqualTo(60));
            Assert.That(File.ReadAllText(_path), Is.EqualTo(saved));
            var reloaded = new AdventureProgressStore(_loot, _path, _shops);
            reloaded.Load();
            Assert.That(reloaded.Progress.HasTrade(current.Id), Is.True);
            Assert.That(reloaded.TryTrade(current), Is.EqualTo(ShopTradeResult.InvalidSelection));
            Assert.That(reloaded.Progress.PlayerGold, Is.EqualTo(60));
        }

        [Test]
        public void ShopAndLootSettlementShareTheSameRevisionAndRejectEachOthersOlderDrafts()
        {
            var store = Seed(100, 0);
            var settlement = new LootSettlement(_loot, "before_trade", new Dictionary<string, int> { { "rat_tail", 2 } }, store.Progress);
            var shop = Shop(store);
            shop.TrySelectItem("health_potion", true);
            Assert.That(store.TryTrade(shop.CreateTrade()), Is.EqualTo(ShopTradeResult.Saved));
            Assert.That(store.TryCommit(settlement), Is.EqualTo(SettlementCommitResult.InvalidSelection));
            shop.ResetSelection();
            shop.TrySelectItem("health_potion", true);
            ShopTrade trade = shop.CreateTrade();
            settlement = new LootSettlement(_loot, "after_trade", new Dictionary<string, int> { { "rat_tail", 2 } }, store.Progress);
            Assert.That(store.TryCommit(settlement), Is.EqualTo(SettlementCommitResult.Saved));
            Assert.That(store.TryTrade(trade), Is.EqualTo(ShopTradeResult.InvalidSelection));
            Assert.That(shop.CreateTrade(), Is.Null);
            Assert.That(store.Progress.PlayerGold, Is.EqualTo(90));
            Assert.That(store.Progress.GetMerchantStock("market")["health_potion"], Is.EqualTo(9));
        }

        [Test]
        public void VersionOneLoadsWithoutRewritingAndMigratesOnlyAfterSuccessfulTrade()
        {
            const string legacy = "{\"Format\":\"P2.Adventure\",\"Version\":1,\"MemberIds\":[\"hero\",\"mira\"],\"MemberGold\":[30,7],\"ItemIds\":[\"rat_tail\"],\"ItemCounts\":[2],\"SettlementIds\":[\"legacy\"]}";
            File.WriteAllText(_path, legacy);
            var store = new AdventureProgressStore(_loot, _path, _shops);
            Assert.That(store.Load(), Is.EqualTo(AdventureLoadResult.Loaded));
            Assert.That(File.ReadAllText(_path), Is.EqualTo(legacy));
            Assert.That(store.Progress.PlayerGold, Is.EqualTo(30));
            Assert.That(store.Progress.Items["rat_tail"], Is.EqualTo(2));
            Assert.That(store.Progress.HasSettlement("legacy"), Is.True);
            Assert.That(store.Progress.GetMerchantStock("market")["health_potion"], Is.EqualTo(10));
            var shop = Shop(store);
            shop.TrySelectItem("health_potion", true);
            Assert.That(store.TryTrade(shop.CreateTrade()), Is.EqualTo(ShopTradeResult.Saved));
            var written = JsonUtility.FromJson<AdventureSaveData>(File.ReadAllText(_path));
            Assert.That(written.Version, Is.EqualTo(AdventureSave.CurrentVersion));
            var reloaded = new AdventureProgressStore(_loot, _path, _shops);
            reloaded.Load();
            Assert.That(reloaded.Progress.PlayerGold, Is.EqualTo(10));
            Assert.That(reloaded.Progress.Items["rat_tail"], Is.EqualTo(2));
            Assert.That(reloaded.Progress.Items["health_potion"], Is.EqualTo(1));
            Assert.That(reloaded.Progress.HasSettlement("legacy"), Is.True);
            Assert.That(reloaded.Progress.GetMerchantStock("market")["health_potion"], Is.EqualTo(9));
        }

        [Test]
        public void VersionTwoWithoutShopSheetPreservesStockThroughAnotherSettlementSave()
        {
            var store = Seed(100, 0);
            var shop = Shop(store);
            shop.TrySelectItem("health_potion", true);
            ShopTrade trade = shop.CreateTrade();
            store.TryTrade(trade);
            var withoutShops = new AdventureProgressStore(_loot, _path);
            Assert.That(withoutShops.Load(), Is.EqualTo(AdventureLoadResult.Loaded));
            var settlement = new LootSettlement(_loot, "without_shops", new Dictionary<string, int> { { "rat_tail", 2 } }, withoutShops.Progress);
            Assert.That(withoutShops.TryCommit(settlement), Is.EqualTo(SettlementCommitResult.Saved));
            var reloaded = new AdventureProgressStore(_loot, _path, _shops);
            reloaded.Load();
            Assert.That(reloaded.Progress.GetMerchantStock("market")["health_potion"], Is.EqualTo(9));
            Assert.That(reloaded.Progress.GetMerchantStock("tavern")["health_potion"], Is.EqualTo(10));
            Assert.That(reloaded.Progress.HasTrade(trade.Id), Is.True);
            Assert.That(reloaded.Progress.PlayerGold, Is.EqualTo(90));
        }

        [Test]
        public void WriteFailurePreservesMoneyInventoryMerchantStockAndAllowsTheSameTradeToRetry()
        {
            var store = Seed(100, 2);
            var shop = Shop(store);
            shop.TrySelectItem("health_potion", true);
            ShopTrade trade = shop.CreateTrade();
            string saved = File.ReadAllText(_path);
            Directory.CreateDirectory(_path + ".tmp");
            Assert.That(store.TryTrade(trade), Is.EqualTo(ShopTradeResult.SaveFailed));
            Assert.That(store.Progress.PlayerGold, Is.EqualTo(100));
            Assert.That(store.Progress.Items["rat_tail"], Is.EqualTo(2));
            Assert.That(store.Progress.Items.ContainsKey("health_potion"), Is.False);
            Assert.That(store.Progress.GetMerchantStock("market")["health_potion"], Is.EqualTo(10));
            Assert.That(store.Progress.HasTrade(trade.Id), Is.False);
            Assert.That(File.ReadAllText(_path), Is.EqualTo(saved));
            Directory.Delete(_path + ".tmp");
            Assert.That(store.TryTrade(trade), Is.EqualTo(ShopTradeResult.Saved));
            Assert.That(store.Progress.PlayerGold, Is.EqualTo(80));
        }

        [Test]
        public void CorruptLoadBlocksTradeAndPreservesTheExistingMemoryAndFile()
        {
            var store = Seed(100, 2);
            File.WriteAllText(_path, "{}");
            Assert.That(store.Load(), Is.EqualTo(AdventureLoadResult.Corrupt));
            var shop = Shop(store);
            shop.TrySelectItem("health_potion", true);
            Assert.That(store.TryTrade(shop.CreateTrade()), Is.EqualTo(ShopTradeResult.LoadBlocked));
            Assert.That(store.Progress.PlayerGold, Is.EqualTo(100));
            Assert.That(store.Progress.Items["rat_tail"], Is.EqualTo(2));
            Assert.That(store.Progress.GetMerchantStock("market")["health_potion"], Is.EqualTo(10));
            Assert.That(File.ReadAllText(_path), Is.EqualTo("{}"));
        }

        [TestCase("gold")]
        [TestCase("player_quantity")]
        [TestCase("merchant_quantity")]
        public void OverflowAtEitherInventoryOrTheWalletRejectsTheWholeTrade(string target)
        {
            var dto = SaveData(target == "gold" ? long.MaxValue : 100, 1);
            if (target == "player_quantity")
            {
                dto.ItemIds = new[] { "health_potion" };
                dto.ItemCounts = new[] { int.MaxValue };
            }
            if (target == "merchant_quantity")
            {
                dto.MerchantIds = new[] { "market" };
                dto.StockMerchantIds = new[] { "market" };
                dto.StockItemIds = new[] { "rat_tail" };
                dto.StockCounts = new[] { int.MaxValue };
            }
            AdventureSave.Save(_path, dto, _loot, _shops);
            var store = new AdventureProgressStore(_loot, _path, _shops);
            store.Load();
            var shop = Shop(store);
            shop.TrySelectItem(target == "player_quantity" ? "health_potion" : "rat_tail", target == "player_quantity");
            Assert.That(shop.CanConfirm, Is.False);
            string saved = File.ReadAllText(_path);
            Assert.That(store.TryTrade(shop.CreateTrade()), Is.EqualTo(ShopTradeResult.InvalidSelection));
            Assert.That(File.ReadAllText(_path), Is.EqualTo(saved));
        }

        [TestCase("MerchantIds")]
        [TestCase("StockMerchantIds")]
        [TestCase("StockItemIds")]
        [TestCase("StockCounts")]
        [TestCase("TradeIds")]
        public void VersionTwoRequiresEveryShopArrayAndRejectsExplicitNull(string field)
        {
            string valid = JsonUtility.ToJson(SaveData(10, 0));
            string pattern = ",\\s*\"" + Regex.Escape(field) + "\"\\s*:\\s*\\[[^\\]]*\\]";
            Assert.That(Regex.Matches(valid, pattern).Count, Is.EqualTo(1));
            foreach (string replacement in new[] { string.Empty, ",\"" + field + "\":null" })
            {
                string contents = Regex.Replace(valid, pattern, replacement);
                File.WriteAllText(_path, contents);
                Assert.That(AdventureSave.Load(_path, _loot, out var dto, _shops), Is.EqualTo(AdventureLoadResult.Corrupt));
                Assert.That(dto, Is.Null);
                Assert.That(File.ReadAllText(_path), Is.EqualTo(contents));
            }
        }

        [TestCase("unknown_merchant")]
        [TestCase("unknown_item")]
        [TestCase("duplicate_stock")]
        [TestCase("zero_stock")]
        [TestCase("mismatched_arrays")]
        [TestCase("duplicate_trade")]
        public void VersionTwoRejectsInvalidStockReferencesQuantitiesAndTradeIds(string mutation)
        {
            var dto = SaveData(10, 0);
            dto.MerchantIds = new[] { "market" };
            dto.StockMerchantIds = new[] { "market" };
            dto.StockItemIds = new[] { "health_potion" };
            dto.StockCounts = new[] { 2 };
            switch (mutation)
            {
                case "unknown_merchant": dto.MerchantIds[0] = "missing"; break;
                case "unknown_item": dto.StockItemIds[0] = "missing"; break;
                case "duplicate_stock":
                    dto.StockMerchantIds = new[] { "market", "market" };
                    dto.StockItemIds = new[] { "health_potion", "health_potion" };
                    dto.StockCounts = new[] { 2, 3 };
                    break;
                case "zero_stock": dto.StockCounts[0] = 0; break;
                case "mismatched_arrays": dto.StockCounts = Array.Empty<int>(); break;
                case "duplicate_trade": dto.TradeIds = new[] { "trade", "trade" }; break;
            }
            File.WriteAllText(_path, JsonUtility.ToJson(dto));
            Assert.That(AdventureSave.Load(_path, _loot, out var parsed, _shops), Is.EqualTo(AdventureLoadResult.Corrupt));
            Assert.That(parsed, Is.Null);
        }

        private AdventureProgressStore Seed(long gold, int tails)
        {
            AdventureSave.Save(_path, SaveData(gold, tails), _loot, _shops);
            var store = new AdventureProgressStore(_loot, _path, _shops);
            Assert.That(store.Load(), Is.EqualTo(AdventureLoadResult.Loaded));
            return store;
        }

        private static AdventureSaveData SaveData(long gold, int tails)
        {
            return new AdventureSaveData
            {
                Format = AdventureSave.CurrentFormat,
                Version = AdventureSave.CurrentVersion,
                MemberIds = new[] { "hero", "mira" },
                MemberGold = new[] { gold, 7L },
                ItemIds = tails > 0 ? new[] { "rat_tail" } : Array.Empty<string>(),
                ItemCounts = tails > 0 ? new[] { tails } : Array.Empty<int>(),
                SettlementIds = new[] { "seed" }
            };
        }

        private ShopDataContainer Shop(AdventureProgressStore store, string id = "market")
        {
            return new ShopDataContainer(_shops, id, store.Progress);
        }

        private ShopSheet CreateSheet(string merchants = null, string stock = null, string texts = null)
        {
            return new ShopSheet(merchants ?? Read("ShopMerchantTable.csv"), stock ?? Read("ShopStockTable.csv"),
                texts ?? Read("ShopTextTable.csv"), _loot, _town);
        }

        private static string Read(string name)
        {
            return File.ReadAllText(Path.Combine(Application.dataPath, "1_InternalTools/Util/DataSheetLoader/Table", name));
        }
    }
}
