using System.Collections;
using System.Linq;
using System.Reflection;
using GachaTrainers;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace P2.Tests
{
    public sealed class CreatureDirectionSceneTests
    {
        private const string TitlePath = "Assets/0_Project/GachaTrainers/Scenes/Title.unity";
        private const string BattlePath = "Assets/0_Project/GachaTrainers/Scenes/Battle.unity";
        private GachaMainFlow _flow;
        private BattleDataContainer _battle;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return SceneManager.LoadSceneAsync(TitlePath, LoadSceneMode.Single);
            yield return null;
            yield return SceneManager.LoadSceneAsync(BattlePath, LoadSceneMode.Single);
            for (int frame = 0; frame < 300; frame++)
            {
                yield return null;
                _flow = Object.FindFirstObjectByType<GachaMainFlow>();
                if (_flow != null && _flow.IsReady && _flow.SceneKind == GachaSceneKind.Battle)
                {
                    _battle = (BattleDataContainer)_flow.Battle;
                    // Render explicit facing fixtures without a live frame changing them underneath the assertions.
                    _flow.enabled = false;
                    _battle.SetPaused(true);
                    yield break;
                }
            }
            Assert.Fail("Battle scene did not become ready.");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Scene active = SceneManager.GetActiveScene();
            if (active.path == TitlePath || active.path == BattlePath)
            {
                Scene empty = SceneManager.CreateScene("Creature Direction Test Cleanup");
                SceneManager.SetActiveScene(empty);
                yield return SceneManager.UnloadSceneAsync(active);
            }
        }

        [UnityTest]
        public IEnumerator BothActorsSelectEverySerializedSpeciesDirectionWithoutMirroring()
        {
            string[] suffixes = { "E", "NE", "N", "NW", "W", "SW", "S", "SE" };
            var actors = Object.FindObjectsByType<BattleActorView>(FindObjectsSortMode.None);
            Assert.That(actors.Length, Is.EqualTo(2));
            foreach (BattleActorView actor in actors)
            {
                CreatureAnimationLibrary library = ReadField<CreatureAnimationLibrary>(actor, "_animations");
                Sprite[] sprites = GachaCatalog.Creatures.SelectMany(creature => Enumerable.Range(0, 8)
                    .Select(direction => library.Get(creature.Kind, (CreatureFacing)direction, CreatureAnimationState.Idle, 0))).ToArray();
                SpriteRenderer renderer = ReadField<SpriteRenderer>(actor, "_creatureSprite");
                Transform body = ReadField<Transform>(actor, "_body");
                Assert.That(sprites.Length, Is.EqualTo(48));
                Assert.That(sprites.All(sprite => sprite != null), Is.True, "Each species needs eight serialized sprites.");
                Assert.That(sprites.Distinct().Count(), Is.EqualTo(48), "Directions must have separate art assets.");

                foreach (var definition in GachaCatalog.Creatures)
                {
                    var run = new RunDataContainer();
                    run.TryPurchase(GachaMachineKind.Creature, _ => GachaCatalog.GetCreatureRollStart(definition.Kind));
                    BattleFighter fighter = new BattleDataContainer(run.Party).Player;
                    SetFixture(fighter, nameof(BattleFighter.AnimationState), CreatureAnimationState.Idle);
                    SetFixture(fighter, nameof(BattleFighter.AnimationTime), 0f);
                    for (int direction = 0; direction < 8; direction++)
                    {
                        float angle = direction * 45f * Mathf.Deg2Rad;
                        Vector2 facing = new(Mathf.Cos(angle), Mathf.Sin(angle));
                        SetFixture(fighter, nameof(BattleFighter.Facing), facing);
                        Vector2 position = fighter.Position;
                        actor.Render(fighter, BattlePhase.Fighting, 0f, 2f);
                        Sprite expected = sprites[(int)fighter.Kind * 8 + direction];
                        Assert.That(expected.name, Is.EqualTo(fighter.Kind + "_Idle_" + suffixes[direction] + "_0"));
                        Assert.That(renderer.sprite, Is.SameAs(expected));
                        Assert.That(renderer.enabled, Is.True);
                        Assert.That(renderer.flipX || renderer.flipY, Is.False);
                        Assert.That(body.localScale.x, Is.GreaterThan(0f), "West art must not also be mirrored by the body transform.");
                        Assert.That(body.localScale.y, Is.GreaterThan(0f));
                        Assert.That(fighter.Facing, Is.EqualTo(facing), "The view must not change simulation state.");
                        Assert.That(fighter.Position, Is.EqualTo(position));
                    }
                }
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator DirectionalSpritesRetainCombatEffectsAndRecoverAfterKnockoutReplacement()
        {
            BattleActorView actor = Object.FindObjectsByType<BattleActorView>(FindObjectsSortMode.None)
                .Single(view => ReadField<bool>(view, "_isPlayer"));
            SpriteRenderer renderer = ReadField<SpriteRenderer>(actor, "_creatureSprite");
            SpriteRenderer shield = ReadField<SpriteRenderer>(actor, "_shield");
            SpriteRenderer ring = ReadField<SpriteRenderer>(actor, "_groundRing");
            Transform body = ReadField<Transform>(actor, "_body");
            BattleFighter fighter = _battle.Player;
            SetFixture(fighter, nameof(BattleFighter.AnimationState), CreatureAnimationState.Idle);
            SetFixture(fighter, nameof(BattleFighter.AnimationTime), 0f);
            SetFixture(fighter, nameof(BattleFighter.Facing), Vector2.up);
            SetFixture(fighter, nameof(BattleFighter.HitFlashTime), 0.16f);
            SetFixture(fighter, nameof(BattleFighter.InvulnerableTime), 0.2f);
            SetFixture(fighter, nameof(BattleFighter.ShieldTime), 1f);
            actor.Render(fighter, BattlePhase.Fighting, 0f, 0f);
            Assert.That(renderer.sprite.name, Is.EqualTo("EmberFox_Idle_N_0"));
            Assert.That(renderer.color.g, Is.LessThan(1f), "Hit tint must remain visible on the full-color sprite.");
            Assert.That(renderer.color.a, Is.LessThan(1f), "Dodge transparency must still be applied.");
            Assert.That(shield.enabled, Is.True);

            SetFixture(fighter, nameof(BattleFighter.HitFlashTime), 0f);
            SetFixture(fighter, nameof(BattleFighter.InvulnerableTime), 0f);
            SetFixture(fighter, nameof(BattleFighter.ShieldTime), 0f);
            actor.Render(fighter, BattlePhase.Summoning, BattleCatalog.SummonDuration * 0.5f, 0f);
            Assert.That(body.localScale.x, Is.InRange(0.1f, 0.9f));
            Assert.That(body.localScale.x, Is.EqualTo(body.localScale.y));
            Assert.That(renderer.color, Is.EqualTo(Color.white));
            Assert.That(shield.enabled, Is.False);

            _battle.SetPaused(false);
            for (int step = 0; step < 10 && _battle.Phase == BattlePhase.Summoning; step++)
            {
                _battle.Tick(0.25f);
            }
            SetFixture(fighter, nameof(BattleFighter.Health), 0f);
            actor.Render(fighter, BattlePhase.Fighting, 0f, 2f);
            Assert.That(renderer.enabled, Is.False);
            Assert.That(ring.enabled, Is.False);
            _battle.Tick(0.05f);
            Assert.That(_battle.Player.Kind, Is.EqualTo(CreatureKind.MossTurtle));
            Assert.That(_battle.Phase, Is.EqualTo(BattlePhase.Summoning));
            actor.Render(_battle.Player, _battle.Phase, _battle.PhaseTime, _battle.ElapsedTime);
            Assert.That(renderer.enabled, Is.True);
            Assert.That(ring.enabled, Is.True);
            Assert.That(renderer.sprite.name, Is.EqualTo("MossTurtle_Idle_E_0"));
            Assert.That(renderer.color, Is.EqualTo(Color.white), "The next species must not inherit the previous actor's hit tint.");
            yield return null;
        }

        private static T ReadField<T>(BattleActorView actor, string name)
        {
            return (T)typeof(BattleActorView).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(actor);
        }

        private static void SetFixture<T>(BattleFighter fighter, string property, T value)
        {
            // Fixture-only changes isolate view behavior; combat rules are covered by BattleDataTests.
            typeof(BattleFighter).GetProperty(property).SetValue(fighter, value);
        }
    }
}
