using System;
using System.Collections.Generic;
using System.Globalization;
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
    public sealed class CharacterProgressTests
    {
        private TownSheet _town;
        private LootSheet _loot;
        private ShopSheet _shops;
        private CharacterSheet _characters;
        private string _directory;
        private string _path;

        [SetUp]
        public void SetUp()
        {
            _town = new TownSheet(Read("TownLocationTable.csv"), Read("TownCommandTable.csv"), Read("TownCharacterTable.csv"), Read("TownTextTable.csv"));
            var dungeon = new DungeonSheet(Read("DungeonDefinitionTable.csv"), Read("DungeonNodeTable.csv"), Read("DungeonPartyTable.csv"),
                Read("DungeonSettingTable.csv"), Read("DungeonTextTable.csv"), _town);
            _loot = new LootSheet(Read("LootItemTable.csv"), Read("LootDropTable.csv"), Read("LootShareTable.csv"), Read("LootSettingTable.csv"), Read("LootTextTable.csv"), dungeon);
            _shops = new ShopSheet(Read("ShopMerchantTable.csv"), Read("ShopStockTable.csv"), Read("ShopTextTable.csv"), _loot, _town);
            var growth = new CharacterGrowthSheet(Read("CharacterGrowthTable.csv"), Read("CharacterLevelTable.csv"), Read("CharacterGrowthSettingTable.csv"));
            var combat = new CombatStatSheet(Read("CombatStatTable.csv"), Read("CombatEnemyTable.csv"), Read("CombatAttackTable.csv"));
            _characters = new CharacterSheet(Read("CharacterDefinitionTable.csv"), Read("CharacterSettingTable.csv"), Read("CharacterTextTable.csv"), _town, growth, combat);
            _directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "CharacterProgressTests", Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(_directory);
            _path = Path.Combine(_directory, "progress.json");
        }

        [TearDown]
        public void TearDown()
        {
            string allowed = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "CharacterProgressTests")) + Path.DirectorySeparatorChar;
            Assert.That(Path.GetFullPath(_directory).StartsWith(allowed, StringComparison.OrdinalIgnoreCase), Is.True);
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, true);
            }
        }

        [Test]
        public void NewProgressHasSevenCharactersAndWalletsButOnlyThePlayerInParty()
        {
            var store = Open();
            Assert.That(store.Progress.Characters.Count, Is.EqualTo(7));
            Assert.That(store.Progress.Gold.Count, Is.EqualTo(7));
            CollectionAssert.AreEqual(new[] { "hero" }, store.Progress.PartyIds);
            CollectionAssert.AreEquivalent(_characters.Characters.Where(item => !item.IsPlayer).Select(item => item.Id), _town.GetLocation("tavern").VisitorIds);
            foreach (var definition in _characters.Characters)
            {
                var state = store.Progress.GetCharacter(definition.Id);
                Assert.That(state.Affinity, Is.Zero);
                Assert.That(state.Level, Is.EqualTo(1));
                Assert.That(state.Experience, Is.Zero);
                Assert.That(state.Stats.Strength, Is.EqualTo(10));
                Assert.That(state.GrowthStats.Strength, Is.Zero);
                Assert.That(definition.LootWeight, Is.EqualTo(1));
            }
            Assert.That(File.Exists(_path), Is.False);
        }

        [TestCase(1)]
        [TestCase(2)]
        public void LegacyMigrationPreservesWealthAndStockWithoutWritingUntilAnActionSucceeds(int version)
        {
            string extra = version == 2 ? ",\"MerchantIds\":[\"market\",\"tavern\"],\"StockMerchantIds\":[\"market\"],\"StockItemIds\":[\"health_potion\"],\"StockCounts\":[3],\"TradeIds\":[\"old_trade\"]" : string.Empty;
            string legacy = "{\"Format\":\"P2.Adventure\",\"Version\":" + version + ",\"MemberIds\":[\"hero\",\"mira\"],\"MemberGold\":[37,9],\"ItemIds\":[\"rat_tail\"],\"ItemCounts\":[2],\"SettlementIds\":[\"old_settlement\"]" + extra + "}";
            File.WriteAllText(_path, legacy);
            var store = Open();
            Assert.That(File.ReadAllText(_path), Is.EqualTo(legacy));
            Assert.That(store.Progress.PlayerGold, Is.EqualTo(37));
            Assert.That(store.Progress.GetGold("mira"), Is.EqualTo(9));
            Assert.That(store.Progress.GetGold("cass"), Is.Zero);
            Assert.That(store.Progress.Items["rat_tail"], Is.EqualTo(2));
            CollectionAssert.AreEqual(new[] { "hero" }, store.Progress.PartyIds);
            Assert.That(store.Progress.GetMerchantStock("market")["health_potion"], Is.EqualTo(version == 2 ? 3 : 10));
            Assert.That(store.TryTalk("mira", "migrated_talk", store.Progress.Revision), Is.EqualTo(CharacterActionResult.Saved));
            var written = JsonUtility.FromJson<AdventureSaveData>(File.ReadAllText(_path));
            Assert.That(written.Version, Is.EqualTo(AdventureSave.CurrentVersion));
            Assert.That(written.MemberIds.Length, Is.EqualTo(7));
            Assert.That(written.CharacterIds.Length, Is.EqualTo(7));
            var reloaded = Open();
            Assert.That(reloaded.Progress.GetCharacter("mira").Affinity, Is.EqualTo(10));
            Assert.That(reloaded.Progress.GetGold("mira"), Is.EqualTo(9));
            Assert.That(reloaded.Progress.HasSettlement("old_settlement"), Is.True);
            if (version == 2)
            {
                Assert.That(reloaded.Progress.HasTrade("old_trade"), Is.True);
                Assert.That(reloaded.Progress.GetMerchantStock("tavern"), Is.Empty);
            }
        }

        [Test]
        public void TalkingCapsAffinityRejectsSelfAndOldRequestsAndPersistsIdempotency()
        {
            var store = Open();
            int originalRevision = store.Progress.Revision;
            Assert.That(store.TryTalk("hero", "self", originalRevision), Is.EqualTo(CharacterActionResult.InvalidTarget));
            Assert.That(store.TryTalk("missing", "missing", originalRevision), Is.EqualTo(CharacterActionResult.InvalidTarget));
            Assert.That(store.TryTalk("mira", "first", originalRevision), Is.EqualTo(CharacterActionResult.Saved));
            Assert.That(store.TryTalk("mira", "first", originalRevision), Is.EqualTo(CharacterActionResult.AlreadyApplied));
            Assert.That(store.TryTalk("mira", "stale", originalRevision), Is.EqualTo(CharacterActionResult.InvalidRequest));
            for (int i = 0; i < 12; i++)
            {
                Assert.That(store.TryTalk("mira", "talk_" + i, store.Progress.Revision), Is.EqualTo(CharacterActionResult.Saved));
            }
            Assert.That(store.Progress.GetCharacter("mira").Affinity, Is.EqualTo(100));
            var reloaded = Open();
            Assert.That(reloaded.TryTalk("mira", "first", reloaded.Progress.Revision), Is.EqualTo(CharacterActionResult.AlreadyApplied));
            Assert.That(reloaded.Progress.GetCharacter("mira").Affinity, Is.EqualTo(100));
        }

        [Test]
        public void RecruitmentHasExactZeroAndHundredPercentEdgesAndRecordsFailures()
        {
            var store = Open();
            Assert.That(store.TryRecruit("mira", "zero", store.Progress.Revision, 0), Is.EqualTo(CharacterActionResult.RecruitFailed));
            Assert.That(store.Progress.IsInParty("mira"), Is.False);
            store = SeedAffinity(100);
            Assert.That(store.TryRecruit("mira", "zero", store.Progress.Revision, 0), Is.EqualTo(CharacterActionResult.AlreadyApplied));
            Assert.That(store.Progress.IsInParty("mira"), Is.False);
            Assert.That(store.TryRecruit("mira", "hundred", store.Progress.Revision, 99), Is.EqualTo(CharacterActionResult.Saved));
            Assert.That(store.Progress.IsInParty("mira"), Is.True);
            Assert.That(store.TryRecruit("mira", "duplicate_member", store.Progress.Revision, 0), Is.EqualTo(CharacterActionResult.AlreadyInParty));
        }

        [Test]
        public void PartyHasAtMostFourAndDismissingCannotRemovePlayerOrEraseNpcProgress()
        {
            var store = SeedAffinity(100);
            foreach (string id in new[] { "mira", "orin", "bran" })
            {
                Assert.That(store.TryRecruit(id, "join_" + id, store.Progress.Revision, 99), Is.EqualTo(CharacterActionResult.Saved));
            }
            Assert.That(store.Progress.PartyIds.Count, Is.EqualTo(4));
            Assert.That(store.TryRecruit("sera", "full", store.Progress.Revision, 0), Is.EqualTo(CharacterActionResult.PartyFull));
            Assert.That(store.TryDismiss("hero", "dismiss_hero", store.Progress.Revision), Is.EqualTo(CharacterActionResult.InvalidTarget));
            Assert.That(store.TryDismiss("sera", "not_member", store.Progress.Revision), Is.EqualTo(CharacterActionResult.NotInParty));
            store.TryGrantExperience("mira_exp", new Dictionary<string, float> { { "mira", 8 } }, new CountingRandom());
            Assert.That(store.TryDismiss("mira", "dismiss_mira", store.Progress.Revision), Is.EqualTo(CharacterActionResult.Saved));
            Assert.That(store.Progress.GetCharacter("mira").Affinity, Is.EqualTo(100));
            Assert.That(store.Progress.GetCharacter("mira").Experience, Is.EqualTo(8));
            Assert.That(store.TryRecruit("sera", "join_sera", store.Progress.Revision, 99), Is.EqualTo(CharacterActionResult.Saved));
            var reloaded = Open();
            CollectionAssert.AreEqual(new[] { "hero", "orin", "bran", "sera" }, reloaded.Progress.PartyIds);
            Assert.That(reloaded.Progress.GetCharacter("mira").Experience, Is.EqualTo(8));
        }

        [Test]
        public void ExperienceUsesEachCharactersGrantAndDuplicateRewardsDoNotGrowAgain()
        {
            var store = Open();
            var random = new CountingRandom();
            var grants = new Dictionary<string, float> { { "hero", 30 }, { "mira", 15.5f } };
            Assert.That(store.TryGrantExperience("reward", grants, random), Is.EqualTo(CharacterActionResult.Saved));
            Assert.That(store.Progress.GetCharacter("hero").Level, Is.EqualTo(2));
            Assert.That(store.Progress.GetCharacter("hero").Experience, Is.Zero);
            Assert.That(store.Progress.GetCharacter("hero").Stats.Strength, Is.EqualTo(12));
            Assert.That(store.Progress.GetCharacter("mira").Experience, Is.EqualTo(15.5f));
            Assert.That(random.Calls, Is.EqualTo(6));
            var reloaded = Open();
            Assert.That(reloaded.TryGrantExperience("reward", grants, random), Is.EqualTo(CharacterActionResult.AlreadyApplied));
            Assert.That(random.Calls, Is.EqualTo(6));
            Assert.That(reloaded.Progress.GetCharacter("hero").Stats.Strength, Is.EqualTo(12));
            Assert.That(reloaded.Progress.GetCharacter("hero").GrowthStats.Strength, Is.EqualTo(2));
        }

        [Test]
        public void SavedGrowthUsesCurrentBaseAndCombatFormulaWithoutRerollingOrRewritingOnLoad()
        {
            var store = Open();
            var random = new CountingRandom();
            Assert.That(store.TryGrantExperience("growth_source", new Dictionary<string, float> { { "hero", 30 } }, random), Is.EqualTo(CharacterActionResult.Saved));
            string originalSave = File.ReadAllText(_path);
            var dto = JsonUtility.FromJson<AdventureSaveData>(originalSave);
            int heroIndex = Array.IndexOf(dto.CharacterIds, "hero");
            Assert.That(dto.CharacterStrengthGrowth[heroIndex], Is.EqualTo(2));
            StringAssert.DoesNotContain("\"CharacterStrength\":", originalSave);

            string definitions = Read("CharacterDefinitionTable.csv").Replace(",10,10,10,10,10,10,", ",20,20,20,20,20,20,");
            var changedCharacters = new CharacterSheet(definitions, Read("CharacterSettingTable.csv"), Read("CharacterTextTable.csv"),
                _town, _characters.Growth, _characters.Combat);
            var changedStore = new AdventureProgressStore(_loot, _path, _shops, changedCharacters);
            Assert.That(changedStore.Load(), Is.EqualTo(AdventureLoadResult.Loaded));
            var hero = changedStore.Progress.GetCharacter("hero");
            Assert.That(hero.Level, Is.EqualTo(2));
            Assert.That(hero.Experience, Is.Zero);
            Assert.That(hero.GrowthStats.Strength, Is.EqualTo(2));
            Assert.That(hero.Stats.Strength, Is.EqualTo(22));
            Assert.That(hero.Stats.Constitution, Is.EqualTo(22));
            Assert.That(random.Calls, Is.EqualTo(6));
            Assert.That(File.ReadAllText(_path), Is.EqualTo(originalSave));

            string combatCsv = Read("CombatStatTable.csv");
            string oldRow = combatCsv.Split('\n').Single(row => row.StartsWith("PhysicalAttackPower,", StringComparison.Ordinal));
            string[] cells = oldRow.Split(',');
            cells[4] = (float.Parse(cells[4], CultureInfo.InvariantCulture) + 1).ToString(CultureInfo.InvariantCulture);
            var changedCombat = new CombatStatSheet(combatCsv.Replace(oldRow, string.Join(",", cells)),
                Read("CombatEnemyTable.csv"), Read("CombatAttackTable.csv"));
            float originalAttack = new CombatStatCalculator(_characters.Combat).Calculate(hero.Stats).PhysicalAttackPower;
            Assert.That(new CombatStatCalculator(changedCombat).Calculate(hero.Stats).PhysicalAttackPower, Is.EqualTo(originalAttack + 22));

            Assert.That(changedStore.TryTalk("mira", "after_base_change", changedStore.Progress.Revision), Is.EqualTo(CharacterActionResult.Saved));
            dto = JsonUtility.FromJson<AdventureSaveData>(File.ReadAllText(_path));
            Assert.That(dto.CharacterStrengthGrowth[Array.IndexOf(dto.CharacterIds, "hero")], Is.EqualTo(2));
            var again = new AdventureProgressStore(_loot, _path, _shops, changedCharacters);
            Assert.That(again.Load(), Is.EqualTo(AdventureLoadResult.Loaded));
            Assert.That(again.Progress.GetCharacter("hero").Stats.Strength, Is.EqualTo(22));
        }

        [Test]
        public void FractionalBasePersistsTheExactRolledIncreaseInsteadOfSubtractingFinalFloats()
        {
            string definitions = Read("CharacterDefinitionTable.csv").Replace(",10,10,10,10,10,10,", ",1.1,1.1,1.1,1.1,1.1,1.1,");
            var fractional = new CharacterSheet(definitions, Read("CharacterSettingTable.csv"), Read("CharacterTextTable.csv"),
                _town, _characters.Growth, _characters.Combat);
            var store = new AdventureProgressStore(_loot, _path, _shops, fractional);
            Assert.That(store.Load(), Is.EqualTo(AdventureLoadResult.Missing));
            Assert.That(store.TryGrantExperience("fractional", new Dictionary<string, float> { { "hero", 30 } }, new CountingRandom()), Is.EqualTo(CharacterActionResult.Saved));
            var dto = JsonUtility.FromJson<AdventureSaveData>(File.ReadAllText(_path));
            Assert.That(dto.CharacterStrengthGrowth[Array.IndexOf(dto.CharacterIds, "hero")], Is.EqualTo(2f));
            var reloaded = new AdventureProgressStore(_loot, _path, _shops, fractional);
            Assert.That(reloaded.Load(), Is.EqualTo(AdventureLoadResult.Loaded));
            Assert.That(reloaded.Progress.GetCharacter("hero").GrowthStats.Strength, Is.EqualTo(2f));
            Assert.That(reloaded.Progress.GetCharacter("hero").Stats.Strength, Is.EqualTo(1.1f + 2f));
        }

        [Test]
        public void LoweredExperienceRequirementClampsLoadAndGrowsOnlyOnTheNextPositiveGrant()
        {
            var store = Open();
            var random = new CountingRandom();
            Assert.That(store.TryGrantExperience("before_table_change", new Dictionary<string, float> { { "hero", 24 } }, random), Is.EqualTo(CharacterActionResult.Saved));
            string saved = File.ReadAllText(_path);
            Assert.That(random.Calls, Is.Zero);
            var preservingCaller = new AdventureProgressStore(_loot, _path, _shops);
            Assert.That(preservingCaller.Load(), Is.EqualTo(AdventureLoadResult.Loaded));
            Assert.That(preservingCaller.Progress.GetCharacter("hero").Experience, Is.EqualTo(24));

            var changed = WithExperienceRequirements(Read("CharacterLevelTable.csv").Replace("1,30", "1,20"));
            var reloaded = new AdventureProgressStore(_loot, _path, _shops, changed);
            Assert.That(reloaded.Load(), Is.EqualTo(AdventureLoadResult.Loaded));
            var hero = reloaded.Progress.GetCharacter("hero");
            Assert.That(hero.Level, Is.EqualTo(1));
            Assert.That(hero.Experience, Is.EqualTo(20));
            Assert.That(hero.GrowthStats.Strength, Is.Zero);
            Assert.That(File.ReadAllText(_path), Is.EqualTo(saved));
            Assert.That(random.Calls, Is.Zero);
            Assert.That(reloaded.TryGrantExperience("zero_grant", new Dictionary<string, float> { { "hero", 0 } }, random), Is.EqualTo(CharacterActionResult.Saved));
            Assert.That(reloaded.Progress.GetCharacter("hero").Level, Is.EqualTo(1));
            Assert.That(random.Calls, Is.Zero);
            Assert.That(reloaded.TryGrantExperience("next_grant", new Dictionary<string, float> { { "hero", 1 } }, random), Is.EqualTo(CharacterActionResult.Saved));
            Assert.That(reloaded.Progress.GetCharacter("hero").Level, Is.EqualTo(2));
            Assert.That(reloaded.Progress.GetCharacter("hero").Experience, Is.Zero);
            Assert.That(reloaded.Progress.GetCharacter("hero").GrowthStats.Strength, Is.EqualTo(2));
            Assert.That(random.Calls, Is.EqualTo(6));
        }

        [Test]
        public void ExactStoredCapIsValidAndMaximumLevelClampsWithoutGrowing()
        {
            SeedAffinity(0);
            var dto = JsonUtility.FromJson<AdventureSaveData>(File.ReadAllText(_path));
            int index = Array.IndexOf(dto.CharacterIds, "hero");
            dto.CharacterExperience[index] = 30;
            AdventureSave.Save(_path, dto, _loot, _shops, _characters);
            var capped = Open();
            Assert.That(capped.Progress.GetCharacter("hero").Level, Is.EqualTo(1));
            Assert.That(capped.Progress.GetCharacter("hero").Experience, Is.EqualTo(30));
            Assert.That(capped.TryTalk("mira", "talk_at_cap", capped.Progress.Revision), Is.EqualTo(CharacterActionResult.Saved));

            dto = JsonUtility.FromJson<AdventureSaveData>(File.ReadAllText(_path));
            dto.CharacterLevels[index] = _characters.Growth.MaxLevel;
            dto.CharacterExperience[index] = _characters.Growth.RequiredExperience(_characters.Growth.MaxLevel);
            AdventureSave.Save(_path, dto, _loot, _shops, _characters);
            string saved = File.ReadAllText(_path);
            var changed = WithExperienceRequirements(Read("CharacterLevelTable.csv").Replace("12,9457", "12,100"));
            var reloaded = new AdventureProgressStore(_loot, _path, _shops, changed);
            Assert.That(reloaded.Load(), Is.EqualTo(AdventureLoadResult.Loaded));
            Assert.That(reloaded.Progress.GetCharacter("hero").Experience, Is.EqualTo(100));
            Assert.That(File.ReadAllText(_path), Is.EqualTo(saved));
            var random = new CountingRandom();
            Assert.That(reloaded.TryGrantExperience("at_maximum", new Dictionary<string, float> { { "hero", 8 } }, random), Is.EqualTo(CharacterActionResult.Saved));
            Assert.That(reloaded.Progress.GetCharacter("hero").Level, Is.EqualTo(12));
            Assert.That(reloaded.Progress.GetCharacter("hero").Experience, Is.EqualTo(100));
            Assert.That(reloaded.Progress.GetCharacter("hero").GrowthStats.Strength, Is.Zero);
            Assert.That(random.Calls, Is.Zero);
        }

        [Test]
        public void FailedExperienceSaveKeepsOriginalStateAndRetriesTheSameRandomGrowth()
        {
            var store = Open();
            var firstRandom = new CountingRandom();
            var grants = new Dictionary<string, float> { { "hero", 30 } };
            Directory.CreateDirectory(_path + ".tmp");
            Assert.That(store.TryGrantExperience("retry_exp", grants, firstRandom), Is.EqualTo(CharacterActionResult.SaveFailed));
            Assert.That(firstRandom.Calls, Is.EqualTo(6));
            Assert.That(store.Progress.GetCharacter("hero").Level, Is.EqualTo(1));
            Assert.That(File.Exists(_path), Is.False);
            Assert.That(store.TryGrantExperience("retry_exp", new Dictionary<string, float> { { "hero", 31 } }, firstRandom), Is.EqualTo(CharacterActionResult.InvalidRequest));
            Directory.Delete(_path + ".tmp");
            var differentRandom = new CountingRandom(true);
            Assert.That(store.TryGrantExperience("retry_exp", grants, differentRandom), Is.EqualTo(CharacterActionResult.Saved));
            Assert.That(differentRandom.Calls, Is.Zero);
            Assert.That(store.Progress.GetCharacter("hero").Stats.Strength, Is.EqualTo(12));
        }

        [Test]
        public void FailedCharacterActionPreservesFileAndMemoryAndInvalidatesOldTradeOnlyAfterSuccess()
        {
            var store = SeedAffinity(50);
            string saved = File.ReadAllText(_path);
            var shop = new ShopDataContainer(_shops, "market", store.Progress);
            shop.TrySelectItem("health_potion", true);
            var trade = shop.CreateTrade();
            int revision = store.Progress.Revision;
            Directory.CreateDirectory(_path + ".tmp");
            Assert.That(store.TryTalk("mira", "retry_talk", revision), Is.EqualTo(CharacterActionResult.SaveFailed));
            Assert.That(store.Progress.GetCharacter("mira").Affinity, Is.EqualTo(50));
            Assert.That(store.Progress.Revision, Is.EqualTo(revision));
            Assert.That(File.ReadAllText(_path), Is.EqualTo(saved));
            Directory.Delete(_path + ".tmp");
            Assert.That(store.TryTalk("mira", "retry_talk", revision), Is.EqualTo(CharacterActionResult.Saved));
            Assert.That(store.TryTrade(trade), Is.EqualTo(ShopTradeResult.InvalidSelection));
        }

        [Test]
        public void OptionalCharacterSheetDoesNotDiscardSavedCharactersPartyOrExpandedWallets()
        {
            var store = SeedAffinity(100);
            store.TryRecruit("neri", "join_neri", store.Progress.Revision, 99);
            store.TryGrantExperience("neri_exp", new Dictionary<string, float> { { "neri", 30 } }, new CountingRandom());
            var legacyCaller = new AdventureProgressStore(_loot, _path, _shops);
            Assert.That(legacyCaller.Load(), Is.EqualTo(AdventureLoadResult.Loaded));
            var source = legacyCaller.Progress.GetCharacter("neri");
            Assert.That(source.Level, Is.EqualTo(2));
            Assert.That(source.Experience, Is.Zero);
            Assert.That(source.Affinity, Is.EqualTo(100));
            Assert.That(source.GrowthStats.Strength, Is.EqualTo(2));
            Assert.Throws<InvalidOperationException>(() => { var ignored = source.Stats; });
            Assert.Throws<InvalidOperationException>(() => { var ignored = source.Growth; });
            var settlement = new LootSettlement(_loot, "compatibility", new Dictionary<string, int> { { "rat_tail", 2 } }, legacyCaller.Progress);
            Assert.That(legacyCaller.TryCommit(settlement), Is.EqualTo(SettlementCommitResult.Saved));
            var reloaded = Open();
            CollectionAssert.AreEqual(new[] { "hero", "neri" }, reloaded.Progress.PartyIds);
            Assert.That(reloaded.Progress.GetCharacter("neri").Level, Is.EqualTo(2));
            Assert.That(reloaded.Progress.GetCharacter("neri").Stats.Strength, Is.EqualTo(12));
            Assert.That(reloaded.Progress.Gold.Count, Is.EqualTo(7));
        }

        [TestCase("CharacterIds")]
        [TestCase("CharacterLevels")]
        [TestCase("CharacterExperience")]
        [TestCase("CharacterAffinity")]
        [TestCase("CharacterStrengthGrowth")]
        [TestCase("CharacterConstitutionGrowth")]
        [TestCase("CharacterWillPowerGrowth")]
        [TestCase("CharacterIntelligenceGrowth")]
        [TestCase("CharacterAgilityGrowth")]
        [TestCase("CharacterPerceptionGrowth")]
        [TestCase("PartyIds")]
        [TestCase("CharacterActionIds")]
        public void VersionThreeRequiresEveryCharacterArrayAndRejectsNull(string field)
        {
            SeedAffinity(0);
            string valid = File.ReadAllText(_path);
            string pattern = ",\\s*\"" + Regex.Escape(field) + "\"\\s*:\\s*\\[[^\\]]*\\]";
            Assert.That(Regex.Matches(valid, pattern).Count, Is.EqualTo(1));
            foreach (string replacement in new[] { string.Empty, ",\"" + field + "\":null" })
            {
                string contents = Regex.Replace(valid, pattern, replacement);
                File.WriteAllText(_path, contents);
                Assert.That(AdventureSave.Load(_path, _loot, out var dto, _shops, _characters), Is.EqualTo(AdventureLoadResult.Corrupt));
                Assert.That(dto, Is.Null);
                Assert.That(File.ReadAllText(_path), Is.EqualTo(contents));
            }
        }

        [TestCase("duplicate_character")]
        [TestCase("level")]
        [TestCase("negative_exp")]
        [TestCase("experience_global_overflow")]
        [TestCase("affinity")]
        [TestCase("stat")]
        [TestCase("stat_overflow")]
        [TestCase("base_plus_growth_overflow")]
        [TestCase("party_empty")]
        [TestCase("party_duplicate")]
        [TestCase("player_missing")]
        [TestCase("party_five")]
        [TestCase("wallet_missing")]
        public void InvalidCharacterStateOrPartyIsRejectedWithoutChangingTheLiveState(string mutation)
        {
            var store = SeedAffinity(0);
            var dto = JsonUtility.FromJson<AdventureSaveData>(File.ReadAllText(_path));
            switch (mutation)
            {
                case "duplicate_character": dto.CharacterIds[1] = dto.CharacterIds[0]; break;
                case "level": dto.CharacterLevels[0] = 0; break;
                case "negative_exp": dto.CharacterExperience[0] = -1; break;
                case "experience_global_overflow": dto.CharacterExperience[0] = AdventureSave.MaximumStoredExperience * 2; break;
                case "affinity": dto.CharacterAffinity[0] = 101; break;
                case "stat": dto.CharacterStrengthGrowth[0] = -1; break;
                case "stat_overflow": dto.CharacterConstitutionGrowth[0] = 1e38f; break;
                case "base_plus_growth_overflow": dto.CharacterConstitutionGrowth[0] = 10000000; break;
                case "party_empty": dto.PartyIds = Array.Empty<string>(); break;
                case "party_duplicate": dto.PartyIds = new[] { "hero", "mira", "mira" }; break;
                case "player_missing": dto.PartyIds = new[] { "mira" }; break;
                case "party_five": dto.PartyIds = new[] { "hero", "mira", "orin", "bran", "sera" }; break;
                case "wallet_missing": dto.MemberIds = new[] { "hero", "mira" }; dto.MemberGold = new long[] { 0, 0 }; break;
            }
            string corrupt = JsonUtility.ToJson(dto);
            File.WriteAllText(_path, corrupt);
            Assert.That(store.Load(), Is.EqualTo(AdventureLoadResult.Corrupt));
            Assert.That(store.CanWrite, Is.False);
            CollectionAssert.AreEqual(new[] { "hero" }, store.Progress.PartyIds);
            Assert.That(store.Progress.Characters.Count, Is.EqualTo(7));
            Assert.That(File.ReadAllText(_path), Is.EqualTo(corrupt));
        }

        private AdventureProgressStore SeedAffinity(int affinity)
        {
            var store = Open();
            Assert.That(store.TryTalk("mira", "seed_" + Guid.NewGuid().ToString("N"), store.Progress.Revision), Is.EqualTo(CharacterActionResult.Saved));
            var dto = JsonUtility.FromJson<AdventureSaveData>(File.ReadAllText(_path));
            for (int i = 0; i < dto.CharacterIds.Length; i++)
            {
                dto.CharacterAffinity[i] = dto.CharacterIds[i] == _characters.PlayerId ? 0 : affinity;
            }
            AdventureSave.Save(_path, dto, _loot, _shops, _characters);
            return Open();
        }

        private CharacterSheet WithExperienceRequirements(string levels)
        {
            var growth = new CharacterGrowthSheet(Read("CharacterGrowthTable.csv"), levels, Read("CharacterGrowthSettingTable.csv"));
            return new CharacterSheet(Read("CharacterDefinitionTable.csv"), Read("CharacterSettingTable.csv"), Read("CharacterTextTable.csv"),
                _town, growth, _characters.Combat);
        }

        private AdventureProgressStore Open()
        {
            var store = new AdventureProgressStore(_loot, _path, _shops, _characters);
            var result = store.Load();
            Assert.That(result == AdventureLoadResult.Loaded || result == AdventureLoadResult.Missing, Is.True, result.ToString());
            return store;
        }

        private static string Read(string name) => File.ReadAllText(Path.Combine(Application.dataPath, "1_InternalTools/Util/DataSheetLoader/Table", name));

        private sealed class CountingRandom : IRandomSource
        {
            private readonly bool _maximum;
            public int Calls { get; private set; }
            public CountingRandom(bool maximum = false) => _maximum = maximum;
            public int NextInt(int minimum, int maximum)
            {
                Calls++;
                return _maximum ? maximum - 1 : minimum;
            }
            public float NextFloat(float minimum, float maximum)
            {
                Calls++;
                return _maximum ? maximum : minimum;
            }
        }
    }
}
