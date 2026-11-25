using System;
using System.Collections.Generic;
using GachaTrainers;
using NUnit.Framework;

namespace P2.Tests
{
    public sealed class GachaRunTests
    {
        [TestCase(0, CreatureKind.EmberFox)]
        [TestCase(1, CreatureKind.MossTurtle)]
        [TestCase(2, CreatureKind.VoltRabbit)]
        [TestCase(3, CreatureKind.FrostOwl)]
        [TestCase(4, CreatureKind.TideOtter)]
        [TestCase(5, CreatureKind.StoneBoar)]
        public void CreaturePurchaseChargesOnceAndUsesEntirePool(int result, CreatureKind expected)
        {
            var data = new RunDataContainer();
            int rolls = 0;
            Assert.That(data.TryPurchase(GachaMachineKind.Creature, count =>
            {
                Assert.That(count, Is.EqualTo(GachaCatalog.CreatureWeightTotal));
                rolls++;
                return GachaCatalog.GetCreatureRollStart(expected);
            }), Is.True);
            Assert.That(rolls, Is.EqualTo(1));
            Assert.That(data.Money, Is.EqualTo(500));
            Assert.That(data.Party.Count, Is.EqualTo(1));
            Assert.That(data.Party[0].Kind, Is.EqualTo(expected));
            Assert.That(data.SelectedPartyIndex, Is.Zero);
        }

        [TestCase(CrystalKind.Ember)]
        [TestCase(CrystalKind.Protection)]
        [TestCase(CrystalKind.Reflection)]
        [TestCase(CrystalKind.DeathThroes)]
        [TestCase(CrystalKind.Sink)]
        [TestCase(CrystalKind.Capital)]
        public void CrystalPurchaseChargesAndAddsOneStock(CrystalKind expected)
        {
            var data = new RunDataContainer();
            Assert.That(data.TryPurchase(GachaMachineKind.Crystal, count =>
            {
                Assert.That(count, Is.EqualTo(GachaCatalog.CrystalWeightTotal));
                return GachaCatalog.GetCrystalRollStart(expected);
            }), Is.True);
            Assert.That(data.Money, Is.EqualTo(540));
            foreach (var definition in GachaCatalog.Crystals)
            {
                Assert.That(data.GetCrystalCount(definition.Kind), Is.EqualTo(definition.Kind == expected ? 1 : 0));
            }
            Assert.That(data.Party, Is.Empty);
        }

        [Test]
        public void FullPartyCanKeepBuyingIntoOwnershipAndReleaseReturnsNoMoney()
        {
            var data = new RunDataContainer();
            for (int i = 0; i < 4; i++)
            {
                Assert.That(data.TryPurchase(GachaMachineKind.Creature, _ => 0), Is.True);
            }
            Assert.That(data.Party.Count, Is.EqualTo(3));
            Assert.That(data.Owned.Count, Is.EqualTo(4));
            Assert.That(data.Money, Is.EqualTo(200));
            Assert.That(data.SelectedPartyIndex, Is.EqualTo(-1));
            Assert.That(data.ReleaseSelectedCreature(), Is.True);
            Assert.That(data.Money, Is.EqualTo(200));
            Assert.That(data.Owned.Count, Is.EqualTo(3));
            Assert.That(data.Party.Count, Is.EqualTo(3));
        }

        [TestCase(GachaMachineKind.Creature)]
        [TestCase(GachaMachineKind.Crystal)]
        public void InsufficientMoneyRejectsBeforeRollAndLeavesInventoryUnchanged(GachaMachineKind machine)
        {
            var data = new RunDataContainer();
            for (int i = 0; i < 10; i++)
            {
                Assert.That(data.TryPurchase(GachaMachineKind.Crystal, _ => 0), Is.True);
            }
            Assert.That(data.Money, Is.Zero);
            Assert.That(data.TryPurchase(machine, _ => throw new Exception("Must not roll")), Is.False);
            Assert.That(data.Money, Is.Zero);
            Assert.That(data.Party, Is.Empty);
            Assert.That(data.GetCrystalCount(CrystalKind.Ember), Is.EqualTo(10));
        }

        [TestCase(GachaMachineKind.Creature, -1)]
        [TestCase(GachaMachineKind.Creature, 600)]
        [TestCase(GachaMachineKind.Crystal, -1)]
        [TestCase(GachaMachineKind.Crystal, 1200)]
        public void InvalidRollResultCannotChargeOrCreateAnItem(GachaMachineKind machine, int result)
        {
            var data = new RunDataContainer();
            Assert.That(data.TryPurchase(machine, _ => result), Is.False);
            Assert.That(data.Money, Is.EqualTo(600));
            Assert.That(data.Party, Is.Empty);
            foreach (var definition in GachaCatalog.Crystals)
            {
                Assert.That(data.GetCrystalCount(definition.Kind), Is.Zero);
            }
        }

