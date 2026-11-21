using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Refactor.Main;
using Refactor.Main.Event;
using Refactor.Main.InputModule;
using Refactor.Util;
using UnityEngine;
using EventHandler = Refactor.Main.Event.EventHandler;

namespace P2.Tests
{
    // 실제 기획 CSV와 공개 UI 요청 경로를 사용한다. 전투 시간만 명시적으로 진행한다.
    public sealed class DungeonFlowTests
    {
        private TownSheet _town;
        private DungeonSheet _sheet;
        private DungeonDataContainer _data;
        private CombatTickFlow _tick;
        private InputStream _stream;
        private UIInputStream _ui;
        private InputFilter _filter;
        private EventQueue _queue;
        private int _settlementRequests;
        private int _confirms;
        private int _cancels;
        private readonly List<KeyValuePair<string, int>> _purchaseRequests = new();
        private string _definitions;
        private string _nodes;
        private string _party;
        private string _settings;
        private string _texts;

        [SetUp]
        public void SetUp()
        {
            DI.Init();
            _town = new TownSheet(Read("TownLocationTable.csv"), Read("TownCommandTable.csv"),
                Read("TownCharacterTable.csv"), Read("TownTextTable.csv"));
            _definitions = Read("DungeonDefinitionTable.csv");
            _nodes = Read("DungeonNodeTable.csv");
            _party = Read("DungeonPartyTable.csv");
            _settings = Read("DungeonSettingTable.csv");
            _texts = Read("DungeonTextTable.csv");
            CreateRuntime(CreateSheet());
        }

        [TearDown]
        public void TearDown()
        {
            DI.Remove<DungeonEventHandler>();
            DI.Remove<DungeonDataContainer>();
            DI.Init();
        }

        [Test]
        public void SixDesignerDungeonsIncreaseAllEnemyStatsAndUseAtMostThreeFloors()
        {
            CollectionAssert.AreEqual(Enumerable.Range(1, 6), _sheet.Dungeons.Select(item => item.Id));
            for (int i = 0; i < _sheet.Dungeons.Count; i++)
            {
                var dungeon = _sheet.Dungeons[i];
                Assert.That(dungeon.Name, Is.Not.Empty);
                Assert.That(dungeon.FloorCount, Is.EqualTo(3));
                if (i == 0)
                {
                    continue;
                }
                var previous = _sheet.Dungeons[i - 1];
                Assert.That(dungeon.RatHp, Is.GreaterThan(previous.RatHp));
                Assert.That(dungeon.RatAttack, Is.GreaterThan(previous.RatAttack));
                Assert.That(dungeon.RatSpeed, Is.GreaterThan(previous.RatSpeed));
            }
            Assert.That(_sheet.Settings.WaitOnReady, Is.True, "기본 전투는 명령을 기다립니다.");
            Assert.That(_sheet.Party.Count(member => member.IsPlayerControlled), Is.EqualTo(1));
            Assert.That(_data.Allies.Single(member => member.IsPlayerControlled).Id,
                Is.EqualTo(_sheet.Party.Single(member => member.IsPlayerControlled).Id));
        }

        [Test]
        public void EveryFloorIsConnectedWithSymmetricEdgesAndOnlyTheFinalFloorHasABoss()
        {
            var observedTypes = new HashSet<DungeonNodeType>();
            for (int floor = 1; floor <= 3; floor++)
            {
                var nodes = _sheet.GetNodes(floor);
                Assert.That(nodes[0].Type, Is.EqualTo(DungeonNodeType.Rest));
                var visited = new HashSet<string>();
                var pending = new Queue<string>();
                pending.Enqueue(nodes[0].Id);
                while (pending.Count > 0)
                {
                    string id = pending.Dequeue();
                    if (!visited.Add(id))
                    {
                        continue;
                    }
                    var node = _sheet.GetNode(floor, id);
                    observedTypes.Add(node.Type);
                    foreach (string neighbor in node.NeighborIds)
                    {
                        CollectionAssert.Contains(_sheet.GetNode(floor, neighbor).NeighborIds, id);
                        pending.Enqueue(neighbor);
                    }
                }
                CollectionAssert.AreEquivalent(nodes.Select(node => node.Id), visited);
                Assert.That(nodes.Count(node => node.Type == DungeonNodeType.Boss), Is.EqualTo(floor == 3 ? 1 : 0));
                Assert.That(nodes.Any(node => node.Type == DungeonNodeType.Stairs), Is.EqualTo(floor < 3));
            }
            CollectionAssert.AreEquivalent(Enum.GetValues(typeof(DungeonNodeType)).Cast<DungeonNodeType>()
                .Where(type => type != DungeonNodeType.Rescue), observedTypes);
            Assert.Throws<FormatException>(() => _sheet.GetNodes(4));
        }

