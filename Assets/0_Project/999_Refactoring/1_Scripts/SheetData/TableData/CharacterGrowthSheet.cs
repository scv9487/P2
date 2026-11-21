using System;
using System.Collections.Generic;
using System.Globalization;
using Refactor.Main;
using Util.Sheet;

namespace Refactor.Util
{
    public sealed class CharacterGrowthRange
    {
        public int MinIncrease { get; }
        public int MaxIncrease { get; }
        internal CharacterGrowthRange(int min, int max)
        {
            MinIncrease = min;
            MaxIncrease = max;
        }
    }

    public sealed class CharacterGrowthSheet
    {
        private readonly Dictionary<MajorStatType, CharacterGrowthRange> _ranges = new();
        private readonly Dictionary<int, float> _experience = new();
        public int MaxLevel { get; }
        public float GrowthMultiplier { get; }

        public CharacterGrowthSheet(string growthCsv, string levelsCsv, string settingsCsv)
        {
            var settings = new Dictionary<string, float>(StringComparer.Ordinal);
            foreach (var row in CsvTable.Read(settingsCsv, "CharacterGrowthSettingTable", "Key", "Value"))
            {
                if (settings.ContainsKey(row[0]))
                {
                    throw new FormatException("성장 설정 중복: " + row[0]);
                }
                settings.Add(row[0], Number(row[1]));
            }
            if (settings.Count != 2 || !settings.ContainsKey("MaxLevel") || !settings.ContainsKey("GrowthMultiplier"))
            {
                throw new FormatException("성장 설정에는 MaxLevel/GrowthMultiplier가 필요합니다.");
            }
            MaxLevel = (int)settings["MaxLevel"];
            GrowthMultiplier = settings["GrowthMultiplier"];
            if (MaxLevel < 1 || MaxLevel > 10000 || MaxLevel != settings["MaxLevel"] || GrowthMultiplier <= 0f || GrowthMultiplier > 1000f)
            {
                throw new FormatException("성장 설정 범위가 올바르지 않습니다.");
            }
            foreach (var row in CsvTable.Read(growthCsv, "CharacterGrowthTable", "Stat", "MinIncrease", "MaxIncrease"))
            {
                if (!Enum.TryParse(row[0], out MajorStatType type) || !Enum.IsDefined(typeof(MajorStatType), type) || _ranges.ContainsKey(type)
                    || !int.TryParse(row[1], NumberStyles.None, CultureInfo.InvariantCulture, out int min)
                    || !int.TryParse(row[2], NumberStyles.None, CultureInfo.InvariantCulture, out int max)
                    || min < 0 || max < min || max > 100000)
                {
                    throw new FormatException("성장 능력치 범위/중복이 올바르지 않습니다.");
                }
                _ranges.Add(type, new CharacterGrowthRange(min, max));
            }
            if (_ranges.Count != 6)
            {
                throw new FormatException("주요 능력치 여섯 개의 성장 범위가 필요합니다.");
            }
            foreach (var row in CsvTable.Read(levelsCsv, "CharacterLevelTable", "Level", "RequiredExperience"))
            {
                if (!int.TryParse(row[0], NumberStyles.None, CultureInfo.InvariantCulture, out int level)
                    || level < 1 || level > MaxLevel || _experience.ContainsKey(level))
                {
                    throw new FormatException("성장 레벨이 중복되거나 범위를 벗어났습니다.");
                }
                float required = Number(row[1]);
                if (required <= 0f)
                {
                    throw new FormatException("레벨 필요 경험치는 양수여야 합니다.");
                }
                _experience.Add(level, required);
            }
            if (_experience.Count != MaxLevel)
            {
                throw new FormatException("모든 레벨의 필요 경험치가 필요합니다.");
            }
        }

        public CharacterGrowthRange Get(MajorStatType type) => _ranges[type];
        public float RequiredExperience(int level) => _experience.TryGetValue(level, out float value) ? value
            : throw new ArgumentOutOfRangeException(nameof(level));

        internal static float Number(string value)
        {
            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float number)
                || float.IsNaN(number) || float.IsInfinity(number)) throw new FormatException("유한한 수치가 필요합니다: " + value);
            return number;
        }
    }
}
