using GachaTrainers;
using NUnit.Framework;
using Refactor.Main;
using UnityEngine;
using EventHandler = Refactor.Main.Event.EventHandler;

namespace P2.Tests
{
    public sealed class GachaShopTests
    {
        private RunDataContainer _run;
        private ShopDataContainer _shop;
        private EventQueue _queue;
        private bool _focused;
        private int _rolls;
        private int _navigations;

        [SetUp]
        public void SetUp()
        {
            _run = new RunDataContainer();
            _shop = new ShopDataContainer();
            _focused = true;
            _rolls = 0;
            _navigations = 0;
            var handler = new GachaEventHandler(GachaSceneKind.Shop, _run, _shop,
                () => _focused, _ => _navigations++, count => { _rolls++; return 0; });
            _queue = new EventQueue(new EventHandler(handler));
        }

        [Test]
        public void PurchaseNeedsAnOpenNearbyMachineAndRunsOnlyWhenQueueProcesses()
        {
            _queue.AddEvent(new GachaActionRequested(GachaAction.Buy));
            _queue.Update();
            Assert.That(_run.Money, Is.EqualTo(GachaCatalog.StartingMoney));
            Assert.That(_rolls, Is.Zero);

            WalkTo(ShopDataContainer.CreatureInteractionPoint);
            _queue.AddEvent(new GachaActionRequested(GachaAction.Interact));
            _queue.AddEvent(new GachaActionRequested(GachaAction.Buy));
            Assert.That(_shop.Modal, Is.EqualTo(ShopModal.None));
            Assert.That(_run.Party, Is.Empty);
            _queue.Update();
            Assert.That(_shop.Modal, Is.EqualTo(ShopModal.Machine));
            Assert.That(_run.Party.Count, Is.EqualTo(1));
            Assert.That(_run.Money, Is.EqualTo(GachaCatalog.StartingMoney - GachaCatalog.CreaturePrice));
            Assert.That(_rolls, Is.EqualTo(1));
            _queue.Update();
            Assert.That(_rolls, Is.EqualTo(1), "이전 프레임의 입력을 다시 실행하면 안 됩니다.");
        }

        [Test]
        public void MovingDiagonallyDoesNotIncreaseSpeedAndLongFramesAreClamped()
        {
            var straight = new ShopDataContainer();
            var diagonal = new ShopDataContainer();
            Vector2 start = straight.Position;
            straight.Move(Vector2.right, 0.05f);
            diagonal.Move(new Vector2(1f, 1f), 0.05f);
            Assert.That(Vector2.Distance(start, diagonal.Position),
                Is.EqualTo(Vector2.Distance(start, straight.Position)).Within(0.0001f));

            var longFrame = new ShopDataContainer();
            longFrame.Move(Vector2.right, 10f);
            Assert.That(longFrame.Position, Is.EqualTo(straight.Position));
            var still = new ShopDataContainer();
            still.Move(Vector2.right, -1f);
            Assert.That(still.Position, Is.EqualTo(start));
        }

        [Test]
        public void PlayerCannotLeaveShopOrWalkThroughMachine()
        {
            for (int index = 0; index < 200; index++)
            {
                _shop.Move(Vector2.left, 0.05f);
                _shop.Move(Vector2.down, 0.05f);
            }
            Assert.That(_shop.Position.x, Is.GreaterThanOrEqualTo(-8.7f));
            Assert.That(_shop.Position.y, Is.GreaterThanOrEqualTo(-3.6f));

            _shop = new ShopDataContainer();
            WalkTo(ShopDataContainer.CreatureInteractionPoint);
            for (int index = 0; index < 100; index++)
            {
                _shop.Move(Vector2.up, 0.05f);
            }
            Assert.That(_shop.Position.y, Is.LessThanOrEqualTo(1.3001f),
                "플레이어 반경을 포함해 머신 전면에서 멈춰야 합니다.");
            Assert.That(_shop.NearestMachine, Is.EqualTo(GachaMachineKind.Creature));
        }

        [Test]
        public void InteractingFromTheEntranceDoesNotOpenOrChargeAMachine()
        {
            Assert.That(_shop.NearestMachine, Is.Null);
            _queue.AddEvent(new GachaActionRequested(GachaAction.Interact));
            _queue.AddEvent(new GachaActionRequested(GachaAction.Buy));
            _queue.Update();
            Assert.That(_shop.Modal, Is.EqualTo(ShopModal.None));
            Assert.That(_run.Money, Is.EqualTo(GachaCatalog.StartingMoney));
            Assert.That(_run.Party, Is.Empty);
            Assert.That(_rolls, Is.Zero);
        }

