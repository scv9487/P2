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
    public sealed class EquipmentProgressTests
    {
        private LootSheet _loot;
        private ShopSheet _shops;
        private CharacterSheet _characters;
        private QuestSheet _quests;
        private SkillSheet _skills;
        private EquipmentSheet _equipment;
        private string _directory;
        private string _path;

        [SetUp]
        public void SetUp()
        {
            var town = new TownSheet(Read("TownLocationTable.csv"), Read("TownCommandTable.csv"), Read("TownCharacterTable.csv"), Read("TownTextTable.csv"));
            var dungeon = new DungeonSheet(Read("DungeonDefinitionTable.csv"), Read("DungeonNodeTable.csv"), Read("DungeonPartyTable.csv"),
                Read("DungeonSettingTable.csv"), Read("DungeonTextTable.csv"), town);
            _loot = new LootSheet(Read("LootItemTable.csv"), Read("LootDropTable.csv"), Read("LootShareTable.csv"), Read("LootSettingTable.csv"), Read("LootTextTable.csv"), dungeon);
            _shops = new ShopSheet(Read("ShopMerchantTable.csv"), Read("ShopStockTable.csv"), Read("ShopTextTable.csv"), _loot, town);
            _characters = new CharacterSheet(Read("CharacterDefinitionTable.csv"), Read("CharacterSettingTable.csv"), Read("CharacterTextTable.csv"), town,
                new CharacterGrowthSheet(Read("CharacterGrowthTable.csv"), Read("CharacterLevelTable.csv"), Read("CharacterGrowthSettingTable.csv")),
                new CombatStatSheet(Read("CombatStatTable.csv"), Read("CombatEnemyTable.csv"), Read("CombatAttackTable.csv")));
            _quests = new QuestSheet(Read("QuestDefinitionTable.csv"), Read("QuestRescueNodeTable.csv"), Read("QuestTextTable.csv"), dungeon, _loot);
            _skills = new SkillSheet(Read("SkillDefinitionTable.csv"), Read("SkillClassTable.csv"), Read("CharacterClassTable.csv"),
                Read("SkillWeaponTable.csv"), Read("SkillSettingTable.csv"), Read("SkillTextTable.csv"));
            _equipment = new EquipmentSheet(Read("EquipmentDefinitionTable.csv"), Read("ConsumableDefinitionTable.csv"),
                Read("CharacterEquipmentTable.csv"), Read("EquipmentTextTable.csv"), _loot, _skills, _characters);
            _directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "EquipmentProgressTests", Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(_directory);
            _path = Path.Combine(_directory, "progress.json");
        }

        [TearDown]
        public void TearDown()
        {
            string allowed = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "EquipmentProgressTests")) + Path.DirectorySeparatorChar;
            Assert.That(_directory.StartsWith(allowed, StringComparison.OrdinalIgnoreCase), Is.True);
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, true);
            }
        }

        [Test]
        public void EquipmentInitializationRequiresShopsSoLegacyWeaponStockMigrationCannotBeSkipped()
        {
            Assert.Throws<ArgumentException>(() => new AdventureProgressStore(_loot, _path, null, _characters, _quests, _skills, _equipment));
            Assert.That(File.Exists(_path), Is.False);
        }

        [Test]
        public void NewProgressOwnsOnlyThePlayerStarterAndInitializesEveryCharacterWithoutWriting()
        {
            var store = Open();
            Assert.That(store.Progress.Items.Count, Is.EqualTo(1));
            Assert.That(store.Progress.Items["weapon_sword"], Is.EqualTo(1));
            Assert.That(store.Progress.GetAvailableItemCount("weapon_sword"), Is.Zero);
            Assert.That(store.Progress.GetAvailableItemCount("missing"), Is.Zero);
            Assert.That(store.Progress.GetAvailableItemCount(null), Is.Zero);
            foreach (var character in store.Progress.Characters.Values)
            {
                var starter = _equipment.GetStartingWeapon(character.Id);
                Assert.That(character.EquippedItemId, Is.EqualTo(starter.ItemId));
                Assert.That(character.WeaponId, Is.EqualTo(starter.WeaponId));
            }
            Assert.That(store.Progress.GetCharacter("mira").EquippedItemId, Is.EqualTo("weapon_blunt"));
            Assert.That(store.Progress.Items.ContainsKey("weapon_blunt"), Is.False);
            Assert.That(File.Exists(_path), Is.False);
        }

        [Test]
        public void EquippingChangesTheActiveTreeButPreservesLearnedSkillsPointsAndInventory()
        {
            var store = Seed();
            Assert.That(store.TryLearnSkill("hero", "SKILL_VerticalSlash", "learn", store.Progress.Revision), Is.EqualTo(SkillLearnResult.Saved));
            var hero = store.Progress.GetCharacter("hero");
            int main = hero.MainSkillPoints;
            int sub = hero.SubSkillPoints;
            int revision = store.Progress.Revision;
            Assert.That(store.TryEquipItem("weapon_blunt", "equip", revision), Is.EqualTo(ItemActionResult.Saved));
            hero = store.Progress.GetCharacter("hero");
            Assert.That(hero.EquippedItemId, Is.EqualTo("weapon_blunt"));
            Assert.That(hero.WeaponId, Is.EqualTo("Equipment_Blunt"));
            Assert.That(_skills.GetClassForCharacter(hero).ActiveTreeId, Is.EqualTo("Blunt"));
            Assert.That(SkillLearningRules.GetLearnFailure(_skills, hero, "SKILL_SwordCounter"), Is.EqualTo(SkillLearnFailure.WrongTree));
            Assert.That(hero.HasSkill("SKILL_VerticalSlash"), Is.True);
            Assert.That(hero.MainSkillPoints, Is.EqualTo(main));
            Assert.That(hero.SubSkillPoints, Is.EqualTo(sub));
            Assert.That(store.Progress.Items["weapon_sword"], Is.EqualTo(1));
            Assert.That(store.Progress.Items["weapon_blunt"], Is.EqualTo(1));
            Assert.That(store.Progress.GetAvailableItemCount("weapon_sword"), Is.EqualTo(1));
            Assert.That(store.Progress.GetAvailableItemCount("weapon_blunt"), Is.Zero);
            Assert.That(store.TryEquipItem("weapon_blunt", "equip", revision), Is.EqualTo(ItemActionResult.AlreadyApplied));
            Assert.That(store.TryEquipItem("weapon_sword", "stale", revision), Is.EqualTo(ItemActionResult.InvalidRequest));
            Assert.That(store.TryEquipItem("weapon_sword", "back", store.Progress.Revision), Is.EqualTo(ItemActionResult.Saved));
            Assert.That(store.Progress.GetCharacter("hero").HasSkill("SKILL_VerticalSlash"), Is.True);
            store = Open();
            Assert.That(store.Progress.GetCharacter("hero").EquippedItemId, Is.EqualTo("weapon_sword"));
            Assert.That(store.Progress.GetCharacter("hero").MainSkillPoints, Is.EqualTo(main));
            Assert.That(store.TryEquipItem("weapon_blunt", "equip", revision), Is.EqualTo(ItemActionResult.AlreadyApplied));
        }

        [Test]
        public void ItemActionsRejectNonWeaponsUnownedItemsAndStaleRequestsWithoutWriting()
        {
            var store = Open();
            int revision = store.Progress.Revision;
            Assert.That(store.TryEquipItem("weapon_sword", "same", revision), Is.EqualTo(ItemActionResult.AlreadyEquipped));
            Assert.That(store.TryEquipItem("weapon_blunt", "unowned", revision), Is.EqualTo(ItemActionResult.NotOwned));
            Assert.That(store.TryEquipItem("health_potion", "potion", revision), Is.EqualTo(ItemActionResult.InvalidItem));
            Assert.That(store.TryEquipItem("missing", "unknown", revision), Is.EqualTo(ItemActionResult.InvalidItem));
            Assert.That(store.TryEquipItem(null, "null", revision), Is.EqualTo(ItemActionResult.InvalidItem));
            Assert.That(store.TryConsumeItem("weapon_sword", "weapon", revision), Is.EqualTo(ItemActionResult.InvalidItem));
            Assert.That(store.TryConsumeItem("health_potion", "empty", revision), Is.EqualTo(ItemActionResult.NotOwned));
            Assert.That(store.TryConsumeItem("health_potion", " ", revision), Is.EqualTo(ItemActionResult.InvalidRequest));
            Assert.That(store.Progress.Revision, Is.EqualTo(revision));
            Assert.That(File.Exists(_path), Is.False);
        }

        [Test]
        public void ShopDisplaysOwnedEquippedItemsButOnlySellsUnreservedCopies()
        {
            var store = Seed();
            var shop = new ShopDataContainer(_shops, "market", store.Progress);
            var row = shop.PlayerItems.Single(item => item.ItemId == "weapon_sword");
            Assert.That(row.Quantity, Is.EqualTo(1));
            Assert.That(row.AvailableQuantity, Is.Zero);
            Assert.That(row.IsEquipped, Is.True);
            Assert.That(shop.TrySelectItem("weapon_sword", false), Is.True);
            Assert.That(shop.AvailableQuantity, Is.Zero);
            Assert.That(shop.CanConfirm, Is.False);
            Assert.That(store.TryTrade(shop.CreateTrade()), Is.EqualTo(ShopTradeResult.InsufficientStock));
            shop.ResetSelection();
            Assert.That(shop.TrySelectItem("weapon_sword", true), Is.True);
            Assert.That(shop.TrySetQuantity(2), Is.True);
            Assert.That(store.TryTrade(shop.CreateTrade()), Is.EqualTo(ShopTradeResult.Saved));
            shop.ResetSelection();
            Assert.That(shop.TrySelectItem("weapon_sword", false), Is.True);
            Assert.That(shop.AvailableQuantity, Is.EqualTo(2));
            Assert.That(shop.TrySetQuantity(3), Is.False);
            Assert.That(shop.TrySetQuantity(2), Is.True);
            Assert.That(store.TryTrade(shop.CreateTrade()), Is.EqualTo(ShopTradeResult.Saved));
            Assert.That(store.Progress.Items["weapon_sword"], Is.EqualTo(1));
            shop.ResetSelection();
            Assert.That(shop.TrySelectItem("weapon_sword", false), Is.True);
            Assert.That(shop.CanConfirm, Is.False);
        }

        [Test]
        public void EquippingInvalidatesPreparedSalesAndThePreviouslyEquippedWeaponBecomesSellable()
        {
            var store = Seed();
            var shop = new ShopDataContainer(_shops, "market", store.Progress);
            Assert.That(shop.TrySelectItem("weapon_blunt", false), Is.True);
            var stale = shop.CreateTrade();
            Assert.That(store.TryEquipItem("weapon_blunt", "equip", store.Progress.Revision), Is.EqualTo(ItemActionResult.Saved));
            Assert.That(store.TryTrade(stale), Is.EqualTo(ShopTradeResult.InvalidSelection));
            shop.ResetSelection();
            Assert.That(shop.TrySelectItem("weapon_sword", false), Is.True);
            Assert.That(shop.CanConfirm, Is.True);
            Assert.That(store.TryTrade(shop.CreateTrade()), Is.EqualTo(ShopTradeResult.Saved));
            Assert.That(store.Progress.Items.ContainsKey("weapon_sword"), Is.False);
            store = Open();
            Assert.That(store.Progress.Items.ContainsKey("weapon_sword"), Is.False, "시작 장비를 로드마다 다시 만들면 안 됩니다.");
            Assert.That(store.Progress.GetCharacter("hero").EquippedItemId, Is.EqualTo("weapon_blunt"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FailedEquipmentOrConsumptionSavePreservesMemoryAndCanRetryExactlyOnce(bool consume)
        {
            var store = Seed();
            string saved = File.ReadAllText(_path);
            var hero = store.Progress.GetCharacter("hero");
            int revision = store.Progress.Revision;
            Directory.CreateDirectory(_path + ".tmp");
            ItemActionResult Act() => consume ? store.TryConsumeItem("health_potion", "action", revision)
                : store.TryEquipItem("weapon_blunt", "action", revision);
            Assert.That(Act(), Is.EqualTo(ItemActionResult.SaveFailed));
            Assert.That(store.Progress.GetCharacter("hero"), Is.SameAs(hero));
            Assert.That(store.Progress.Revision, Is.EqualTo(revision));
            Assert.That(store.Progress.Items["health_potion"], Is.EqualTo(2));
            Assert.That(File.ReadAllText(_path), Is.EqualTo(saved));
            Directory.Delete(_path + ".tmp");
            Assert.That(Act(), Is.EqualTo(ItemActionResult.Saved));
            Assert.That(Act(), Is.EqualTo(ItemActionResult.AlreadyApplied));
            Assert.That(store.Progress.Items["health_potion"], Is.EqualTo(consume ? 1 : 2));
            Assert.That(store.Progress.GetCharacter("hero").EquippedItemId, Is.EqualTo(consume ? "weapon_sword" : "weapon_blunt"));
            store = Open();
            Assert.That(store.TryConsumeItem("health_potion", "action", store.Progress.Revision), Is.EqualTo(ItemActionResult.AlreadyApplied));
        }

        [Test]
        public void ConsumingLastPotionRemovesItAndKeepsOtherProgressUnchanged()
        {
            var store = Seed();
            Assert.That(store.TryLearnSkill("hero", "SKILL_VerticalSlash", "learn", store.Progress.Revision), Is.EqualTo(SkillLearnResult.Saved));
            Assert.That(store.TryAcceptQuest("kill_rats", "quest", store.Progress.Revision), Is.EqualTo(QuestActionResult.Saved));
            long gold = store.Progress.PlayerGold;
            int stock = store.Progress.GetMerchantStock("market")["health_potion"];
            Assert.That(store.TryConsumeItem("health_potion", "one", store.Progress.Revision), Is.EqualTo(ItemActionResult.Saved));
            Assert.That(store.TryConsumeItem("health_potion", "two", store.Progress.Revision), Is.EqualTo(ItemActionResult.Saved));
            Assert.That(store.Progress.Items.ContainsKey("health_potion"), Is.False);
            Assert.That(store.TryConsumeItem("health_potion", "three", store.Progress.Revision), Is.EqualTo(ItemActionResult.NotOwned));
            store = Open();
            Assert.That(store.Progress.PlayerGold, Is.EqualTo(gold));
            Assert.That(store.Progress.GetMerchantStock("market")["health_potion"], Is.EqualTo(stock));
            Assert.That(store.Progress.Quest.ActiveQuestId, Is.EqualTo("kill_rats"));
            Assert.That(store.Progress.GetCharacter("hero").HasSkill("SKILL_VerticalSlash"), Is.True);
            Assert.That(store.Progress.GetCharacter("hero").EquippedItemId, Is.EqualTo("weapon_sword"));
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void LegacyMigrationSeedsOnlyNewWeaponsOnceAndDoesNotWriteOnRead(int version)
        {
            SeedLegacy(version);
            string original = File.ReadAllText(_path);
            var store = Open();
            Assert.That(File.ReadAllText(_path), Is.EqualTo(original));
            Assert.That(store.Progress.PlayerGold, Is.EqualTo(5000));
            Assert.That(store.Progress.Items["rat_tail"], Is.EqualTo(2));
            Assert.That(store.Progress.Items["health_potion"], Is.EqualTo(2));
            Assert.That(store.Progress.Items["weapon_sword"], Is.EqualTo(1));
            Assert.That(store.Progress.GetCharacter("hero").WeaponId, Is.EqualTo("Equipment_Sword"));
            Assert.That(store.Progress.GetMerchantStock("market")["weapon_sword"], Is.EqualTo(3));
            if (version >= 2)
            {
                Assert.That(store.Progress.GetMerchantStock("market").ContainsKey("health_potion"), Is.False);
                Assert.That(store.Progress.GetMerchantStock("market")["rat_tail"], Is.EqualTo(4));
            }
            var shop = new ShopDataContainer(_shops, "market", store.Progress);
            Assert.That(shop.TrySelectItem("weapon_sword", true), Is.True);
            Assert.That(shop.TrySetQuantity(3), Is.True);
            Assert.That(store.TryTrade(shop.CreateTrade()), Is.EqualTo(ShopTradeResult.Saved));
            Assert.That(JsonUtility.FromJson<AdventureSaveData>(File.ReadAllText(_path)).Version, Is.EqualTo(6));
            store = Open();
            Assert.That(store.Progress.GetMerchantStock("market").ContainsKey("weapon_sword"), Is.False, "이행 완료 후 품절 무기를 다시 채우지 않습니다.");
            Assert.That(store.Progress.Items["weapon_sword"], Is.EqualTo(4));
        }

        [Test]
        public void LoadingWithoutEquipmentPreservesSourceAndProtectsEquippedSalesDuringOtherWrites()
        {
            var store = Seed();
            Assert.That(store.TryEquipItem("weapon_blunt", "equip", store.Progress.Revision), Is.EqualTo(ItemActionResult.Saved));
            var financial = new AdventureProgressStore(_loot, _path, _shops, _characters, _quests, _skills);
            Assert.That(financial.Load(), Is.EqualTo(AdventureLoadResult.Loaded));
            Assert.That(financial.Progress.GetCharacter("hero").EquippedItemId, Is.EqualTo("weapon_blunt"));
            Assert.That(financial.Progress.GetCharacter("hero").WeaponId, Is.Null);
            Assert.That(financial.Progress.GetAvailableItemCount("weapon_blunt"), Is.Zero);
            Assert.That(financial.TryLearnSkill("hero", "SKILL_FastHand", "no_sheet", financial.Progress.Revision), Is.EqualTo(SkillLearnResult.InvalidRequest));
            Assert.That(financial.TryGrantExperience("no_growth", new[] { "mira" }, 1, new MinimumRandom()), Is.EqualTo(CharacterActionResult.InvalidRequest));
            Assert.That(financial.TryTalk("mira", "talk", financial.Progress.Revision), Is.EqualTo(CharacterActionResult.Saved));
            var shop = new ShopDataContainer(_shops, "market", financial.Progress);
            Assert.That(shop.TrySelectItem("rat_tail", false), Is.True);
            Assert.That(financial.TryTrade(shop.CreateTrade()), Is.EqualTo(ShopTradeResult.Saved));
            store = Open();
            Assert.That(store.Progress.GetCharacter("hero").WeaponId, Is.EqualTo("Equipment_Blunt"));
            Assert.That(store.TryEquipItem("weapon_blunt", "equip", store.Progress.Revision), Is.EqualTo(ItemActionResult.AlreadyApplied));
        }

        [TestCase("EquipmentCharacterIds")]
        [TestCase("EquippedItemIds")]
        [TestCase("ItemActionIds")]
        public void EveryEquipmentArrayRequiresAnActualUniqueNonNullJsonField(string field)
        {
            Seed();
            string valid = File.ReadAllText(_path);
            string property = "\"" + field + "\"\\s*:\\s*\\[[^\\]]*\\]";
            Assert.That(Regex.Matches(valid, property).Count, Is.EqualTo(1));
            foreach (string invalid in new[]
            {
                Regex.Replace(valid, ",\\s*" + property, string.Empty),
                Regex.Replace(valid, property, "\"" + field + "\":null"),
                Regex.Replace(valid, property, match => match.Value + "," + match.Value)
            })
            {
                File.WriteAllText(_path, invalid);
                var blocked = new AdventureProgressStore(_loot, _path, _shops, _characters, _quests, _skills, _equipment);
                Assert.That(blocked.Load(), Is.EqualTo(AdventureLoadResult.Corrupt), field);
                Assert.That(blocked.TryConsumeItem("health_potion", "blocked", blocked.Progress.Revision), Is.EqualTo(ItemActionResult.LoadBlocked));
                Assert.That(File.ReadAllText(_path), Is.EqualTo(invalid));
            }
        }

        [TestCase("length")]
        [TestCase("duplicate_character")]
        [TestCase("unknown_character")]
        [TestCase("unknown_item")]
        [TestCase("consumable_equipped")]
        [TestCase("unowned_equipped")]
        [TestCase("duplicate_receipt")]
        public void InvalidEquipmentSourceCannotBeLoadedOrOverwriteTheLiveState(string mutation)
        {
            var store = Seed();
            var hero = store.Progress.GetCharacter("hero");
            var dto = JsonUtility.FromJson<AdventureSaveData>(File.ReadAllText(_path));
            int index = Array.IndexOf(dto.EquipmentCharacterIds, "hero");
            switch (mutation)
            {
                case "length": dto.EquippedItemIds = Array.Empty<string>(); break;
                case "duplicate_character": dto.EquipmentCharacterIds[0] = dto.EquipmentCharacterIds[1]; break;
                case "unknown_character": dto.EquipmentCharacterIds[0] = "absent"; break;
                case "unknown_item": dto.EquippedItemIds[index] = "absent"; break;
                case "consumable_equipped": dto.EquippedItemIds[index] = "health_potion"; break;
                case "unowned_equipped": dto.EquippedItemIds[index] = "weapon_bow"; break;
                case "duplicate_receipt": dto.ItemActionIds = new[] { "same", "same" }; break;
            }
            File.WriteAllText(_path, JsonUtility.ToJson(dto));
            Assert.That(store.Load(), Is.EqualTo(AdventureLoadResult.Corrupt));
            Assert.That(store.Progress.GetCharacter("hero"), Is.SameAs(hero));
            Assert.That(store.CanWrite, Is.False);
        }

        private AdventureProgressStore Open()
        {
            var store = new AdventureProgressStore(_loot, _path, _shops, _characters, _quests, _skills, _equipment);
            var result = store.Load();
            Assert.That(result == AdventureLoadResult.Loaded || result == AdventureLoadResult.Missing, Is.True, result.ToString());
            return store;
        }

        private AdventureProgressStore Seed()
        {
            var store = Open();
            Assert.That(store.TryTalk("mira", "seed", store.Progress.Revision), Is.EqualTo(CharacterActionResult.Saved));
            var dto = JsonUtility.FromJson<AdventureSaveData>(File.ReadAllText(_path));
            dto.MemberGold[Array.IndexOf(dto.MemberIds, "hero")] = 5000;
            dto.ItemIds = new[] { "weapon_sword", "weapon_blunt", "health_potion", "rat_tail" };
            dto.ItemCounts = new[] { 1, 1, 2, 2 };
            AdventureSave.Save(_path, dto, _loot, _shops, _characters, _quests, _skills, _equipment);
            return Open();
        }

        private void SeedLegacy(int version)
        {
            if (version <= 2)
            {
                string stocks = version == 2 ? ",\"MerchantIds\":[\"market\"],\"StockMerchantIds\":[\"market\"],\"StockItemIds\":[\"rat_tail\"],\"StockCounts\":[4],\"TradeIds\":[\"old_trade\"]" : string.Empty;
                File.WriteAllText(_path, "{\"Format\":\"P2.Adventure\",\"Version\":" + version + ",\"MemberIds\":[\"hero\",\"mira\"],\"MemberGold\":[5000,17],\"ItemIds\":[\"rat_tail\",\"health_potion\"],\"ItemCounts\":[2,2],\"SettlementIds\":[\"old\"]" + stocks + "}");
                return;
            }
            var store = new AdventureProgressStore(_loot, _path, _shops, _characters, _quests, _skills);
            Assert.That(store.Load(), Is.EqualTo(AdventureLoadResult.Missing));
            Assert.That(store.TryTalk("mira", "old_talk", store.Progress.Revision), Is.EqualTo(CharacterActionResult.Saved));
            var dto = JsonUtility.FromJson<AdventureSaveData>(File.ReadAllText(_path));
            dto.Version = version;
            dto.MemberGold[Array.IndexOf(dto.MemberIds, "hero")] = 5000;
            dto.ItemIds = new[] { "rat_tail", "health_potion" };
            dto.ItemCounts = new[] { 2, 2 };
            dto.MerchantIds = new[] { "market" };
            dto.StockMerchantIds = new[] { "market" };
            dto.StockItemIds = new[] { "rat_tail" };
            dto.StockCounts = new[] { 4 };
            string raw = JsonUtility.ToJson(dto);
            foreach (string field in new[] { "EquipmentCharacterIds", "EquippedItemIds", "ItemActionIds" })
            {
                raw = Regex.Replace(raw, ",\"" + field + "\":\\[[^\\]]*\\]", string.Empty);
            }
            File.WriteAllText(_path, raw);
        }

        private static string Read(string name) => File.ReadAllText(Path.Combine(Application.dataPath, "1_InternalTools/Util/DataSheetLoader/Table", name));

        private sealed class MinimumRandom : IRandomSource
        {
            public int NextInt(int minimum, int maximum) => minimum;
            public float NextFloat(float minimum, float maximum) => minimum;
        }
    }
}
