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
    public sealed class QuestDungeonTests
    {
        private TownSheet _town;
        private DungeonSheet _sheet;
        private LootSheet _loot;
        private ShopSheet _shops;
        private CharacterSheet _characters;
        private QuestSheet _quests;
        private AdventureProgressStore _store;
        private DungeonDataContainer _data;
        private CombatTickFlow _tick;
        private UIInputStream _input;
        private InputStream _stream;
        private EventQueue _queue;
        private MainFlow _flow;
        private GameObject _host;
        private Func<bool, bool> _commit;
        private Func<bool, bool> _commitExperience;
        private NormalRandom _random;
        private string _directory;
        private string _path;

        [SetUp]
        public void SetUp()
        {
            DI.Init();
            _random = new NormalRandom();
            _town = new TownSheet(Read("TownLocationTable.csv"), Read("TownCommandTable.csv"), Read("TownCharacterTable.csv"), Read("TownTextTable.csv"));
            _sheet = new DungeonSheet(Read("DungeonDefinitionTable.csv"), Read("DungeonNodeTable.csv"), Read("DungeonPartyTable.csv"),
                Read("DungeonSettingTable.csv"), Read("DungeonTextTable.csv"), _town);
            _loot = new LootSheet(Read("LootItemTable.csv"), Read("LootDropTable.csv"), Read("LootShareTable.csv"),
                Read("LootSettingTable.csv"), Read("LootTextTable.csv"), _sheet);
            _shops = new ShopSheet(Read("ShopMerchantTable.csv"), Read("ShopStockTable.csv"), Read("ShopTextTable.csv"), _loot, _town);
            _quests = new QuestSheet(Read("QuestDefinitionTable.csv"), Read("QuestRescueNodeTable.csv"), Read("QuestTextTable.csv"), _sheet, _loot);
            _directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "QuestDungeonTests", Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(_directory);
            _path = Path.Combine(_directory, "progress.json");
            CreateStore();
        }

        [TearDown]
        public void TearDown()
        {
            if (_host != null)
            {
                Object.DestroyImmediate(_host);
            }
            DI.Init();
            string allowed = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "QuestDungeonTests")) + Path.DirectorySeparatorChar;
            Assert.That(_directory.StartsWith(allowed, StringComparison.OrdinalIgnoreCase), Is.True);
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, true);
            }
        }

        [Test]
        public void RescueGraphIsAnEntrySnapshotAndOnlyExtendsTheAcceptedDungeonInstance()
        {
            var beforeAcceptance = NewDungeon(1);
            var definition = Quest(QuestKind.RescueAdventurers);
            var rescue = _quests.GetRescueNode(definition.Id);
            Accept(definition);
            var accepted = NewDungeon(definition.DungeonId);
            Assert.That(beforeAcceptance.Nodes.Any(node => node.Type == DungeonNodeType.Rescue), Is.False);
            Assert.That(NewDungeon(2).Nodes.Any(node => node.Type == DungeonNodeType.Rescue), Is.False);
            Assert.That(_sheet.GetNodes(rescue.Floor).Any(node => node.Id == rescue.Id), Is.False);
            Assert.That(_sheet.GetNode(rescue.Floor, rescue.AnchorId).NeighborIds.Contains(rescue.Id), Is.False);
            var node = accepted.Nodes.Single(item => item.Id == rescue.Id);
            Assert.That(node.Type, Is.EqualTo(DungeonNodeType.Rescue));
            Assert.That(node.Icon, Is.EqualTo(rescue.Icon));
            Assert.That(node.X, Is.EqualTo(rescue.X));
            Assert.That(node.Y, Is.EqualTo(rescue.Y));
            CollectionAssert.AreEqual(new[] { rescue.AnchorId }, node.NeighborIds);
            CollectionAssert.Contains(accepted.Nodes.Single(item => item.Id == rescue.AnchorId).NeighborIds, rescue.Id);
            AssertConnected(accepted.Nodes);
            Assert.Throws<FormatException>(() => _sheet.GetNode(rescue.Floor, rescue.Id));
        }

        [Test]
        public void RescueVictoryQueuesKillsThenRescueAndRevisitingCannotDuplicateFactsOrRecruitAnyone()
        {
            var definition = Quest(QuestKind.RescueAdventurers);
            Accept(definition);
            CreateRuntime();
            EnterRescue(definition);
            Assert.That(_data.Enemies.Count, Is.EqualTo(_quests.GetRescueNode(definition.Id).EnemyCount));
            FightWithoutCommitting();
            Assert.That(_data.DetailsTitle, Is.EqualTo(_quests.Text("rescue.success_title")));
            Assert.That(_data.DetailsBody, Is.EqualTo(_quests.Text("rescue.success_body")));
            Assert.That(_data.IsCleared, Is.False);
            Assert.That(_data.IsNodeResolved(_data.NodeId), Is.True);
            CollectionAssert.AreEqual(new[] { "hero" }, _store.Progress.PartyIds);
            var facts = DrainFacts();
            Assert.That(facts.Count, Is.EqualTo(_quests.GetRescueNode(definition.Id).EnemyCount + 1));
            Assert.That(facts.Take(facts.Count - 1).All(fact => fact.Type == QuestFactType.KillMonster && fact.TargetId == "gray_rat"), Is.True);
            Assert.That(facts.Last().Type, Is.EqualTo(QuestFactType.AdventurersRescued));
            Assert.That(facts.Last().TargetId, Is.EqualTo(definition.TargetId));
            Assert.That(facts.Last().Floor, Is.EqualTo(definition.Floor));
            Assert.That(facts.Select(fact => fact.Id).Distinct().Count(), Is.EqualTo(facts.Count));
            Assert.That(_store.Progress.Quest.ProgressCount, Is.EqualTo(definition.RequiredCount));
            Assert.That(_commitExperience(false), Is.True);
            Request(UIInputType.DungeonCloseDetails);
            Move(_quests.GetRescueNode(definition.Id).AnchorId);
            Move(definition.TargetId);
            Assert.That(_data.InCombat, Is.False);
            Assert.That(_data.PendingQuestFact, Is.Null);
            CollectionAssert.AreEqual(new[] { "hero" }, _store.Progress.PartyIds);
        }

        [Test]
        public void AchievedOrReportedRescueDoesNotCreateAnotherRescueNodeOnReentry()
        {
            var definition = Quest(QuestKind.RescueAdventurers);
            Accept(definition);
            var fact = new QuestFact(QuestFactType.AdventurersRescued, "completed_rescue", definition.DungeonId,
                definition.TargetId, definition.RequiredCount, _store.Progress.Quest.AcceptanceId, definition.Floor);
            Assert.That(_store.TryApplyQuestFact(fact), Is.EqualTo(QuestActionResult.Saved));
            Assert.That(NewDungeon(definition.DungeonId).Nodes.Any(node => node.Type == DungeonNodeType.Rescue), Is.False);
            Assert.That(_store.TryReportQuest(definition.Id, "report_rescue", _store.Progress.Revision), Is.EqualTo(QuestActionResult.Saved));
            Assert.That(NewDungeon(definition.DungeonId).Nodes.Any(node => node.Type == DungeonNodeType.Rescue), Is.False);
        }

        [Test]
        public void CompanionKillFactsCaptureTheOriginalAcceptanceAndDoNotAdvanceALaterQuest()
        {
            Assert.That(_store.TryTalk("mira", "talk_mira", _store.Progress.Revision), Is.EqualTo(CharacterActionResult.Saved));
            Assert.That(_store.TryRecruit("mira", "join_mira", _store.Progress.Revision, 0), Is.EqualTo(CharacterActionResult.Saved));
            CreateRuntime();
            Move("guard");
            _tick.Update(1);
            Assert.That(_data.Enemies.Count(unit => !unit.IsAlive), Is.EqualTo(1), "첫 자동 처치 뒤 후속 사실 저장을 위해 반환합니다.");
            var fact = _data.PendingQuestFact;
            Assert.That(fact, Is.Not.Null);
            Assert.That(fact.AcceptanceId, Is.Empty);
            Assert.That(_data.IsAwaitingCommand, Is.False);
            Accept(Quest(QuestKind.KillMonster));
            Assert.That(_store.TryApplyQuestFact(fact), Is.EqualTo(QuestActionResult.Ignored));
            Assert.That(_store.Progress.Quest.ProgressCount, Is.Zero);
            Acknowledge("another_fact");
            Assert.That(_data.PendingQuestFact, Is.SameAs(fact));
            Acknowledge(fact.Id);
            Assert.That(_data.PendingQuestFact, Is.Null);
            _tick.Update(1);
            var second = _data.PendingQuestFact;
            Assert.That(second, Is.Not.Null);
            Assert.That(second.AcceptanceId, Is.EqualTo(_store.Progress.Quest.AcceptanceId));
            Assert.That(second.Id, Is.Not.EqualTo(fact.Id));
            Assert.That(_store.TryApplyQuestFact(second), Is.EqualTo(QuestActionResult.Saved));
            Assert.That(_store.TryApplyQuestFact(second), Is.EqualTo(QuestActionResult.AlreadyApplied));
            Assert.That(_store.Progress.Quest.ProgressCount, Is.EqualTo(1));
        }

        [Test]
        public void OnlyFinalBossVictoryCreatesTheDungeonClearFact()
        {
            var definition = Quest(QuestKind.ClearDungeon);
            Accept(definition);
            CreateRuntime(definition.DungeonId);
            var facts = new List<QuestFact>();
            for (int floor = 1; floor <= _data.Dungeon.FloorCount; floor++)
            {
                Move("visitor");
                Move("cache");
                Request(UIInputType.DungeonCloseDetails);
                if (floor < _data.Dungeon.FloorCount)
                {
                    Move("passage");
                    FightWithoutCommitting();
                    facts.AddRange(DrainFacts());
                    Assert.That(facts.All(fact => fact.Type != QuestFactType.DungeonCleared), Is.True);
                    Request(UIInputType.DungeonCloseDetails);
                    Move("stairs");
                    Request(UIInputType.DungeonNextFloor, context: _data.Context);
                }
                else
                {
                    Move("boss");
                    FightWithoutCommitting();
                    facts.AddRange(DrainFacts());
                }
            }
            Assert.That(_data.IsCleared, Is.True);
            var clear = facts.Single(fact => fact.Type == QuestFactType.DungeonCleared);
            Assert.That(clear.DungeonId, Is.EqualTo(definition.DungeonId));
            Assert.That(clear.AcceptanceId, Is.EqualTo(_store.Progress.Quest.AcceptanceId));
            Assert.That(_store.Progress.Quest.ProgressCount, Is.EqualTo(definition.RequiredCount));
            Assert.That(facts.Last(), Is.SameAs(clear));
            Request(UIInputType.DungeonAttack, "rat_0", "hero");
            Assert.That(_data.PendingQuestFact, Is.Null);
        }

        [Test]
        public void FailedQuestFactSaveBlocksCombatAndRetainsTheSameFactUntilRetry()
        {
            Accept(Quest(QuestKind.KillMonster));
            CreateRuntime();
            Move("guard");
            AttackOne();
            Assert.That(_commitExperience(false), Is.True);
            var pending = _data.PendingQuestFact;
            string previousSave = File.ReadAllText(_path);
            Directory.CreateDirectory(_path + ".tmp");
            Assert.That(_commit(false), Is.False);
            float hp = _data.Enemies.Single(unit => unit.IsAlive).Hp;
            _tick.Update(100);
            Request(UIInputType.DungeonAttack, "rat_1", "hero");
            Request(UIInputType.DungeonCloseDetails);
            Request(UIInputType.DungeonReturnTown);
            Assert.That(_data.ProgressSaveBlocked, Is.True);
            Assert.That(_data.PendingQuestFact, Is.SameAs(pending));
            Assert.That(_data.Enemies.Single(unit => unit.IsAlive).Hp, Is.EqualTo(hp));
            Assert.That(_data.SettlementOpen, Is.False);
            Assert.That(File.ReadAllText(_path), Is.EqualTo(previousSave));
            Directory.Delete(_path + ".tmp");
            Request(UIInputType.DungeonCloseDetails);
            Assert.That(_data.ProgressSaveBlocked, Is.False);
            Assert.That(_data.DetailsOpen, Is.False);
            Assert.That(_data.PendingQuestFact, Is.Null);
            Assert.That(_store.Progress.Quest.ProgressCount, Is.EqualTo(1));
        }

        [Test]
        public void SameBatchKillRejectsCharacterPopupUntilBothProgressQueuesCommit()
        {
            CreateStore(fastHero: true);
            Assert.That(_store.TryTalk("sera", "talk_sera", _store.Progress.Revision), Is.EqualTo(CharacterActionResult.Saved));
            Assert.That(_store.TryRecruit("sera", "join_sera", _store.Progress.Revision, 0), Is.EqualTo(CharacterActionResult.Saved));
            Accept(Quest(QuestKind.KillMonster));
            CreateRuntime();
            Move("guard");
            _tick.Update(2);
            Assert.That(_data.IsAwaitingCommand, Is.True);
            _input.AddInput(UIInputType.DungeonAttack, "rat_0", _data.ReadyAllyId);
            _input.AddInput(UIInputType.PartyCharacterOpen, "sera", "party");
            DispatchInputs();
            Assert.That(_data.InCombat, Is.True);
            Assert.That(_data.Enemies.Count(unit => !unit.IsAlive), Is.EqualTo(1));
            Assert.That(_data.PendingExperience, Is.Not.Null);
            Assert.That(_data.PendingQuestFact, Is.Not.Null);
            Assert.That(_flow.CharacterInteraction.Open, Is.False, "저장 전 Revision으로 인물 상세를 열지 않습니다.");
            Assert.That(_commit(false), Is.True);
            Assert.That(_data.PendingExperience, Is.Null);
            Assert.That(_data.PendingQuestFact, Is.Null);
            Request(UIInputType.PartyCharacterOpen, "sera", "party");
            Assert.That(_flow.CharacterInteraction.Open, Is.True);
            int previousAffinity = _store.Progress.GetCharacter("sera").Affinity;
            Request(UIInputType.CharacterTalk, "sera", _flow.CharacterInteraction.Context);
            Assert.That(_flow.CharacterInteraction.DialogueOpen, Is.True);
            Assert.That(_store.Progress.GetCharacter("sera").Affinity, Is.EqualTo(previousAffinity + _characters.TalkAffinityGain));
        }

        [Test]
        public void RescueSaveRetryRestoresTheVictoryMessage()
        {
            var definition = Quest(QuestKind.RescueAdventurers);
            Accept(definition);
            CreateRuntime();
            EnterRescue(definition);
            FightWithoutCommitting();
            Assert.That(_commitExperience(false), Is.True);
            Directory.CreateDirectory(_path + ".tmp");
            Assert.That(_commit(false), Is.False);
            Assert.That(_data.PendingQuestFact.Type, Is.EqualTo(QuestFactType.AdventurersRescued));
            Assert.That(_data.ProgressSaveBlocked, Is.True);
            Directory.Delete(_path + ".tmp");
            Assert.That(_commit(true), Is.True);
            Assert.That(_data.DetailsOpen, Is.True);
            Assert.That(_data.DetailsTitle, Is.EqualTo(_quests.Text("rescue.success_title")));
            Assert.That(_store.Progress.Quest.ProgressCount, Is.EqualTo(definition.RequiredCount));
        }

        [Test]
        public void DefeatAtRescueNodeDoesNotProduceARescueOrClearFact()
        {
            CreateStore(dangerous: true);
            var definition = Quest(QuestKind.RescueAdventurers);
            Accept(definition);
            CreateRuntime();
            EnterRescue(definition);
            _tick.Update(1);
            Assert.That(_data.IsDefeated, Is.True);
            Assert.That(_data.PendingQuestFact, Is.Null);
            Assert.That(_store.Progress.Quest.ProgressCount, Is.Zero);
            Assert.That(_data.IsNodeResolved(definition.TargetId), Is.False);
        }

        private void CreateStore(bool dangerous = false, bool fastHero = false)
        {
            string definitions = Read("CharacterDefinitionTable.csv");
            foreach (string id in new[] { "hero", "mira" })
            {
                definitions = ReplaceCell(definitions, id, "Strength", "1000");
                definitions = ReplaceCell(definitions, id, "Constitution", "1000");
            }
            definitions = ReplaceCell(definitions, "mira", "Agility", "1000");
            if (fastHero)
            {
                definitions = ReplaceCell(definitions, "hero", "Agility", "100");
            }
            string enemies = ReplaceCell(Read("CombatEnemyTable.csv"), "grey_rat", "MaxHealth", dangerous ? "500" : "1");
            enemies = ReplaceCell(enemies, "grey_rat", "PhysicalAttackPower", dangerous ? "100000" : "1");
            enemies = ReplaceCell(enemies, "grey_rat", "AttackSpeed", dangerous ? "10" : "0.1");
            var growth = new CharacterGrowthSheet(Read("CharacterGrowthTable.csv"), Read("CharacterLevelTable.csv"), Read("CharacterGrowthSettingTable.csv"));
            var combat = new CombatStatSheet(Read("CombatStatTable.csv"), enemies, Read("CombatAttackTable.csv"));
            _characters = new CharacterSheet(definitions, Read("CharacterSettingTable.csv"), Read("CharacterTextTable.csv"), _town, growth, combat);
            _store = new AdventureProgressStore(_loot, _path, _shops, _characters, _quests);
            Assert.That(_store.Load(), Is.EqualTo(AdventureLoadResult.Missing));
        }

        private QuestDefinition Quest(QuestKind kind) => _quests.Quests.Single(quest => quest.Kind == kind);
        private void Accept(QuestDefinition definition) => Assert.That(_store.TryAcceptQuest(definition.Id, "accept_" + definition.Id,
            _store.Progress.Revision), Is.EqualTo(QuestActionResult.Saved));
        private DungeonDataContainer NewDungeon(int id) => new(_sheet, id, _loot, _characters, _store.Progress, _random, _quests);

        private void CreateRuntime(int dungeonId = 1)
        {
            _data = NewDungeon(dungeonId);
            _tick = new CombatTickFlow(_data);
            DI.PubSub(_data);
            _host = new GameObject("QuestDungeonTests isolated composition");
            _host.SetActive(false);
            _flow = _host.AddComponent<MainFlow>();
            Field("_sceneType", SceneType.Dungeon);
            Field("_characterSheet", _characters);
            Field("_questSheet", _quests);
            Field("_characterRandom", _random);
            Field("_adventureStore", _store);
            Field("_dungeonData", _data);
            _commit = Bind<Func<bool, bool>>("CommitDungeonProgress");
            _commitExperience = Bind<Func<bool, bool>>("CommitDungeonExperience");
            _stream = new InputStream();
            _input = new UIInputStream(_stream);
            var filter = new InputFilter(() => false);
            _queue = new EventQueue(new EventHandler(new DungeonEventHandler(filter, () => { },
                isCharacterOpen: () => _flow.CharacterInteraction.Open, closeCharacter: Bind<Action>("CloseCharacter"),
                beforeCloseDetails: () => _commit(true)),
                new CharacterEventHandler(filter, Bind<Action<CharacterActionRequested>>("HandleCharacterAction"))));
        }

        private void EnterRescue(QuestDefinition definition)
        {
            Move("visitor");
            Move("cache");
            Request(UIInputType.DungeonCloseDetails);
            Move(definition.TargetId);
            Assert.That(_data.InCombat, Is.True);
        }

        private void AttackOne()
        {
            _tick.Update(2);
            Assert.That(_data.IsAwaitingCommand, Is.True);
            Request(UIInputType.DungeonAttack, _data.Enemies.First(unit => unit.IsAlive).Id, _data.ReadyAllyId);
        }

        private void FightWithoutCommitting()
        {
            for (int step = 0; step < 20 && _data.InCombat; step++)
            {
                AttackOne();
            }
            Assert.That(_data.InCombat, Is.False);
            Assert.That(_data.IsDefeated, Is.False);
        }

        private List<QuestFact> DrainFacts()
        {
            var facts = new List<QuestFact>();
            while (_data.PendingQuestFact != null)
            {
                var fact = _data.PendingQuestFact;
                facts.Add(fact);
                var result = _store.TryApplyQuestFact(fact);
                Assert.That(result, Is.EqualTo(QuestActionResult.Saved).Or.EqualTo(QuestActionResult.Ignored));
                Acknowledge(fact.Id);
            }
            return facts;
        }

        private void Move(string id) => Request(UIInputType.DungeonMove, id, _data.Context);
        private void Request(UIInputType type, string target = null, string context = null)
        {
            _input.AddInput(type, target, context);
            DispatchInputs();
        }

        private void DispatchInputs()
        {
            foreach (var request in new InputBinding(_stream).ConvertInputStreamToEventData())
            {
                _queue.AddEvent(request);
            }
            _stream.ClearInputs();
            _queue.Update();
        }

        // 비공개 접근은 MainFlow 조립/저장 후속 단계와 저장 확인 응답에 한정한다.
        private void Field(string name, object value) => typeof(MainFlow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_flow, value);
        private T Bind<T>(string name) where T : Delegate => (T)Delegate.CreateDelegate(typeof(T), _flow,
            typeof(MainFlow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic));
        private void Acknowledge(string id) => typeof(DungeonDataContainer).GetMethod("CompleteQuestFact", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(_data, new object[] { id });
        private static void AssertConnected(IReadOnlyList<DungeonNodeDefinition> nodes)
        {
            var visited = new HashSet<string>();
            var pending = new Queue<string>();
            pending.Enqueue(nodes[0].Id);
            while (pending.Count != 0)
            {
                string id = pending.Dequeue();
                if (!visited.Add(id))
                {
                    continue;
                }
                foreach (string neighbor in nodes.Single(node => node.Id == id).NeighborIds)
                {
                    CollectionAssert.Contains(nodes.Single(node => node.Id == neighbor).NeighborIds, id);
                    pending.Enqueue(neighbor);
                }
            }
            CollectionAssert.AreEquivalent(nodes.Select(node => node.Id), visited);
        }

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
            public float NextFloat(float minimum, float maximum) => _draw++ % 3 == 0 ? 0f : 99f;
        }
    }
}
