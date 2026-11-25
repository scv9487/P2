using GachaTrainers;
using NUnit.Framework;
using Refactor.Main;
using EventHandler = Refactor.Main.Event.EventHandler;

namespace P2.Tests
{
    public sealed class BattleFlowTests
    {
        [Test]
        public void EmptyPartyCannotEnterBattleOrClaimAnUnstartedVictory()
        {
            var run = new RunDataContainer();
            Assert.That(run.TryBeginBattle(), Is.False);
            Assert.That(run.BattleInProgress, Is.False);
            Assert.That(run.CompleteBattle(true), Is.False);
            Assert.That(run.Money, Is.EqualTo(GachaCatalog.StartingMoney));
        }

        [Test]
        public void EachEnteredBattleCanPayVictoryPrizeOnlyOnce()
        {
            var run = RunDataContainer.CreateBattleTestRun();
            int before = run.Money;
            Assert.That(run.TryBeginBattle(), Is.True);
            Assert.That(run.TryBeginBattle(), Is.False);
            Assert.That(run.Money, Is.EqualTo(before), "전투 입장 자체는 돈을 차감하지 않습니다.");
            Assert.That(run.CompleteBattle(true), Is.True);
            Assert.That(run.BattleInProgress, Is.False);
            Assert.That(run.Money, Is.EqualTo(before + RunDataContainer.VictoryPrize));
            Assert.That(run.CompleteBattle(true), Is.False);
            Assert.That(run.CompleteBattle(false), Is.False);
            Assert.That(run.Money, Is.EqualTo(before + RunDataContainer.VictoryPrize));
            Assert.That(run.TryBeginBattle(), Is.True);
            Assert.That(run.CompleteBattle(true), Is.True);
            Assert.That(run.Money, Is.EqualTo(before + 2 * RunDataContainer.VictoryPrize));
        }

        [Test]
        public void DefeatCompletesPendingBattleWithoutAwardingCurrency()
        {
            var run = RunDataContainer.CreateBattleTestRun();
            int before = run.Money;
            Assert.That(run.TryBeginBattle(), Is.True);
            Assert.That(run.CompleteBattle(false), Is.True);
            Assert.That(run.BattleInProgress, Is.False);
            Assert.That(run.Money, Is.EqualTo(before));
            Assert.That(run.CompleteBattle(true), Is.False, "이미 종료한 패배를 승리로 재정산할 수 없습니다.");
            Assert.That(run.Money, Is.EqualTo(before));
        }

        [Test]
        public void BattleLocksShopMutationsAndCompletionPreservesEquipmentAndInventory()
        {
            var run = new RunDataContainer();
            run.TryPurchase(GachaMachineKind.Creature, _ => 0);
            run.TryPurchase(GachaMachineKind.Crystal, _ => 0);
            run.TryPurchase(GachaMachineKind.Crystal, _ => GachaCatalog.GetCrystalRollStart(CrystalKind.Protection));
            run.TryEquipCrystal(CrystalKind.Ember);
            PartyCreature creature = run.Party[0];
            int before = run.Money;
            Assert.That(run.TryBeginBattle(), Is.True);
            Assert.That(run.TryPurchase(GachaMachineKind.Creature, _ => throw new System.Exception("전투 중 추첨 금지")), Is.False);
            Assert.That(run.TryPurchase(GachaMachineKind.Crystal, _ => throw new System.Exception("전투 중 추첨 금지")), Is.False);
            Assert.That(run.ReleaseSelectedCreature(), Is.False);
            Assert.That(run.UnequipSelectedCrystal(), Is.False);
            Assert.That(run.TryEquipCrystal(CrystalKind.Protection), Is.False);
            Assert.That(run.Party[0], Is.SameAs(creature));
            Assert.That(run.GetCrystalCount(CrystalKind.Ember), Is.Zero);
            Assert.That(run.GetCrystalCount(CrystalKind.Protection), Is.EqualTo(1));
            Assert.That(run.Money, Is.EqualTo(before));
            Assert.That(run.CompleteBattle(true), Is.True);
            Assert.That(run.Party[0], Is.SameAs(creature));
            Assert.That(run.TryEquipCrystal(CrystalKind.Protection), Is.True, "전투가 끝나면 상점 조작이 다시 가능합니다.");
            Assert.That(run.GetCrystalCount(CrystalKind.Ember), Is.EqualTo(1));
            Assert.That(run.GetCrystalCount(CrystalKind.Protection), Is.Zero);
            Assert.That(run.Money, Is.EqualTo(before + RunDataContainer.VictoryPrize));
        }