        [Test]
        public void CrystalEquipmentPreservesBaseStatsAndReturnsReplacedCrystal()
        {
            var data = WithCreatureAndCrystals();
            var before = data.Party[0];
            Assert.That(data.TryEquipCrystal(CrystalKind.Ember), Is.True);
            Assert.That(data.Party[0].MaxHealth, Is.EqualTo(85));
            Assert.That(data.Party[0].EquippedCrystal, Is.EqualTo(CrystalKind.Ember));
            Assert.That(data.GetCrystalCount(CrystalKind.Ember), Is.Zero);
            Assert.That(before.EquippedCrystal, Is.Null, "Published party values must not mutate.");
            Assert.That(data.TryEquipCrystal(CrystalKind.Protection), Is.True);
            Assert.That(data.Party[0].MaxHealth, Is.EqualTo(85));
            Assert.That(data.Party[0].Attack, Is.EqualTo(18));
            Assert.That(data.Party[0].EquippedCrystal, Is.EqualTo(CrystalKind.Protection));
            Assert.That(data.GetCrystalCount(CrystalKind.Ember), Is.EqualTo(1));
            Assert.That(data.GetCrystalCount(CrystalKind.Protection), Is.Zero);
            Assert.That(data.TryEquipCrystal(CrystalKind.Reflection), Is.True);
            Assert.That(data.Party[0].Attack, Is.EqualTo(18));
            Assert.That(data.Party[0].AttackSpeed, Is.EqualTo(0.9f).Within(0.0001f));
            Assert.That(data.Party[0].EquippedCrystal, Is.EqualTo(CrystalKind.Reflection));
            Assert.That(data.GetCrystalCount(CrystalKind.Protection), Is.EqualTo(1));
            Assert.That(data.Money, Is.EqualTo(320), "Equipping is free.");
        }

        [Test]
        public void InvalidOrRepeatedEquipmentDoesNotConsumeCrystals()
        {
            var data = new RunDataContainer();
            data.TryPurchase(GachaMachineKind.Crystal, _ => 0);
            Assert.That(data.TryEquipCrystal(CrystalKind.Ember), Is.False);
            Assert.That(data.GetCrystalCount(CrystalKind.Ember), Is.EqualTo(1));
            data.TryPurchase(GachaMachineKind.Creature, _ => 0);
            Assert.That(data.TryEquipCrystal(CrystalKind.Protection), Is.False);
            Assert.That(data.Party[0].EquippedCrystal, Is.Null);
            Assert.That(data.TryEquipCrystal(CrystalKind.Ember), Is.True);
            data.TryPurchase(GachaMachineKind.Crystal, _ => 0);
            Assert.That(data.TryEquipCrystal(CrystalKind.Ember), Is.False);
            Assert.That(data.GetCrystalCount(CrystalKind.Ember), Is.EqualTo(1));
            Assert.That(data.Party[0].MaxHealth, Is.EqualTo(85));
            Assert.That(data.Party[0].EquippedCrystal, Is.EqualTo(CrystalKind.Ember));
        }

        [Test]
        public void UnequipAndReleaseReturnExactlyOneCrystalAndKeepSelectionValid()
        {
            var data = WithCreatureAndCrystals();
            data.TryEquipCrystal(CrystalKind.Protection);
            Assert.That(data.UnequipSelectedCrystal(), Is.True);
            Assert.That(data.UnequipSelectedCrystal(), Is.False);
            Assert.That(data.GetCrystalCount(CrystalKind.Protection), Is.EqualTo(1));
            Assert.That(data.Party[0].Attack, Is.EqualTo(18));
            data.TryEquipCrystal(CrystalKind.Protection);
            Assert.That(data.ReleaseSelectedCreature(), Is.True);
            Assert.That(data.ReleaseSelectedCreature(), Is.False);
            Assert.That(data.GetCrystalCount(CrystalKind.Protection), Is.EqualTo(1));
            Assert.That(data.Party, Is.Empty);
            Assert.That(data.SelectedPartyIndex, Is.EqualTo(-1));
        }

