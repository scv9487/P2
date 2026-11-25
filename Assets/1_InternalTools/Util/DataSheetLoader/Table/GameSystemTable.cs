using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Util.Sheet
{
    // DS의 GameSystemTable 위치를 유지한다. 로컬 CSV 한 장만 읽으며 네트워크나 생성된 사본은 사용하지 않는다.
    public sealed class GameSystemTable
    {
        private readonly Dictionary<string, float> _values = new();

        public GameSystemTable(string csv)
        {
            var rows = ReadRows(csv.TrimStart('\uFEFF'));
            if (rows.Count == 0 || rows[0].Length != 3 || rows[0][0] != "Key"
                || rows[0][1] != "Value" || rows[0][2] != "Description")
            {
                throw new FormatException("GameSystemTable.csv: 첫 행은 Key,Value,Description이어야 합니다.");
            }

            for (int i = 1; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row.Length != 3 || string.IsNullOrWhiteSpace(row[0]))
                {
                    throw new FormatException($"GameSystemTable.csv: {i + 1}번째 행의 열 또는 Key가 잘못되었습니다.");
                }
                string key = row[0].Trim();
                if (!float.TryParse(row[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
                    || float.IsNaN(value) || float.IsInfinity(value))
                {
                    throw new FormatException($"GameSystemTable.csv: {key}의 Value는 유한한 숫자여야 합니다. 소수점은 .을 사용합니다.");
                }
                if (!_values.TryAdd(key, value))
                {
                    throw new FormatException($"GameSystemTable.csv: 중복된 Key {key}");
                }
            }
        }

        public float GetValue(string key)
        {
            if (!_values.TryGetValue(key, out float value))
            {
                throw new FormatException($"GameSystemTable.csv: 필수 Key {key}가 없습니다.");
            }
            return value;
        }

        // 엑셀 CSV의 따옴표, 쉼표, 셀 내부 줄바꿈과 "" 이스케이프를 보존한다.
        internal static List<string[]> ReadRows(string csv, string tableName = "GameSystemTable.csv")
        {
            var rows = new List<string[]>();
            var cells = new List<string>();
            var cell = new StringBuilder();
            bool quoted = false;
            bool closedQuote = false;
            for (int i = 0; i <= csv.Length; i++)
            {
                char c = i == csv.Length ? '\n' : csv[i];
                if (quoted)
                {
                    if (i == csv.Length)
                    {
                        throw new FormatException($"{tableName}: 닫히지 않은 따옴표가 있습니다.");
                    }
                    if (c == '"')
                    {
                        if (i + 1 < csv.Length && csv[i + 1] == '"')
                        {
                            cell.Append('"');
                            i++;
                        }
                        else
                        {
                            quoted = false;
                            closedQuote = true;
                        }
                    }
                    else
                    {
                        cell.Append(c);
                    }
                }
                else if (c == ',' || c == '\r' || c == '\n')
                {
                    cells.Add(cell.ToString());
                    cell.Clear();
                    closedQuote = false;
                    if (c != ',')
                    {
                        if (cells.Count != 1 || !string.IsNullOrWhiteSpace(cells[0]))
                        {
                            rows.Add(cells.ToArray());
                        }
                        cells.Clear();
                        if (c == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n')
                        {
                            i++;
                        }
                    }
                }
                else if (c == '"' && cell.Length == 0 && !closedQuote)
                {
                    quoted = true;
                }
                else if (closedQuote || c == '"')
                {
                    throw new FormatException($"{tableName}: 따옴표가 잘못된 위치에 있습니다.");
                }
                else
                {
                    cell.Append(c);
                }
            }
            return rows;
        }
    }
}