        [Test]
        public void QueuedMovesAreDeferredAndRejectNonAdjacentCurrentUnknownAndStaleTargets()
        {
            string context = _data.Context;
            foreach (string invalid in new[] { "camp", "stairs", "missing" })
            {
                Request(UIInputType.DungeonMove, invalid, context);
            }
            Assert.That(_data.Revision, Is.Zero);
            _ui.AddInput(UIInputType.DungeonMove, "visitor", context);
            _ui.AddInput(UIInputType.DungeonMove, "cache", context);
            TransferInputs();
            Assert.That(_data.NodeId, Is.EqualTo("camp"));
            _queue.Update();
            Assert.That(_data.NodeId, Is.EqualTo("visitor"), "이전 노드에서 생성된 후속 요청은 폐기합니다.");
            Assert.That(_data.Revision, Is.EqualTo(1));
            Move("camp");
            Assert.That(_data.IsNodeVisited("visitor"), Is.True);
            Assert.That(_data.IsNodeVisited("camp"), Is.True);
            Assert.That(_data.Revision, Is.EqualTo(2));
        }

        [Test]
        public void RestEventAndBattleResolveOnceEvenAfterDamageAndRevisiting()
        {
            CreateControlledRuntime(enemyHp: 10, enemyAttack: 40, allyHp: 500, allyAttack: 100);
            Move("guard");
            WinCombat();
            Assert.That(_data.Allies[0].Hp, Is.EqualTo(460));
            CloseDetails();
            Move("camp");
            Request(UIInputType.DungeonRest, context: _data.Context);
            Assert.That(_data.Allies[0].Hp, Is.EqualTo(488));
            Assert.That(_data.DetailsOpen, Is.True);
            Assert.That(_data.CanRest, Is.False);
            CloseDetails();
            Move("visitor");
            Move("cache");
            Assert.That(_data.Allies[0].Hp, Is.EqualTo(500));
            Assert.That(_data.IsNodeResolved("cache"), Is.True);
            CloseDetails();
            Move("passage");
            WinCombat();
            CloseDetails();
            float woundedHp = _data.Allies[0].Hp;
            Move("cache");
            Assert.That(_data.DetailsOpen, Is.False);
            Assert.That(_data.Allies[0].Hp, Is.EqualTo(woundedHp), "소모한 이벤트는 다시 회복시키지 않습니다.");
            Move("guard");
            Assert.That(_data.InCombat, Is.False);
            Assert.That(_data.IsNodeResolved("guard"), Is.True);
            Move("camp");
            int revision = _data.Revision;
            Request(UIInputType.DungeonRest, context: _data.Context);
            Assert.That(_data.Revision, Is.EqualTo(revision));
            Assert.That(_data.Allies[0].Hp, Is.EqualTo(woundedHp));
        }

        [Test]
        public void DialogueUsesTheNodeVisitorAndModalBlocksTravelUntilCloseInFifoOrder()
        {
            Request(UIInputType.DungeonTalk, context: _data.Context);
            Assert.That(_data.DetailsOpen, Is.False);
            Move("visitor");
            Request(UIInputType.DungeonTalk, context: _data.Context);
            var visitor = _sheet.GetCharacter(_sheet.GetNode(1, "visitor").CharacterId);
            Assert.That(_data.DetailsBody, Does.Contain(visitor.Dialogue));
            Move("camp");
            Assert.That(_data.NodeId, Is.EqualTo("visitor"));
            _ui.AddInput(UIInputType.DungeonCloseDetails);
            _ui.AddInput(UIInputType.DungeonMove, "camp", _data.Context);
            ExecuteInputs();
            Assert.That(_data.NodeId, Is.EqualTo("camp"));
            Assert.That(_data.DetailsOpen, Is.False);
        }

