using System.Collections;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using GachaTrainers;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace P2.Tests
{
    public sealed class BalanceSceneTests
    {
        private GachaMainFlow _flow;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return SceneManager.LoadSceneAsync(GachaMainFlow.TitleScenePath);
            yield return WaitFor(GachaSceneKind.Title);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            var previous = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(SceneManager.CreateScene("Balance Cleanup"));
            yield return SceneManager.UnloadSceneAsync(previous);
        }

        [UnityTest]
        public IEnumerator EditedSheetValuesAppearOnTitleMachinesPurchasesOddsAndStageRewards()
        {
            InjectParsedSheet(("Economy.StartingGold", "1234"), ("Economy.CreaturePrice", "137"),
                ("Economy.CrystalPrice", "29"), ("Economy.VictoryGold", "17|34|51|68|85|102|119|136|153|170"),
                ("Gacha.CreatureRarityWeights", "100|0|0|0"), ("Gacha.CrystalRarityWeights", "0|0|0|100"));
            yield return null;
            Assert.That(Text("StartingRules").text, Does.Contain("1234 G"));
            Button("StartRunButton").onClick.Invoke();
            yield return WaitFor(GachaSceneKind.Shop);
            Assert.That(_flow.Run.Money, Is.EqualTo(1234));
            Assert.That(Text("CreatureMachinePrice").text, Does.Contain("137 G"));
            Assert.That(Text("CrystalMachinePrice").text, Does.Contain("29 G"));
            yield return OpenMachine(ShopDataContainer.CreatureInteractionPoint);
            Assert.That(Button("BuyButton").GetComponentInChildren<Text>().text, Does.Contain("137 G"));
            Assert.That(ViewText<GachaSceneView>("_modalSubtitle").text, Does.Contain("일반 100%"));
            Button("BuyButton").onClick.Invoke();
            yield return null;
            Assert.That(_flow.Run.Money, Is.EqualTo(1097));
            _flow.Submit(GachaAction.CloseModal);
            yield return null;
            yield return OpenMachine(ShopDataContainer.CrystalInteractionPoint);
            Assert.That(Button("BuyButton").GetComponentInChildren<Text>().text, Does.Contain("29 G"));
            Assert.That(ViewText<GachaSceneView>("_modalSubtitle").text, Does.Contain("전설 100%"));
            Button("BuyButton").onClick.Invoke();
            yield return null;
            Assert.That(_flow.Run.GetCrystalCount(CrystalKind.Capital), Is.EqualTo(1));
            Assert.That(_flow.Run.Money, Is.EqualTo(1068));
            _flow.Submit(GachaAction.CloseModal);
            yield return null;
            var run = _flow.Run;
            for (int stage = 1; stage <= 2; stage++)
            {
                Button("BeginBattleButton").onClick.Invoke();
                yield return WaitFor(GachaSceneKind.Battle);
                SetVictory((BattleDataContainer)_flow.Battle);
                yield return null;
                Assert.That(ViewText<BattleSceneView>("_resultDescription").text, Does.Contain("상금 " + (17 * stage) + " G"));
                Button("FinishBattleButton").onClick.Invoke();
                Button("FinishBattleButton").onClick.Invoke();
                yield return WaitFor(GachaSceneKind.Shop);
                Assert.That(_flow.Run, Is.SameAs(run));
                Assert.That(run.Money, Is.EqualTo(1068 + 17 * stage * (stage + 1) / 2));
            }
        }

        [UnityTest]
        public IEnumerator EvolutionPreviewAndBattleEnergyCostUseTheSameEditedRunSnapshot()
        {
            InjectParsedSheet(("Evolution.RankGrowth", "1|2|4"), ("Evolution.A.PhysicalMultiplier", "1.4"),
                ("Battle.SkillEnergyCost", "13"));
            Button("StartRunButton").onClick.Invoke();
            yield return WaitFor(GachaSceneKind.Shop);
            var run = (RunDataContainer)_flow.Run;
            for (int i = 0; i < 3; i++)
            {
                run.TryPurchase(GachaMachineKind.Creature, _ => 0);
            }
            Button("OpenCollectionButton").onClick.Invoke();
            yield return null;
            Button("OpenEvolutionButton").onClick.Invoke();
            yield return null;
            var view = Object.FindFirstObjectByType<CollectionSceneView>();
            var descriptions = (Text[])typeof(CollectionSceneView).GetField("_formDescriptions", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(view);
            Assert.That(descriptions[0].text, Does.Contain("체력 85 → 170"));
            Assert.That(descriptions[0].text, Does.Contain("50.4"));
            Button("ChooseEvolution0").onClick.Invoke();
            yield return null;
            Button("CloseCollectionButton").onClick.Invoke();
            yield return null;
            Button("BeginBattleButton").onClick.Invoke();
            yield return WaitFor(GachaSceneKind.Battle);
            Assert.That(_flow.Battle.Player.MaxHealth, Is.EqualTo(170));
            Assert.That(_flow.Battle.Player.Stats.PhysicalAttackPower, Is.EqualTo(50.4f).Within(0.001f));
            Assert.That(ViewText<BattleSceneView>("_controlsText").text, Does.Contain("기력 13"));
        }

        private void InjectParsedSheet(params (string key, string value)[] edits)
        {
            var asset = Resources.Load<GachaBalanceAsset>("GachaBalance");
            var source = (TextAsset[])typeof(GachaBalanceAsset).GetField("_tables", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(asset);
            var tables = source.ToDictionary(table => table.name + ".csv", table => table.text);
            foreach (var edit in edits)
            {
                string name = tables.Keys.Single(table => tables[table].Contains(edit.key + ","));
                tables[name] = Regex.Replace(tables[name], "(?m)^" + Regex.Escape(edit.key) + ",[^,\\r\\n]*,", _ => edit.key + "," + edit.value + ",");
            }
            // Inject the parsed content at the scene composition boundary, without editing shared assets on disk.
            typeof(GachaMainFlow).GetField("_balance", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_flow, new GachaBalance(tables));
        }

        private IEnumerator OpenMachine(Vector2 position)
        {
            typeof(ShopDataContainer).GetProperty("Position").SetValue(_flow.Shop, position);
            _flow.Submit(GachaAction.Interact);
            yield return null;
            Assert.That(_flow.Shop.Modal, Is.EqualTo(ShopModal.Machine));
        }

        private static void SetVictory(BattleDataContainer battle)
        {
            battle.SetPaused(false);
            for (int step = 0; step < 12 && battle.Phase == BattlePhase.Summoning; step++)
            {
                battle.Tick(0.1f);
            }
            Assert.That(battle.Phase, Is.EqualTo(BattlePhase.Fighting));
            foreach (var enemy in battle.EnemyTeam)
            {
                typeof(BattleFighter).GetProperty("Health").SetValue(enemy, 0f);
            }
            battle.Tick(0.1f);
            Assert.That(battle.Phase, Is.EqualTo(BattlePhase.Victory));
        }

        private static Text ViewText<T>(string field) where T : Object
        {
            return (Text)typeof(T).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(Object.FindFirstObjectByType<T>());
        }

        private static Text Text(string name)
        {
            return Object.FindObjectsByType<Text>(FindObjectsInactive.Include, FindObjectsSortMode.None).Single(item => item.name == name);
        }

        private static Button Button(string name)
        {
            return Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None).Single(item => item.name == name);
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
            Assert.Fail("Balance scene did not become ready: " + kind);
        }
    }
}
