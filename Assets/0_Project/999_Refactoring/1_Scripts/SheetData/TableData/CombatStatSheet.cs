using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Refactor.Main;
using Util.Sheet;

namespace Refactor.Util
{
    public sealed class CombatStatDefinition
    {
        public CombatStatType Stat { get; }
        public string Name { get; }
        public float DefaultValue { get; }
        public float NonPlayerDefaultValue { get; }
        public IReadOnlyList<float> Coefficients { get; }
        internal CombatStatDefinition(CombatStatType stat, string name, float player, float other, float[] coefficients)
        {
            Stat = stat;
            Name = name;
            DefaultValue = player;
            NonPlayerDefaultValue = other;
            Coefficients = Array.AsReadOnly(coefficients);
        }
    }

    public sealed class CombatEnemyDefinition
    {
        public string Id { get; }
        public float OnDeathExperience { get; }
        public IReadOnlyDictionary<CombatStatType, float> Values { get; }
        internal CombatEnemyDefinition(string id, float experience, Dictionary<CombatStatType, float> values)
        {
            Id = id;
            OnDeathExperience = experience;
            Values = new ReadOnlyDictionary<CombatStatType, float>(values);
        }
    }

    public sealed class CombatAttackDefinition
    {
        public string Id { get; }
        public float Coefficient { get; }
        public float BaseCycle { get; }
        internal CombatAttackDefinition(string id, float coefficient, float baseCycle)
        {
            Id = id;
            Coefficient = coefficient;
            BaseCycle = baseCycle;
        }
    }

    // DS StatusValueTable / UnitTable의 회색쥐 / SkillTable의 기본 공격에서 이식한 최소 데이터.
    public sealed class CombatStatSheet
    {
        private readonly Dictionary<CombatStatType, CombatStatDefinition> _stats = new();
        private readonly Dictionary<string, CombatEnemyDefinition> _enemies = new(StringComparer.Ordinal);
        private readonly Dictionary<string, CombatAttackDefinition> _attacks = new(StringComparer.Ordinal);

        public CombatStatSheet(string statsCsv, string enemiesCsv, string attacksCsv)
        {
            foreach (var row in CsvTable.Read(statsCsv, "CombatStatTable", "Stat", "Name", "DefaultValue", "NonPlayerDefaultValue",
                "Strength", "Constitution", "WillPower", "Intelligence", "Agility", "Perception"))
            {
                if (!Enum.TryParse(row[0], out CombatStatType type) || !Enum.IsDefined(typeof(CombatStatType), type) || _stats.ContainsKey(type))
                {
                    throw new FormatException("전투 능력치 키가 올바르지 않거나 중복됩니다: " + row[0]);
                }
                float[] coefficients = new float[6];
                for (int i = 0; i < 6; i++)
                {
                    coefficients[i] = Nonnegative(row[i + 4]);
                }
                _stats.Add(type, new CombatStatDefinition(type, Required(row[1]), Number(row[2]), Number(row[3]), coefficients));
            }
            if (_stats.Count != Enum.GetValues(typeof(CombatStatType)).Length)
            {
                throw new FormatException("모든 전투 능력치 정의가 필요합니다.");
            }

            CombatStatType[] overrides =
            {
                CombatStatType.MaxHealth, CombatStatType.PhysicalAttackPower, CombatStatType.MagicAttackPower,
                CombatStatType.AttackSpeed, CombatStatType.CriticalChance, CombatStatType.PhysicalResistance,
                CombatStatType.MagicResistance, CombatStatType.MovementSpeed
            };
            foreach (var row in CsvTable.Read(enemiesCsv, "CombatEnemyTable", "Id", "MaxHealth", "PhysicalAttackPower", "MagicAttackPower",
                "AttackSpeed", "CriticalChance", "PhysicalResistance", "MagicResistance", "MovementSpeed", "OnDeathExperience"))
            {
                string id = Required(row[0]);
                if (_enemies.ContainsKey(id))
                {
                    throw new FormatException("적 능력치 ID 중복: " + id);
                }
                var values = new Dictionary<CombatStatType, float>();
                for (int i = 0; i < overrides.Length; i++)
                {
                    values.Add(overrides[i], Nonnegative(row[i + 1]));
                }
                if (values[CombatStatType.MaxHealth] <= 0f || values[CombatStatType.AttackSpeed] <= 0f)
                {
                    throw new FormatException("적 생명력/공격속도는 양수여야 합니다.");
                }
                _enemies.Add(id, new CombatEnemyDefinition(id, Nonnegative(row[9]), values));
            }
            foreach (var row in CsvTable.Read(attacksCsv, "CombatAttackTable", "Id", "Coefficient", "BaseCycle"))
            {
                string id = Required(row[0]);
                float coefficient = Nonnegative(row[1]);
                float cycle = Nonnegative(row[2]);
                if (_attacks.ContainsKey(id) || cycle <= 0f)
                {
                    throw new FormatException("기본 공격 ID/주기가 올바르지 않습니다.");
                }
                _attacks.Add(id, new CombatAttackDefinition(id, coefficient, cycle));
            }
            GetEnemy("grey_rat");
            GetAttack("player");
            GetAttack("grey_rat");
        }

        public CombatStatDefinition Get(CombatStatType type) => _stats[type];
        public CombatEnemyDefinition GetEnemy(string id) => id != null && _enemies.TryGetValue(id, out var definition)
            ? definition : throw new KeyNotFoundException("적 능력치 정의 누락: " + id);
        public CombatAttackDefinition GetAttack(string id) => id != null && _attacks.TryGetValue(id, out var definition)
            ? definition : throw new KeyNotFoundException("기본 공격 정의 누락: " + id);

        private static float Number(string value) => CharacterGrowthSheet.Number(value);
        private static float Nonnegative(string value)
        {
            float number = Number(value);
            if (number < 0f)
            {
                throw new FormatException("음수 설정값은 사용할 수 없습니다.");
            }
            return number;
        }
        private static string Required(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value != value.Trim())
            {
                throw new FormatException("ID/문구의 빈 값과 양끝 공백은 허용하지 않습니다.");
            }
            return value;
        }
    }
}
