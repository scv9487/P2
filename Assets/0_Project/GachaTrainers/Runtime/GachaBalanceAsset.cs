using System;
using System.Linq;
using UnityEngine;

namespace GachaTrainers
{
    public sealed class GachaBalanceAsset : ScriptableObject
    {
        [SerializeField] private TextAsset[] _tables;

        public GachaBalance Load()
        {
            if (_tables == null || _tables.Length == 0 || _tables.Any(table => table == null))
            {
                throw new FormatException("GachaBalance: 밸런스 CSV 연결이 누락되었습니다.");
            }
            return new GachaBalance(_tables.ToDictionary(table => table.name + ".csv", table => table.text));
        }
    }
}
