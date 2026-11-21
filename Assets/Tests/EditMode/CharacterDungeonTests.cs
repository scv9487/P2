using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Refactor.Main;
using Refactor.Main.Event;
using Refactor.Main.InputModule;
using Refactor.Util;
using UnityEngine;
using EventHandler = Refactor.Main.Event.EventHandler;
using Object = UnityEngine.Object;

namespace P2.Tests
{
    // 비활성 MainFlow는 조립과 실제 조율 메서드만 사용한다. 씬 Awake/Update나 사용자 저장은 실행하지 않는다.
    public sealed class CharacterDungeonTests
    {
        private TownSheet _town;
        private DungeonSheet _sheet;
        private LootSheet _loot;
        private ShopSheet _shops;
        private CharacterSheet _characters;
        private AdventureProgressStore _store;
        private DungeonDataContainer _data;
        private CombatTickFlow _tick;
        private MainFlow _flow;
        private GameObject _host;
        private InputStream _stream;
        private UIInputStream _input;
        private EventQueue _queue;
        private Func<bool, bool> _commitExperience;
        private string _directory;
        private string _path;
        private string _characterCsv;
        private string _enemyCsv;
        private readonly NormalRandom _random = new();

        [SetUp]
        public void SetUp()
        {
            DI.Init();
            _town = new TownSheet(Read("TownLocationTable.csv"), Read("TownCommandTable.csv"), Read("TownCharacterTable.csv"), Read("TownTextTable.csv"));
            _sheet = new DungeonSheet(Read("DungeonDefinitionTable.csv"), Read("DungeonNodeTable.csv"), Read("DungeonPartyTable.csv"),
                Read("DungeonSettingTable.csv"), Read("DungeonTextTable.csv"), _town);
            _loot = new LootSheet(Read("LootItemTable.csv"), Read("LootDropTable.csv"), Read("LootShareTable.csv"), Read("LootSettingTable.csv"), Read("LootTextTable.csv"), _sheet);
            _shops = new ShopSheet(Read("ShopMerchantTable.csv"), Read("ShopStockTable.csv"), Read("ShopTextTable.csv"), _loot, _town);
            _characterCsv = Read("CharacterDefinitionTable.csv");
            _enemyCsv = Read("CombatEnemyTable.csv");
            _directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "CharacterDungeonTests", Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(_directory);
            _path = Path.Combine(_directory, "progress.json");
        }

