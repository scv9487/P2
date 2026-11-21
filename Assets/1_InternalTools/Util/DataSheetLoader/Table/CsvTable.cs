using System;
using System.Collections.Generic;
using System.Text;

namespace Util.Sheet
{
    // GameSystemTable의 CSV 읽기를 문자열 콘텐츠에도 적용한다. BOM/따옴표/셀 내부 줄바꿈을 보존한다.
    internal static class CsvTable
    {
        public static List<string[]> Read(string csv, string tableName, params string[] columns)
        {
            if (csv == null)
            {
                throw new ArgumentNullException(nameof(csv), tableName + ": CSV가 없습니다.");
            }
            var rows = ReadRows(csv.TrimStart('\uFEFF'), tableName);
            if (rows.Count == 0 || rows[0].Length != columns.Length)
            {
                throw new FormatException(tableName + ": 첫 행은 " + string.Join(",", columns) + "이어야 합니다.");
            }
            for (int i = 0; i < columns.Length; i++)
            {
                if (rows[0][i] != columns[i])
                {
                    throw new FormatException(tableName + ": 첫 행은 " + string.Join(",", columns) + "이어야 합니다.");
                }
            }
            rows.RemoveAt(0);
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].Length != columns.Length)
                {
                    throw new FormatException($"{tableName}: 데이터 {i + 1}행의 열 수가 올바르지 않습니다.");
                }
            }
            return rows;
        }

        private static List<string[]> ReadRows(string csv, string tableName)
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
                        throw new FormatException(tableName + ": 닫히지 않은 따옴표가 있습니다.");
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
                    throw new FormatException(tableName + ": 따옴표가 잘못된 위치에 있습니다.");
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
