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
    public sealed class SkillProgressTests
    {
        private LootSheet _loot;
        private ShopSheet _shops;
        private CharacterSheet _characters;
        private QuestSheet _quests;
        private SkillSheet _skills;
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
            _quests = new QuestSheet(Read("QuestDefinitionTable.csv"), Read("QuestRescueNodeTable.csv"), Read("QuestTextTable.csv"), dungeon, _loot);
            _characters = new CharacterSheet(Read("CharacterDefinitionTable.csv"), Read("CharacterSettingTable.csv"), Read("CharacterTextTable.csv"), town,
                new CharacterGrowthSheet(Read("CharacterGrowthTable.csv"), Read("CharacterLevelTable.csv"), Read("CharacterGrowthSettingTable.csv")),
                new CombatStatSheet(Read("CombatStatTable.csv"), Read("CombatEnemyTable.csv"), Read("CombatAttackTable.csv")));
            _skills = Fixture();
            _directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "SkillProgressTests", Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(_directory);
            _path = Path.Combine(_directory, "progress.json");
        }

        [TearDown]
        public void TearDown()
        {
            string allowed = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "SkillProgressTests")) + Path.DirectorySeparatorChar;
            Assert.That(Path.GetFullPath(_directory).StartsWith(allowed, StringComparison.OrdinalIgnoreCase), Is.True);
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, true);
            }
        }

        [Test]
        public void AuthoredContentUsesDsPointValuesAndAValidClassAndWeaponForEveryCharacter()
        {
            var actual = new SkillSheet(Read("SkillDefinitionTable.csv"), Read("SkillClassTable.csv"), Read("CharacterClassTable.csv"),
                Read("SkillWeaponTable.csv"), Read("SkillSettingTable.csv"), Read("SkillTextTable.csv"));
            Assert.That(actual.InitialMainSkillPoints, Is.EqualTo(10));
            Assert.That(actual.InitialSubSkillPoints, Is.EqualTo(6));
            Assert.That(actual.MainSkillPointsPerLevel, Is.EqualTo(6));
            Assert.That(actual.SubSkillPointsPerLevel, Is.EqualTo(6));
            Assert.That(actual.MaxSkillPoints, Is.EqualTo(9999));
            foreach (var character in _characters.Characters)
            {
                var role = actual.GetClassForCharacter(character.Id);
                Assert.That(role.ActiveTreeId, Is.Not.Empty);
                Assert.That(actual.GetWeapon(role.WeaponId), Is.Not.Null);
                Assert.That(actual.GetSkill(role.BasicAttackId).IsBasicAttack, Is.True);
            }
            Assert.That(actual.BasicSkills, Is.Not.Empty);
            Assert.That(actual.BasicSkills.All(skill => !actual.Skills.Contains(skill)), Is.True);
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void NonPositiveEnergyTickIsRejectedBeforeCombatCanLoop(int seconds)
        {
            string settings = Read("SkillSettingTable.csv").Replace("EnergyRefreshSeconds,1", "EnergyRefreshSeconds," + seconds);
            Assert.Throws<FormatException>(() => new SkillSheet(Read("SkillDefinitionTable.csv"), Read("SkillClassTable.csv"),
                Read("CharacterClassTable.csv"), Read("SkillWeaponTable.csv"), settings, Read("SkillTextTable.csv")));
        }

        [Test]
        public void NewCharactersHaveSeparatePointPoolsAndNoAutomaticPurchaseOnCreationOrLoad()
        {
            var store = Open();
            foreach (var character in store.Progress.Characters.Values)
            {
                Assert.That(character.MainSkillPoints, Is.EqualTo(10));
                Assert.That(character.SubSkillPoints, Is.EqualTo(6));
                Assert.That(character.LearnedSkillIds, Is.Empty);
                Assert.Throws<NotSupportedException>(() => ((IList<string>)character.LearnedSkillIds).Add("bad"));
            }
            Assert.That(File.Exists(_path), Is.False);
            Assert.That(store.TryTalk("mira", "save", store.Progress.Revision), Is.EqualTo(CharacterActionResult.Saved));
            string saved = File.ReadAllText(_path);
            store = Open();
            Assert.That(store.Progress.GetCharacter("mira").LearnedSkillIds, Is.Empty);
            Assert.That(File.ReadAllText(_path), Is.EqualTo(saved));
        }

        [Test]
        public void PlayerPurchasesUseTheRightPoolAndRecheckOwnershipLevelPrerequisitesAndPoints()
        {
            var store = Open();
            var hero = store.Progress.GetCharacter("hero");
            Assert.That(SkillLearningRules.GetLearnFailure(_skills, hero, "B_LOW"), Is.EqualTo(SkillLearnFailure.WrongTree));
            Assert.That(SkillLearningRules.GetLearnFailure(_skills, hero, "A_SECOND"), Is.EqualTo(SkillLearnFailure.PrerequisiteRequired));
            Assert.That(SkillLearningRules.GetLearnFailure(_skills, hero, "A_EXPENSIVE"), Is.EqualTo(SkillLearnFailure.InsufficientPoints));
            Assert.That(SkillLearningRules.GetLearnFailure(_skills, hero, "A_HIGH"), Is.EqualTo(SkillLearnFailure.LevelRequired));
            Assert.That(SkillLearningRules.GetLearnFailure(_skills, hero, "BASIC_TEST"), Is.EqualTo(SkillLearnFailure.InvalidSkill));
            Assert.That(store.TryLearnSkill("hero", "missing", "invalid", store.Progress.Revision), Is.EqualTo(SkillLearnResult.InvalidRequest));
            Assert.That(store.TryLearnSkill("mira", "B_LOW", "npc", store.Progress.Revision), Is.EqualTo(SkillLearnResult.PlayerOnly));
            int revision = store.Progress.Revision;
            Assert.That(store.TryLearnSkill("hero", "A_FIRST", "main", revision), Is.EqualTo(SkillLearnResult.Saved));
            hero = store.Progress.GetCharacter("hero");
            Assert.That(hero.MainSkillPoints, Is.EqualTo(6));
            Assert.That(hero.SubSkillPoints, Is.EqualTo(6));
            Assert.That(hero.HasSkill("A_FIRST"), Is.True);
            Assert.That(SkillLearningRules.GetLearnFailure(_skills, hero, "A_SECOND"), Is.EqualTo(SkillLearnFailure.LevelRequired));
            Assert.That(store.TryLearnSkill("hero", "A_FIRST", "main", revision), Is.EqualTo(SkillLearnResult.AlreadyApplied));
            Assert.That(store.TryLearnSkill("hero", "A_FIRST", "new", store.Progress.Revision), Is.EqualTo(SkillLearnResult.AlreadyLearned));
            Assert.That(store.TryLearnSkill("hero", "P_FIRST", "stale", revision), Is.EqualTo(SkillLearnResult.InvalidRequest));
            Assert.That(store.TryLearnSkill("hero", "P_FIRST", "sub", store.Progress.Revision), Is.EqualTo(SkillLearnResult.Saved));
            hero = store.Progress.GetCharacter("hero");
            Assert.That(hero.MainSkillPoints, Is.EqualTo(6));
            Assert.That(hero.SubSkillPoints, Is.EqualTo(3));
            store = Open();
            CollectionAssert.AreEqual(new[] { "A_FIRST", "P_FIRST" }, store.Progress.GetCharacter("hero").LearnedSkillIds);
            Assert.That(store.TryLearnSkill("hero", "A_FIRST", "main", revision), Is.EqualTo(SkillLearnResult.AlreadyApplied));
        }

        [TestCase(false, "B_HIGH")]
        [TestCase(true, "B_TIE")]
        public void NpcLevelUpBuysTheHighestEligibleRequirementFirstAndRandomizesTies(bool maximum, string expected)
        {
            var store = Open();
            var random = new CountingRandom(maximum);
            Assert.That(store.TryGrantExperience("little", new[] { "mira" }, 1, random), Is.EqualTo(CharacterActionResult.Saved));
            Assert.That(random.Calls, Is.Zero);
            Assert.That(store.Progress.GetCharacter("mira").LearnedSkillIds, Is.Empty);
            Assert.That(store.TryGrantExperience("level", new[] { "hero", "mira" }, 30, random), Is.EqualTo(CharacterActionResult.Saved));
            var hero = store.Progress.GetCharacter("hero");
            Assert.That(hero.Level, Is.EqualTo(2));
            Assert.That(hero.MainSkillPoints, Is.EqualTo(16));
            Assert.That(hero.SubSkillPoints, Is.EqualTo(12));
            Assert.That(hero.LearnedSkillIds, Is.Empty);
            var npc = store.Progress.GetCharacter("mira");
            Assert.That(npc.LearnedSkillIds[0], Is.EqualTo(expected));
            Assert.That(npc.HasSkill("B_LOW"), Is.False, "저레벨 스킬을 먼저 사서 최고 요구 레벨 후보를 놓치면 안 됩니다.");
            Assert.That(npc.HasSkill("P_FIRST"), Is.True);
            Assert.That(npc.HasSkill("P_SECOND"), Is.True, "첫 구매 뒤 해제된 선행 조건을 다시 평가해야 합니다.");
            Assert.That(npc.MainSkillPoints, Is.Zero);
            Assert.That(npc.SubSkillPoints, Is.EqualTo(6));
            Assert.That(_skills.Skills.Any(skill => SkillLearningRules.CanLearn(_skills, npc, skill.Id)), Is.False);
            int calls = random.Calls;
            Assert.That(store.TryGrantExperience("level", new[] { "hero", "mira" }, 30, random), Is.EqualTo(CharacterActionResult.AlreadyApplied));
            Assert.That(random.Calls, Is.EqualTo(calls));
            CollectionAssert.AreEqual(npc.LearnedSkillIds, Open().Progress.GetCharacter("mira").LearnedSkillIds);
        }

        [Test]
        public void MaximumLevelGrantsNoMorePointsAndDoesNotRunNpcPurchases()
        {
            var store = SeedLegacy(4, _characters.Growth.MaxLevel);
            var original = store.Progress.GetCharacter("mira");
            var random = new CountingRandom();
            Assert.That(store.TryGrantExperience("capped", new[] { "mira" }, 100000, random), Is.EqualTo(CharacterActionResult.Saved));
            Assert.That(store.Progress.GetCharacter("mira").MainSkillPoints, Is.EqualTo(original.MainSkillPoints));
            Assert.That(store.Progress.GetCharacter("mira").SubSkillPoints, Is.EqualTo(original.SubSkillPoints));
            Assert.That(store.Progress.GetCharacter("mira").LearnedSkillIds, Is.Empty);
            Assert.That(random.Calls, Is.Zero);
        }

        [Test]
        public void FailedPurchasePreservesFilePointsLearnedIdsAndRevisionAndTheSameRequestCanRetry()
        {
            var store = Open();
            Assert.That(store.TryTalk("mira", "seed", store.Progress.Revision), Is.EqualTo(CharacterActionResult.Saved));
            var before = store.Progress.GetCharacter("hero");
            int revision = store.Progress.Revision;
            string saved = File.ReadAllText(_path);
            Directory.CreateDirectory(_path + ".tmp");
            Assert.That(store.TryLearnSkill("hero", "A_FIRST", "retry", revision), Is.EqualTo(SkillLearnResult.SaveFailed));
            Assert.That(store.Progress.GetCharacter("hero"), Is.SameAs(before));
            Assert.That(store.Progress.Revision, Is.EqualTo(revision));
            Assert.That(File.ReadAllText(_path), Is.EqualTo(saved));
            Directory.Delete(_path + ".tmp");
            Assert.That(store.TryLearnSkill("hero", "A_FIRST", "retry", revision), Is.EqualTo(SkillLearnResult.Saved));
            Assert.That(store.Progress.GetCharacter("hero").MainSkillPoints, Is.EqualTo(6));
        }

        [Test]
        public void FailedExperienceSaveRetriesTheSameGrowthAndNpcPurchasesWithoutAnyNewRandomDraw()
        {
            var store = Open();
            var random = new CountingRandom(true);
            Directory.CreateDirectory(_path + ".tmp");
            Assert.That(store.TryGrantExperience("reward", new[] { "mira" }, 30, random), Is.EqualTo(CharacterActionResult.SaveFailed));
            Assert.That(store.Progress.GetCharacter("mira").Level, Is.EqualTo(1));
            Assert.That(store.Progress.GetCharacter("mira").MainSkillPoints, Is.EqualTo(10));
            Assert.That(store.Progress.GetCharacter("mira").LearnedSkillIds, Is.Empty);
            int calls = random.Calls;
            Assert.That(calls, Is.GreaterThan(6));
            Assert.That(store.TryGrantExperience("reward", new[] { "mira" }, 30, random), Is.EqualTo(CharacterActionResult.SaveFailed));
            Assert.That(random.Calls, Is.EqualTo(calls));
            Directory.Delete(_path + ".tmp");
            Assert.That(store.TryGrantExperience("reward", new[] { "mira" }, 30, random), Is.EqualTo(CharacterActionResult.Saved));
            Assert.That(random.Calls, Is.EqualTo(calls));
            var npc = store.Progress.GetCharacter("mira");
            Assert.That(npc.GrowthStats.Strength, Is.EqualTo(4));
            Assert.That(npc.LearnedSkillIds[0], Is.EqualTo("B_TIE"));
            CollectionAssert.AreEqual(npc.LearnedSkillIds, Open().Progress.GetCharacter("mira").LearnedSkillIds);
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public void OldSavesMigrateCumulativePointsWithoutPurchasesRandomnessOrWriting(int version)
        {
            var store = SeedLegacy(version, 5);
            string saved = File.ReadAllText(_path);
            int level = version < 3 ? 1 : 5;
            foreach (var character in store.Progress.Characters.Values)
            {
                Assert.That(character.MainSkillPoints, Is.EqualTo(10 + (level - 1) * 6));
                Assert.That(character.SubSkillPoints, Is.EqualTo(6 + (level - 1) * 6));
                Assert.That(character.LearnedSkillIds, Is.Empty);
            }
            Assert.That(store.Progress.PlayerGold, Is.EqualTo(47));
            Assert.That(store.Progress.Items["rat_tail"], Is.EqualTo(3));
            Assert.That(File.ReadAllText(_path), Is.EqualTo(saved));
            Assert.That(store.TryLearnSkill("hero", "A_FIRST", "migrated", store.Progress.Revision), Is.EqualTo(SkillLearnResult.Saved));
            var written = JsonUtility.FromJson<AdventureSaveData>(File.ReadAllText(_path));
            Assert.That(written.Version, Is.EqualTo(AdventureSave.CurrentVersion));
            Assert.That(written.SkillCharacterIds.Length, Is.EqualTo(7));
            if (version == 4)
            {
                Assert.That(store.Progress.Quest.ActiveQuestId, Is.EqualTo("kill_rats"));
                Assert.That(store.Progress.Quest.ProgressCount, Is.EqualTo(1));
            }
            Assert.That(Open().Progress.GetCharacter("hero").MainSkillPoints, Is.EqualTo(10 + (level - 1) * 6 - 4));
        }

        [Test]
        public void OtherActionsAndOptionalSheetStoresPreserveLearningAndBlockGrowthWithoutItsRewardSheet()
        {
            var store = SeedLegacy(4, 1);
            Assert.That(store.TryLearnSkill("hero", "A_FIRST", "learn", store.Progress.Revision), Is.EqualTo(SkillLearnResult.Saved));
            Assert.That(store.TryTalk("mira", "talk", store.Progress.Revision), Is.EqualTo(CharacterActionResult.Saved));
            Assert.That(store.TryRecruit("mira", "join", store.Progress.Revision, 0), Is.EqualTo(CharacterActionResult.Saved));
            Assert.That(store.TryDismiss("mira", "leave", store.Progress.Revision), Is.EqualTo(CharacterActionResult.Saved));
            Assert.That(store.TryApplyQuestFact(new QuestFact(QuestFactType.KillMonster, "kill", 1, "gray_rat", 2, store.Progress.Quest.AcceptanceId)), Is.EqualTo(QuestActionResult.Saved));
            Assert.That(store.TryReportQuest("kill_rats", "report", store.Progress.Revision), Is.EqualTo(QuestActionResult.Saved));
            var shop = new ShopDataContainer(_shops, "market", store.Progress);
            Assert.That(shop.TrySelectItem("health_potion", true), Is.True);
            Assert.That(store.TryTrade(shop.CreateTrade()), Is.EqualTo(ShopTradeResult.Saved));
            var bare = new AdventureProgressStore(_loot, _path);
            Assert.That(bare.Load(), Is.EqualTo(AdventureLoadResult.Loaded));
            Assert.That(bare.TryCommit(new LootSettlement(_loot, "settlement", new Dictionary<string, int>(), bare.Progress)), Is.EqualTo(SettlementCommitResult.Saved));
            var withoutSkills = new AdventureProgressStore(_loot, _path, _shops, _characters, _quests);
            Assert.That(withoutSkills.Load(), Is.EqualTo(AdventureLoadResult.Loaded));
            Assert.That(withoutSkills.TryGrantExperience("not_supported", new[] { "hero" }, 30, new CountingRandom()), Is.EqualTo(CharacterActionResult.InvalidRequest));
            store = Open();
            Assert.That(store.Progress.GetCharacter("hero").HasSkill("A_FIRST"), Is.True);
            Assert.That(store.Progress.GetCharacter("hero").MainSkillPoints, Is.EqualTo(6));
            Assert.That(store.Progress.GetCharacter("hero").SubSkillPoints, Is.EqualTo(6));
            Assert.That(store.Progress.Quest.IsCompleted("kill_rats"), Is.True);
            Assert.That(store.Progress.Items["health_potion"], Is.EqualTo(1));
            Assert.That(store.Progress.GetMerchantStock("market")["health_potion"], Is.EqualTo(9));
            Assert.That(store.TryLearnSkill("hero", "A_FIRST", "learn", 0), Is.EqualTo(SkillLearnResult.AlreadyApplied));
        }

        [Test]
        public void LearningCostAndRequirementChangesDoNotRepriceOrForgetPreviouslyPurchasedSkills()
        {
            var store = Open();
            Assert.That(store.TryLearnSkill("hero", "A_FIRST", "learn", store.Progress.Revision), Is.EqualTo(SkillLearnResult.Saved));
            string saved = File.ReadAllText(_path);
            var changed = Fixture(firstCost: 100, firstLevel: 12);
            store = new AdventureProgressStore(_loot, _path, _shops, _characters, _quests, changed);
            Assert.That(store.Load(), Is.EqualTo(AdventureLoadResult.Loaded));
            Assert.That(store.Progress.GetCharacter("hero").MainSkillPoints, Is.EqualTo(6));
            Assert.That(store.Progress.GetCharacter("hero").HasSkill("A_FIRST"), Is.True);
            Assert.That(File.ReadAllText(_path), Is.EqualTo(saved));
        }

        [Test]
        public void BasicAttackKillsRequireThePassiveAtTheTimeOfTheFactAndPersistWithExperienceOnlyOnce()
        {
            _skills = Fixture(sharpShooter: true);
            var store = Open();
            var gains = new Dictionary<string, float> { { "hero", 8 } };
            var kills = new Dictionary<string, int> { { "hero", 1 } };
            var random = new CountingRandom();
            Assert.That(store.TryGrantExperience("before", gains, random, kills), Is.EqualTo(CharacterActionResult.InvalidRequest));
            Assert.That(store.Progress.GetCharacter("hero").BasicAttackKills, Is.Zero);
            Assert.That(store.TryLearnSkill("hero", "SKILL_SharpShooter", "learn", store.Progress.Revision), Is.EqualTo(SkillLearnResult.Saved));
            Assert.That(store.TryGrantExperience("kill", gains, random, kills), Is.EqualTo(CharacterActionResult.Saved));
            Assert.That(store.Progress.GetCharacter("hero").BasicAttackKills, Is.EqualTo(1));
            Assert.That(store.Progress.GetCharacter("hero").Experience, Is.EqualTo(8));
            Assert.That(store.TryGrantExperience("kill", gains, random, kills), Is.EqualTo(CharacterActionResult.AlreadyApplied));
            store = Open();
            Assert.That(store.Progress.GetCharacter("hero").BasicAttackKills, Is.EqualTo(1));
            Assert.That(store.TryGrantExperience("no_basic_fact", gains, random), Is.EqualTo(CharacterActionResult.Saved));
            Assert.That(store.Progress.GetCharacter("hero").BasicAttackKills, Is.EqualTo(1));
            Assert.That(store.TryGrantExperience("npc_level", new[] { "mira" }, 30, random), Is.EqualTo(CharacterActionResult.Saved));
            Assert.That(store.Progress.GetCharacter("mira").HasSkill("SKILL_SharpShooter"), Is.True);
            Assert.That(store.Progress.GetCharacter("mira").BasicAttackKills, Is.Zero, "현재 처치의 성장으로 배운 스킬에 과거 처치를 소급하면 안 됩니다.");
        }

        [Test]
        public void BasicKillFactsArePartOfTheCachedAtomicRewardAndSurviveAStoreWithoutSkillSheets()
        {
            _skills = Fixture(sharpShooter: true);
            var store = Open();
            Assert.That(store.TryLearnSkill("hero", "SKILL_SharpShooter", "learn", store.Progress.Revision), Is.EqualTo(SkillLearnResult.Saved));
            var gains = new Dictionary<string, float> { { "hero", 30 } };
            var kills = new Dictionary<string, int> { { "hero", 1 } };
            var random = new CountingRandom();
            string saved = File.ReadAllText(_path);
            Directory.CreateDirectory(_path + ".tmp");
            Assert.That(store.TryGrantExperience("retry", gains, random, kills), Is.EqualTo(CharacterActionResult.SaveFailed));
            int calls = random.Calls;
            Assert.That(store.Progress.GetCharacter("hero").BasicAttackKills, Is.Zero);
            Assert.That(store.Progress.GetCharacter("hero").Level, Is.EqualTo(1));
            Assert.That(File.ReadAllText(_path), Is.EqualTo(saved));
            Assert.That(store.TryGrantExperience("retry", gains, random, new Dictionary<string, int> { { "hero", 2 } }), Is.EqualTo(CharacterActionResult.InvalidRequest));
            Directory.Delete(_path + ".tmp");
            Assert.That(store.TryGrantExperience("retry", gains, random, kills), Is.EqualTo(CharacterActionResult.Saved));
            Assert.That(random.Calls, Is.EqualTo(calls));
            Assert.That(store.Progress.GetCharacter("hero").BasicAttackKills, Is.EqualTo(1));
            var bare = new AdventureProgressStore(_loot, _path);
            Assert.That(bare.Load(), Is.EqualTo(AdventureLoadResult.Loaded));
            Assert.That(bare.TryCommit(new LootSettlement(_loot, "save_without_skills", new Dictionary<string, int>(), bare.Progress)), Is.EqualTo(SettlementCommitResult.Saved));
            Assert.That(Open().Progress.GetCharacter("hero").BasicAttackKills, Is.EqualTo(1));
        }

        [TestCaseSource(nameof(SkillFields))]
        public void RequiredSkillArraysRejectMissingExplicitNullAndDuplicateFields(string field)
        {
            var store = Open();
            Assert.That(store.TryLearnSkill("hero", "A_FIRST", "learn", store.Progress.Revision), Is.EqualTo(SkillLearnResult.Saved));
            string saved = File.ReadAllText(_path);
            string pattern = "\"" + Regex.Escape(field) + "\"\\s*:\\s*\\[[^\\]]*\\]";
            string[] invalid = { RemoveArray(saved, field), Regex.Replace(saved, pattern, "\"" + field + "\":null"), saved.Insert(saved.LastIndexOf('}'), ",\"" + field + "\":[]") };
            foreach (string content in invalid)
            {
                File.WriteAllText(_path, content);
                Assert.That(store.Load(), Is.EqualTo(AdventureLoadResult.Corrupt), field);
                Assert.That(store.Progress.GetCharacter("hero").HasSkill("A_FIRST"), Is.True);
                Assert.That(store.TryLearnSkill("hero", "P_FIRST", "blocked", store.Progress.Revision), Is.EqualTo(SkillLearnResult.LoadBlocked));
                Assert.That(File.ReadAllText(_path), Is.EqualTo(content));
            }
        }

        [TestCase("points_length")]
        [TestCase("owner_missing")]
        [TestCase("duplicate_owner")]
        [TestCase("negative_points")]
        [TestCase("overflow_points")]
        [TestCase("negative_kills")]
        [TestCase("learned_length")]
        [TestCase("unknown_owner")]
        [TestCase("unknown_skill")]
        [TestCase("duplicate_skill")]
        [TestCase("duplicate_receipt")]
        public void InvalidLearningSourcesAreRejectedWithoutReplacingCurrentProgress(string mutation)
        {
            var store = Open();
            Assert.That(store.TryLearnSkill("hero", "A_FIRST", "learn", store.Progress.Revision), Is.EqualTo(SkillLearnResult.Saved));
            var dto = JsonUtility.FromJson<AdventureSaveData>(File.ReadAllText(_path));
            switch (mutation)
            {
                case "points_length": dto.CharacterMainSkillPoints = Array.Empty<int>(); break;
                case "owner_missing": dto.SkillCharacterIds = new[] { "hero" }; dto.CharacterMainSkillPoints = new[] { 6 }; dto.CharacterSubSkillPoints = new[] { 6 }; break;
                case "duplicate_owner": dto.SkillCharacterIds[1] = dto.SkillCharacterIds[0]; break;
                case "negative_points": dto.CharacterSubSkillPoints[0] = -1; break;
                case "overflow_points": dto.CharacterMainSkillPoints[0] = AdventureSave.MaximumStoredSkillPoints + 1; break;
                case "negative_kills": dto.CharacterBasicAttackKills[0] = -1; break;
                case "learned_length": dto.LearnedSkillCharacterIds = Array.Empty<string>(); break;
                case "unknown_owner": dto.LearnedSkillCharacterIds[0] = "unknown"; break;
                case "unknown_skill": dto.LearnedSkillIds[0] = "unknown"; break;
                case "duplicate_skill": dto.LearnedSkillCharacterIds = new[] { "hero", "hero" }; dto.LearnedSkillIds = new[] { "A_FIRST", "A_FIRST" }; break;
                case "duplicate_receipt": dto.SkillActionIds = new[] { "learn", "learn" }; break;
            }
            string content = JsonUtility.ToJson(dto);
            File.WriteAllText(_path, content);
            Assert.That(store.Load(), Is.EqualTo(AdventureLoadResult.Corrupt));
            Assert.That(store.Progress.GetCharacter("hero").MainSkillPoints, Is.EqualTo(6));
            Assert.That(store.Progress.GetCharacter("hero").HasSkill("A_FIRST"), Is.True);
            Assert.That(File.ReadAllText(_path), Is.EqualTo(content));
        }

        private AdventureProgressStore Open()
        {
            var store = new AdventureProgressStore(_loot, _path, _shops, _characters, _quests, _skills);
            var result = store.Load();
            Assert.That(result == AdventureLoadResult.Loaded || result == AdventureLoadResult.Missing, Is.True, result.ToString());
            return store;
        }

        private AdventureProgressStore SeedLegacy(int version, int level)
        {
            string content;
            if (version < 3)
            {
                string stock = version == 2 ? ",\"MerchantIds\":[\"market\"],\"StockMerchantIds\":[\"market\"],\"StockItemIds\":[\"health_potion\"],\"StockCounts\":[10],\"TradeIds\":[]" : string.Empty;
                content = "{\"Format\":\"P2.Adventure\",\"Version\":" + version + ",\"MemberIds\":[\"hero\",\"mira\"],\"MemberGold\":[47,9],\"ItemIds\":[\"rat_tail\"],\"ItemCounts\":[3],\"SettlementIds\":[\"old\"]" + stock + "}";
            }
            else
            {
                var seed = new AdventureProgressStore(_loot, _path, _shops, _characters, _quests);
                Assert.That(seed.Load(), Is.EqualTo(AdventureLoadResult.Missing));
                Assert.That(seed.TryAcceptQuest("kill_rats", "accept", seed.Progress.Revision), Is.EqualTo(QuestActionResult.Saved));
                var dto = JsonUtility.FromJson<AdventureSaveData>(File.ReadAllText(_path));
                dto.Version = version;
                dto.CharacterLevels = Enumerable.Repeat(level, dto.CharacterIds.Length).ToArray();
                dto.MemberGold[Array.IndexOf(dto.MemberIds, "hero")] = 47;
                dto.ItemIds = new[] { "rat_tail" };
                dto.ItemCounts = new[] { 3 };
                dto.QuestProgressCounts[0] = 1;
                content = JsonUtility.ToJson(dto);
                foreach (string field in SkillFields)
                {
                    content = RemoveArray(content, field);
                }
                if (version == 3)
                {
                    foreach (string field in new[] { "ActiveQuestIds", "QuestAcceptanceIds", "QuestProgressCounts", "CompletedQuestIds", "QuestActionIds" })
                    {
                        content = RemoveArray(content, field);
                    }
                }
            }
            File.WriteAllText(_path, content);
            return Open();
        }

        private SkillSheet Fixture(int firstCost = 4, int firstLevel = 1, bool sharpShooter = false)
        {
            const string header = "Id,Name,Description,TreeId,TreeKind,Order,RequiredLevel,Cost,PrerequisiteIds,TargetType,DamageType,AttackType,Values,Cooldown,EnergyCost,StartDelay,EndDelay,IsDelayAdjustedByAttackSpeed,RequiredEquipment,IsBasicAttack";
            string definitions = header + "\n" + string.Join("\n", new[]
            {
                Row("BASIC_TEST", "Basic", "Main", 0, 0, "", true),
                Row("A_FIRST", "Sword", "Main", firstLevel, firstCost),
                Row("A_SECOND", "Sword", "Main", 2, 6, "A_FIRST"),
                Row("A_EXPENSIVE", "Sword", "Main", 1, 100),
                Row("A_HIGH", "Sword", "Main", 12, 1),
                Row("B_LOW", "Bow", "Main", 1, 16),
                Row("B_HIGH", "Bow", "Main", 2, 16),
                Row("B_TIE", "Bow", "Main", 2, 16),
                Row("P_FIRST", "Common", "Sub", 1, 3),
                Row("P_SECOND", "Common", "Sub", 1, 3, "P_FIRST")
            });
            if (sharpShooter)
            {
                definitions += "\n" + Row("SKILL_SharpShooter", "Common", "Sub", 1, 3);
            }
            string classes = "Id,Name,ActiveTreeId,WeaponId\nfighter,전사,Sword,Equipment_Sword\narcher,궁수,Bow,Equipment_Bow";
            string assignments = "CharacterId,ClassId\nhero,fighter\nmira,archer\norin,fighter\nbran,fighter\nsera,fighter\nneri,fighter\ncass,fighter";
            string weapons = "Id,Name,BasicSkillId\nEquipment_Sword,검,BASIC_TEST\nEquipment_Bow,활,BASIC_TEST";
            return new SkillSheet(definitions, classes, assignments, weapons, Read("SkillSettingTable.csv"), Read("SkillTextTable.csv"));
        }

        private static string Row(string id, string tree, string kind, int level, int cost, string prerequisites = "", bool basic = false)
        {
            return string.Join(",", new[] { id, id, "검증용 스킬", tree, kind, "0", level.ToString(), cost.ToString(), prerequisites,
                kind == "Sub" ? "None" : "SingleEnemy", "Physical", "Melee", "100", "0", "0", "0.2", "0.2", "1", "", basic ? "1" : "0" });
        }

        private static readonly string[] SkillFields = { "SkillCharacterIds", "CharacterMainSkillPoints", "CharacterSubSkillPoints", "CharacterBasicAttackKills", "LearnedSkillCharacterIds", "LearnedSkillIds", "SkillActionIds" };

        private static string RemoveArray(string json, string field)
        {
            string pattern = ",\\s*\"" + Regex.Escape(field) + "\"\\s*:\\s*\\[[^\\]]*\\]";
            Assert.That(Regex.Matches(json, pattern).Count, Is.EqualTo(1));
            return Regex.Replace(json, pattern, string.Empty);
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
