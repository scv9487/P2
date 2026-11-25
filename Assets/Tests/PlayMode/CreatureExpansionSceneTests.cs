using System.Collections;
using System.Linq;
using System.Reflection;
using GachaTrainers;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace P2.Tests
{
    public sealed class CreatureExpansionSceneTests
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
            Scene previous = SceneManager.GetActiveScene();
            Scene empty = SceneManager.CreateScene("Creature Expansion Cleanup");
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync(previous);
        }

        [UnityTest]
        public IEnumerator CreatureMachineShowsSixDistinctOutcomesInsideTheDialog()
        {
            for (int step = 0; step < 180 && Vector2.Distance(_flow.Shop.Position, ShopDataContainer.CreatureInteractionPoint) > 0.1f; step++)
            {
                Vector2 delta = ShopDataContainer.CreatureInteractionPoint - _flow.Shop.Position;
                _flow.SubmitMove(delta.normalized, Mathf.Min(0.05f, delta.magnitude / ShopDataContainer.MoveSpeed));
                yield return null;
            }
            _flow.Submit(GachaAction.Interact);
            yield return null;
            Assert.That(_flow.Shop.Modal, Is.EqualTo(ShopModal.Machine));
            var labels = Object.FindObjectsByType<Text>(FindObjectsSortMode.None)
                .Where(label => label.name.StartsWith("PoolName")).ToArray();
            Assert.That(labels.Length, Is.EqualTo(6));
            foreach (var creature in GachaCatalog.Creatures)
            {
                Text label = labels.Single(item => item.text.Contains(creature.Name));
                Assert.That(label.text, Does.Contain(GachaCatalog.GetCreatureDropPercent(creature.Kind).ToString("0.##") + "%"));
                RectTransform card = (RectTransform)label.transform.parent;
                RectTransform dialog = (RectTransform)card.parent;
                var corners = new Vector3[4];
                card.GetWorldCorners(corners);
                foreach (Vector3 corner in corners)
                {
                    Assert.That(dialog.rect.Contains(dialog.InverseTransformPoint(corner)), Is.True,
                        creature.Name + " card must fit inside the purchase dialog.");
                }
            }
            Button("BuyButton").onClick.Invoke();
            yield return null;
            Assert.That(_flow.Run.Owned.Count + _flow.Run.LegendaryTokens, Is.EqualTo(1));
            Assert.That(_flow.Run.Money, Is.EqualTo(500));
        }

        [UnityTest]
        public IEnumerator NewPartyEntersBattleAndAllThreeUseTheirOwnSkillAndAnimation()
        {
            CreatureKind[] kinds = { CreatureKind.FrostOwl, CreatureKind.TideOtter, CreatureKind.StoneBoar };
            var run = (RunDataContainer)_flow.Run;
            foreach (CreatureKind kind in kinds)
            {
                Assert.That(run.TryPurchase(GachaMachineKind.Creature, _ => GachaCatalog.GetCreatureRollStart(kind)), Is.True);
            }
            yield return null;
            Button("BeginBattleButton").onClick.Invoke();
            yield return WaitFor(GachaSceneKind.Battle);
            var battle = (BattleDataContainer)_flow.Battle;
            Assert.That(battle.PlayerTeam.Select(fighter => fighter.Kind), Is.EqualTo(kinds));
            Assert.That(battle.EnemyTeam.Select(fighter => fighter.Kind),
                Is.EqualTo(new[] { CreatureKind.EmberFox, CreatureKind.MossTurtle, CreatureKind.VoltRabbit }));
            while (battle.Phase == BattlePhase.Summoning)
            {
                battle.Tick(0.1f);
            }
            SetFixture(battle.Enemy, nameof(BattleFighter.BasicCooldown), 100f);
            SetFixture(battle.Enemy, nameof(BattleFighter.SpecialCooldown), 100f);
            yield return null;
            var actor = Object.FindObjectsByType<BattleActorView>(FindObjectsSortMode.None)
                .Single(item => (bool)typeof(BattleActorView).GetField("_isPlayer", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(item));
            var sprite = (SpriteRenderer)typeof(BattleActorView).GetField("_creatureSprite", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(actor);
            for (int index = 0; index < kinds.Length; index++)
            {
                if (index != battle.ActivePlayerIndex)
                {
                    Button("PlayerSlot" + index).onClick.Invoke();
                    yield return null;
                }
                Assert.That(battle.Player.Kind, Is.EqualTo(kinds[index]));
                Assert.That(sprite.sprite.name, Does.StartWith(kinds[index] + "_"));
                Assert.That(Button("SpecialButton").interactable, Is.True);
                Button("SpecialButton").onClick.Invoke();
                yield return null;
                Assert.That(battle.Player.SpecialCooldown, Is.GreaterThan(0f));
                Assert.That(battle.Projectiles.Any(projectile => projectile.IsPlayer && projectile.Kind == kinds[index]), Is.True);
                Assert.That(sprite.sprite.name, Does.StartWith(kinds[index] + "_Skill_"));
            }
            Button("ControlModeButton").onClick.Invoke();
            yield return null;
            Assert.That(battle.ControlMode, Is.EqualTo(BattleControlMode.Auto));
            Assert.That(Button("PlayerSlot0").interactable, Is.True);
        }

        private static void SetFixture(BattleFighter fighter, string property, object value)
        {
            typeof(BattleFighter).GetProperty(property).SetValue(fighter, value);
        }

        private static Button Button(string name)
        {
            return Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None).Single(button => button.name == name);
        }

        private IEnumerator WaitFor(GachaSceneKind kind)
        {
            float deadline = Time.realtimeSinceStartup + 10f;
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                _flow = Object.FindFirstObjectByType<GachaMainFlow>();
                if (_flow != null && _flow.IsReady && _flow.SceneKind == kind)
                {
                    _flow.SendMessage("OnApplicationFocus", true);
                    if (kind == GachaSceneKind.Battle && _flow.Battle.Paused)
                    {
                        _flow.Submit(GachaAction.ResumeBattle);
                        yield return null;
                    }
                    yield break;
                }
            }
            Assert.Fail("Scene not ready: " + kind);
        }
    }
}