        [Test]
        public void SelectedCreatureOnlyReceivesEquipment()
        {
            var data = WithCreatureAndCrystals();
            data.TryPurchase(GachaMachineKind.Creature, _ => GachaCatalog.GetCreatureRollStart((CreatureKind)1));
            Assert.That(data.SelectedPartyIndex, Is.EqualTo(1));
            data.SelectParty(0);
            data.TryEquipCrystal(CrystalKind.Protection);
            Assert.That(data.Party[0].EquippedCrystal, Is.EqualTo(CrystalKind.Protection));
            Assert.That(data.Party[1].EquippedCrystal, Is.Null);
            data.SelectParty(7);
            Assert.That(data.SelectedPartyIndex, Is.Zero, "Invalid selection must preserve the valid selection.");
            data.SelectParty(-1);
            Assert.That(data.SelectedPartyIndex, Is.Zero);
        }

        [Test]
        public void NewRunHasFreshMoneyPartyInventoryAndSelection()
        {
            var oldRun = WithCreatureAndCrystals();
            oldRun.TryEquipCrystal(CrystalKind.Ember);
            oldRun.SetAutoBattle(true);
            var newRun = new RunDataContainer();
            Assert.That(newRun.Money, Is.EqualTo(600));
            Assert.That(newRun.Party, Is.Empty);
            Assert.That(newRun.SelectedPartyIndex, Is.EqualTo(-1));
            Assert.That(newRun.AutoBattle, Is.False);
            foreach (var definition in GachaCatalog.Crystals)
            {
                Assert.That(newRun.GetCrystalCount(definition.Kind), Is.Zero);
            }
            Assert.That(oldRun.Party.Count, Is.EqualTo(1));
            Assert.That(oldRun.AutoBattle, Is.True);
            Assert.That(oldRun.Party[0].EquippedCrystal, Is.EqualTo(CrystalKind.Ember));
        }

        [Test]
        public void AutoBattleSelectionCanChangeDuringBattleAndPersistsWithinTheRun()
        {
            var data = WithCreatureAndCrystals();
            Assert.That(data.AutoBattle, Is.False);
            data.SetAutoBattle(true);
            Assert.That(data.AutoBattle, Is.True);
            Assert.That(data.TryBeginBattle(), Is.True);
            data.SetAutoBattle(false);
            Assert.That(data.AutoBattle, Is.False);
            data.SetAutoBattle(true);
            Assert.That(data.CompleteBattle(true), Is.True);
            Assert.That(data.AutoBattle, Is.True);
            int revision = data.Revision;
            data.SetAutoBattle(true);
            Assert.That(data.Revision, Is.EqualTo(revision), "Selecting the current mode must not publish a change.");
        }

        [Test]
        public void UnknownEnumsAndMissingRollCannotMutateEconomy()
        {
            var data = WithCreatureAndCrystals();
            Assert.That(data.TryPurchase((GachaMachineKind)42, _ => throw new Exception("Must not roll")), Is.False);
            Assert.That(data.TryPurchase(GachaMachineKind.Creature, null), Is.False);
            Assert.That(data.TryEquipCrystal((CrystalKind)(-1)), Is.False);
            Assert.That(data.TryEquipCrystal((CrystalKind)99), Is.False);
            Assert.That(data.Money, Is.EqualTo(320));
            Assert.That(data.Party.Count, Is.EqualTo(1));
            Assert.That(data.Party[0].EquippedCrystal, Is.Null);
            Assert.Throws<ArgumentOutOfRangeException>(() => data.GetCrystalCount((CrystalKind)99));
            Assert.Throws<ArgumentOutOfRangeException>(() => GachaCatalog.GetCreature((CreatureKind)7));
            Assert.Throws<ArgumentOutOfRangeException>(() => GachaCatalog.GetCrystal((CrystalKind)99));
        }

        [Test]
        public void ReadOnlyCollectionsDoNotExposeMutableLists()
        {
            var data = WithCreatureAndCrystals();
            Assert.Throws<NotSupportedException>(() => ((IList<PartyCreature>)data.Party).Clear());
            Assert.Throws<NotSupportedException>(() => ((IList<CreatureDefinition>)GachaCatalog.Creatures).Clear());
            Assert.Throws<NotSupportedException>(() => ((IList<CrystalDefinition>)GachaCatalog.Crystals).Clear());
        }

        private static RunDataContainer WithCreatureAndCrystals()
        {
            var data = new RunDataContainer();
            data.TryPurchase(GachaMachineKind.Creature, _ => 0);
            for (int i = 0; i < 3; i++)
            {
                int index = i;
                data.TryPurchase(GachaMachineKind.Crystal, _ => GachaCatalog.GetCrystalRollStart((CrystalKind)index));
            }
            return data;
        }
    }
}
