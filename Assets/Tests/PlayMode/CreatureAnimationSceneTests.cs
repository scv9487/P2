using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using GachaTrainers;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace P2.Tests
{
    public sealed class CreatureAnimationSceneTests
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
                Scene empty = SceneManager.CreateScene("Creature Animation Test Cleanup");
                SceneManager.SetActiveScene(empty);
                yield return SceneManager.UnloadSceneAsync(active);
            }
        }

        [UnityTest]
        public IEnumerator SerializedLibrariesRenderAllSpeciesStatesDirectionsAndFourFrames()
        {
            string[] directions = { "E", "NE", "N", "NW", "W", "SW", "S", "SE" };
            var actors = Object.FindObjectsByType<BattleActorView>(FindObjectsSortMode.None);
            Assert.That(actors.Length, Is.EqualTo(2));
            foreach (BattleActorView actor in actors)
            {
                CreatureAnimationLibrary library = ReadField<CreatureAnimationLibrary>(actor, "_animations");
                SpriteRenderer renderer = ReadField<SpriteRenderer>(actor, "_creatureSprite");
                Assert.That(library, Is.Not.Null);
                Assert.That(library.Count, Is.EqualTo(1152));
                var distinctSprites = new HashSet<Sprite>();
                foreach (var definition in GachaCatalog.Creatures)
                {
                    var run = new RunDataContainer();
                    run.TryPurchase(GachaMachineKind.Creature, _ => GachaCatalog.GetCreatureRollStart(definition.Kind));
                    BattleFighter fighter = new BattleDataContainer(run.Party).Player;
                    foreach (CreatureAnimationState state in Enum.GetValues(typeof(CreatureAnimationState)))
                    {
                        SetFixture(fighter, nameof(BattleFighter.AnimationState), state);
                        for (int direction = 0; direction < 8; direction++)
                        {
                            float radians = direction * 45f * Mathf.Deg2Rad;
                            SetFixture(fighter, nameof(BattleFighter.Facing), new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)));
                            for (int frame = 0; frame < 4; frame++)
                            {
                                float time = CreatureAnimationCatalog.Duration(state) * (frame + 0.25f) / 4f;
                                SetFixture(fighter, nameof(BattleFighter.AnimationTime), time);
                                Sprite expected = library.Get(fighter.Kind, (CreatureFacing)direction, state, frame);
                                Assert.That(expected, Is.Not.Null);
                                Assert.That(expected.name, Is.EqualTo(fighter.Kind + "_" + state + "_" + directions[direction] + "_" + frame));
                                distinctSprites.Add(expected);
                                actor.Render(fighter, BattlePhase.Fighting, 0f, 5f);
                                Assert.That(renderer.sprite, Is.SameAs(expected));
                                Assert.That(fighter.AnimationTime, Is.EqualTo(time), "Rendering must not advance the simulation clock.");
                            }
                        }
                    }
                }
                Assert.That(distinctSprites.Count, Is.EqualTo(1152));
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator SpriteFrameFollowsTheCombatClockAndFreezesWhilePaused()
        {
            BattleActorView actor = Object.FindFirstObjectByType<BattleActorView>();
            SpriteRenderer renderer = ReadField<SpriteRenderer>(actor, "_creatureSprite");
            _battle.SetPaused(false);
            while (_battle.Phase == BattlePhase.Summoning)
            {
                _battle.Tick(1f / 120f);
            }
            Assert.That(_battle.TrySpecial(), Is.True);
            Advance(0.15f);
            actor.Render(_battle.Player, _battle.Phase, _battle.PhaseTime, _battle.ElapsedTime);
            Sprite beforePause = renderer.sprite;
            float clock = _battle.Player.AnimationTime;
            _battle.SetPaused(true);
            Advance(0.3f);
            actor.Render(_battle.Player, _battle.Phase, _battle.PhaseTime, _battle.ElapsedTime + 10f);
            Assert.That(_battle.Player.AnimationTime, Is.EqualTo(clock));
            Assert.That(renderer.sprite, Is.SameAs(beforePause), "Rendering time must not animate a paused creature.");
            _battle.SetPaused(false);
            Advance(0.15f);
            actor.Render(_battle.Player, _battle.Phase, _battle.PhaseTime, _battle.ElapsedTime);
            Assert.That(_battle.Player.AnimationState, Is.EqualTo(CreatureAnimationState.Skill));
            Assert.That(renderer.sprite, Is.Not.SameAs(beforePause), "Resuming the model must advance to the next skill frame.");
            yield return null;
        }

        private void Advance(float seconds)
        {
            for (int step = 0; step < Mathf.RoundToInt(seconds * 120f); step++)
            {
                _battle.Tick(1f / 120f);
            }
        }

        private static T ReadField<T>(BattleActorView actor, string field)
        {
            return (T)typeof(BattleActorView).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(actor);
        }

        private static void SetFixture<T>(BattleFighter fighter, string property, T value)
        {
            typeof(BattleFighter).GetProperty(property).SetValue(fighter, value);
        }
    }
}
