using System;
using System.Collections.Generic;
using System.Globalization;

namespace GachaTrainers
{
    public sealed class StageDefinition
    {
        public int Number { get; }
        public float PhysicalAttackMultiplier { get; }
        public float MagicAttackMultiplier { get; }
        public float HealthMultiplier { get; }
        public IReadOnlyList<CreatureKind> Enemies { get; }

        internal StageDefinition(int number, float physical, float magic, float health, IList<CreatureKind> enemies)
        {
            Number = number;
            PhysicalAttackMultiplier = physical;
            MagicAttackMultiplier = magic;
            HealthMultiplier = health;
            Enemies = new List<CreatureKind>(enemies).AsReadOnly();
        }
    }

    // The CSV is the only source of gameplay stage balance. Arrays preserve the summon order.
    public sealed class GachaStageSheet
    {
        public const int StageCount = 10;
        public const string AssetPath = "Assets/1_InternalTools/Util/DataSheetLoader/Table/GachaStageTable.csv";
        private const string TableName = "GachaStageTable.csv";
        private readonly StageDefinition[] _stages = new StageDefinition[StageCount];
        public IReadOnlyList<StageDefinition> Stages { get; }

        public GachaStageSheet(string csv, GachaBalance balance = null)
        {
            if (string.IsNullOrWhiteSpace(csv))
            {
                throw new FormatException(TableName + ": 시트가 비어 있습니다.");
            }
            var rows = global::Util.Sheet.GameSystemTable.ReadRows(csv.TrimStart('\uFEFF'), TableName);
            string[] header = { "Stage", "PhysicalAttackMultiplier", "MagicAttackMultiplier", "HealthMultiplier", "EnemyCreatures" };
            if (rows.Count != StageCount + 1)
            {
                throw new FormatException(TableName + ": 헤더와 1~10 스테이지의 10개 행이 필요합니다.");
            }
            if (rows[0].Length != header.Length)
            {
                throw new FormatException(TableName + ": 헤더는 " + string.Join(",", header) + "이어야 합니다.");
            }
            for (int column = 0; column < header.Length; column++)
            {
                if (rows[0][column] != header[column])
                {
                    throw new FormatException(TableName + ": 헤더는 " + string.Join(",", header) + "이어야 합니다.");
                }
            }
            for (int rowIndex = 1; rowIndex < rows.Count; rowIndex++)
            {
                string[] row = rows[rowIndex];
                if (row.Length != header.Length
                    || !int.TryParse(row[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int number)
                    || number < 1 || number > StageCount)
                {
                    throw new FormatException($"{TableName}: {rowIndex + 1}행의 열 수 또는 Stage(1~10)가 잘못되었습니다.");
                }
                if (_stages[number - 1] != null)
                {
                    throw new FormatException($"{TableName}: Stage {number}가 중복되었습니다.");
                }
                float physical = ReadMultiplier(row[1], number, header[1], false);
                float magic = ReadMultiplier(row[2], number, header[2], false);
                float health = ReadMultiplier(row[3], number, header[3], true);
                string[] entries = row[4].Split('|');
                if (entries.Length > GachaCatalog.MaxPartySize)
                {
                    throw new FormatException($"{TableName}: Stage {number}의 적은 1~3마리여야 합니다.");
                }
                var enemies = new List<CreatureKind>();
                foreach (string entry in entries)
                {
                    string name = entry.Trim();
                    if (!Enum.TryParse(name, out CreatureKind kind) || !Enum.IsDefined(typeof(CreatureKind), kind)
                        || kind.ToString() != name)
                    {
                        throw new FormatException($"{TableName}: Stage {number}의 알 수 없는 크리쳐 '{name}'. 영문 ID와 | 구분자를 확인하세요.");
                    }
                    enemies.Add(kind);
                }
                var stage = new StageDefinition(number, physical, magic, health, enemies);
                // Reject overflow at sheet load, before any run state or battle is changed.
                foreach (var kind in enemies)
                {
                    CombatStats.Build(kind, 0, null, balance).WithStageMultipliers(stage);
                }
                _stages[number - 1] = stage;
            }
            Stages = Array.AsReadOnly(_stages);
        }

        public StageDefinition GetStage(int number)
        {
            if (number < 1 || number > StageCount)
            {
                throw new ArgumentOutOfRangeException(nameof(number), "스테이지는 1~10입니다.");
            }
            return _stages[number - 1];
        }

        private static float ReadMultiplier(string text, int stage, string column, bool positive)
        {
            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
                || float.IsNaN(value) || float.IsInfinity(value) || value < 0f || (positive && value == 0f))
            {
                throw new FormatException($"{TableName}: Stage {stage}의 {column}은 "
                    + (positive ? "0보다 큰" : "0 이상의") + " 유한한 배율이어야 합니다. 소수점은 .을 사용하세요.");
            }
            return value;
        }
    }
}
