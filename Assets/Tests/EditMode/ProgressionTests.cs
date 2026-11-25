using System.Linq;
using GachaTrainers;
using NUnit.Framework;

namespace P2.Tests
{
    public sealed class ProgressionTests
    {
        [Test]
        public void OwnershipCanGrowAcrossPagesWhileOnlyAssignedThreeEnterBattle()
        {
            var run = new RunDataContainer();
            for (int i = 0; i < 20; i++)
            {
                Buy(run, (CreatureKind)(i % 6));
            }
            Assert.That(run.Owned.Count, Is.EqualTo(20));
            Assert.That(run.Owned.Select(x => x.InstanceId).Distinct().Count(), Is.EqualTo(20));
            Assert.That(run.Party.Count, Is.EqualTo(3));
            var selected = run.Owned.Last();
            Assert.That(run.AssignSelected(1), Is.True);
            Assert.That(run.Party[1], Is.SameAs(selected));
            Assert.That(run.Owned.Count, Is.EqualTo(20));
            var battle = new BattleDataContainer(run.Party);
            Assert.That(battle.PlayerTeam.Count, Is.EqualTo(3));
            Assert.That(battle.PlayerTeam[1].Kind, Is.EqualTo(selected.Kind));
            Assert.That(run.BenchSelected(), Is.True);
            Assert.That(run.Party.Count, Is.EqualTo(2));
            Assert.That(run.Owned.Count, Is.EqualTo(20));
        }

        [Test]
        public void ReassigningAPartyMemberSwapsSlotsWithoutDuplicatingIt()
        {
            var run = RunDataContainer.CreateBattleTestRun();
            var first = run.Party[0];
            var last = run.Party[2];
            run.SelectParty(0);
            Assert.That(run.AssignSelected(2), Is.True);
            Assert.That(run.Party[0], Is.SameAs(last));
            Assert.That(run.Party[2], Is.SameAs(first));
            Assert.That(run.Party.Distinct().Count(), Is.EqualTo(3));
            Assert.That(run.AssignSelected(99), Is.False);
        }

        [TestCase(0, 1)]
        [TestCase(1, 2)]
        public void ThreeCopiesOfferTwoFormsAndConsumeExactlyThree(int branch, int expectedNode)
        {
            var run = new RunDataContainer();
            Buy(run, CreatureKind.StoneBoar);
            Buy(run, CreatureKind.StoneBoar);
            Assert.That(run.EvolutionPending, Is.False);
            Assert.That(run.TryEvolve(branch), Is.False);
            Buy(run, CreatureKind.StoneBoar);
            var snapshot = run.Owned[0];
            Assert.That(run.EvolutionPending, Is.True);
            int money = run.Money;
            Assert.That(run.TryEvolve(branch), Is.True);
            Assert.That(run.Owned.Count, Is.EqualTo(1));
            Assert.That(run.Party.Count, Is.EqualTo(1));
            Assert.That(run.Owned[0].FormNode, Is.EqualTo(expectedNode));
            Assert.That(run.Owned[0].Rank, Is.EqualTo(2));
            Assert.That(run.Owned[0].Rarity, Is.EqualTo(CrystalRarity.Rare));
            Assert.That(run.Money, Is.EqualTo(money));
            Assert.That(snapshot.Rank, Is.EqualTo(1), "Published snapshots cannot mutate.");
            Assert.That(run.TryEvolve(branch), Is.False, "A repeated queued click cannot consume more copies.");
        }

        [Test]
        public void EvolutionPreservesPrimaryEquipmentAndReturnsOtherCrystals()
        {
            var run = new RunDataContainer();
            for (int i = 0; i < 3; i++)
            {
                Buy(run, CreatureKind.EmberFox);
                Fund(run, GachaCatalog.CrystalPrice);
                run.TryPurchase(GachaMachineKind.Crystal, _ => GachaCatalog.GetCrystalRollStart((CrystalKind)i));
                Assert.That(run.TryEquipCrystal((CrystalKind)i), Is.True);
            }
            int primaryId = run.Owned[2].InstanceId;
            Assert.That(run.TryEvolve(0), Is.True);
            Assert.That(run.Owned[0].InstanceId, Is.EqualTo(primaryId));
            Assert.That(run.Owned[0].EquippedCrystal, Is.EqualTo(CrystalKind.Reflection));
            Assert.That(run.GetCrystalCount(CrystalKind.Ember), Is.EqualTo(1));
            Assert.That(run.GetCrystalCount(CrystalKind.Protection), Is.EqualTo(1));
            Assert.That(run.GetCrystalCount(CrystalKind.Reflection), Is.Zero);
            Assert.That(run.UnequipSelectedCrystal(), Is.True);
            Assert.That(run.Owned[0].Rank, Is.EqualTo(2));
            Assert.That(run.GetCrystalCount(CrystalKind.Reflection), Is.EqualTo(1));
        }