        [Test]
        public void ModalBlocksMovementAndPartyChangesButCloseThenMoveKeepsFifoOrder()
        {
            WalkTo(ShopDataContainer.CreatureInteractionPoint);
            _queue.AddEvent(new GachaActionRequested(GachaAction.Interact));
            _queue.AddEvent(new GachaActionRequested(GachaAction.Buy));
            _queue.Update();
            Vector2 before = _shop.Position;
            _queue.AddEvent(new GachaMoveRequested(Vector2.down, 0.05f));
            _queue.AddEvent(new GachaActionRequested(GachaAction.ReleaseCreature));
            _queue.Update();
            Assert.That(_shop.Position, Is.EqualTo(before));
            Assert.That(_run.Party.Count, Is.EqualTo(1));

            _queue.AddEvent(new GachaActionRequested(GachaAction.CloseModal));
            _queue.AddEvent(new GachaMoveRequested(Vector2.down, 0.05f));
            _queue.Update();
            Assert.That(_shop.Modal, Is.EqualTo(ShopModal.None));
            Assert.That(_shop.Position.y, Is.LessThan(before.y));
        }

        [Test]
        public void LostFocusIsCheckedWhenPendingActionsExecute()
        {
            WalkTo(ShopDataContainer.CreatureInteractionPoint);
            _shop.OpenNearestMachine();
            _queue.AddEvent(new GachaActionRequested(GachaAction.Buy));
            _focused = false;
            _queue.Update();
            Assert.That(_run.Party, Is.Empty);
            Assert.That(_run.Money, Is.EqualTo(GachaCatalog.StartingMoney));
            Assert.That(_rolls, Is.Zero);
            _focused = true;
            _queue.Update();
            Assert.That(_run.Party, Is.Empty, "포커스 복귀 시 이전 구매를 다시 실행하면 안 됩니다.");
        }

        [Test]
        public void ReturnTitleConfirmationBlocksPurchaseAndRequiresExplicitConfirm()
        {
            _queue.AddEvent(new GachaActionRequested(GachaAction.ConfirmReturnTitle));
            _queue.Update();
            Assert.That(_navigations, Is.Zero);
            _queue.AddEvent(new GachaActionRequested(GachaAction.ReturnTitle));
            _queue.AddEvent(new GachaActionRequested(GachaAction.Buy));
            _queue.Update();
            Assert.That(_shop.Modal, Is.EqualTo(ShopModal.ReturnTitle));
            Assert.That(_run.Money, Is.EqualTo(GachaCatalog.StartingMoney));
            Assert.That(_rolls, Is.Zero);
            Assert.That(_navigations, Is.Zero);
            _queue.AddEvent(new GachaActionRequested(GachaAction.CloseModal));
            _queue.Update();
            Assert.That(_navigations, Is.Zero);
            _queue.AddEvent(new GachaActionRequested(GachaAction.ReturnTitle));
            _queue.AddEvent(new GachaActionRequested(GachaAction.ConfirmReturnTitle));
            _queue.Update();
            Assert.That(_navigations, Is.EqualTo(1));
        }

        [Test]
        public void CrystalMachineOnlyAddsCrystalsAndChargesItsOwnPrice()
        {
            WalkTo(ShopDataContainer.CrystalInteractionPoint);
            _queue.AddEvent(new GachaActionRequested(GachaAction.Interact));
            _queue.AddEvent(new GachaActionRequested(GachaAction.Buy));
            _queue.Update();
            Assert.That(_shop.ActiveMachine, Is.EqualTo(GachaMachineKind.Crystal));
            Assert.That(_run.Party, Is.Empty);
            Assert.That(_run.GetCrystalCount(CrystalKind.Ember), Is.EqualTo(1));
            Assert.That(_run.Money, Is.EqualTo(GachaCatalog.StartingMoney - GachaCatalog.CrystalPrice));
        }

        private void WalkTo(Vector2 target)
        {
            for (int step = 0; step < 150 && Vector2.Distance(_shop.Position, target) > 0.12f; step++)
            {
                Vector2 delta = target - _shop.Position;
                _shop.Move(delta.normalized, Mathf.Min(0.05f, delta.magnitude / 4.2f));
            }
            Assert.That(Vector2.Distance(_shop.Position, target), Is.LessThanOrEqualTo(0.12f));
        }
    }
}