        [Test]
        public void DirectBattlePreviewCreatesIndependentBaseTrioWithoutChangingNormalStart()
        {
            var preview = RunDataContainer.CreateBattleTestRun();
            var another = RunDataContainer.CreateBattleTestRun();
            Assert.That(preview.Money, Is.EqualTo(GachaCatalog.StartingMoney));
            Assert.That(preview.Party.Count, Is.EqualTo(3));
            Assert.That(preview.Party[0].Kind, Is.EqualTo(CreatureKind.EmberFox));
            Assert.That(preview.Party[1].Kind, Is.EqualTo(CreatureKind.MossTurtle));
            Assert.That(preview.Party[2].Kind, Is.EqualTo(CreatureKind.VoltRabbit));
            foreach (PartyCreature creature in preview.Party)
            {
                Assert.That(creature.EquippedCrystal, Is.Null);
            }
            preview.SelectParty(0);
            preview.ReleaseSelectedCreature();
            Assert.That(another.Party.Count, Is.EqualTo(3));
            Assert.That(new RunDataContainer().Party, Is.Empty, "일반 새 런에 테스트 파티가 유입되면 안 됩니다.");
        }

        [Test]
        public void ShopEntryRequiresPartyAndClosedModalAndOnlyNavigatesOnce()
        {
            var run = new RunDataContainer();
            var shop = new ShopDataContainer();
            int navigations = 0;
            var queue = new EventQueue(new EventHandler(new GachaEventHandler(GachaSceneKind.Shop,
                run, shop, () => true, _ => navigations++, _ => 0)));
            queue.AddEvent(new GachaActionRequested(GachaAction.BeginBattle));
            queue.Update();
            Assert.That(navigations, Is.Zero);
            Assert.That(run.BattleInProgress, Is.False);
            run.TryPurchase(GachaMachineKind.Creature, _ => 0);
            shop.OpenReturnTitle();
            queue.AddEvent(new GachaActionRequested(GachaAction.BeginBattle));
            queue.Update();
            Assert.That(navigations, Is.Zero);
            Assert.That(run.BattleInProgress, Is.False);
            queue.AddEvent(new GachaActionRequested(GachaAction.CloseModal));
            queue.AddEvent(new GachaActionRequested(GachaAction.BeginBattle));
            queue.AddEvent(new GachaActionRequested(GachaAction.BeginBattle));
            Assert.That(navigations, Is.Zero, "입력 등록 시점에는 씬 전환이 실행되지 않습니다.");
            queue.Update();
            Assert.That(navigations, Is.EqualTo(1));
            Assert.That(run.BattleInProgress, Is.True);
        }

        [Test]
        public void BattleCannotFinishEarlyAndPauseBlocksAttacksUntilResume()
        {
            var run = RunDataContainer.CreateBattleTestRun();
            run.TryBeginBattle();
            var battle = new BattleDataContainer(run.Party);
            int navigations = 0;
            var queue = new EventQueue(new EventHandler(new GachaEventHandler(GachaSceneKind.Battle,
                run, null, () => true, _ => navigations++, _ => 0, battle)));
            queue.AddEvent(new GachaActionRequested(GachaAction.FinishBattle));
            queue.AddEvent(new GachaActionRequested(GachaAction.LeaveBattle));
            queue.Update();
            Assert.That(navigations, Is.Zero);
            for (int step = 0; step < 30 && battle.Phase == BattlePhase.Summoning; step++)
            {
                battle.Tick(0.05f);
            }
            queue.AddEvent(new GachaActionRequested(GachaAction.BattlePause));
            queue.AddEvent(new GachaActionRequested(GachaAction.BattleSpecial));
            queue.AddEvent(new GachaActionRequested(GachaAction.BattleDodge));
            queue.Update();
            Assert.That(battle.Paused, Is.True);
            Assert.That(battle.Player.SpecialCooldown, Is.Zero);
            Assert.That(battle.Player.DodgeCooldown, Is.Zero);
            queue.AddEvent(new GachaActionRequested(GachaAction.ResumeBattle));
            queue.AddEvent(new GachaActionRequested(GachaAction.BattleDodge));
            queue.Update();
            Assert.That(battle.Paused, Is.False);
            Assert.That(battle.Player.DodgeCooldown, Is.GreaterThan(0f));
            queue.AddEvent(new GachaActionRequested(GachaAction.BattlePause));
            queue.AddEvent(new GachaActionRequested(GachaAction.LeaveBattle));
            queue.AddEvent(new GachaActionRequested(GachaAction.LeaveBattle));
            queue.Update();
            Assert.That(navigations, Is.EqualTo(1));
        }
    }
}