        [Test]
        public void AtbRequiresReadinessAndOneAttackConsumesExactlyOneGauge()
        {
            CreateControlledRuntime();
            Move("guard");
            Request(UIInputType.DungeonAttack, "rat_0", "hero");
            Assert.That(_data.Enemies[0].Hp, Is.EqualTo(100));
            _tick.Update(0.25f);
            Assert.That(_data.Allies[0].Gauge, Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(_data.Allies[1].Gauge, Is.EqualTo(0.125f).Within(0.0001f));
            Assert.That(_data.Enemies[0].Gauge, Is.EqualTo(0.5f).Within(0.0001f));
            _tick.Update(0.75f);
            Assert.That(_data.ReadyAllyId, Is.EqualTo("hero"));
            Request(UIInputType.DungeonAttack, "rat_0", "support");
            Request(UIInputType.DungeonAttack, "missing", "hero");
            Assert.That(_data.Enemies[0].Hp, Is.EqualTo(100));
            _ui.AddInput(UIInputType.DungeonAttack, "rat_0", "hero");
            _ui.AddInput(UIInputType.DungeonAttack, "rat_0", "hero");
            ExecuteInputs();
            Assert.That(_data.Enemies[0].Hp, Is.EqualTo(80));
            Assert.That(_data.Allies[0].Gauge, Is.Zero);
            Assert.That(_data.IsAwaitingCommand, Is.False);
        }

        [Test]
        public void WaitingFreezesEveryGaugeAndEnemyAttackWithoutReplayingDiscardedTime()
        {
            CreateControlledRuntime();
            Move("guard");
            _tick.Update(100f);
            Assert.That(_data.IsAwaitingCommand, Is.True);
            var gauges = _data.Allies.Concat(_data.Enemies).Select(unit => unit.Gauge).ToArray();
            var hp = _data.Allies.Select(unit => unit.Hp).ToArray();
            _tick.Update(1000f);
            CollectionAssert.AreEqual(gauges, _data.Allies.Concat(_data.Enemies).Select(unit => unit.Gauge));
            CollectionAssert.AreEqual(hp, _data.Allies.Select(unit => unit.Hp));
            Request(UIInputType.DungeonAttack, "rat_0", "hero");
            _tick.Update(0.125f);
            Assert.That(_data.Allies[0].Gauge, Is.EqualTo(0.125f).Within(0.0001f));
            Assert.That(_data.Enemies[0].Gauge, Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(_data.Allies[1].Hp, Is.EqualTo(hp[1] - 3), "경계에 도달한 적의 공격 하나만 처리합니다.");
        }

        [Test]
        public void RealtimeSettingLetsCompanionAndEnemiesActWhilePlayerIsIdle()
        {
            CreateControlledRuntime(enemyAttack: 5, allyHp: 10, wait: false);
            Move("guard");
            _tick.Update(2f);
            Assert.That(_data.Enemies[0].Hp, Is.EqualTo(90), "동료는 플레이어 입력 없이도 준비되면 공격합니다.");
            Assert.That(_data.IsDefeated, Is.True);
            Assert.That(_data.InCombat, Is.False);
            Assert.That(_data.Allies.All(unit => unit.Hp == 0 && unit.Gauge == 0), Is.True);
            Assert.That(_data.DetailsOpen, Is.True);
            Assert.That(_data.CanReturnTown, Is.True);
            Request(UIInputType.DungeonReturnTown);
            Assert.That(_settlementRequests, Is.EqualTo(1));
            Assert.That(_data.SettlementOpen, Is.True);
            Request(UIInputType.DungeonCancelSettlement, context: _data.SettlementContext);
            Assert.That(_data.SettlementOpen, Is.False);
            Assert.That(_data.IsDefeated, Is.True);
            Assert.That(_data.DetailsOpen, Is.True);
            Assert.That(_data.CanMoveTo("camp"), Is.False);
        }

        [Test]
        public void FallenPlayerDoesNotStopAutomaticCompanionAndCannotBeResurrectedByRestOrEvents()
        {
            string party = "Id,Name,MaxHp,Attack,Speed,PortraitIndex,IsPlayer\nhero,Hero,3,100,0.5,2,1\nsupport,Support,500,100,1,0,0\n";
            CreateControlledRuntime(enemyHp: 10, enemyAttack: 3, partyOverride: party);
            Move("guard");
            _tick.Update(0.5f);
            Assert.That(_data.Allies[0].Hp, Is.Zero);
            Assert.That(_data.Allies[0].IsPlayerControlled, Is.True);
            Assert.That(_data.InCombat, Is.True);
            Assert.That(_data.IsAwaitingCommand, Is.False);
            _tick.Update(0.5f);
            Assert.That(_data.InCombat, Is.False, "플레이어가 쓰러져도 살아 있는 동료가 자동으로 전투를 마칩니다.");
            Assert.That(_data.IsDefeated, Is.False);
            Assert.That(_data.IsNodeResolved("guard"), Is.True);
            CloseDetails();
            Move("camp");
            Request(UIInputType.DungeonRest, context: _data.Context);
            CloseDetails();
            Move("visitor");
            Move("cache");
            Assert.That(_data.Allies[0].Hp, Is.Zero);
            Assert.That(_data.Allies[0].IsAlive, Is.False);
        }

        [Test]
        public void FasterCompanionAttacksRepeatedlyWithoutInputAndOnlyPlayerCanBeCommanded()
        {
            string party = "Id,Name,MaxHp,Attack,Speed,PortraitIndex,IsPlayer\nhero,Hero,1000,20,0.5,2,1\nsupport,Support,1000,10,2,0,0\n";
            CreateControlledRuntime(partyOverride: party);
            Move("guard");
            _tick.Update(0.5f);
            Assert.That(_data.Enemies[0].Hp, Is.EqualTo(90));
            Assert.That(_data.Allies[1].Gauge, Is.Zero);
            Assert.That(_data.Allies[0].Gauge, Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(_data.ReadyAllyId, Is.Empty);
            Request(UIInputType.DungeonAttack, "rat_0", "support");
            Assert.That(_data.Enemies[0].Hp, Is.EqualTo(90));
            _tick.Update(0.5f);
            Assert.That(_data.Enemies[0].Hp, Is.EqualTo(80));
            Assert.That(_data.IsAwaitingCommand, Is.False);
            _tick.Update(1f);
            Assert.That(_data.ReadyAllyId, Is.EqualTo("hero"));
            Assert.That(_data.Enemies[0].Hp, Is.EqualTo(60));
            Request(UIInputType.DungeonAttack, "rat_0", "support");
            Assert.That(_data.Enemies[0].Hp, Is.EqualTo(60), "플레이어 차례도 동료 ID로 대신 실행할 수 없습니다.");
            Request(UIInputType.DungeonAttack, "rat_0", "hero");
            Assert.That(_data.Enemies[0].Hp, Is.EqualTo(40));
        }

        [TestCase(100, false)]
        [TestCase(10, true)]
        public void SimultaneousReadinessResolvesCompanionBeforePlayerPauseOrVictory(int enemyHp, bool victory)
        {
            string party = "Id,Name,MaxHp,Attack,Speed,PortraitIndex,IsPlayer\nhero,Hero,1000,20,1,2,1\nsupport,Support,1000,10,1,0,0\n";
            CreateControlledRuntime(enemyHp: enemyHp, partyOverride: party);
            Move("guard");
            _tick.Update(100f);
            Assert.That(_data.Enemies[0].Hp, Is.EqualTo(enemyHp - 10));
            Assert.That(_data.Allies[1].Gauge, Is.Zero);
            Assert.That(_data.InCombat, Is.EqualTo(!victory));
            Assert.That(_data.IsAwaitingCommand, Is.EqualTo(!victory));
            Assert.That(_data.Allies[0].Hp, Is.EqualTo(997));
            Assert.That(_data.Allies[1].Hp, Is.EqualTo(1000), "동시 준비된 적은 플레이어 선택보다 먼저 공격하지 않습니다.");
            if (victory)
            {
                Assert.That(_data.IsNodeResolved("guard"), Is.True);
                Assert.That(_data.DetailsOpen, Is.True);
                return;
            }
            Assert.That(_data.ReadyAllyId, Is.EqualTo("hero"));
            Assert.That(_data.Enemies[0].Gauge, Is.EqualTo(1f));
            var gauges = _data.Allies.Concat(_data.Enemies).Select(unit => unit.Gauge).ToArray();
            _tick.Update(100f);
            CollectionAssert.AreEqual(gauges, _data.Allies.Concat(_data.Enemies).Select(unit => unit.Gauge));
            Assert.That(_data.Enemies[0].Hp, Is.EqualTo(90));
            Request(UIInputType.DungeonAttack, "rat_0", "hero");
            _tick.Update(0.1f);
            Assert.That(_data.Allies[1].Hp, Is.EqualTo(997));
            Assert.That(_data.Allies[1].Gauge, Is.EqualTo(0.1f).Within(0.0001f));
        }

        [Test]
        public void PlayerRoleComesFromTheSheetRatherThanPartyOrderOrHeroId()
        {
            string party = "Id,Name,MaxHp,Attack,Speed,PortraitIndex,IsPlayer\nhero,Hero,1000,20,2,2,0\nsupport,Support,1000,10,1,0,1\n";
            CreateControlledRuntime(partyOverride: party);
            Move("guard");
            _tick.Update(0.5f);
            Assert.That(_data.Enemies[0].Hp, Is.EqualTo(80));
            Assert.That(_data.IsAwaitingCommand, Is.False);
            _tick.Update(0.5f);
            Assert.That(_data.ReadyAllyId, Is.EqualTo("support"));
            Assert.That(_data.Enemies[0].Hp, Is.EqualTo(60));
            Request(UIInputType.DungeonAttack, "rat_0", "hero");
            Assert.That(_data.Enemies[0].Hp, Is.EqualTo(60));
            Request(UIInputType.DungeonAttack, "rat_0", "support");
            Assert.That(_data.Enemies[0].Hp, Is.EqualTo(50));
        }

        [TestCase(0, 0)]
        [TestCase(1, 1)]
        [TestCase(2, 0)]
        public void PartySheetRejectsMissingDuplicateOrInvalidPlayerRoles(int firstRole, int secondRole)
        {
            string party = "Id,Name,MaxHp,Attack,Speed,PortraitIndex,IsPlayer\nhero,Hero,100,20,1,2," + firstRole
                + "\nsupport,Support,100,10,1,0," + secondRole + "\n";
            Assert.Throws<FormatException>(() => CreateSheet(party: party));
        }

        [Test]
        public void StairCommandsPreserveHpAndOnlyTheThirdFloorBossCompletesTheRun()
        {
            CreateControlledRuntime(enemyHp: 10, enemyAttack: 7, allyHp: 500, allyAttack: 100);
            Request(UIInputType.DungeonNextFloor, context: _data.Context);
            Assert.That(_data.Floor, Is.EqualTo(1));
            for (int floor = 1; floor <= 3; floor++)
            {
                Move("visitor");
                Move("cache");
                CloseDetails();
                if (floor == 3)
                {
                    int lootBeforeBoss = LootCount();
                    Move("boss");
                    Assert.That(_data.Enemies.All(unit => unit.IsBoss), Is.True);
                    WinCombat();
                    Assert.That(LootCount() - lootBeforeBoss, Is.EqualTo(_sheet.Settings.BossEnemyCount),
                        "강화된 회색쥐 보스도 처치 한 마리마다 꼬리 하나를 남깁니다.");
                    break;
                }
                Move("passage");
                Request(UIInputType.DungeonReturnTown);
                Assert.That(_settlementRequests, Is.Zero, "전투 도중 귀환은 차단합니다.");
                WinCombat();
                CloseDetails();
                Move("stairs");
                Assert.That(_data.Floor, Is.EqualTo(floor), "계단에 닿는 것만으로 층을 변경하지 않습니다.");
                var hp = _data.Allies.Select(unit => unit.Hp).ToArray();
                string oldContext = _data.Context;
                Request(UIInputType.DungeonNextFloor, context: oldContext);
                CollectionAssert.AreEqual(hp, _data.Allies.Select(unit => unit.Hp));
                Assert.That(_data.Floor, Is.EqualTo(floor + 1));
                Assert.That(_data.NodeId, Is.EqualTo("camp"));
                Assert.That(_data.IsNodeResolved("cache"), Is.False, "같은 ID라도 다른 층의 이벤트는 새로 처리합니다.");
                Request(UIInputType.DungeonNextFloor, context: oldContext);
                Assert.That(_data.Floor, Is.EqualTo(floor + 1));
            }
            Assert.That(_data.IsCleared, Is.True);
            Assert.That(_data.Floor, Is.EqualTo(3));
            Assert.That(_data.CanNextFloor, Is.False);
            Assert.That(_data.DetailsOpen, Is.True);
            Request(UIInputType.DungeonReturnTown);
            Assert.That(_settlementRequests, Is.EqualTo(1));
            Assert.That(_data.SettlementOpen, Is.True);
            Request(UIInputType.DungeonCancelSettlement, context: _data.SettlementContext);
            Assert.That(_data.IsCleared, Is.True);
            Assert.That(_data.DetailsOpen, Is.True);
            Assert.That(_data.CanReturnTown, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PlayerAndCompanionKillsAwardOneTailWithoutDuplicateOrRevisitRewards(bool companionKill)
        {
            string party = companionKill
                ? "Id,Name,MaxHp,Attack,Speed,PortraitIndex,IsPlayer\nhero,Hero,1000,100,0.5,2,1\nsupport,Support,1000,100,2,0,0\n"
                : null;
            CreateControlledRuntime(enemyHp: 10, allyAttack: 100, partyOverride: party);
            Assert.That(LootCount(), Is.Zero);
            Move("guard");
            if (companionKill)
            {
                _tick.Update(0.5f);
                Assert.That(_data.InCombat, Is.False, "동료가 자동으로 마무리하며 수동 공격은 보내지 않습니다.");
            }
            else
            {
                WinCombat();
            }
            Assert.That(LootCount(), Is.EqualTo(1));
            Request(UIInputType.DungeonAttack, "rat_0", "hero");
            _tick.Update(100f);
            Assert.That(LootCount(), Is.EqualTo(1));
            CloseDetails();
            Move("camp");
            Move("guard");
            Assert.That(_data.InCombat, Is.False);
            Assert.That(LootCount(), Is.EqualTo(1));
        }

        [Test]
        public void DefeatKeepsLootFromEnemiesKilledBeforeThePartyFalls()
        {
            string party = "Id,Name,MaxHp,Attack,Speed,PortraitIndex,IsPlayer\nhero,Hero,5,100,2,2,1\nsupport,Support,5,10,0.5,0,0\n";
            CreateControlledRuntime(enemyHp: 10, enemyAttack: 5, partyOverride: party, normalEnemies: 2);
            Move("guard");
            _tick.Update(0.5f);
            Assert.That(_data.ReadyAllyId, Is.EqualTo("hero"));
            Request(UIInputType.DungeonAttack, "rat_0", "hero");
            Assert.That(LootCount(), Is.EqualTo(1));
            _tick.Update(1f);
            Assert.That(_data.IsDefeated, Is.True);
            Assert.That(LootCount(), Is.EqualTo(1));
            Request(UIInputType.DungeonReturnTown);
            Assert.That(_data.SettlementOpen, Is.True);
            Assert.That(LootCount(), Is.EqualTo(1), "패배해도 이미 획득한 전리품은 정산 대상에 남습니다.");
            Request(UIInputType.DungeonCancelSettlement, context: _data.SettlementContext);
            Assert.That(_data.IsDefeated, Is.True);
            Assert.That(_data.DetailsOpen, Is.True);
            Assert.That(LootCount(), Is.EqualTo(1));
        }

        [Test]
        public void SettlementCancelPreservesExplorationAndStaleOrHiddenRequestsAreRejected()
        {
            CreateControlledRuntime(enemyHp: 10, allyAttack: 100);
            Move("guard");
            Request(UIInputType.DungeonReturnTown);
            Assert.That(_data.SettlementOpen, Is.False);
            Assert.That(_settlementRequests, Is.Zero);
            WinCombat();
            CloseDetails();
            string node = _data.NodeId;
            var hp = _data.Allies.Select(unit => unit.Hp).ToArray();
            Request(UIInputType.DungeonReturnTown);
            string firstContext = _data.SettlementContext;
            Assert.That(firstContext, Is.Not.Empty);
            Assert.That(_data.SettlementOpen, Is.True);
            Assert.That(_settlementRequests, Is.EqualTo(1));
            Request(UIInputType.DungeonReturnTown);
            Move("camp");
            Request(UIInputType.DungeonRest, context: _data.Context);
            Request(UIInputType.DungeonLootIncrease, "rat_tail", "stale");
            Request(UIInputType.DungeonConfirmSettlement, context: "stale");
            Request(UIInputType.DungeonCancelSettlement, context: "stale");
            Assert.That(_settlementRequests, Is.EqualTo(1));
            Assert.That(_data.NodeId, Is.EqualTo(node));
            Assert.That(_purchaseRequests, Is.Empty);
            Assert.That(_confirms, Is.Zero);
            Assert.That(_cancels, Is.Zero);
            Request(UIInputType.DungeonLootIncrease, "rat_tail", firstContext);
            Assert.That(_purchaseRequests.Count, Is.EqualTo(1));
            Assert.That(_purchaseRequests[0], Is.EqualTo(new KeyValuePair<string, int>("rat_tail", 1)));
            Request(UIInputType.DungeonCancelSettlement, context: firstContext);
            Assert.That(_data.SettlementOpen, Is.False);
            Assert.That(_data.SettlementContext, Is.Empty);
            Assert.That(_data.NodeId, Is.EqualTo(node));
            CollectionAssert.AreEqual(hp, _data.Allies.Select(unit => unit.Hp));
            Assert.That(_data.IsNodeResolved("guard"), Is.True);
            Assert.That(LootCount(), Is.EqualTo(1));
            Request(UIInputType.DungeonReturnTown);
            Assert.That(_data.SettlementContext, Is.Not.EqualTo(firstContext));
            Request(UIInputType.DungeonConfirmSettlement, context: firstContext);
            Request(UIInputType.DungeonCancelSettlement, context: firstContext);
            Assert.That(_confirms, Is.Zero);
            Assert.That(_cancels, Is.EqualTo(1));
            Assert.That(_data.SettlementOpen, Is.True);
            Request(UIInputType.DungeonBack);
            Assert.That(_data.SettlementOpen, Is.False);
            Assert.That(_cancels, Is.EqualTo(2));
        }

        [Test]
        public void InputFocusAndTransitionLocksRejectMutationsAndReturnRequests()
        {
            _filter.Focused = false;
            Move("visitor");
            Request(UIInputType.DungeonReturnTown);
            _filter.Focused = true;
            _filter.IsLocked = true;
            Request(UIInputType.DungeonRest, context: _data.Context);
            Request(UIInputType.DungeonReturnTown);
            Assert.That(_data.Revision, Is.Zero);
            Assert.That(_settlementRequests, Is.Zero);
            _filter.IsLocked = false;
            Move("visitor");
            Assert.That(_data.NodeId, Is.EqualTo("visitor"));
        }

        [Test]
        public void InvalidTimeDoesNotChargeOrDamageCombatants()
        {
            Move("guard");
            int revision = _data.Revision;
            foreach (float delta in new[] { 0f, -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                _tick.Update(delta);
            }
            Assert.That(_data.Revision, Is.EqualTo(revision));
            Assert.That(_data.Allies.Concat(_data.Enemies).All(unit => unit.Gauge == 0 && unit.Hp == unit.MaxHp), Is.True);
        }

        [Test]
        public void SheetValidationRejectsBrokenEdgesEarlyBossAndOutOfRangeFloorCounts()
        {
            string broken = _nodes.Replace("camp;cache", "camp;missing");
            Assert.Throws<FormatException>(() => CreateSheet(nodes: broken));
            string earlyBoss = _nodes.Replace("1,guard,Battle", "1,guard,Boss");
            Assert.Throws<FormatException>(() => CreateSheet(nodes: earlyBoss));
            string tooDeep = "Id,Name,Description,FloorCount,RatHp,RatAttack,RatSpeed\n1,Test,Test,4,10,1,1\n";
            Assert.Throws<FormatException>(() => CreateSheet(definitions: tooDeep));
            Assert.Throws<FormatException>(() => CreateSheet(settings: Setting(_settings, "WaitOnReady", "2")));
        }

        private void CreateControlledRuntime(int enemyHp = 100, int enemyAttack = 3, int allyHp = 1000,
            int allyAttack = 20, bool wait = true, string partyOverride = null, int normalEnemies = 1)
        {
            string definitions = "Id,Name,Description,FloorCount,RatHp,RatAttack,RatSpeed\n1,Test,Test,3,"
                + enemyHp + "," + enemyAttack + ",2\n";
            string party = partyOverride ?? "Id,Name,MaxHp,Attack,Speed,PortraitIndex,IsPlayer\nhero,Hero,"
                + allyHp + "," + allyAttack + ",1,2,1\nsupport,Support," + allyHp + ",10,0.5,0,0\n";
            string settings = Setting(_settings, "NormalEnemyCount", normalEnemies.ToString());
            settings = Setting(settings, "WaitOnReady", wait ? "1" : "0");
            settings = Setting(settings, "FloorHpBonus", "0");
            settings = Setting(settings, "FloorAttackBonus", "0");
            settings = Setting(settings, "FloorSpeedBonus", "0");
            CreateRuntime(CreateSheet(definitions: definitions, party: party, settings: settings));
        }

        private DungeonSheet CreateSheet(string definitions = null, string nodes = null, string party = null, string settings = null)
        {
            return new DungeonSheet(definitions ?? _definitions, nodes ?? _nodes, party ?? _party,
                settings ?? _settings, _texts, _town);
        }

        private void CreateRuntime(DungeonSheet sheet)
        {
            DI.Init();
            _sheet = sheet;
            // 제어용 파티 ID에도 연결되는 분배 시트를 쓴다. 분배 계산 자체는 별도 정산 테스트가 담당한다.
            string shares = "MemberId,Percent\n" + string.Join("\n", sheet.Party.Select(member => member.Id + ",50")) + "\n";
            var lootSheet = new LootSheet(Read("LootItemTable.csv"), Read("LootDropTable.csv"), shares,
                Read("LootSettingTable.csv"), Read("LootTextTable.csv"), sheet);
            _data = new DungeonDataContainer(sheet, 1, lootSheet);
            DI.PubSub(_data);
            _stream = new InputStream();
            _ui = new UIInputStream(_stream);
            _filter = new InputFilter(() => false);
            _settlementRequests = 0;
            _confirms = 0;
            _cancels = 0;
            _purchaseRequests.Clear();
            _queue = new EventQueue(new EventHandler(new DungeonEventHandler(_filter, () => _settlementRequests++,
                (id, delta) => _purchaseRequests.Add(new KeyValuePair<string, int>(id, delta)),
                () => _confirms++, () => _cancels++)));
            _tick = new CombatTickFlow(_data);
        }

        private int LootCount() => _data.Loot.TryGetValue("rat_tail", out int count) ? count : 0;

        private void WinCombat()
        {
            Assert.That(_data.InCombat, Is.True);
            for (int step = 0; step < 200 && _data.InCombat; step++)
            {
                _tick.Update(0.25f);
                if (_data.IsAwaitingCommand)
                {
                    Request(UIInputType.DungeonAttack, _data.Enemies.First(unit => unit.IsAlive).Id, _data.ReadyAllyId);
                }
            }
            Assert.That(_data.InCombat, Is.False, "유한한 행동 수 안에 전투가 끝나야 합니다.");
            Assert.That(_data.IsDefeated, Is.False);
            Assert.That(_data.IsNodeResolved(_data.NodeId), Is.True);
        }

        private void Move(string id) => Request(UIInputType.DungeonMove, id, _data.Context);
        private void CloseDetails() => Request(UIInputType.DungeonCloseDetails);

        private void Request(UIInputType type, string target = null, string context = null)
        {
            _ui.AddInput(type, target, context);
            ExecuteInputs();
        }

        private void ExecuteInputs()
        {
            TransferInputs();
            _queue.Update();
        }

        private void TransferInputs()
        {
            foreach (var request in new InputBinding(_stream).ConvertInputStreamToEventData())
            {
                _queue.AddEvent(request);
            }
            _stream.ClearInputs();
        }

        private static string Setting(string csv, string key, string value)
        {
            return string.Join("\n", csv.Split('\n').Select(line => line.StartsWith(key + ",", StringComparison.Ordinal)
                ? key + "," + value + ",Test setting" : line));
        }

        private static string Read(string file)
        {
            return File.ReadAllText(Path.Combine(Application.dataPath, "1_InternalTools", "Util", "DataSheetLoader", "Table", file));
        }
    }
}
