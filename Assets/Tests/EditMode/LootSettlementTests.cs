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
    public sealed class LootSettlementTests
    {
        private DungeonSheet _dungeon;
        private LootSheet _sheet;
        private string _directory;
        private string _path;

        [SetUp]
        public void SetUp()
        {
            _dungeon = CreateDungeon();
            _sheet = CreateSheet();
            _directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "LootSettlementTests", Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(_directory);
            _path = Path.Combine(_directory, "progress.json");
        }

        [TearDown]
        public void TearDown()
        {
            string allowedRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "LootSettlementTests")) + Path.DirectorySeparatorChar;
            Assert.That(Path.GetFullPath(_directory).StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase), Is.True);
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, true);
            }
        }

        [Test]
        public void AuthoredDropAndSharesFollowTheExistingPartyAndStartingGold()
        {
            Assert.That(_sheet.GetItem("rat_tail").Price, Is.EqualTo(10));
            Assert.That(_sheet.GetDrops("gray_rat").Single().ItemId, Is.EqualTo("rat_tail"));
            Assert.That(_sheet.GetDrops("gray_rat").Single().Quantity, Is.EqualTo(1));
            Assert.That(_sheet.GetDrops("no_drop_enemy"), Is.Empty);
            Assert.That(_sheet.Shares.Sum(member => member.Percent), Is.EqualTo(100));
            CollectionAssert.AreEquivalent(_dungeon.Party.Select(member => member.Id), _sheet.Shares.Select(member => member.MemberId));
            Assert.That(_sheet.PlayerMemberId, Is.EqualTo(_dungeon.Party.Single(member => member.IsPlayerControlled).Id));
            var store = NewStore();
            Assert.That(store.CanWrite, Is.True);
            Assert.That(store.Progress.PlayerGold, Is.EqualTo(_sheet.StartingGold));
            Assert.That(File.Exists(_path), Is.False);
        }

        [Test]
        public void ConfigRejectsInvalidSharesReferencesAndUnsafeNumericBounds()
        {
            Assert.Throws<FormatException>(() => CreateSheet(shares: "MemberId,Percent\nhero,50\nmira,49"));
            Assert.Throws<FormatException>(() => CreateSheet(shares: "MemberId,Percent\nhero,50\nhero,50"));
            Assert.Throws<FormatException>(() => CreateSheet(shares: "MemberId,Percent\nhero,50\nstranger,50"));
            Assert.Throws<FormatException>(() => CreateSheet(items: "Id,Name,Price\nrat_tail,쥐꼬리,1000000001"));
            Assert.Throws<FormatException>(() => CreateSheet(drops: "EnemyId,ItemId,Quantity\ngray_rat,rat_tail,100001"));
            Assert.Throws<FormatException>(() => CreateSheet(settings: "Key,Value\nStartingGold,1000000000001"));
            Assert.Throws<KeyNotFoundException>(() => CreateSheet(drops: "EnemyId,ItemId,Quantity\ngray_rat,unknown,1"));
            Assert.Throws<KeyNotFoundException>(() => CreateSheet(texts: "Key,Value\nsettlement.title,정산"));
        }

        [TestCase(1, 50, 1, 0)]
        [TestCase(3, 30, 1, 2)]
        [TestCase(99, 33, 33, 66)]
        public void LargestRemainderDistributionPreservesEveryCoin(int total, int heroPercent, long heroGold, long miraGold)
        {
            _sheet = CreateSheet(items: "Id,Name,Price\nrat_tail,쥐꼬리,1",
                shares: $"MemberId,Percent\nhero,{heroPercent}\nmira,{100 - heroPercent}");
            var store = NewStore();
            var settlement = Draft(store, "rounding", total);
            Assert.That(settlement.Shares.Sum(share => share.Gold), Is.EqualTo(total));
            Assert.That(settlement.Shares.Single(share => share.MemberId == "hero").Gold, Is.EqualTo(heroGold));
            Assert.That(settlement.Shares.Single(share => share.MemberId == "mira").Gold, Is.EqualTo(miraGold));
            Assert.That(store.TryCommit(settlement), Is.EqualTo(SettlementCommitResult.Saved));
            Assert.That(store.Progress.Gold.Values.Sum(), Is.EqualTo(total));
        }

        [Test]
        public void OwnShareCanFundPurchasesWithoutReducingAnyMembersDistribution()
        {
            var store = NewStore();
            var settlement = Draft(store, "purchase", 4);
            Assert.That(settlement.TrySetPurchase("rat_tail", 2), Is.True);
            Assert.That(settlement.TotalValue, Is.EqualTo(40));
            Assert.That(settlement.PlayerShare, Is.EqualTo(20));
            Assert.That(settlement.PurchaseCost, Is.EqualTo(20));
            Assert.That(settlement.Items.Single().ExternalQuantity, Is.EqualTo(2));
            Assert.That(settlement.CanConfirm, Is.True);
            Assert.That(store.TryCommit(settlement), Is.EqualTo(SettlementCommitResult.Saved));
            Assert.That(store.Progress.PlayerGold, Is.Zero);
            Assert.That(store.Progress.GetGold("mira"), Is.EqualTo(20));
            Assert.That(store.Progress.Items["rat_tail"], Is.EqualTo(2));
            Assert.That(store.Progress.Gold.Values.Sum() + settlement.PurchaseCost, Is.EqualTo(settlement.TotalValue));
        }

        [Test]
        public void InsufficientPurchaseCannotWriteAndReducingSelectionAllowsConfirmation()
        {
            var store = NewStore();
            var settlement = Draft(store, "insufficient", 4);
            settlement.TrySetPurchase("rat_tail", 3);
            Assert.That(settlement.FinalPlayerGold, Is.EqualTo(-10));
            Assert.That(settlement.CanConfirm, Is.False);
            Assert.That(store.TryCommit(settlement), Is.EqualTo(SettlementCommitResult.InsufficientGold));
            Assert.That(store.Progress.PlayerGold, Is.Zero);
            Assert.That(store.Progress.Items, Is.Empty);
            Assert.That(File.Exists(_path), Is.False);
            settlement.TryAdjustPurchase("rat_tail", -1);
            Assert.That(store.TryCommit(settlement), Is.EqualTo(SettlementCommitResult.Saved));
        }

        [Test]
        public void ExistingGoldAndInventoryAccumulateAcrossSeparateRuns()
        {
            var store = NewStore();
            Assert.That(store.TryCommit(Draft(store, "sell_first", 4)), Is.EqualTo(SettlementCommitResult.Saved));
            var second = Draft(store, "buy_second", 2);
            second.TrySetPurchase("rat_tail", 2);
            Assert.That(second.FinalPlayerGold, Is.EqualTo(10));
            Assert.That(store.TryCommit(second), Is.EqualTo(SettlementCommitResult.Saved));
            var reloaded = new AdventureProgressStore(_sheet, _path);
            Assert.That(reloaded.Load(), Is.EqualTo(AdventureLoadResult.Loaded));
            Assert.That(reloaded.Progress.PlayerGold, Is.EqualTo(10));
            Assert.That(reloaded.Progress.GetGold("mira"), Is.EqualTo(30));
            Assert.That(reloaded.Progress.Items["rat_tail"], Is.EqualTo(2));
            Assert.That(reloaded.Progress.HasSettlement("sell_first"), Is.True);
            Assert.That(reloaded.Progress.HasSettlement("buy_second"), Is.True);
        }

        [Test]
        public void InvalidOrDuplicateQuantityRequestsDoNotChangeTheDraft()
        {
            var store = NewStore();
            var settlement = Draft(store, "invalid_quantity", 4);
            settlement.TrySetPurchase("rat_tail", 1);
            int revision = settlement.Revision;
            Assert.That(settlement.TrySetPurchase("rat_tail", -1), Is.False);
            Assert.That(settlement.TrySetPurchase("rat_tail", 5), Is.False);
            Assert.That(settlement.TrySetPurchase("rat_tail", 1), Is.False);
            Assert.That(settlement.TrySetPurchase("unknown", 1), Is.False);
            Assert.That(settlement.TryAdjustPurchase("rat_tail", int.MaxValue), Is.False);
            Assert.That(settlement.PurchaseCost, Is.EqualTo(10));
            Assert.That(settlement.Items.Single().Purchased, Is.EqualTo(1));
            Assert.That(settlement.Revision, Is.EqualTo(revision));
        }

        [Test]
        public void CommittedPreviewRemainsStableAndIdempotencySurvivesReload()
        {
            var store = NewStore();
            var settlement = Draft(store, "once", 4);
            settlement.TrySetPurchase("rat_tail", 1);
            Assert.That(settlement.PlayerGold, Is.Zero);
            Assert.That(settlement.FinalPlayerGold, Is.EqualTo(10));
            Assert.That(store.TryCommit(settlement), Is.EqualTo(SettlementCommitResult.Saved));
            Assert.That(settlement.PlayerGold, Is.Zero, "전환 실패 후에도 정산 전 금화를 표시한다.");
            Assert.That(settlement.FinalPlayerGold, Is.EqualTo(10));
            Assert.That(settlement.IsCommitted, Is.True);
            Assert.That(settlement.TryAdjustPurchase("rat_tail", 1), Is.False);
            string saved = File.ReadAllText(_path);
            Assert.That(store.TryCommit(settlement), Is.EqualTo(SettlementCommitResult.AlreadyCommitted));
            Assert.That(File.ReadAllText(_path), Is.EqualTo(saved));
            var reloaded = new AdventureProgressStore(_sheet, _path);
            Assert.That(reloaded.Load(), Is.EqualTo(AdventureLoadResult.Loaded));
            Assert.That(reloaded.TryCommit(Draft(reloaded, "once", 4)), Is.EqualTo(SettlementCommitResult.AlreadyCommitted));
            Assert.That(reloaded.Progress.PlayerGold, Is.EqualTo(10));
            Assert.That(reloaded.Progress.GetGold("mira"), Is.EqualTo(20));
            Assert.That(reloaded.Progress.Items["rat_tail"], Is.EqualTo(1));
            Assert.That(File.ReadAllText(_path), Is.EqualTo(saved));
        }

        [Test]
        public void ACommittedDraftInvalidatesOlderDraftsWithoutOverwritingNewBalances()
        {
            var store = NewStore();
            var old = Draft(store, "stale", 2);
            var current = Draft(store, "current", 4);
            Assert.That(store.TryCommit(current), Is.EqualTo(SettlementCommitResult.Saved));
            string saved = File.ReadAllText(_path);
            Assert.That(old.PlayerGold, Is.Zero);
            Assert.That(old.CanConfirm, Is.False);
            Assert.That(old.TrySetPurchase("rat_tail", 1), Is.False);
            Assert.That(store.TryCommit(old), Is.EqualTo(SettlementCommitResult.InvalidSelection));
            Assert.That(store.Progress.PlayerGold, Is.EqualTo(20));
            Assert.That(File.ReadAllText(_path), Is.EqualTo(saved));
        }

        [Test]
        public void FailedSavePreservesOriginalFileAndMemoryThenCanRetrySameDraft()
        {
            var store = NewStore();
            store.TryCommit(Draft(store, "original", 2));
            string saved = File.ReadAllText(_path);
            var settlement = Draft(store, "retry", 4);
            settlement.TrySetPurchase("rat_tail", 1);
            Directory.CreateDirectory(_path + ".tmp");
            Assert.That(store.TryCommit(settlement), Is.EqualTo(SettlementCommitResult.SaveFailed));
            Assert.That(settlement.IsCommitted, Is.False);
            Assert.That(store.Progress.PlayerGold, Is.EqualTo(10));
            Assert.That(store.Progress.GetGold("mira"), Is.EqualTo(10));
            Assert.That(store.Progress.Items, Is.Empty);
            Assert.That(store.Progress.HasSettlement("retry"), Is.False);
            Assert.That(File.ReadAllText(_path), Is.EqualTo(saved));
            Directory.Delete(_path + ".tmp");
            Assert.That(store.TryCommit(settlement), Is.EqualTo(SettlementCommitResult.Saved));
            Assert.That(store.Progress.PlayerGold, Is.EqualTo(20));
            Assert.That(store.Progress.Items["rat_tail"], Is.EqualTo(1));
        }

        [Test]
        public void CorruptLoadPreservesLiveStateAndBlocksReplacingTheCorruptFile()
        {
            var store = NewStore();
            store.TryCommit(Draft(store, "original", 4));
            const string corrupt = "{\"Format\":\"P2.Adventure\",\"Version\":1}";
            File.WriteAllText(_path, corrupt);
            Assert.That(store.Load(), Is.EqualTo(AdventureLoadResult.Corrupt));
            Assert.That(store.CanWrite, Is.False);
            Assert.That(store.Progress.PlayerGold, Is.EqualTo(20));
            Assert.That(store.Progress.HasSettlement("original"), Is.True);
            Assert.That(store.TryCommit(Draft(store, "blocked", 2)), Is.EqualTo(SettlementCommitResult.LoadBlocked));
            Assert.That(File.ReadAllText(_path), Is.EqualTo(corrupt));
            var fresh = new AdventureProgressStore(_sheet, _path);
            Assert.That(fresh.Load(), Is.EqualTo(AdventureLoadResult.Corrupt));
            Assert.That(fresh.CanWrite, Is.False);
        }

        [TestCase("gold_missing")]
        [TestCase("gold_negative")]
        [TestCase("members_duplicate")]
        [TestCase("member_unknown")]
        [TestCase("items_mismatch")]
        [TestCase("item_unknown")]
        [TestCase("item_zero")]
        [TestCase("settlements_missing")]
        [TestCase("settlements_duplicate")]
        public void SaveSchemaRejectsMissingInvalidAndDuplicateValues(string mutation)
        {
            AdventureSaveData dto = ValidSave();
            switch (mutation)
            {
                case "gold_missing": break;
                case "gold_negative": dto.MemberGold[0] = -1; break;
                case "members_duplicate": dto.MemberIds[1] = dto.MemberIds[0]; break;
                case "member_unknown": dto.MemberIds[1] = "stranger"; break;
                case "items_mismatch": dto.ItemCounts = Array.Empty<int>(); break;
                case "item_unknown": dto.ItemIds[0] = "unknown"; break;
                case "item_zero": dto.ItemCounts[0] = 0; break;
                case "settlements_missing": break;
                case "settlements_duplicate": dto.SettlementIds = new[] { "once", "once" }; break;
            }
            string contents = JsonUtility.ToJson(dto);
            // JsonUtility.ToJson은 null 배열을 []로 정규화한다. 실제 필드 누락을 만든다.
            if (mutation == "gold_missing" || mutation == "settlements_missing")
            {
                contents = RemoveArrayField(contents, mutation == "gold_missing" ? "MemberGold" : "SettlementIds");
            }
            File.WriteAllText(_path, contents);
            Assert.That(AdventureSave.Load(_path, _sheet, out var loaded), Is.EqualTo(AdventureLoadResult.Corrupt));
            Assert.That(loaded, Is.Null);
            Assert.That(File.ReadAllText(_path), Is.EqualTo(contents));
        }

        [TestCase("MemberIds")]
        [TestCase("MemberGold")]
        [TestCase("ItemIds")]
        [TestCase("ItemCounts")]
        [TestCase("SettlementIds")]
        public void MissingRequiredArrayIsRejectedEvenWhenOtherInventoryAndLedgerArraysAreEmpty(string field)
        {
            var dto = ValidSave();
            dto.ItemIds = Array.Empty<string>();
            dto.ItemCounts = Array.Empty<int>();
            dto.SettlementIds = Array.Empty<string>();
            string contents = RemoveArrayField(JsonUtility.ToJson(dto), field);
            File.WriteAllText(_path, contents);
            var store = new AdventureProgressStore(_sheet, _path);
            Assert.That(store.Load(), Is.EqualTo(AdventureLoadResult.Corrupt));
            Assert.That(store.CanWrite, Is.False);
            Assert.That(File.ReadAllText(_path), Is.EqualTo(contents));
        }

        [Test]
        public void ExplicitEmptyInventoryAndLedgerArraysRemainValid()
        {
            var dto = ValidSave();
            dto.ItemIds = Array.Empty<string>();
            dto.ItemCounts = Array.Empty<int>();
            dto.SettlementIds = Array.Empty<string>();
            AdventureSave.Save(_path, dto, _sheet);
            var store = new AdventureProgressStore(_sheet, _path);
            Assert.That(store.Load(), Is.EqualTo(AdventureLoadResult.Loaded));
            Assert.That(store.CanWrite, Is.True);
            Assert.That(store.Progress.Items, Is.Empty);
            Assert.That(store.Progress.HasSettlement("previous"), Is.False);
        }

        [TestCase("MemberIds")]
        [TestCase("MemberGold")]
        [TestCase("ItemIds")]
        [TestCase("ItemCounts")]
        [TestCase("SettlementIds")]
        public void ExplicitNullRequiredArrayIsRejected(string field)
        {
            string json = JsonUtility.ToJson(ValidSave());
            string pattern = ",\\s*\"" + Regex.Escape(field) + "\"\\s*:\\s*\\[[^\\]]*\\]";
            Assert.That(Regex.Matches(json, pattern).Count, Is.EqualTo(1));
            string contents = Regex.Replace(json, pattern, ",\"" + field + "\":null");
            File.WriteAllText(_path, contents);
            Assert.That(AdventureSave.Load(_path, _sheet, out var loaded), Is.EqualTo(AdventureLoadResult.Corrupt));
            Assert.That(loaded, Is.Null);
            Assert.That(File.ReadAllText(_path), Is.EqualTo(contents));
        }

        [Test]
        public void ArrayValidationIgnoresEscapedArrayLikeTextInsideStringsAndNestedObjects()
        {
            string json = JsonUtility.ToJson(ValidSave());
            string extra = ",\"Notes\":\"A \\\"SettlementIds\\\":null text and \\\\ backslash\",\"Extra\":{\"SettlementIds\":null}";
            string contents = json.Insert(json.Length - 1, extra);
            File.WriteAllText(_path, contents);
            Assert.That(AdventureSave.Load(_path, _sheet, out var loaded), Is.EqualTo(AdventureLoadResult.Loaded));
            CollectionAssert.AreEqual(new[] { "previous" }, loaded.SettlementIds);
        }

        [TestCase("duplicate")]
        [TestCase("escaped_null")]
        [TestCase("string")]
        [TestCase("object")]
        public void RawRequiredArrayShapeRejectsDuplicateOrWronglyTypedFields(string mutation)
        {
            string contents = JsonUtility.ToJson(ValidSave());
            string original = "\"SettlementIds\":[\"previous\"]";
            Assert.That(contents.Contains(original), Is.True);
            switch (mutation)
            {
                case "duplicate": contents = contents.Replace(original, original + "," + original); break;
                case "escaped_null": contents = contents.Replace(original, "\"\\u0053ettlementIds\":null"); break;
                case "string": contents = contents.Replace(original, "\"SettlementIds\":\"[]\""); break;
                case "object": contents = contents.Replace(original, "\"SettlementIds\":{}"); break;
            }
            File.WriteAllText(_path, contents);
            Assert.That(AdventureSave.Load(_path, _sheet, out var loaded), Is.EqualTo(AdventureLoadResult.Corrupt));
            Assert.That(loaded, Is.Null);
        }

        [Test]
        public void UnsupportedVersionAndIoFailurePreventWrites()
        {
            var dto = ValidSave();
            dto.Version++;
            File.WriteAllText(_path, JsonUtility.ToJson(dto));
            var store = new AdventureProgressStore(_sheet, _path);
            Assert.That(store.Load(), Is.EqualTo(AdventureLoadResult.UnsupportedVersion));
            Assert.That(store.CanWrite, Is.False);
            var directoryStore = new AdventureProgressStore(_sheet, _directory);
            Assert.That(directoryStore.Load(), Is.EqualTo(AdventureLoadResult.IOError));
            Assert.That(directoryStore.CanWrite, Is.False);
        }

        [Test]
        public void LongGoldRoundTripsAndOverflowBlocksCommitWithoutPartialChanges()
        {
            var dto = ValidSave();
            dto.MemberGold[0] = 3000000000;
            AdventureSave.Save(_path, dto, _sheet);
            var store = new AdventureProgressStore(_sheet, _path);
            Assert.That(store.Load(), Is.EqualTo(AdventureLoadResult.Loaded));
            Assert.That(store.Progress.PlayerGold, Is.EqualTo(3000000000));
            dto.MemberGold[0] = long.MaxValue;
            AdventureSave.Save(_path, dto, _sheet);
            store.Load();
            string saved = File.ReadAllText(_path);
            var settlement = Draft(store, "gold_overflow", 2);
            Assert.That(settlement.CanConfirm, Is.False);
            Assert.That(store.TryCommit(settlement), Is.EqualTo(SettlementCommitResult.InvalidSelection));
            Assert.That(File.ReadAllText(_path), Is.EqualTo(saved));
            dto.MemberGold[0] = 0;
            dto.ItemCounts[0] = int.MaxValue;
            AdventureSave.Save(_path, dto, _sheet);
            store.Load();
            settlement = Draft(store, "item_overflow", 2);
            settlement.TrySetPurchase("rat_tail", 1);
            Assert.That(settlement.CanConfirm, Is.False);
            Assert.That(store.TryCommit(settlement), Is.EqualTo(SettlementCommitResult.InvalidSelection));
            Assert.That(store.Progress.Items["rat_tail"], Is.EqualTo(int.MaxValue));
        }

        [Test]
        public void PlayerRoleComesFromPartyRatherThanTheHeroId()
        {
            string party = Read("DungeonPartyTable.csv");
            party = party.Replace("hero,모험가,96,16,0.65,2,1", "hero,모험가,96,16,0.65,2,0")
                .Replace("mira,미라,76,12,0.75,0,0", "mira,미라,76,12,0.75,0,1");
            _dungeon = CreateDungeon(party);
            _sheet = CreateSheet(settings: "Key,Value\nStartingGold,25");
            var store = NewStore();
            Assert.That(_sheet.PlayerMemberId, Is.EqualTo("mira"));
            Assert.That(store.Progress.GetGold("mira"), Is.EqualTo(25));
            Assert.That(store.Progress.GetGold("hero"), Is.Zero);
            var settlement = Draft(store, "different_player", 2);
            settlement.TrySetPurchase("rat_tail", 1);
            Assert.That(store.TryCommit(settlement), Is.EqualTo(SettlementCommitResult.Saved));
            Assert.That(store.Progress.PlayerGold, Is.EqualTo(25));
            Assert.That(store.Progress.GetGold("hero"), Is.EqualTo(10));
        }

        private AdventureProgressStore NewStore()
        {
            var store = new AdventureProgressStore(_sheet, _path);
            Assert.That(store.Load(), Is.EqualTo(AdventureLoadResult.Missing));
            return store;
        }

        private LootSettlement Draft(AdventureProgressStore store, string id, int quantity)
        {
            return new LootSettlement(_sheet, id, new Dictionary<string, int> { { "rat_tail", quantity } }, store.Progress);
        }

        private LootSheet CreateSheet(string items = null, string drops = null, string shares = null, string settings = null, string texts = null)
        {
            return new LootSheet(items ?? Read("LootItemTable.csv"), drops ?? Read("LootDropTable.csv"),
                shares ?? Read("LootShareTable.csv"), settings ?? Read("LootSettingTable.csv"), texts ?? Read("LootTextTable.csv"), _dungeon);
        }

        private static DungeonSheet CreateDungeon(string party = null)
        {
            var town = new TownSheet(Read("TownLocationTable.csv"), Read("TownCommandTable.csv"),
                Read("TownCharacterTable.csv"), Read("TownTextTable.csv"));
            return new DungeonSheet(Read("DungeonDefinitionTable.csv"), Read("DungeonNodeTable.csv"),
                party ?? Read("DungeonPartyTable.csv"), Read("DungeonSettingTable.csv"), Read("DungeonTextTable.csv"), town);
        }

        private static AdventureSaveData ValidSave()
        {
            return new AdventureSaveData
            {
                Format = AdventureSave.CurrentFormat,
                Version = AdventureSave.CurrentVersion,
                MemberIds = new[] { "hero", "mira" },
                MemberGold = new long[] { 0, 0 },
                ItemIds = new[] { "rat_tail" },
                ItemCounts = new[] { 1 },
                SettlementIds = new[] { "previous" }
            };
        }

        private static string RemoveArrayField(string json, string field)
        {
            // 이 테스트 DTO의 배열은 루트 첫 필드가 아니며 중첩 배열을 포함하지 않는다.
            string pattern = ",\\s*\"" + Regex.Escape(field) + "\"\\s*:\\s*\\[[^\\]]*\\]";
            Assert.That(Regex.Matches(json, pattern).Count, Is.EqualTo(1), "테스트에서 실제 배열 필드를 제거해야 한다.");
            return Regex.Replace(json, pattern, string.Empty);
        }

        private static string Read(string name)
        {
            return File.ReadAllText(Path.Combine(Application.dataPath, "1_InternalTools/Util/DataSheetLoader/Table", name));
        }
    }
}
