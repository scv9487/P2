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
    public sealed class QuestEditModeTests
    {
        private DungeonSheet _dungeon;
        private LootSheet _loot;
        private ShopSheet _shops;
        private CharacterSheet _characters;
        private QuestSheet _quests;
        private string _directory;
        private string _path;

        [SetUp]
        public void SetUp()
        {
            var town = new TownSheet(Read("TownLocationTable.csv"), Read("TownCommandTable.csv"), Read("TownCharacterTable.csv"), Read("TownTextTable.csv"));
            _dungeon = new DungeonSheet(Read("DungeonDefinitionTable.csv"), Read("DungeonNodeTable.csv"), Read("DungeonPartyTable.csv"),
                Read("DungeonSettingTable.csv"), Read("DungeonTextTable.csv"), town);
            _loot = new LootSheet(Read("LootItemTable.csv"), Read("LootDropTable.csv"), Read("LootShareTable.csv"), Read("LootSettingTable.csv"), Read("LootTextTable.csv"), _dungeon);
            _shops = new ShopSheet(Read("ShopMerchantTable.csv"), Read("ShopStockTable.csv"), Read("ShopTextTable.csv"), _loot, town);
            var growth = new CharacterGrowthSheet(Read("CharacterGrowthTable.csv"), Read("CharacterLevelTable.csv"), Read("CharacterGrowthSettingTable.csv"));
            var combat = new CombatStatSheet(Read("CombatStatTable.csv"), Read("CombatEnemyTable.csv"), Read("CombatAttackTable.csv"));
            _characters = new CharacterSheet(Read("CharacterDefinitionTable.csv"), Read("CharacterSettingTable.csv"), Read("CharacterTextTable.csv"), town, growth, combat);
            _quests = CreateSheet();
            _directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "QuestEditModeTests", Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(_directory);
            _path = Path.Combine(_directory, "progress.json");
        }

        [TearDown]
        public void TearDown()
        {
            string allowed = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "QuestEditModeTests")) + Path.DirectorySeparatorChar;
            Assert.That(Path.GetFullPath(_directory).StartsWith(allowed, StringComparison.OrdinalIgnoreCase), Is.True);
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, true);
            }
        }

        [Test]
        public void AuthoredQuestsHaveFourKindsAndRescueDefinitionDoesNotChangeTheOriginalGraph()
        {
            Assert.That(_quests.Quests.Count, Is.EqualTo(4));
            CollectionAssert.AreEquivalent(Enum.GetValues(typeof(QuestKind)), _quests.Quests.Select(item => item.Kind));
            CollectionAssert.AreEqual(new long[] { 100, 40, 80, 50 }, _quests.Quests.Select(item => item.RewardGold));
            Assert.That(_quests.GetQuest("deliver_tails").TargetName, Is.EqualTo(_loot.GetItem("rat_tail").Name));
            var rescue = _quests.GetRescueNode("rescue_adventurers");
            Assert.That(rescue.DungeonId, Is.EqualTo(1));
            Assert.That(rescue.Floor, Is.EqualTo(1));
            Assert.That(rescue.EnemyCount, Is.EqualTo(2));
            Assert.That(_dungeon.GetNode(1, rescue.AnchorId), Is.Not.Null);
            Assert.That(_dungeon.GetNodes(1).Any(node => node.Id == rescue.Id), Is.False);
        }

        [TestCase("duplicate")]
        [TestCase("kind")]
        [TestCase("item")]
        [TestCase("monster")]
        [TestCase("dungeon")]
        [TestCase("negative_reward")]
        [TestCase("zero_count")]
        [TestCase("anchor")]
        [TestCase("collision")]
        [TestCase("rescue_missing")]
        [TestCase("enemy_count")]
        [TestCase("text_missing")]
        public void InvalidQuestContentFailsAtLoad(string mutation)
        {
            string definitions = Read("QuestDefinitionTable.csv");
            string nodes = Read("QuestRescueNodeTable.csv");
            string texts = Read("QuestTextTable.csv");
            switch (mutation)
            {
                case "duplicate": definitions += "\n" + definitions.Split('\n')[1]; break;
                case "kind": definitions = definitions.Replace(",ClearDungeon,", ",Unsupported,"); break;
                case "item": definitions = definitions.Replace(",rat_tail,", ",missing,"); break;
                case "monster": definitions = definitions.Replace(",gray_rat,", ",grey_rat,"); break;
                case "dungeon": definitions = definitions.Replace(",ClearDungeon,1,", ",ClearDungeon,999,"); break;
                case "negative_reward": definitions = definitions.Replace(",1,100", ",1,-1"); break;
                case "zero_count": definitions = definitions.Replace(",rat_tail,3,", ",rat_tail,0,"); break;
                case "anchor": nodes = nodes.Replace(",cache,", ",absent,"); break;
                case "collision": nodes = nodes.Replace(",rescue,", ",camp,"); definitions = definitions.Replace(",rescue,", ",camp,"); break;
                case "rescue_missing": nodes = nodes.Split('\n')[0]; break;
                case "enemy_count": nodes = nodes.Replace(",cache,2", ",cache,4"); break;
                case "text_missing": texts = string.Join("\n", texts.Split('\n').Where(line => !line.StartsWith("board.title,"))); break;
            }
            Assert.That(() => CreateSheet(definitions, nodes, texts), Throws.Exception);
        }

        [Test]
        public void AcceptanceIsSingleActiveAtomicAndRevisionCheckedAndDuplicateSafe()
        {
            var store = Open();
            int oldRevision = store.Progress.Revision;
            Assert.That(store.Progress.Quest.ActiveQuestId, Is.Empty);
            Assert.That(store.TryAcceptQuest("kill_rats", "accept", oldRevision), Is.EqualTo(QuestActionResult.Saved));
            Assert.That(store.Progress.Quest.AcceptanceId, Is.EqualTo("accept"));
            string saved = File.ReadAllText(_path);
            Assert.That(store.TryAcceptQuest("kill_rats", "accept", oldRevision), Is.EqualTo(QuestActionResult.AlreadyApplied));
            Assert.That(store.TryAcceptQuest("deliver_tails", "stale", oldRevision), Is.EqualTo(QuestActionResult.InvalidRequest));
            Assert.That(store.TryAcceptQuest("deliver_tails", "other", store.Progress.Revision), Is.EqualTo(QuestActionResult.ActiveQuestExists));
            Assert.That(store.TryAcceptQuest("missing", "bad", store.Progress.Revision), Is.EqualTo(QuestActionResult.InvalidRequest));
            Assert.That(File.ReadAllText(_path), Is.EqualTo(saved));
            Assert.That(Open().Progress.Quest.ActiveQuestId, Is.EqualTo("kill_rats"));
        }

        [Test]
        public void KillProgressOnlyCountsMatchingFactsFromThisAcceptanceAndCapsAtTheRequirement()
        {
            var store = Open();
            var before = new QuestFact(QuestFactType.KillMonster, "before", 1, "gray_rat", 2, string.Empty);
            Assert.That(store.TryApplyQuestFact(before), Is.EqualTo(QuestActionResult.Ignored));
            Accept(store, "kill_rats");
            Assert.That(store.TryApplyQuestFact(before), Is.EqualTo(QuestActionResult.Ignored));
            Assert.That(store.TryApplyQuestFact(Fact(store, "old", acceptance: "other_acceptance")), Is.EqualTo(QuestActionResult.Ignored));
            Assert.That(store.TryApplyQuestFact(Fact(store, "wrong", target: "grey_rat")), Is.EqualTo(QuestActionResult.Ignored));
            Assert.That(store.TryApplyQuestFact(Fact(store, "wrong_type", type: QuestFactType.AdventurersRescued)), Is.EqualTo(QuestActionResult.Ignored));
            var first = Fact(store, "kill_one", dungeon: 6);
            Assert.That(store.TryApplyQuestFact(first), Is.EqualTo(QuestActionResult.Saved));
            Assert.That(store.TryApplyQuestFact(first), Is.EqualTo(QuestActionResult.AlreadyApplied));
            Assert.That(store.Progress.Quest.ProgressCount, Is.EqualTo(1));
            store = Open();
            Assert.That(store.TryApplyQuestFact(first), Is.EqualTo(QuestActionResult.AlreadyApplied));
            Assert.That(store.TryApplyQuestFact(Fact(store, "many", count: 1000000)), Is.EqualTo(QuestActionResult.Saved));
            Assert.That(store.Progress.Quest.ProgressCount, Is.EqualTo(3));
            Assert.That(store.TryApplyQuestFact(Fact(store, "after_ready")), Is.EqualTo(QuestActionResult.Ignored));
            Assert.That(QuestRules.CanReport(_quests.GetQuest("kill_rats"), store.Progress), Is.True);
        }

        [TestCase("clear_dungeon", QuestFactType.DungeonCleared, "", 0)]
        [TestCase("rescue_adventurers", QuestFactType.AdventurersRescued, "rescue", 1)]
        public void DungeonAndRescueObjectivesUseTheConfiguredDungeonFloorAndTarget(string id, QuestFactType type, string target, int floor)
        {
            var store = Open();
            Accept(store, id);
            Assert.That(store.TryApplyQuestFact(Fact(store, "wrong_dungeon", type, target, dungeon: 2, floor: floor)), Is.EqualTo(QuestActionResult.Ignored));
            Assert.That(store.TryApplyQuestFact(Fact(store, "wrong_target", type, "other", floor: floor)), Is.EqualTo(QuestActionResult.Ignored));
            if (floor != 0)
            {
                Assert.That(store.TryApplyQuestFact(Fact(store, "wrong_floor", type, target, floor: 2)), Is.EqualTo(QuestActionResult.Ignored));
            }
            Assert.That(store.TryReportQuest(id, "not_ready", store.Progress.Revision), Is.EqualTo(QuestActionResult.NotReady));
            Assert.That(store.TryApplyQuestFact(Fact(store, "done", type, target, floor: floor)), Is.EqualTo(QuestActionResult.Saved));
            Assert.That(store.TryReportQuest(id, "report", store.Progress.Revision), Is.EqualTo(QuestActionResult.Saved));
            Assert.That(store.Progress.PlayerGold, Is.EqualTo(_quests.GetQuest(id).RewardGold));
            Assert.That(store.Progress.GetGold("mira"), Is.Zero);
        }

        [Test]
        public void ReportingRewardsOnlyOnceAndACompletedQuestCannotBeAcceptedAgain()
        {
            var store = Open();
            Accept(store, "kill_rats");
            var fact = Fact(store, "done", count: 3);
            Assert.That(store.TryApplyQuestFact(fact), Is.EqualTo(QuestActionResult.Saved));
            Assert.That(store.Progress.PlayerGold, Is.Zero);
            int revision = store.Progress.Revision;
            Assert.That(store.TryReportQuest("kill_rats", "report", revision), Is.EqualTo(QuestActionResult.Saved));
            Assert.That(store.Progress.PlayerGold, Is.EqualTo(50));
            Assert.That(store.Progress.Quest.ActiveQuestId, Is.Empty);
            Assert.That(store.Progress.Quest.AcceptanceId, Is.Empty);
            Assert.That(store.Progress.Quest.ProgressCount, Is.Zero);
            Assert.That(store.Progress.Quest.IsCompleted("kill_rats"), Is.True);
            store = Open();
            Assert.That(store.TryReportQuest("kill_rats", "report", revision), Is.EqualTo(QuestActionResult.AlreadyApplied));
            Assert.That(store.TryReportQuest("kill_rats", "again", store.Progress.Revision), Is.EqualTo(QuestActionResult.AlreadyCompleted));
            Assert.That(store.TryAcceptQuest("kill_rats", "accept_again", store.Progress.Revision), Is.EqualTo(QuestActionResult.AlreadyCompleted));
            Accept(store, "clear_dungeon");
            Assert.That(store.TryApplyQuestFact(fact), Is.EqualTo(QuestActionResult.AlreadyApplied));
            Assert.That(store.TryApplyQuestFact(new QuestFact(QuestFactType.DungeonCleared, "late", 1, "", 1, "accept_kill_rats")), Is.EqualTo(QuestActionResult.Ignored));
            Assert.That(store.Progress.Quest.ProgressCount, Is.Zero);
            Assert.That(store.Progress.PlayerGold, Is.EqualTo(50));
        }

        [Test]
        public void DeliveryUsesCurrentOwnedItemsAndOnlyConsumesThemTogetherWithRewardOnReport()
        {
            var store = SeedWealth(200, 3);
            Accept(store, "deliver_tails");
            var quest = _quests.GetQuest("deliver_tails");
            Assert.That(QuestRules.CanReport(quest, store.Progress), Is.True);
            Assert.That(store.Progress.Items["rat_tail"], Is.EqualTo(3));
            var shop = new ShopDataContainer(_shops, "market", store.Progress);
            Assert.That(shop.TrySelectItem("rat_tail", false), Is.True);
            Assert.That(store.TryTrade(shop.CreateTrade()), Is.EqualTo(ShopTradeResult.Saved));
            Assert.That(QuestRules.GetProgress(quest, store.Progress), Is.EqualTo(2));
            Assert.That(store.TryReportQuest(quest.Id, "early", store.Progress.Revision), Is.EqualTo(QuestActionResult.NotReady));
            var settlement = new LootSettlement(_loot, "keep_tail", new Dictionary<string, int> { { "rat_tail", 2 } }, store.Progress);
            Assert.That(settlement.TrySetPurchase("rat_tail", 1), Is.True);
            Assert.That(store.TryCommit(settlement), Is.EqualTo(SettlementCommitResult.Saved));
            Assert.That(QuestRules.CanReport(quest, store.Progress), Is.True);
            long before = store.Progress.PlayerGold;
            Assert.That(store.TryReportQuest(quest.Id, "report", store.Progress.Revision), Is.EqualTo(QuestActionResult.Saved));
            Assert.That(store.Progress.Items.ContainsKey("rat_tail"), Is.False);
            Assert.That(store.Progress.PlayerGold, Is.EqualTo(before + 40));
            store = Open();
            Assert.That(store.Progress.Items.ContainsKey("rat_tail"), Is.False);
            Assert.That(store.Progress.Quest.IsCompleted(quest.Id), Is.True);
            Assert.That(store.Progress.GetMerchantStock("market")["rat_tail"], Is.EqualTo(1));
        }

        [TestCase("accept")]
        [TestCase("fact")]
        [TestCase("report")]
        public void FailedQuestWritesLeaveMoneyItemsProgressAndRevisionUntouchedAndCanRetry(string action)
        {
            var store = SeedWealth(10, 3);
            string questId = action == "fact" ? "kill_rats" : "deliver_tails";
            if (action != "accept")
            {
                Accept(store, questId);
            }
            int revision = store.Progress.Revision;
            var state = store.Progress.Quest;
            string saved = File.ReadAllText(_path);
            var fact = Fact(store, "retry_fact");
            Func<QuestActionResult> execute = action == "accept" ? () => store.TryAcceptQuest(questId, "retry_accept", revision)
                : action == "report" ? () => store.TryReportQuest(questId, "retry_report", revision)
                : () => store.TryApplyQuestFact(fact);
            Directory.CreateDirectory(_path + ".tmp");
            Assert.That(execute(), Is.EqualTo(QuestActionResult.SaveFailed));
            Assert.That(store.Progress.Quest, Is.SameAs(state));
            Assert.That(store.Progress.Revision, Is.EqualTo(revision));
            Assert.That(store.Progress.PlayerGold, Is.EqualTo(10));
            Assert.That(store.Progress.Items["rat_tail"], Is.EqualTo(3));
            Assert.That(File.ReadAllText(_path), Is.EqualTo(saved));
            Directory.Delete(_path + ".tmp");
            Assert.That(execute(), Is.EqualTo(QuestActionResult.Saved));
            Assert.That(execute(), Is.EqualTo(QuestActionResult.AlreadyApplied));
        }

        [Test]
        public void RewardOverflowRejectsTheEntireHandIn()
        {
            var store = SeedWealth(long.MaxValue, 3);
            Accept(store, "deliver_tails");
            string saved = File.ReadAllText(_path);
            Assert.That(store.TryReportQuest("deliver_tails", "overflow", store.Progress.Revision), Is.EqualTo(QuestActionResult.InvalidRequest));
            Assert.That(store.Progress.Items["rat_tail"], Is.EqualTo(3));
            Assert.That(store.Progress.PlayerGold, Is.EqualTo(long.MaxValue));
            Assert.That(File.ReadAllText(_path), Is.EqualTo(saved));
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void LegacyVersionsPreserveExistingAssetsAndOnlyWriteCurrentVersionAfterSuccessfulChanges(int version)
        {
            string legacy;
            if (version == 3)
            {
                var seed = SeedWealth(37, 3);
                Assert.That(seed.TryTalk("mira", "old_talk", seed.Progress.Revision), Is.EqualTo(CharacterActionResult.Saved));
                var dto = JsonUtility.FromJson<AdventureSaveData>(File.ReadAllText(_path));
                dto.Version = 3;
                legacy = JsonUtility.ToJson(dto);
                foreach (string field in QuestFields)
                {
                    legacy = RemoveArray(legacy, field);
                }
            }
            else
            {
                string shop = version == 2 ? ",\"MerchantIds\":[\"market\"],\"StockMerchantIds\":[\"market\"],\"StockItemIds\":[\"health_potion\"],\"StockCounts\":[7],\"TradeIds\":[\"old_trade\"]" : string.Empty;
                legacy = "{\"Format\":\"P2.Adventure\",\"Version\":" + version + ",\"MemberIds\":[\"hero\",\"mira\"],\"MemberGold\":[37,9],\"ItemIds\":[\"rat_tail\"],\"ItemCounts\":[3],\"SettlementIds\":[\"old_settlement\"]" + shop + "}";
            }
            File.WriteAllText(_path, legacy);
            var store = Open();
            Assert.That(File.ReadAllText(_path), Is.EqualTo(legacy));
            Assert.That(store.Progress.PlayerGold, Is.EqualTo(37));
            Assert.That(store.Progress.Items["rat_tail"], Is.EqualTo(3));
            Assert.That(store.Progress.Quest.ActiveQuestId, Is.Empty);
            Assert.That(store.Progress.Quest.CompletedQuestIds, Is.Empty);
            Accept(store, "deliver_tails");
            Assert.That(JsonUtility.FromJson<AdventureSaveData>(File.ReadAllText(_path)).Version, Is.EqualTo(AdventureSave.CurrentVersion));
            Assert.That(Open().Progress.Quest.ActiveQuestId, Is.EqualTo("deliver_tails"));
            if (version == 3)
            {
                Assert.That(store.Progress.GetCharacter("mira").Affinity, Is.EqualTo(10));
            }
            else
            {
                Assert.That(store.Progress.GetGold("mira"), Is.EqualTo(9));
                Assert.That(store.Progress.HasSettlement("old_settlement"), Is.True);
            }
            if (version == 2)
            {
                Assert.That(store.Progress.GetMerchantStock("market")["health_potion"], Is.EqualTo(7));
                Assert.That(store.Progress.HasTrade("old_trade"), Is.True);
            }
        }

        [Test]
        public void AllExistingMutationPathsAndAStoreWithoutOptionalSheetsPreserveQuestSourceData()
        {
            var store = SeedWealth(100, 3);
            Accept(store, "kill_rats");
            Assert.That(store.TryApplyQuestFact(Fact(store, "one")), Is.EqualTo(QuestActionResult.Saved));
            Assert.That(store.TryTalk("mira", "talk", store.Progress.Revision), Is.EqualTo(CharacterActionResult.Saved));
            Assert.That(store.TryRecruit("mira", "recruit", store.Progress.Revision, 0), Is.EqualTo(CharacterActionResult.Saved));
            Assert.That(store.TryDismiss("mira", "dismiss", store.Progress.Revision), Is.EqualTo(CharacterActionResult.Saved));
            Assert.That(store.TryGrantExperience("exp", new[] { "hero" }, 30, new MinimumRandom()), Is.EqualTo(CharacterActionResult.Saved));
            var shop = new ShopDataContainer(_shops, "market", store.Progress);
            shop.TrySelectItem("health_potion", true);
            Assert.That(store.TryTrade(shop.CreateTrade()), Is.EqualTo(ShopTradeResult.Saved));
            var bare = new AdventureProgressStore(_loot, _path);
            Assert.That(bare.Load(), Is.EqualTo(AdventureLoadResult.Loaded));
            Assert.That(bare.TryCommit(new LootSettlement(_loot, "settlement", new Dictionary<string, int>(), bare.Progress)), Is.EqualTo(SettlementCommitResult.Saved));
            store = Open();
            Assert.That(store.Progress.Quest.ActiveQuestId, Is.EqualTo("kill_rats"));
            Assert.That(store.Progress.Quest.AcceptanceId, Is.EqualTo("accept_kill_rats"));
            Assert.That(store.Progress.Quest.ProgressCount, Is.EqualTo(1));
            Assert.That(store.TryApplyQuestFact(Fact(store, "one")), Is.EqualTo(QuestActionResult.AlreadyApplied));
            Assert.That(store.Progress.GetCharacter("hero").Level, Is.EqualTo(2));
            Assert.That(store.Progress.GetCharacter("hero").GrowthStats.Strength, Is.EqualTo(2));
            Assert.That(store.Progress.Items["health_potion"], Is.EqualTo(1));
            Assert.That(store.Progress.GetMerchantStock("market")["health_potion"], Is.EqualTo(9));
        }

        [Test]
        public void LoweredQuestRequirementUsesSavedProgressWithoutRejectingOrRewritingTheSave()
        {
            var store = Open();
            Accept(store, "kill_rats");
            Assert.That(store.TryApplyQuestFact(Fact(store, "two", count: 2)), Is.EqualTo(QuestActionResult.Saved));
            string saved = File.ReadAllText(_path);
            var changed = CreateSheet(Read("QuestDefinitionTable.csv").Replace(",gray_rat,3,50", ",gray_rat,1,50"));
            store = new AdventureProgressStore(_loot, _path, _shops, _characters, changed);
            Assert.That(store.Load(), Is.EqualTo(AdventureLoadResult.Loaded));
            Assert.That(store.Progress.Quest.ProgressCount, Is.EqualTo(2));
            Assert.That(QuestRules.GetProgress(changed.GetQuest("kill_rats"), store.Progress), Is.EqualTo(1));
            Assert.That(QuestRules.CanReport(changed.GetQuest("kill_rats"), store.Progress), Is.True);
            Assert.That(File.ReadAllText(_path), Is.EqualTo(saved));
        }

        [TestCaseSource(nameof(QuestFields))]
        public void RequiredQuestArraysRejectMissingNullAndDuplicateRootFields(string field)
        {
            var store = Open();
            Accept(store, "kill_rats");
            string valid = File.ReadAllText(_path);
            string pattern = "\\\"" + field + "\\\"\\s*:\\s*\\[[^\\]]*\\]";
            string[] invalid = { RemoveArray(valid, field), Regex.Replace(valid, pattern, "\"" + field + "\":null"), valid.Insert(valid.LastIndexOf('}'), ",\"" + field + "\":[]") };
            foreach (string text in invalid)
            {
                File.WriteAllText(_path, text);
                Assert.That(store.Load(), Is.EqualTo(AdventureLoadResult.Corrupt), field);
                Assert.That(store.CanWrite, Is.False);
                Assert.That(store.Progress.Quest.ActiveQuestId, Is.EqualTo("kill_rats"));
                Assert.That(store.TryAcceptQuest("clear_dungeon", "blocked", store.Progress.Revision), Is.EqualTo(QuestActionResult.LoadBlocked));
                Assert.That(File.ReadAllText(_path), Is.EqualTo(text));
            }
        }

        [TestCase("parallel_length")]
        [TestCase("two_active")]
        [TestCase("unknown_quest")]
        [TestCase("negative_count")]
        [TestCase("huge_count")]
        [TestCase("missing_acceptance_receipt")]
        [TestCase("completed_active")]
        [TestCase("duplicate_completed")]
        [TestCase("duplicate_receipt")]
        [TestCase("delivery_count")]
        public void InvalidQuestStateIsRejectedWithoutChangingLiveProgress(string mutation)
        {
            var store = Open();
            Accept(store, "kill_rats");
            var dto = JsonUtility.FromJson<AdventureSaveData>(File.ReadAllText(_path));
            switch (mutation)
            {
                case "parallel_length": dto.QuestProgressCounts = Array.Empty<int>(); break;
                case "two_active": dto.ActiveQuestIds = new[] { "kill_rats", "clear_dungeon" }; dto.QuestAcceptanceIds = new[] { "accept_kill_rats", "other" }; dto.QuestProgressCounts = new[] { 0, 0 }; break;
                case "unknown_quest": dto.ActiveQuestIds[0] = "unknown"; break;
                case "negative_count": dto.QuestProgressCounts[0] = -1; break;
                case "huge_count": dto.QuestProgressCounts[0] = 1000001; break;
                case "missing_acceptance_receipt": dto.QuestActionIds = Array.Empty<string>(); break;
                case "completed_active": dto.CompletedQuestIds = new[] { "kill_rats" }; break;
                case "duplicate_completed": dto.CompletedQuestIds = new[] { "clear_dungeon", "clear_dungeon" }; break;
                case "duplicate_receipt": dto.QuestActionIds = new[] { "accept_kill_rats", "accept_kill_rats" }; break;
                case "delivery_count": dto.ActiveQuestIds[0] = "deliver_tails"; dto.QuestProgressCounts[0] = 1; break;
            }
            string invalid = JsonUtility.ToJson(dto);
            File.WriteAllText(_path, invalid);
            Assert.That(store.Load(), Is.EqualTo(AdventureLoadResult.Corrupt));
            Assert.That(store.Progress.Quest.ActiveQuestId, Is.EqualTo("kill_rats"));
            Assert.That(File.ReadAllText(_path), Is.EqualTo(invalid));
        }

        private static readonly string[] QuestFields = { "ActiveQuestIds", "QuestAcceptanceIds", "QuestProgressCounts", "CompletedQuestIds", "QuestActionIds" };

        private AdventureProgressStore Open()
        {
            var store = new AdventureProgressStore(_loot, _path, _shops, _characters, _quests);
            var result = store.Load();
            Assert.That(result == AdventureLoadResult.Loaded || result == AdventureLoadResult.Missing, Is.True, result.ToString());
            return store;
        }

        private AdventureProgressStore SeedWealth(long gold, int tails)
        {
            string json = "{\"Format\":\"P2.Adventure\",\"Version\":1,\"MemberIds\":[\"hero\",\"mira\"],\"MemberGold\":[" + gold + ",0],\"ItemIds\":[\"rat_tail\"],\"ItemCounts\":[" + tails + "],\"SettlementIds\":[]}";
            File.WriteAllText(_path, json);
            return Open();
        }

        private void Accept(AdventureProgressStore store, string questId)
        {
            Assert.That(store.TryAcceptQuest(questId, "accept_" + questId, store.Progress.Revision), Is.EqualTo(QuestActionResult.Saved));
        }

        private static QuestFact Fact(AdventureProgressStore store, string id, QuestFactType type = QuestFactType.KillMonster,
            string target = "gray_rat", int count = 1, string acceptance = null, int dungeon = 1, int floor = 0)
        {
            return new QuestFact(type, id, dungeon, target, count, acceptance ?? store.Progress.Quest.AcceptanceId, floor);
        }

        private QuestSheet CreateSheet(string definitions = null, string nodes = null, string texts = null)
        {
            return new QuestSheet(definitions ?? Read("QuestDefinitionTable.csv"), nodes ?? Read("QuestRescueNodeTable.csv"),
                texts ?? Read("QuestTextTable.csv"), _dungeon, _loot);
        }

        private static string RemoveArray(string json, string field)
        {
            string pattern = ",\\s*\"" + Regex.Escape(field) + "\"\\s*:\\s*\\[[^\\]]*\\]";
            Assert.That(Regex.Matches(json, pattern).Count, Is.EqualTo(1));
            return Regex.Replace(json, pattern, string.Empty);
        }

        private static string Read(string name) => File.ReadAllText(Path.Combine(Application.dataPath, "1_InternalTools/Util/DataSheetLoader/Table", name));

        private sealed class MinimumRandom : IRandomSource
        {
            public int NextInt(int minimum, int maximum) => minimum;
            public float NextFloat(float minimum, float maximum) => minimum;
        }
    }
}
