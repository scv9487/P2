using System;
using Util.Sheet;

namespace Refactor.Util
{
    // DS/IZ처럼 원본 테이블의 Key를 실행용 값으로 해석한다. 변경은 다음 Play부터 적용한다.
    public sealed class GameSystemSheet
    {
        public int CounterStep { get; }
        public int CounterLimit { get; }
        public int LabelMaxLength { get; }

        public GameSystemSheet(GameSystemTable table)
        {
            if (table == null)
            {
                throw new ArgumentNullException(nameof(table));
            }
            CounterLimit = Integer(table, "CounterLimit", 1, 999999);
            CounterStep = Integer(table, "CounterStep", 1, CounterLimit);
            LabelMaxLength = Integer(table, "LabelMaxLength", 2, 128);
        }

        private static int Integer(GameSystemTable table, string name, int min, int max)
        {
            float value = table.GetValue("GAMESYSTEM_Foundation_" + name);
            if (value < min || value > max || value != Math.Truncate(value))
            {
                throw new FormatException($"GameSystemTable.csv: {name}은 {min}~{max} 범위의 정수여야 합니다.");
            }
            return (int)value;
        }
    }
}
