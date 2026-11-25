using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using GachaTrainers;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace P2.Tests
{
    public sealed class BalanceSheetTests
    {
        [Test]
        public void ShippedSheetsRetainCurrentEconomyAndCombatDefaults()
        {
            var balance = new GachaBalance(Tables());
            Assert.That(balance.StartingGold, Is.EqualTo(600));
            Assert.That(balance.CreaturePrice, Is.EqualTo(100));
            Assert.That(balance.CrystalPrice, Is.EqualTo(60));
            Assert.That(Enumerable.Range(1, 10).Select(balance.VictoryGold), Is.All.EqualTo(200));
            Assert.That(balance.CreatureWeightTotal, Is.EqualTo(600));
            Assert.That(balance.CrystalWeightTotal, Is.EqualTo(1200));
            Assert.That(balance.Number("Battle.SkillEnergyCost"), Is.EqualTo(25));
            Assert.That(balance.RaritySummary(true), Does.Contain("일반 50%"));
        }

        [Test]
        public void EditedStartingGoldPricesAndTenDifferentRewardsDriveActualSettlement()
        {
            var balance = Edited(("Economy.StartingGold", "1234"), ("Economy.CreaturePrice", "137"),
                ("Economy.CrystalPrice", "29"), ("Economy.VictoryGold", "17|34|51|68|85|102|119|136|153|170"));
            var run = new RunDataContainer(balance);
            Assert.That(run.Money, Is.EqualTo(1234));
            Assert.That(run.TryPurchase(GachaMachineKind.Creature, _ => 0), Is.True);
            Assert.That(run.TryPurchase(GachaMachineKind.Crystal, _ => 0), Is.True);
            Assert.That(run.Money, Is.EqualTo(1068));
            for (int stage = 1; stage <= 10; stage++)
            {
                Assert.That(run.CurrentVictoryGold, Is.EqualTo(17 * stage));
                Assert.That(run.TryBeginBattle(), Is.True);
                Assert.That(run.CompleteBattle(true), Is.True);
                Assert.That(run.CompleteBattle(true), Is.False);
                Assert.That(run.Money, Is.EqualTo(1068 + 17 * stage * (stage + 1) / 2));
            }
            Assert.That(run.TryBeginBattle(), Is.False);
            Assert.That(new RunDataContainer(balance).Money, Is.EqualTo(1234));
            Assert.That(GachaBalance.Default.StartingGold, Is.EqualTo(600), "Custom runs cannot overwrite the default configuration.");
        }

        [Test]
        public void FreePricesZeroRewardsAndInsufficientFundsKeepPurchaseRules()
        {
            var free = new RunDataContainer(Edited(("Economy.StartingGold", "0"), ("Economy.CreaturePrice", "0"),
                ("Economy.CrystalPrice", "0"), ("Economy.VictoryGold", "0|0|0|0|0|0|0|0|0|0")));
            Assert.That(free.TryPurchase(GachaMachineKind.Creature, _ => 0), Is.True);
            Assert.That(free.TryPurchase(GachaMachineKind.Crystal, _ => 0), Is.True);
            free.TryBeginBattle();
            free.CompleteBattle(true);
            Assert.That(free.Money, Is.Zero);
            var poor = new RunDataContainer(Edited(("Economy.StartingGold", "10"), ("Economy.CreaturePrice", "11")));
            Assert.That(poor.TryPurchase(GachaMachineKind.Creature, _ => throw new Exception("Must not roll")), Is.False);
            Assert.That(poor.Money, Is.EqualTo(10));
            Assert.That(poor.Owned, Is.Empty);
        }

        [Test]
        public void WeightArraysAcceptPercentageRatiosWithoutRoundingAwayAnEntireGrade()
        {
            var balance = Edited(("Gacha.CreatureRarityWeights", "50|30|15|5"), ("Gacha.CrystalRarityWeights", "0|0|0|100"));
            var totals = new int[4];
            for (int roll = 0; roll < balance.CreatureWeightTotal; roll++)
            {
                var creature = GachaCatalog.GetCreatureForRoll(roll, balance);
                totals[creature == null ? 3 : (int)creature.Rarity]++;
            }
            Assert.That(totals.Select(count => count * 100f / balance.CreatureWeightTotal), Is.EqualTo(new[] { 50f, 30f, 15f, 5f }));
            for (int roll = 0; roll < balance.CrystalWeightTotal; roll++)
            {
                Assert.That(GachaCatalog.GetCrystalForRoll(roll, balance).Kind, Is.EqualTo(CrystalKind.Capital));
            }
            Assert.That(GachaCatalog.GetCrystalDropPercent(CrystalKind.Capital, balance), Is.EqualTo(100));
            Assert.That(balance.RaritySummary(false), Does.Contain("전설 100%"));
        }

        [Test]
        public void CreatureSkillEnergyAndDodgeEditsReachActualCombatAndDescriptions()
        {
            var balance = Edited(("Creature.EmberFox.Health", "123"), ("Creature.EmberFox.PhysicalAttack", "22.5"),
                ("Creature.EmberFox.MagicAttack", "35"), ("Creature.EmberFox.SkillDamage", "1.7"),
                ("Creature.EmberFox.SkillCooldown", "11"), ("Battle.SkillEnergyCost", "40"),
                ("Battle.DodgeCooldown", "7"), ("Battle.DodgeInvulnerability", "0.4"));
            var battle = Ready(balance);
            Assert.That(battle.Player.MaxHealth, Is.EqualTo(123));
            Assert.That(battle.Enemy.MaxHealth, Is.EqualTo(123));
            Assert.That(battle.TryBasicAttack(), Is.True);
            Assert.That(Damage(battle.Projectiles.Single()), Is.EqualTo(22.5f));
            Assert.That(battle.TrySpecial(), Is.True);
            Assert.That(battle.Player.SpecialCooldown, Is.EqualTo(11));
            Assert.That(battle.Player.Energy, Is.EqualTo(60));
            Assert.That(battle.Projectiles.Skip(1).Select(Damage), Is.All.EqualTo(59.5f).Within(0.001f));
            Assert.That(battle.TryDodge(), Is.True);
            Assert.That(battle.Player.DodgeCooldown, Is.EqualTo(7));
            Assert.That(battle.Player.InvulnerableTime, Is.EqualTo(0.4f));
            Assert.That(BattleCatalog.Get(CreatureKind.EmberFox, balance).SpecialDescription, Does.Contain("170%"));
            Assert.That(battle.Player.Stats.Describe(), Does.Contain("스킬 비용 40"));
        }

        [Test]
        public void CrystalHitCountBurnDamageAndTimingUseEditedNumbers()
        {
            var balance = Edited(("Crystal.Ember.HitCount", "2"), ("Battle.BurnDamage", "7"),
                ("Battle.BurnSeconds", "3"), ("Battle.BurnInterval", "0.5"));
            var battle = Ready(balance, true);
            for (int i = 0; i < 2; i++)
            {
                Set(battle.Player, "BasicCooldown", 0f);
                Assert.That(battle.TryBasicAttack(), Is.True);
                var shot = battle.Projectiles.Last(projectile => projectile.IsPlayer);
                typeof(BattleDataContainer).GetMethod("ApplyDirectHit", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(battle, new object[] { shot, battle.Enemy });
            }
            Assert.That(battle.Enemy.BurnTime, Is.EqualTo(3f));
            float health = battle.Enemy.Health;
            for (int i = 0; i < 6; i++)
            {
                typeof(BattleFighter).GetMethod("TickBurn", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(battle.Enemy, new object[] { 0.5f, true });
            }
            Assert.That(health - battle.Enemy.Health, Is.EqualTo(42f));
            Assert.That(battle.Player.PassiveStatus, Does.Contain("/2"));
            Assert.That(GachaCatalog.GetCrystal(CrystalKind.Ember, balance).Description, Does.Contain("0.5초마다 7"));
        }

        [Test]
        public void EvolutionAndEquipmentPreserveTheRunsBalanceSnapshot()
        {
            var balance = Edited(("Evolution.RankGrowth", "1|2|4"), ("Evolution.A.PhysicalMultiplier", "1.4"),
                ("Evolution.A.MagicMultiplier", "1.25"), ("Crystal.Flint.CriticalChance", "23"));
            var run = new RunDataContainer(balance);
            for (int i = 0; i < 3; i++)
            {
                run.TryPurchase(GachaMachineKind.Creature, _ => 0);
            }
            run.TryPurchase(GachaMachineKind.Crystal, _ => GachaCatalog.GetCrystalRollStart(CrystalKind.Flint, balance));
            run.TryEquipCrystal(CrystalKind.Flint);
            Assert.That(run.TryEvolve(0), Is.True);
            var creature = run.Party.Single();
            Assert.That(creature.Balance, Is.SameAs(balance));
            Assert.That(creature.MaxHealth, Is.EqualTo(170));
            Assert.That(creature.Stats.PhysicalAttackPower, Is.EqualTo(50.4f).Within(0.001f));
            Assert.That(creature.Stats.MagicAttackPower, Is.EqualTo(45));
            Assert.That(creature.Stats.CriticalChance, Is.EqualTo(23));
            run.UnequipSelectedCrystal();
            Assert.That(run.Party.Single().Balance, Is.SameAs(balance));
            Assert.That(run.Party.Single().Stats.CriticalChance, Is.Zero);
        }

        [TestCase("Economy.StartingGold", "-1")]
        [TestCase("Economy.CreaturePrice", "1.5")]
        [TestCase("Economy.CrystalPrice", "9999999.1")]
        [TestCase("Economy.VictoryGold", "1|2|3")]
        [TestCase("Gacha.CreatureRarityWeights", "0|0|0|0")]
        [TestCase("Gacha.CrystalRarityWeights", "1|2|3|-1")]
        [TestCase("Battle.BurnInterval", "0")]
        [TestCase("Creature.EmberFox.Health", "NaN")]
        [TestCase("Creature.EmberFox.AttackSpeed", "0")]
        [TestCase("Crystal.Ember.HitCount", "2.5")]
        [TestCase("Crystal.Capital.Threshold", "1.1")]
        [TestCase("Evolution.RankGrowth", "1|2")]
        public void InvalidBalanceIsRejectedBeforeARunCanUseIt(string key, string value)
        {
            Assert.Throws<FormatException>(() => Edited((key, value)));
        }

        [Test]
        public void MissingDuplicateAndUnknownKeysAreErrorsAndInputCannotMutateSnapshots()
        {
            var tables = Tables();
            var good = new GachaBalance(tables);
            string name = "GachaEconomyTable.csv";
            tables[name] = tables[name].Replace("Economy.StartingGold,600", "Economy.Unknown,900");
            Assert.Throws<FormatException>(() => new GachaBalance(tables));
            Assert.That(good.StartingGold, Is.EqualTo(600));
            tables = Tables();
            tables[name] += "\nEconomy.StartingGold,600,duplicate\n";
            Assert.Throws<FormatException>(() => new GachaBalance(tables));
            tables = Tables();
            tables[name] += "\nUnknown,1,unused\n";
            Assert.Throws<FormatException>(() => new GachaBalance(tables));
        }

        private static Dictionary<string, string> Tables()
        {
            var asset = AssetDatabase.LoadAssetAtPath<GachaBalanceAsset>(GachaBalance.AssetPath);
            var tables = (TextAsset[])typeof(GachaBalanceAsset).GetField("_tables", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(asset);
            return tables.ToDictionary(table => table.name + ".csv", table => table.text);
        }

        private static GachaBalance Edited(params (string key, string value)[] edits)
        {
            var tables = Tables();
            foreach (var edit in edits)
            {
                string table = tables.Keys.Single(name => tables[name].Contains(edit.key + ","));
                tables[table] = Regex.Replace(tables[table], "(?m)^" + Regex.Escape(edit.key) + ",[^,\\r\\n]*,",
                    _ => edit.key + "," + edit.value + ",");
            }
            return new GachaBalance(tables);
        }

        private static BattleDataContainer Ready(GachaBalance balance, bool ember = false)
        {
            var run = new RunDataContainer(balance);
            run.TryPurchase(GachaMachineKind.Creature, _ => 0);
            if (ember)
            {
                run.TryPurchase(GachaMachineKind.Crystal, _ => 0);
                run.TryEquipCrystal(CrystalKind.Ember);
            }
            var battle = new BattleDataContainer(run.Party, false, () => 99f);
            for (int i = 0; i < 4; i++)
            {
                battle.Tick(0.25f);
            }
            Set(battle.Enemy, "BasicCooldown", 100f);
            Set(battle.Enemy, "SpecialCooldown", 100f);
            return battle;
        }

        private static float Damage(BattleProjectile projectile)
        {
            return (float)typeof(BattleProjectile).GetProperty("Damage", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(projectile);
        }

        private static void Set(object target, string property, object value)
        {
            target.GetType().GetProperty(property).SetValue(target, value);
        }
    }
}
