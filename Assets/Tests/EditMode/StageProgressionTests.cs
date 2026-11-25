using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using GachaTrainers;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace P2.Tests
{
    public sealed class StageProgressionTests
    {
        private static string Csv => AssetDatabase.LoadAssetAtPath<TextAsset>(GachaStageSheet.AssetPath).text;

        [Test]
        public void ShippedSheetDefinesTenExplicitScalesAndTenDifferentOrderedTeams()
        {
            var sheet = new GachaStageSheet(Csv);
            float[] expected = { 0.5f, 0.7f, 0.9f, 1.1f, 1.3f, 1.5f, 1.7f, 1.9f, 2.1f, 2.3f };
            Assert.That(sheet.Stages.Count, Is.EqualTo(10));
            for (int i = 0; i < expected.Length; i++)
            {
                var stage = sheet.GetStage(i + 1);
                Assert.That(stage.Number, Is.EqualTo(i + 1));
                Assert.That(stage.PhysicalAttackMultiplier, Is.EqualTo(expected[i]));
                Assert.That(stage.MagicAttackMultiplier, Is.EqualTo(expected[i]));
                Assert.That(stage.HealthMultiplier, Is.EqualTo(expected[i]));
                Assert.That(stage.Enemies.Count, Is.EqualTo(3));
            }
            Assert.That(sheet.Stages.Select(stage => string.Join("|", stage.Enemies)).Distinct().Count(), Is.EqualTo(10));
            Assert.That(sheet.Stages.SelectMany(stage => stage.Enemies).Distinct().Count(), Is.EqualTo(6));
            Assert.That(sheet.GetStage(1).Enemies, Is.EqualTo(BattleCatalog.TestRoster));
            Assert.That(sheet.GetStage(10).Enemies, Is.EqualTo(new[] { CreatureKind.StoneBoar, CreatureKind.FrostOwl, CreatureKind.EmberFox }));
            Assert.Throws<ArgumentOutOfRangeException>(() => sheet.GetStage(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => sheet.GetStage(11));
        }

        [Test]
        public void SheetAcceptsExcelBomQuotesAndReorderedRowsRegardlessOfSystemCulture()
        {
            var rows = Csv.Trim().Split('\n').Select(row => row.TrimEnd('\r')).ToArray();
            var culture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                string reordered = "\uFEFF" + rows[0] + "\r\n" + string.Join("\r\n", rows.Skip(1).Reverse()
                    .Select(row => string.Join(",", row.Split(',').Select(cell => "\"" + cell + "\""))));
                var sheet = new GachaStageSheet(reordered);
                Assert.That(sheet.GetStage(1).HealthMultiplier, Is.EqualTo(0.5f));
                Assert.That(sheet.GetStage(10).PhysicalAttackMultiplier, Is.EqualTo(2.3f));
            }
            finally
            {
                CultureInfo.CurrentCulture = culture;
            }
        }

        [TestCase("0,1,1,1,EmberFox")]
        [TestCase("11,1,1,1,EmberFox")]
        [TestCase("2,1,1,1,EmberFox")]
        [TestCase("1,-1,1,1,EmberFox")]
        [TestCase("1,1,NaN,1,EmberFox")]
        [TestCase("1,Infinity,1,1,EmberFox")]
        [TestCase("1,1,1,0,EmberFox")]
        [TestCase("1,1,1,1e30,EmberFox")]
        [TestCase("1,1e30,1,1,EmberFox")]
        [TestCase("1,1,1,1,Unknown")]
        [TestCase("1,1,1,1,0")]
        [TestCase("1,1,1,1,")]
        [TestCase("1,1,1,1,EmberFox|")]
        [TestCase("1,1,1,1,EmberFox|MossTurtle|VoltRabbit|TideOtter")]
        [TestCase("1,1,1,1")]
        public void InvalidStageRowsFailBeforeStartingABattle(string row)
        {
            var error = Assert.Throws<FormatException>(() => new GachaStageSheet(WithFirstRow(row)));
            Assert.That(error.Message, Does.Contain("GachaStageTable.csv"));
        }

        [Test]
        public void MissingExtraOrMalformedSheetDataIsRejected()
        {
            Assert.Throws<FormatException>(() => new GachaStageSheet(null));
            Assert.Throws<FormatException>(() => new GachaStageSheet(Csv.Replace("EnemyCreatures", "Enemy")));
            Assert.Throws<FormatException>(() => new GachaStageSheet(string.Join("\n", Csv.Trim().Split('\n').Take(10))));
            Assert.Throws<FormatException>(() => new GachaStageSheet(Csv.Trim() + "\n11,1,1,1,EmberFox"));
            Assert.Throws<FormatException>(() => new GachaStageSheet(WithFirstRow("1,1,1,1,\"EmberFox")));
        }

        [Test]
        public void EditedMultipliersAndDuplicateRosterOnlyChangeTheThreeEnemyStats()
        {
            var sheet = new GachaStageSheet(WithFirstRow("1,0.25,0.75,1.4,FrostOwl|EmberFox|FrostOwl"));
            var run = RunDataContainer.CreateBattleTestRun();
            var battle = new BattleDataContainer(run.Party, sheet.GetStage(1));
            Assert.That(battle.EnemyTeam.Select(enemy => enemy.Kind), Is.EqualTo(sheet.GetStage(1).Enemies));
            foreach (var enemy in battle.EnemyTeam)
            {
                var referenceRun = new RunDataContainer();
                referenceRun.TryPurchase(GachaMachineKind.Creature, _ => GachaCatalog.GetCreatureRollStart(enemy.Kind));
                CombatStats baseline = referenceRun.Party[0].Stats;
                Assert.That(enemy.MaxHealth, Is.EqualTo(Mathf.RoundToInt(baseline.MaxHealth * 1.4f)));
                Assert.That(enemy.Health, Is.EqualTo(enemy.MaxHealth));
                Assert.That(enemy.Stats.PhysicalAttackPower, Is.EqualTo(baseline.PhysicalAttackPower * 0.25f));
                Assert.That(enemy.Stats.MagicAttackPower, Is.EqualTo(baseline.MagicAttackPower * 0.75f));
                foreach (var property in typeof(CombatStats).GetProperties())
                {
                    if (property.Name != "MaxHealth" && property.Name != "PhysicalAttackPower" && property.Name != "MagicAttackPower")
                    {
                        Assert.That(property.GetValue(enemy.Stats), Is.EqualTo(property.GetValue(baseline)), property.Name);
                    }
                }
            }
            for (int i = 0; i < run.Party.Count; i++)
            {
                Assert.That(battle.PlayerTeam[i].Stats, Is.SameAs(run.Party[i].Stats));
                Assert.That(battle.PlayerTeam[i].MaxHealth, Is.EqualTo(GachaCatalog.GetCreature(run.Party[i].Kind).MaxHealth));
            }
        }

        [TestCase(1, CreatureKind.EmberFox, 42)]
        [TestCase(2, CreatureKind.TideOtter, 74)]
        [TestCase(10, CreatureKind.EmberFox, 196)]
        public void HealthRoundingMatchesCreatureStatsAtHalfPointBoundaries(int stageNumber, CreatureKind kind, int expected)
        {
            var stage = new GachaStageSheet(Csv).GetStage(stageNumber);
            var battle = new BattleDataContainer(RunDataContainer.CreateBattleTestRun().Party, stage);
            Assert.That(battle.EnemyTeam.Single(enemy => enemy.Kind == kind).MaxHealth, Is.EqualTo(expected));
        }

        [Test]
        public void EditedPhysicalAndMagicPowerReachActualEnemyAttackProjectiles()
        {
            var stage = new GachaStageSheet(WithFirstRow("1,0.5,0.25,0.5,EmberFox")).GetStage(1);
            var battle = new BattleDataContainer(RunDataContainer.CreateBattleTestRun().Party, stage);
            typeof(BattleDataContainer).GetMethod("Basic", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(battle, new object[] { battle.Enemy, battle.Player, false });
            Assert.That(Damage(battle.Projectiles.Single()), Is.EqualTo(18f * 0.5f));
            typeof(BattleDataContainer).GetMethod("Special", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(battle, new object[] { battle.Enemy, battle.Player, false, Vector2.zero });
            Assert.That(battle.Projectiles.Count, Is.EqualTo(4));
            foreach (var projectile in battle.Projectiles.Skip(1))
            {
                Assert.That(Damage(projectile), Is.EqualTo(18f * 0.25f * 1.15f).Within(0.0001f));
            }
        }

        [TestCase("TideOtter")]
        [TestCase("FrostOwl|TideOtter")]
        public void ShortRosterCanBeClearedWithoutIndexErrors(string enemies)
        {
            var stage = new GachaStageSheet(WithFirstRow("1,0,0,0.001," + enemies)).GetStage(1);
            var battle = new BattleDataContainer(RunDataContainer.CreateBattleTestRun().Party, stage, true, () => 99f);
            Assert.That(battle.EnemyTeam.All(enemy => enemy.MaxHealth == 1), Is.True);
            for (int step = 0; step < 1200 && battle.Phase != BattlePhase.Victory; step++)
            {
                battle.Tick(0.1f);
            }
            Assert.That(battle.Phase, Is.EqualTo(BattlePhase.Victory));
            Assert.That(battle.EnemyTeam.All(enemy => !enemy.Alive), Is.True);
        }

        [Test]
        public void TenVictoriesAdvanceOnceEachThenCloseTheRunWithoutStageEleven()
        {
            var run = RunDataContainer.CreateBattleTestRun();
            int initialMoney = run.Money;
            Assert.That(run.CompleteBattle(true), Is.False);
            for (int stage = 1; stage <= 10; stage++)
            {
                Assert.That(run.StageNumber, Is.EqualTo(stage));
                Assert.That(run.TryBeginBattle(), Is.True);
                Assert.That(run.TryBeginBattle(), Is.False);
                Assert.That(run.CompleteBattle(true), Is.True);
                Assert.That(run.CompleteBattle(true), Is.False);
                Assert.That(run.ClearedStages, Is.EqualTo(stage));
                Assert.That(run.Money, Is.EqualTo(initialMoney + stage * RunDataContainer.VictoryPrize));
                Assert.That(run.RunCompleted, Is.EqualTo(stage == 10));
            }
            Assert.That(run.StageNumber, Is.EqualTo(10));
            Assert.That(run.TryBeginBattle(), Is.False);
            Assert.That(run.CompleteBattle(true), Is.False);
            Assert.That(run.ClearedStages, Is.EqualTo(10));
            Assert.That(new RunDataContainer().StageNumber, Is.EqualTo(1));
            Assert.That(new RunDataContainer().ClearedStages, Is.Zero);
        }

        [Test]
        public void DefeatCannotAdvanceTheStageOrGrantAPrize()
        {
            var run = RunDataContainer.CreateBattleTestRun();
            run.TryBeginBattle();
            run.CompleteBattle(true);
            int money = run.Money;
            run.TryBeginBattle();
            run.CompleteBattle(false);
            Assert.That(run.ClearedStages, Is.EqualTo(1));
            Assert.That(run.StageNumber, Is.EqualTo(2));
            Assert.That(run.RunCompleted, Is.False);
            Assert.That(run.Money, Is.EqualTo(money));
        }

        private static string WithFirstRow(string row)
        {
            string[] rows = Csv.Trim().Split('\n');
            rows[1] = row;
            return string.Join("\n", rows);
        }

        private static float Damage(BattleProjectile projectile)
        {
            return (float)typeof(BattleProjectile).GetProperty("Damage", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(projectile);
        }
    }
}