        [Test]
        public void NineBaseCopiesReachRankThreeAndDifferentBranchesCannotMerge()
        {
            var run = new RunDataContainer();
            for (int batch = 0; batch < 3; batch++)
            {
                for (int i = 0; i < 3; i++)
                {
                    Buy(run, CreatureKind.FrostOwl);
                }
                Assert.That(run.TryEvolve(0), Is.True);
            }
            Assert.That(run.Owned.Select(x => x.FormNode), Is.EqualTo(new[] { 1, 1, 1 }));
            Assert.That(run.TryEvolve(1), Is.True);
            Assert.That(run.Owned.Single().FormNode, Is.EqualTo(4));
            Assert.That(run.Owned.Single().Rank, Is.EqualTo(3));
            Assert.That(run.Owned.Single().Rarity, Is.EqualTo(CrystalRarity.Uncommon));
            Assert.That(run.TryEvolve(0), Is.False);
            for (int batch = 0; batch < 2; batch++)
            {
                for (int i = 0; i < 3; i++)
                {
                    Buy(run, CreatureKind.FrostOwl);
                }
                Assert.That(run.TryEvolve(batch), Is.True);
            }
            Assert.That(run.Owned.Count, Is.EqualTo(3));
            Assert.That(run.EvolutionPending, Is.False, "Rank 3 + rank 2 A + rank 2 B are not the same form.");
        }

        [Test]
        public void BattleLocksOwnershipEvolutionAndEquipmentButNewRunResetsEverything()
        {
            var run = new RunDataContainer();
            for (int i = 0; i < 3; i++)
            {
                Buy(run, CreatureKind.MossTurtle);
            }
            Assert.That(run.TryBeginBattle(), Is.True);
            Assert.That(run.TryEvolve(0), Is.False);
            Assert.That(run.AssignSelected(0), Is.False);
            Assert.That(run.BenchSelected(), Is.False);
            Assert.That(run.ReleaseSelectedCreature(), Is.False);
            Assert.That(run.TryPurchase(GachaMachineKind.Creature, _ => throw new System.Exception()), Is.False);
            var fresh = new RunDataContainer();
            Assert.That(fresh.Owned, Is.Empty);
            Assert.That(fresh.Money, Is.EqualTo(600));
        }

        [Test]
        public void FortyTwoFormsHaveUniqueNamesAndEveryBranchImprovesItsRole()
        {
            Assert.That(CreatureEvolution.Forms.Count, Is.EqualTo(42));
            Assert.That(CreatureEvolution.Forms.Select(x => x.Name).Distinct().Count(), Is.EqualTo(42));
            foreach (var kind in GachaCatalog.Creatures.Select(x => x.Kind))
            {
                Assert.That(CreatureEvolution.Forms.Count(x => x.Kind == kind && x.Rank == 3), Is.EqualTo(4));
            }
        }

        [Test]
        public void QueuedEvolutionDoubleClickCannotChooseAnotherUnseenRecipe()
        {
            var run = new RunDataContainer();
            for (int i = 0; i < 6; i++)
            {
                Buy(run, CreatureKind.EmberFox);
            }
            var shop = new ShopDataContainer();
            shop.OpenEvolution();
            var handler = new GachaEventHandler(GachaSceneKind.Shop, run, shop, () => true, _ => { }, _ => 0);
            handler.OnEvent(new GachaActionRequested(GachaAction.EvolveCreature, 0));
            handler.OnEvent(new GachaActionRequested(GachaAction.EvolveCreature, 0));
            Assert.That(run.Owned.Count, Is.EqualTo(4));
            Assert.That(run.Owned.Count(x => x.Rank == 2), Is.EqualTo(1));
            Assert.That(run.EvolutionPending, Is.True);
            Assert.That(shop.Modal, Is.EqualTo(ShopModal.Collection));
        }

        private static void Buy(RunDataContainer run, CreatureKind kind)
        {
            Fund(run, 100);
            Assert.That(run.TryPurchase(GachaMachineKind.Creature, _ => GachaCatalog.GetCreatureRollStart(kind)), Is.True);
        }

        private static void Fund(RunDataContainer run, int price)
        {
            if (run.Money < price)
            {
                Assert.That(run.TryBeginBattle(), Is.True);
                Assert.That(run.CompleteBattle(true), Is.True);
            }
        }
    }
}
