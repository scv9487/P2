using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace GachaTrainers
{
    // Immutable numeric configuration. Runs and fighters keep the snapshot they started with.
    public sealed class GachaBalance
    {
        public const string TableDirectory = "Assets/1_InternalTools/Util/DataSheetLoader/Table/";
        public const string AssetPath = "Assets/0_Project/GachaTrainers/Resources/GachaBalance.asset";
        private static GachaBalance _default;
        private readonly Dictionary<string, IReadOnlyList<float>> _values = new();
        private readonly Dictionary<string, double[]> _inputNumbers = new();
        private readonly HashSet<string> _validated = new();

        public static GachaBalance Default
        {
            get
            {
                if (_default == null)
                {
                    var asset = Resources.Load<GachaBalanceAsset>("GachaBalance");
                    if (asset == null)
                    {
                        throw new FormatException("GachaBalance.asset 연결이 필요합니다. 코드 기본값으로 대체하지 않습니다.");
                    }
                    _default = asset.Load();
                }
                return _default;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetDefault()
        {
            _default = null;
        }

        public int StartingGold => Integer("Economy.StartingGold");
        public int CreaturePrice => Integer("Economy.CreaturePrice");
        public int CrystalPrice => Integer("Economy.CrystalPrice");
        public IReadOnlyList<CreatureDefinition> Creatures { get; }
        public IReadOnlyList<CrystalDefinition> Crystals { get; }
        public IReadOnlyList<BattleDefinition> Moves { get; }
        public IReadOnlyList<int> CreatureWeights { get; }
        public IReadOnlyList<int> CrystalWeights { get; }
        public int CreatureWeightTotal { get; }
        public int CrystalWeightTotal { get; }
        public int LegendaryRollStart => CreatureWeightTotal - CreatureWeights[6];

        public GachaBalance(IReadOnlyDictionary<string, string> csvTables)
        {
            if (csvTables == null || csvTables.Count == 0)
            {
                throw new FormatException("GachaBalance: 밸런스 시트가 비어 있습니다.");
            }
            foreach (var table in csvTables)
            {
                ReadTable(table.Key, table.Value);
            }
            ValidateSchema();
            CreatureWeights = BuildWeights("Gacha.CreatureRarityWeights", new[] { 0, 0, 0, 1, 1, 2, 3 });
            CrystalWeights = BuildWeights("Gacha.CrystalRarityWeights", new[] { 1, 2, 2, 2, 2, 3, 0, 0, 0, 1, 1, 2 });
            CreatureWeightTotal = CreatureWeights.Sum();
            CrystalWeightTotal = CrystalWeights.Sum();
            Creatures = GachaCatalog.CreateCreatures(this);
            Crystals = GachaCatalog.CreateCrystals(this);
            Moves = BattleCatalog.CreateDefinitions(this);
            foreach (CreatureKind kind in Enum.GetValues(typeof(CreatureKind)))
            {
                for (int node = 0; node < 7; node++)
                {
                    CombatStats.Build(kind, node, null, this);
                }
            }
        }

        public float Number(string key)
        {
            return Values(key)[0];
        }

        public int Integer(string key)
        {
            return checked((int)Number(key));
        }

        public IReadOnlyList<float> Values(string key)
        {
            if (!_values.TryGetValue(key, out var values))
            {
                throw new FormatException("GachaBalance: 필수 Key 누락: " + key);
            }
            return values;
        }

        public int VictoryGold(int stage)
        {
            if (stage < 1 || stage > GachaStageSheet.StageCount)
            {
                throw new ArgumentOutOfRangeException(nameof(stage));
            }
            return (int)Values("Economy.VictoryGold")[stage - 1];
        }

        public float Creature(CreatureKind kind, string stat)
        {
            return Number("Creature." + kind + "." + stat);
        }

        public string RaritySummary(bool creature)
        {
            var weights = Values(creature ? "Gacha.CreatureRarityWeights" : "Gacha.CrystalRarityWeights");
            float total = weights.Sum();
            return string.Join(" · ", Enumerable.Range(0, 4).Select(index => GachaCatalog.GetRarityName((CrystalRarity)index)
                + (creature && index == 3 ? " 선택권 " : " ") + (weights[index] * 100f / total).ToString("0.##") + "%"));
        }

        private void ReadTable(string name, string csv)
        {
            if (string.IsNullOrWhiteSpace(csv))
            {
                throw new FormatException(name + ": 시트가 비어 있습니다.");
            }
            var rows = global::Util.Sheet.GameSystemTable.ReadRows(csv.TrimStart('\uFEFF'), name);
            if (rows.Count < 2 || !rows[0].SequenceEqual(new[] { "Key", "Values", "Description" }))
            {
                throw new FormatException(name + ": 헤더는 Key,Values,Description이어야 합니다.");
            }
            for (int i = 1; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row.Length != 3 || string.IsNullOrWhiteSpace(row[0]))
                {
                    throw new FormatException($"{name}: {i + 1}행의 Key 또는 열 수가 잘못되었습니다.");
                }
                string key = row[0].Trim();
                var cells = row[1].Split('|');
                var values = new float[cells.Length];
                var originals = new double[cells.Length];
                for (int j = 0; j < cells.Length; j++)
                {
                    if (!double.TryParse(cells[j], NumberStyles.Float, CultureInfo.InvariantCulture, out originals[j])
                        || !double.IsFinite(originals[j]) || !float.IsFinite((float)originals[j]))
                    {
                        throw new FormatException($"{name}: {key}의 값은 유한한 숫자여야 합니다. 배열은 |로 구분합니다.");
                    }
                    values[j] = (float)originals[j];
                }
                if (!_values.TryAdd(key, Array.AsReadOnly(values)))
                {
                    throw new FormatException(name + ": 중복 Key: " + key);
                }
                _inputNumbers.Add(key, originals);
            }
        }

        private void Require(string key, float minimum = 0f, float maximum = 1000000f, int length = 1, bool integer = false)
        {
            var values = Values(key);
            if (values.Count != length || _inputNumbers[key].Any(value => value < minimum || value > maximum || (integer && value != Math.Truncate(value))))
            {
                throw new FormatException($"GachaBalance: {key}는 {minimum}~{maximum} 범위의 "
                    + (integer ? "정수 " : "숫자 ") + length + "개가 필요합니다.");
            }
            _validated.Add(key);
        }

        private IReadOnlyList<int> BuildWeights(string key, int[] grades)
        {
            var input = Values(key).Select(value => (int)value).ToArray();
            int scale = 1;
            for (int grade = 0; grade < 4; grade++)
            {
                int count = grades.Count(value => value == grade);
                int divisor = count / Gcd(count, input[grade]);
                scale = scale / Gcd(scale, divisor) * divisor;
            }
            var result = grades.Select(grade => checked(input[grade] * scale / grades.Count(value => value == grade))).ToArray();
            if (result.Sum(value => (long)value) > int.MaxValue || result.All(value => value == 0))
            {
                throw new FormatException("GachaBalance: " + key + "의 가중치 합이 올바르지 않습니다.");
            }
            return Array.AsReadOnly(result);
        }

        private static int Gcd(int a, int b)
        {
            while (b != 0)
            {
                (a, b) = (b, a % b);
            }
            return a;
        }

        private void ValidateSchema()
        {
            Require("Economy.StartingGold", 0, 10000000, integer: true);
            Require("Economy.CreaturePrice", 0, 10000000, integer: true);
            Require("Economy.CrystalPrice", 0, 10000000, integer: true);
            Require("Economy.VictoryGold", 0, 10000000, GachaStageSheet.StageCount, true);
            Require("Gacha.CreatureRarityWeights", 0, 1000000, 4, true);
            Require("Gacha.CrystalRarityWeights", 0, 1000000, 4, true);
            foreach (CreatureKind kind in Enum.GetValues(typeof(CreatureKind)))
            {
                string prefix = "Creature." + kind + ".";
                Require(prefix + "Health", 1, 1000000, integer: true);
                foreach (string stat in new[] { "PhysicalAttack", "MagicAttack", "AttackSpeed", "MoveSpeed", "BasicRange", "ProjectileSpeed", "SkillCooldown", "BasicRadius", "SkillDamage", "SkillRadius", "SkillSpeedScale", "SkillRangeScale" })
                {
                    Require(prefix + stat, stat == "AttackSpeed" || stat == "ProjectileSpeed" || stat.EndsWith("Radius") ? 0.001f : 0f, 1000f);
                }
            }
            Require("Creature.EmberFox.SkillAngles", -180, 180, 3);
            Require("Creature.MossTurtle.ShieldSeconds", 0, 3600);
            Require("Creature.MossTurtle.ShieldReduction", 0, 1);
            Require("Creature.TideOtter.HealRatio", 0, 1);
            Require("Creature.StoneBoar.PushDistance", 0, 100);
            foreach (string key in new[] { "Battle.SummonSeconds", "Battle.DodgeCooldown", "Battle.DodgeInvulnerability", "Battle.DashSeconds", "Battle.DashSpeed", "Battle.SkillEnergyCost", "Battle.FighterRadius", "Battle.ProjectileSpawnOffset", "Battle.SlowSeconds", "Battle.BurnSeconds", "Battle.BurnDamage", "Battle.BurnInterval", "Battle.ResistanceScale", "AI.PreferredRangeRatio", "AI.RetreatRangeRatio", "AI.RetreatSpeed", "AI.StrafeFrequency", "AI.StrafeStrength", "AI.SkillRangeRatio", "AI.HealThreshold", "AI.ThreatDistanceSquared", "Shop.MoveSpeed", "Shop.InteractionRange" })
            {
                Require(key, key == "Battle.BurnInterval" || key == "Battle.ResistanceScale" ? 0.001f : 0f, 1000);
            }
            Require("Battle.SlowMoveRatio", 0, 1);
            Require("Battle.GuardReductionCap", 0, 100);
            foreach (string stat in new[] { "CriticalChance", "CriticalDamageBonus", "HitChance", "DodgeChance", "GuardChance", "GuardDamageReduction", "CooldownReduction", "LifeSteal", "DamageDealtBonus", "DamageTakenBonus", "PhysicalDamageDealtBonus", "MagicDamageDealtBonus", "PhysicalResistance", "MagicResistance", "HealthPercentageRegeneration", "MaxEnergy", "EnergyRegeneration" })
            {
                Require("Stats." + stat, stat == "DamageTakenBonus" ? -99 : 0, 10000);
            }
            foreach (string key in new[] { "Crystal.Ember.HitCount", "Crystal.Sink.HitCount" })
            {
                Require(key, 1, 1000, integer: true);
            }
            foreach (string key in new[] { "Crystal.Protection.ShieldRatio", "Crystal.Reflection.Ratio", "Crystal.DeathThroes.MaxBonus", "Crystal.Sink.MaxBonus", "Crystal.Capital.Threshold", "Crystal.MagicSmite.Ratio", "Crystal.Giant.HealthRatio", "Crystal.Divine.HealthRatio", "Crystal.Elasticity.HealRatio", "Crystal.Elasticity.Reduction" })
            {
                Require(key, 0, 1);
            }
            foreach (string key in new[] { "Crystal.Protection.Seconds", "Crystal.Protection.Cooldown", "Crystal.Elasticity.Seconds", "Crystal.Jester.DodgeChance", "Crystal.Flint.CriticalChance", "Crystal.Elasticity.GuardChance" })
            {
                Require(key, 0, 1000);
            }
            Require("Evolution.RankGrowth", 0.01f, 100, 3);
            foreach (string key in new[] { "A.PhysicalMultiplier", "A.MagicMultiplier", "A.AttackSpeedMultiplier", "A.DamageBonus", "B.HealthMultiplier", "B.PhysicalResistance", "B.MagicResistance", "B.GuardChance", "AA.CriticalChance", "AA.CriticalDamageBonus", "AA.LifeSteal", "AA.HitChance", "AA.DodgeChance", "AA.MoveMultiplier", "AB.MagicMultiplier", "AB.MagicDamageBonus", "AB.CooldownReduction", "AB.MaxEnergy", "AB.EnergyRegeneration", "BA.PhysicalDamageBonus", "BA.GuardChance", "BA.GuardReduction", "BA.LifeSteal", "BB.PhysicalResistance", "BB.MagicResistance", "BB.HealthRegeneration" })
            {
                Require("Evolution." + key, key.EndsWith("Multiplier") ? 0.01f : 0, 1000);
            }
            Require("Evolution.BB.DamageTakenBonus", -99, 1000);
            var unknown = _values.Keys.FirstOrDefault(key => !_validated.Contains(key));
            if (unknown != null)
            {
                throw new FormatException("GachaBalance: 사용하지 않는 Key 또는 오타: " + unknown);
            }
        }
    }
}