        [TearDown]
        public void TearDown()
        {
            if (_host != null)
            {
                Object.DestroyImmediate(_host);
            }
            DI.Init();
            string allowed = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "CharacterDungeonTests")) + Path.DirectorySeparatorChar;
            Assert.That(_directory.StartsWith(allowed, StringComparison.OrdinalIgnoreCase), Is.True);
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, true);
            }
        }

        [Test]
        public void NewExpeditionUsesSavedSoloHeroAndDsRatAndPunchRatherThanLegacyPartyStats()
        {
            CreateStore();
            CreateRuntime();
            CollectionAssert.AreEqual(new[] { "hero" }, _data.Allies.Select(unit => unit.Id));
            var hero = _data.Allies[0];
            Assert.That(hero.MaxHp, Is.EqualTo(295));
            Assert.That(hero.Attack, Is.EqualTo(33.5f));
            Assert.That(hero.AttackCoefficient, Is.EqualTo(0.3f));
            Assert.That(hero.Speed, Is.EqualTo(1f / 1.2f).Within(0.0001f));
            Request(UIInputType.DungeonMove, "guard", _data.Context);
            Assert.That(_data.Enemies.Count, Is.EqualTo(2));
            Assert.That(_data.Enemies[0].MaxHp, Is.EqualTo(500));
            Assert.That(_data.Enemies[0].Attack, Is.EqualTo(30));
            Assert.That(_data.Enemies[0].Speed, Is.EqualTo(0.3f));
            _tick.Update(1.2f);
            Request(UIInputType.DungeonAttack, "rat_0", _data.ReadyAllyId);
            Assert.That(_data.Enemies[0].Hp, Is.EqualTo(489.95f).Within(0.0001f));
            Assert.That(_data.PendingExperience, Is.Null);
        }

        [Test]
        public void DismissAndRejoinPreserveExpeditionInjuriesAndRemovePartyMembersFromNodeVisitors()
        {
            SetCharacter("hero", "Strength", "100");
            SetEnemy("MaxHealth", "25");
            SetEnemy("PhysicalAttackPower", "10");
            SetEnemy("AttackSpeed", "1");
            CreateStore();
            Recruit("sera");
            CreateRuntime();
            Request(UIInputType.DungeonMove, "visitor", _data.Context);
            Assert.That(_data.VisitorId, Is.Empty);
            Assert.That(_data.CanTalk, Is.False);
            Request(UIInputType.DungeonMove, "camp", _data.Context);
            Request(UIInputType.DungeonMove, "guard", _data.Context);
            _tick.Update(1.1f);
            var sera = _data.Allies.Single(unit => unit.Id == "sera");
            float woundedHp = sera.Hp;
            Assert.That(woundedHp, Is.LessThan(sera.MaxHp));
            Request(UIInputType.PartyCharacterOpen, "sera", "party");
            string oldPopup = _flow.CharacterInteraction.Context;
            Request(UIInputType.CharacterDismiss, "sera", oldPopup);
            Assert.That(_flow.CharacterInteraction.Open, Is.False);
            CollectionAssert.AreEqual(new[] { "hero" }, _data.Allies.Select(unit => unit.Id));
            FinishBattle();
            Request(UIInputType.DungeonCloseDetails);
            Request(UIInputType.DungeonMove, "camp", _data.Context);
            Request(UIInputType.DungeonMove, "visitor", _data.Context);
            Assert.That(_data.VisitorId, Is.EqualTo("sera"));
            Request(UIInputType.CharacterOpen, "sera", _data.Context);
            string freshPopup = _flow.CharacterInteraction.Context;
            Request(UIInputType.CharacterRecruit, "sera", oldPopup);
            Assert.That(_store.Progress.IsInParty("sera"), Is.False);
            Assert.That(_flow.CharacterInteraction.Context, Is.EqualTo(freshPopup));
            Request(UIInputType.CharacterRecruit, "sera", freshPopup);
            Assert.That(_store.Progress.IsInParty("sera"), Is.True);
            Assert.That(_data.Allies.Single(unit => unit.Id == "sera"), Is.SameAs(sera));
            Assert.That(sera.Hp, Is.EqualTo(woundedHp));
            Assert.That(_data.VisitorId, Is.Empty);
            CollectionAssert.AreEqual(new[] { "hero", "sera" }, _data.ExpeditionMemberIds);
        }

        [Test]
        public void KillExperienceImmediatelyRecalculatesGrowthWithoutHealingCurrentHp()
        {
            SetCharacter("hero", "Strength", "100");
            SetEnemy("MaxHealth", "25");
            SetEnemy("AttackSpeed", "1");
            CreateStore();
            Assert.That(_store.TryGrantExperience("almost_level_two", new Dictionary<string, float> { ["hero"] = 29 }, _random), Is.EqualTo(CharacterActionResult.Saved));
            CreateRuntime();
            Request(UIInputType.DungeonMove, "guard", _data.Context);
            _tick.Update(1.2f);
            var hero = _data.Allies.Single();
            float beforeHp = hero.Hp;
            Assert.That(beforeHp, Is.LessThan(hero.MaxHp));
            Request(UIInputType.DungeonAttack, "rat_0", _data.ReadyAllyId);
            Assert.That(_data.PendingExperience, Is.Not.Null);
            Assert.That(_commitExperience(false), Is.True);
            Assert.That(_store.Progress.GetCharacter("hero").Level, Is.EqualTo(2));
            Assert.That(_store.Progress.GetCharacter("hero").Experience, Is.Zero);
            Assert.That(hero.MaxHp, Is.EqualTo(313));
            Assert.That(hero.Hp, Is.EqualTo(beforeHp), "레벨업은 현재 HP를 회복시키지 않습니다.");
            Assert.That(hero.Attack, Is.EqualTo(127.5f));
        }

        [Test]
        public void AutomaticCompanionKillsRewardDeadPartyMembersOnceAndRevisitedBattlesDoNotPayAgain()
        {
            SetCharacter("mira", "Strength", "100");
            SetCharacter("mira", "Constitution", "1000");
            SetEnemy("MaxHealth", "25");
            SetEnemy("PhysicalAttackPower", "400");
            SetEnemy("AttackSpeed", "3");
            CreateStore();
            Recruit("mira");
            CreateRuntime();
            Request(UIInputType.DungeonMove, "guard", _data.Context);
            _tick.Update(2f);
            Assert.That(_data.Allies.Single(unit => unit.Id == "hero").IsAlive, Is.False);
            var first = _data.PendingExperience;
            Assert.That(first, Is.Not.Null);
            CollectionAssert.AreEquivalent(new[] { "hero", "mira" }, first.Grants.Keys);
            Assert.That(first.Grants["hero"], Is.EqualTo(4));
            Assert.That(first.Grants["mira"], Is.EqualTo(4));
            Assert.That(_commitExperience(false), Is.True);
            Assert.That(_store.TryGrantExperience(first.Id, first.Grants, _random), Is.EqualTo(CharacterActionResult.AlreadyApplied));
            FinishBattle();
            Assert.That(_data.IsDefeated, Is.False);
            Assert.That(_data.Loot["rat_tail"], Is.EqualTo(2));
            Assert.That(_store.Progress.GetCharacter("hero").Experience, Is.EqualTo(8));
            Assert.That(_store.Progress.GetCharacter("mira").Experience, Is.EqualTo(8));
            Request(UIInputType.DungeonCloseDetails);
            Request(UIInputType.DungeonMove, "camp", _data.Context);
            Request(UIInputType.DungeonMove, "guard", _data.Context);
            Request(UIInputType.DungeonAttack, "rat_0", "hero");
            Assert.That(_data.InCombat, Is.False);
            Assert.That(_data.PendingExperience, Is.Null);
            Assert.That(_data.Loot["rat_tail"], Is.EqualTo(2));
            Assert.That(_store.Progress.GetCharacter("hero").Experience, Is.EqualTo(8));
        }

        [Test]
        public void ExperienceSaveFailureBlocksReadyPlayerAndTicksUntilTheActualRetrySucceeds()
        {
            SetCharacter("mira", "Strength", "100");
            SetEnemy("MaxHealth", "25");
            CreateStore();
            Recruit("mira");
            CreateRuntime();
            Request(UIInputType.DungeonMove, "guard", _data.Context);
            _tick.Update(1.2f);
            Assert.That(_data.PendingExperience, Is.Not.Null);
            Assert.That(_data.IsAwaitingCommand, Is.True);
            string beforeFile = File.ReadAllText(_path);
            Directory.CreateDirectory(_path + ".tmp");
            Assert.That(_commitExperience(false), Is.False);
            Assert.That(_data.ProgressSaveBlocked, Is.True);
            float hp = _data.Enemies.Single(unit => unit.IsAlive).Hp;
            float heroGauge = _data.Allies.Single(unit => unit.IsPlayerControlled).Gauge;
            Request(UIInputType.DungeonAttack, "rat_1", _data.ReadyAllyId);
            _tick.Update(100);
            Request(UIInputType.DungeonCloseDetails);
            Request(UIInputType.DungeonReturnTown);
            Assert.That(_data.Enemies.Single(unit => unit.IsAlive).Hp, Is.EqualTo(hp));
            Assert.That(_data.Allies.Single(unit => unit.IsPlayerControlled).Gauge, Is.EqualTo(heroGauge));
            Assert.That(_data.DetailsOpen, Is.True);
            Assert.That(_data.SettlementOpen, Is.False);
            Assert.That(File.ReadAllText(_path), Is.EqualTo(beforeFile));
            Directory.Delete(_path + ".tmp");
            Request(UIInputType.DungeonCloseDetails);
            Assert.That(_data.ProgressSaveBlocked, Is.False);
            Assert.That(_data.DetailsOpen, Is.False);
            Assert.That(_store.Progress.GetCharacter("hero").Experience, Is.EqualTo(4));
            Assert.That(_data.PendingExperience, Is.Null);
        }

        [Test]
        public void SettlementUsesEveryoneWhoJoinedThisExpeditionIncludingDismissedMembers()
        {
            CreateStore();
            Recruit("mira");
            Recruit("sera");
            CreateRuntime();
            Request(UIInputType.PartyCharacterOpen, "mira", "party");
            Request(UIInputType.CharacterDismiss, "mira", _flow.CharacterInteraction.Context);
            Assert.That(_store.Progress.IsInParty("mira"), Is.False);
            CollectionAssert.AreEqual(new[] { "hero", "mira", "sera" }, _data.ExpeditionMemberIds);
            var shares = Bind<Func<IReadOnlyList<LootShareDefinition>>>("ExpeditionShares")();
            CollectionAssert.AreEqual(new[] { 34, 33, 33 }, shares.Select(share => share.Percent));
            var settlement = new LootSettlement(_loot, "participating_members", new Dictionary<string, int> { ["rat_tail"] = 10 }, _store.Progress, shares);
            Assert.That(_store.TryCommit(settlement), Is.EqualTo(SettlementCommitResult.Saved));
            Assert.That(_store.Progress.GetGold("hero"), Is.EqualTo(34));
            Assert.That(_store.Progress.GetGold("mira"), Is.EqualTo(33));
            Assert.That(_store.Progress.GetGold("sera"), Is.EqualTo(33));
            Assert.That(_store.Progress.GetGold("bran"), Is.Zero);
            Assert.That(_store.TryCommit(settlement), Is.EqualTo(SettlementCommitResult.AlreadyCommitted));
        }

        private void CreateStore()
        {
            var growth = new CharacterGrowthSheet(Read("CharacterGrowthTable.csv"), Read("CharacterLevelTable.csv"), Read("CharacterGrowthSettingTable.csv"));
            var combat = new CombatStatSheet(Read("CombatStatTable.csv"), _enemyCsv, Read("CombatAttackTable.csv"));
            _characters = new CharacterSheet(_characterCsv, Read("CharacterSettingTable.csv"), Read("CharacterTextTable.csv"), _town, growth, combat);
            _store = new AdventureProgressStore(_loot, _path, _shops, _characters);
            Assert.That(_store.Load(), Is.EqualTo(AdventureLoadResult.Missing));
        }

        private void Recruit(string id)
        {
            Assert.That(_store.TryTalk(id, "talk_" + id, _store.Progress.Revision), Is.EqualTo(CharacterActionResult.Saved));
            Assert.That(_store.TryRecruit(id, "join_" + id, _store.Progress.Revision, 0), Is.EqualTo(CharacterActionResult.Saved));
        }

        private void CreateRuntime()
        {
            _data = new DungeonDataContainer(_sheet, 1, _loot, _characters, _store.Progress, _random);
            DI.PubSub(_data);
            _tick = new CombatTickFlow(_data);
            _host = new GameObject("CharacterDungeonTests isolated composition");
            _host.SetActive(false);
            _flow = _host.AddComponent<MainFlow>();
            Field("_sceneType", SceneType.Dungeon);
            Field("_characterSheet", _characters);
            Field("_characterRandom", _random);
            Field("_adventureStore", _store);
            Field("_dungeonData", _data);
            _commitExperience = Bind<Func<bool, bool>>("CommitDungeonExperience");
            _stream = new InputStream();
            _input = new UIInputStream(_stream);
            var filter = new InputFilter(() => false);
            _queue = new EventQueue(new EventHandler(new DungeonEventHandler(filter, () => { },
                isCharacterOpen: () => _flow.CharacterInteraction.Open,
                closeCharacter: Bind<Action>("CloseCharacter"), openCharacter: Bind<Action<string>>("OpenDungeonCharacter"),
                beforeCloseDetails: () => _commitExperience(true)),
                new CharacterEventHandler(filter, Bind<Action<CharacterActionRequested>>("HandleCharacterAction"))));
        }

        private void FinishBattle()
        {
            for (int step = 0; step < 250 && _data.InCombat; step++)
            {
                Assert.That(_commitExperience(false), Is.True);
                _tick.Update(0.2f);
                Assert.That(_commitExperience(false), Is.True);
                if (_data.IsAwaitingCommand)
                {
                    Request(UIInputType.DungeonAttack, _data.Enemies.First(unit => unit.IsAlive).Id, _data.ReadyAllyId);
                }
            }
            Assert.That(_commitExperience(false), Is.True);
            Assert.That(_data.InCombat, Is.False, "유한한 테스트 전투가 끝나야 합니다.");
        }

        private void Request(UIInputType type, string target = null, string context = null)
        {
            _input.AddInput(type, target, context);
            foreach (var request in new InputBinding(_stream).ConvertInputStreamToEventData())
            {
                _queue.AddEvent(request);
            }
            _stream.ClearInputs();
            _queue.Update();
        }

        // private 접근은 MainFlow 조립과 실제 후속단계 호출에만 한정한다. HP/게이지/성장 상태를 직접 변경하지 않는다.
        private void Field(string name, object value) => typeof(MainFlow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_flow, value);
        private T Bind<T>(string name) where T : Delegate => (T)Delegate.CreateDelegate(typeof(T), _flow,
            typeof(MainFlow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic));
        private void SetCharacter(string id, string column, string value) => _characterCsv = ReplaceCell(_characterCsv, id, column, value);
        private void SetEnemy(string column, string value) => _enemyCsv = ReplaceCell(_enemyCsv, "grey_rat", column, value);
        private static string ReplaceCell(string csv, string id, string column, string value)
        {
            string[] lines = csv.Replace("\r", string.Empty).Split('\n');
            int index = Array.IndexOf(lines[0].Split(','), column);
            Assert.That(index, Is.GreaterThanOrEqualTo(0));
            for (int i = 1; i < lines.Length; i++)
            {
                var cells = lines[i].Split(',');
                if (cells[0] == id)
                {
                    cells[index] = value;
                    lines[i] = string.Join(",", cells);
                }
            }
            return string.Join("\n", lines);
        }
        private static string Read(string name) => File.ReadAllText(Path.Combine(Application.dataPath, "1_InternalTools/Util/DataSheetLoader/Table", name));

        private sealed class NormalRandom : IRandomSource
        {
            private int _draw;
            public int NextInt(int minimum, int maximum) => minimum;
            public float NextFloat(float minimum, float maximum)
            {
                // 모든 기본 물리 공격이 명중/비가드/비치명타가 되도록 실제 세 판정 순서를 따른다.
                return _draw++ % 3 == 0 ? 0f : 99f;
            }
        }
    }
}
