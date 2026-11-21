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
    // 실제 콘텐츠 CSV와 UI → 요청 → Handler 경로를 검증한다. 거래 금액/저장 단위 검증은 별도 테스트가 담당한다.
    public sealed class TownFlowTests
    {
        private TownSheet _sheet;
        private TownDataContainer _data;
        private InputStream _stream;
        private UIInputStream _ui;
        private InputFilter _filter;
        private EventQueue _queue;
        private readonly List<SceneType> _destinations = new();
        private readonly List<string> _openedShops = new();
        private readonly List<TownActionRequested> _shopActions = new();
        private int _closedShops;
        private int _quitCount;
        private string _locations;
        private string _commands;
        private string _characters;
        private string _texts;

        [SetUp]
        public void SetUp()
        {
            DI.Init();
            _locations = ReadTable("TownLocationTable.csv");
            _commands = ReadTable("TownCommandTable.csv");
            _characters = ReadTable("TownCharacterTable.csv");
            _texts = ReadTable("TownTextTable.csv");
            _sheet = new TownSheet(_locations, _commands, _characters, _texts);
            _data = new TownDataContainer(_sheet, shopSheet: CreateShopSheet());
            DI.PubSub(_data);
            _stream = new InputStream();
            _ui = new UIInputStream(_stream);
            _filter = new InputFilter(() => false);
            _destinations.Clear();
            _openedShops.Clear();
            _shopActions.Clear();
            _closedShops = 0;
            _quitCount = 0;
            CreateQueue(SceneType.Town);
        }

        [TearDown]
        public void TearDown()
        {
            DI.Remove<TownEventHandler>();
            DI.Remove<TownDataContainer>();
            DI.Init();
        }

        [Test]
        public void LocationSheetsExposeTheRequestedCommandsAndValidVisitors()
        {
            CollectionAssert.AreEquivalent(new[] { "tavern", "market", "inn", "guild", "dungeon" }, _sheet.Locations.Select(location => location.Id));
            CollectionAssert.AreEqual(new[] { "trade", "rumor", "leave" }, _sheet.GetLocation("tavern").CommandIds);
            CollectionAssert.AreEqual(new[] { "trade", "leave" }, _sheet.GetLocation("market").CommandIds);
            CollectionAssert.AreEqual(new[] { "rest", "leave" }, _sheet.GetLocation("inn").CommandIds);
            CollectionAssert.AreEqual(new[] { "quest_accept", "quest_complete" }, _sheet.GetLocation("guild").CommandIds);
            CollectionAssert.AreEqual(new[] { "enter_dungeon", "leave" }, _sheet.GetLocation("dungeon").CommandIds);
            foreach (var location in _sheet.Locations)
            {
                if (location.Id == "dungeon")
                {
                    Assert.That(location.VisitorIds, Is.Empty);
                    continue;
                }
                Assert.That(location.VisitorIds.Count, Is.GreaterThan(0), location.Id);
                foreach (string visitor in location.VisitorIds)
                {
                    Assert.That(_sheet.GetCharacter(visitor).Name, Is.Not.Empty);
                }
            }
        }

        [Test]
        public void EnterThenCommandIsDeferredAndProcessedInFifoOrder()
        {
            _ui.AddInput(UIInputType.EnterLocation, "tavern");
            _ui.AddInput(UIInputType.LocationCommand, "rumor", "tavern");
            Assert.That(_data.LocationId, Is.Empty);
            TransferInputs();
            Assert.That(_data.LocationId, Is.Empty, "입력 변환만으로 상태를 변경하지 않습니다.");
            _queue.Update();
            Assert.That(_data.LocationId, Is.EqualTo("tavern"));
            Assert.That(_data.CommandId, Is.EqualTo("rumor"));
            Assert.That(_data.Revision, Is.EqualTo(2));
            _queue.Update();
            Assert.That(_data.Revision, Is.EqualTo(2), "이미 처리한 요청은 반복하지 않습니다.");
        }

        [Test]
        public void StaleLocationAndUnsupportedCommandCannotAffectTheCurrentPlace()
        {
            Request(UIInputType.EnterLocation, "market");
            _ui.AddInput(UIInputType.LocationCommand, "trade", "tavern");
            _ui.AddInput(UIInputType.LocationCommand, "rest", "market");
            _ui.AddInput(UIInputType.EnterLocation, "inn");
            ExecuteInputs();
            Assert.That(_data.LocationId, Is.EqualTo("market"));
            Assert.That(_data.CommandId, Is.Empty);
            Assert.That(_data.Revision, Is.EqualTo(1));
        }

        [Test]
        public void ShopBlocksBackgroundRequestsUntilClosedIncludingSameFrameInput()
        {
            Request(UIInputType.EnterLocation, "tavern");
            _ui.AddInput(UIInputType.LocationCommand, "trade", "tavern");
            _ui.AddInput(UIInputType.SelectCharacter, "mira", "tavern");
            _ui.AddInput(UIInputType.LeaveLocation);
            _ui.AddInput(UIInputType.ReturnTitle);
            ExecuteInputs();
            Assert.That(_data.CommandId, Is.Empty);
            Assert.That(_data.ShopOpen, Is.True);
            Assert.That(_data.ShopId, Is.EqualTo("tavern"));
            CollectionAssert.AreEqual(new[] { "tavern" }, _openedShops);
            Assert.That(_data.CharacterId, Is.Empty);
            Assert.That(_destinations, Is.Empty);
            Assert.That(_data.Revision, Is.EqualTo(2));

            _ui.AddInput(UIInputType.CloseDetails);
            _ui.AddInput(UIInputType.LocationCommand, "rumor", "tavern");
            ExecuteInputs();
            Assert.That(_data.ShopOpen, Is.False);
            Assert.That(_closedShops, Is.EqualTo(1));
            Assert.That(_data.CommandId, Is.EqualTo("rumor"));
            Assert.That(_data.Revision, Is.EqualTo(4));
        }

        [Test]
        public void BackClosesDetailsThenLeavesLocationAndStopsAtTownOverview()
        {
            Request(UIInputType.EnterLocation, "inn");
            Request(UIInputType.LocationCommand, "rest", "inn");
            Request(UIInputType.Back);
            Assert.That(_data.CommandId, Is.Empty);
            Assert.That(_data.LocationId, Is.EqualTo("inn"));
            Request(UIInputType.Back);
            Assert.That(_data.LocationId, Is.Empty);
            int revision = _data.Revision;
            Request(UIInputType.Back);
            Assert.That(_data.Revision, Is.EqualTo(revision));
            Assert.That(_destinations, Is.Empty, "마을 개요에서 Back을 눌러도 임의로 씬을 이동하지 않습니다.");
        }

        [Test]
        public void GuildUsesCommonLeaveAndDoesNotAcceptAnotherLocationsLeaveCommand()
        {
            Request(UIInputType.EnterLocation, "guild");
            Request(UIInputType.LocationCommand, "leave", "guild");
            Assert.That(_data.LocationId, Is.EqualTo("guild"));
            Request(UIInputType.LeaveLocation);
            Assert.That(_data.LocationId, Is.Empty);
            Request(UIInputType.ReturnTitle);
            CollectionAssert.AreEqual(new[] { SceneType.Title }, _destinations);
        }

        [Test]
        public void ConversationOnlyOpensForAVisitorOfTheCurrentLocation()
        {
            Request(UIInputType.EnterLocation, "tavern");
            Request(UIInputType.SelectCharacter, "missing_character", "tavern");
            Request(UIInputType.SelectCharacter, "mira", "market");
            Assert.That(_data.CharacterId, Is.Empty);
            Assert.That(_data.Revision, Is.EqualTo(1));
            Request(UIInputType.SelectCharacter, "mira", "tavern");
            Assert.That(_data.CharacterId, Is.EqualTo("mira"));
            Request(UIInputType.SelectCharacter, "bran", "tavern");
            Assert.That(_data.CharacterId, Is.EqualTo("mira"));
            Request(UIInputType.CloseDetails);
            Request(UIInputType.SelectCharacter, "bran", "tavern");
            Assert.That(_data.CharacterId, Is.EqualTo("bran"));
            Assert.That(_data.CommandId, Is.Empty);
        }

        [Test]
        public void FocusAndTransitionLockRejectAlreadyQueuedInput()
        {
            _ui.AddInput(UIInputType.EnterLocation, "tavern");
            TransferInputs();
            _filter.Focused = false;
            _queue.Update();
            Assert.That(_data.LocationId, Is.Empty);
            _filter.Focused = true;
            _filter.IsLocked = true;
            Request(UIInputType.EnterLocation, "tavern");
            Request(UIInputType.ReturnTitle);
            Assert.That(_data.LocationId, Is.Empty);
            Assert.That(_destinations, Is.Empty);
            _filter.IsLocked = false;
            Request(UIInputType.EnterLocation, "tavern");
            Assert.That(_data.LocationId, Is.EqualTo("tavern"));
        }

        [Test]
        public void TitleRoutesStartAndQuitButIgnoresTownActions()
        {
            CreateQueue(SceneType.Title);
            _ui.AddInput(UIInputType.EnterLocation, "tavern");
            _ui.AddInput(UIInputType.LocationCommand, "trade", "tavern");
            _ui.AddInput(UIInputType.ReturnTitle);
            _ui.AddInput(UIInputType.Back);
            ExecuteInputs();
            Assert.That(_data.LocationId, Is.Empty);
            Assert.That(_destinations, Is.Empty);
            Assert.That(_quitCount, Is.Zero);
            Request(UIInputType.StartGame);
            Request(UIInputType.QuitGame);
            CollectionAssert.AreEqual(new[] { SceneType.Town }, _destinations);
            Assert.That(_quitCount, Is.EqualTo(1));
        }

        [Test]
        public void TableValidationRejectsBrokenReferencesAndMissingRequiredText()
        {
            string missingCommand = _locations.Replace("trade;rumor;leave", "trade;unknown_command;leave");
            Assert.Throws<FormatException>(() => new TownSheet(missingCommand, _commands, _characters, _texts));
            string missingVisitor = _locations.Replace("mira;orin;bran;sera;neri;cass", "missing_visitor;orin;bran;sera;neri;cass");
            Assert.Throws<FormatException>(() => new TownSheet(missingVisitor, _commands, _characters, _texts));
            string missingText = string.Join("\n", _texts.Split('\n').Where(line => !line.StartsWith("title.start,", StringComparison.Ordinal)));
            Assert.Throws<FormatException>(() => new TownSheet(_locations, _commands, _characters, missingText));
        }

        [Test]
        public void DesignerTextPreservesQuotedCommasQuotesAndLineBreaks()
        {
            string text = string.Join("\n", _texts.Split('\n').Where(line => !line.StartsWith("title.footer,", StringComparison.Ordinal)));
            text += "\ntitle.footer,\"쉼표, 인용 \"\"안녕\"\"\n다음 줄\"\n";
            var sheet = new TownSheet(_locations, _commands, _characters, text);
            Assert.That(sheet.Text("title.footer"), Is.EqualTo("쉼표, 인용 \"안녕\"\n다음 줄"));
        }

        [Test]
        public void ShopActionsOutsideShopAreIgnoredAndBackClosesOnlyTheCurrentMerchant()
        {
            Request(UIInputType.ShopSelectBuy, "health_potion", "old-shop");
            Request(UIInputType.ShopConfirm, location: "old-shop");
            Request(UIInputType.ShopClose, location: "old-shop");
            Assert.That(_shopActions, Is.Empty);
            Request(UIInputType.EnterLocation, "inn");
            Request(UIInputType.LocationCommand, "trade", "inn");
            Assert.That(_data.ShopOpen, Is.False);
            Assert.That(_openedShops, Is.Empty);
            Request(UIInputType.LeaveLocation);
            Request(UIInputType.EnterLocation, "market");
            _ui.AddInput(UIInputType.LocationCommand, "trade", "market");
            _ui.AddInput(UIInputType.LocationCommand, "trade", "market");
            ExecuteInputs();
            Assert.That(_data.ShopId, Is.EqualTo("market"));
            CollectionAssert.AreEqual(new[] { "market" }, _openedShops);
            Request(UIInputType.Back);
            Assert.That(_data.ShopOpen, Is.False);
            Assert.That(_data.ShopId, Is.Empty);
            Assert.That(_closedShops, Is.EqualTo(1));
            Assert.That(_data.LocationId, Is.EqualTo("market"));
            Request(UIInputType.Back);
            Assert.That(_data.LocationId, Is.Empty);
        }

        [Test]
        public void ConversationAndInputLocksPreventShopEntryAndQueuedTradeActions()
        {
            Request(UIInputType.EnterLocation, "tavern");
            Request(UIInputType.SelectCharacter, "mira", "tavern");
            Request(UIInputType.LocationCommand, "trade", "tavern");
            Assert.That(_data.CharacterId, Is.EqualTo("mira"));
            Assert.That(_openedShops, Is.Empty);
            Request(UIInputType.CloseDetails);
            _filter.Focused = false;
            Request(UIInputType.LocationCommand, "trade", "tavern");
            _filter.Focused = true;
            _filter.IsLocked = true;
            Request(UIInputType.LocationCommand, "trade", "tavern");
            Assert.That(_data.ShopOpen, Is.False);
            _filter.IsLocked = false;
            Request(UIInputType.LocationCommand, "trade", "tavern");
            Assert.That(_data.ShopOpen, Is.True);
            _ui.AddInput(UIInputType.ShopSelectBuy, "health_potion", "shop-context");
            TransferInputs();
            _filter.Focused = false;
            _queue.Update();
            Assert.That(_shopActions, Is.Empty);
            _filter.Focused = true;
            _filter.IsLocked = true;
            Request(UIInputType.ShopConfirm, location: "shop-context");
            Assert.That(_shopActions, Is.Empty);
        }

        private ShopSheet CreateShopSheet()
        {
            var dungeon = new DungeonSheet(ReadTable("DungeonDefinitionTable.csv"), ReadTable("DungeonNodeTable.csv"),
                ReadTable("DungeonPartyTable.csv"), ReadTable("DungeonSettingTable.csv"), ReadTable("DungeonTextTable.csv"), _sheet);
            var loot = new LootSheet(ReadTable("LootItemTable.csv"), ReadTable("LootDropTable.csv"), ReadTable("LootShareTable.csv"),
                ReadTable("LootSettingTable.csv"), ReadTable("LootTextTable.csv"), dungeon);
            return new ShopSheet(ReadTable("ShopMerchantTable.csv"), ReadTable("ShopStockTable.csv"),
                ReadTable("ShopTextTable.csv"), loot, _sheet);
        }

        private void CreateQueue(SceneType scene)
        {
            _queue = new EventQueue(new EventHandler(new TownEventHandler(scene, _filter,
                destination => _destinations.Add(destination), () => _quitCount++,
                openShop: _openedShops.Add, shopAction: _shopActions.Add, closeShop: () => _closedShops++)));
        }

        private void Request(UIInputType action, string target = null, string location = null)
        {
            _ui.AddInput(action, target, location);
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

        private static string ReadTable(string name)
        {
            return File.ReadAllText(Path.Combine(Application.dataPath, "1_InternalTools", "Util", "DataSheetLoader", "Table", name));
        }
    }
}
