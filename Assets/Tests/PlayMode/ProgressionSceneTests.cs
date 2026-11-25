using System.Collections;
using System.Linq;
using GachaTrainers;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace P2.Tests
{
    public sealed class ProgressionSceneTests
    {
        private GachaMainFlow _flow;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return SceneManager.LoadSceneAsync(GachaMainFlow.TitleScenePath);
            yield return WaitFor(GachaSceneKind.Title);
            Button("StartRunButton").onClick.Invoke();
            yield return WaitFor(GachaSceneKind.Shop);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            var previous = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(SceneManager.CreateScene("Progression Cleanup"));
            yield return SceneManager.UnloadSceneAsync(previous);
        }

        [UnityTest]
        public IEnumerator CollectionButtonsSelectStoredCreaturesAssignSlotsAndShowFinalStats()
        {
            var run = (RunDataContainer)_flow.Run;
            for (int i = 0; i < 4; i++)
            {
                run.TryPurchase(GachaMachineKind.Creature, _ => GachaCatalog.GetCreatureRollStart((CreatureKind)i));
            }
            Button("OpenCollectionButton").onClick.Invoke();
            yield return null;
            Assert.That(_flow.Shop.Modal, Is.EqualTo(ShopModal.Collection));
            Assert.That(Object.FindObjectsByType<Text>(FindObjectsSortMode.None).Single(x => x.name == "CombatStats").text, Does.Contain("마법 저항"));
            var original = run.Party[0];
            Button("AssignSlot0").onClick.Invoke();
            Assert.That(run.Party[0], Is.SameAs(original), "View must only queue the intent.");
            yield return null;
            Assert.That(run.Party[0].Kind, Is.EqualTo(CreatureKind.FrostOwl));
            Assert.That(run.Owned.Count, Is.EqualTo(4));
            Button("BenchCreatureButton").onClick.Invoke();
            yield return null;
            Assert.That(run.Party.Count, Is.EqualTo(2));
            Button("CloseCollectionButton").onClick.Invoke();
            yield return null;
            Assert.That(_flow.Shop.Modal, Is.EqualTo(ShopModal.None));
        }

        [UnityTest]
        public IEnumerator EvolutionChoiceConsumesThreeAndTheEvolvedFormEntersBattleWithMouseAim()
        {
            var run = (RunDataContainer)_flow.Run;
            for (int i = 0; i < 3; i++)
            {
                run.TryPurchase(GachaMachineKind.Creature, _ => 0);
            }
            Button("OpenCollectionButton").onClick.Invoke();
            yield return null;
            Button("OpenEvolutionButton").onClick.Invoke();
            yield return null;
            Assert.That(_flow.Shop.Modal, Is.EqualTo(ShopModal.Evolution));
            Assert.That(Button("ChooseEvolution0").interactable, Is.True);
            Assert.That(Button("ChooseEvolution1").interactable, Is.True);
            Button("ChooseEvolution1").onClick.Invoke();
            yield return null;
            Assert.That(run.Owned.Single().Name, Is.EqualTo("연무여우"));
            Assert.That(run.Owned.Single().Rank, Is.EqualTo(2));
            Assert.That(run.Owned.Single().Rarity, Is.EqualTo(CrystalRarity.Common));
            Button("CloseCollectionButton").onClick.Invoke();
            yield return null;
            Button("BeginBattleButton").onClick.Invoke();
            yield return WaitFor(GachaSceneKind.Battle);
            var battle = (BattleDataContainer)_flow.Battle;
            Assert.That(battle.Player.Form.Node, Is.EqualTo(2));
            for (int step = 0; step < 12 && battle.Phase == BattlePhase.Summoning; step++)
            {
                battle.Tick(0.1f);
            }
            Assert.That(battle.Phase, Is.EqualTo(BattlePhase.Fighting));
            Vector2 aim = battle.Player.Position + Vector2.up * 3f;
            _flow.SubmitCombat(Vector2.zero, true, aim);
            yield return null;
            Assert.That(battle.Projectiles.Any(x => x.IsPlayer && x.Direction.y > 0.99f), Is.True);
            var marker = Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None).Single(x => x.name == "MouseAimMarker");
            Assert.That(marker.enabled, Is.True);
            Assert.That((Vector2)marker.transform.position, Is.EqualTo(aim));
            Assert.That(Object.FindObjectsByType<EvolutionAdornmentView>(FindObjectsSortMode.None)
                .SelectMany(x => x.GetComponentsInChildren<SpriteRenderer>()).Count(x => x.enabled), Is.EqualTo(3));
        }

        [UnityTest]
        public IEnumerator CrystalPoolHasTwoPagesAndCollectionBlocksShopMovement()
        {
            var shop = (ShopDataContainer)_flow.Shop;
            typeof(ShopDataContainer).GetProperty("Position").SetValue(shop, ShopDataContainer.CrystalInteractionPoint);
            _flow.Submit(GachaAction.Interact);
            yield return null;
            Assert.That(_flow.Shop.Modal, Is.EqualTo(ShopModal.Machine));
            Button("PoolPageButton").onClick.Invoke();
            yield return null;
            Assert.That(_flow.Shop.PoolPage, Is.EqualTo(1));
            var texts = Object.FindObjectsByType<Text>(FindObjectsSortMode.None).Where(x => x.name.StartsWith("PoolName")).ToArray();
            Assert.That(texts.Any(x => x.text.Contains("마격")), Is.True);
            Assert.That(texts.Any(x => x.text.Contains("탄성")), Is.True);
            _flow.Submit(GachaAction.CloseModal);
            yield return null;
            Button("OpenCollectionButton").onClick.Invoke();
            yield return null;
            var before = shop.Position;
            _flow.SubmitMove(Vector2.down, 0.05f);
            yield return null;
            Assert.That(shop.Position, Is.EqualTo(before));
        }

        private static Button Button(string name)
        {
            return Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None).Single(x => x.name == name);
        }

        private IEnumerator WaitFor(GachaSceneKind kind)
        {
            float deadline = Time.realtimeSinceStartup + 10f;
            do
            {
                yield return null;
                _flow = Object.FindFirstObjectByType<GachaMainFlow>();
                if (_flow != null && _flow.IsReady && _flow.SceneKind == kind)
                {
                    _flow.SendMessage("OnApplicationFocus", true);
                    if (_flow.Battle != null && _flow.Battle.Paused)
                    {
                        _flow.Submit(GachaAction.ResumeBattle);
                        yield return null;
                    }
                    yield break;
                }
            }
            while (Time.realtimeSinceStartup < deadline);
            Assert.Fail("Scene did not become ready: " + kind);
        }
    }
}
