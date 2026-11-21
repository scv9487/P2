using System;
using UnityEngine;

namespace Refactor.Util
{
    [CreateAssetMenu(menuName = "P2/Shop Content", fileName = "ShopContent")]
    public sealed class ShopContent : ScriptableObject
    {
        public TextAsset Merchants;
        public TextAsset Stock;
        public TextAsset Texts;

        public ShopSheet Load(LootSheet items, TownSheet town)
        {
            if (Merchants == null || Stock == null || Texts == null)
            {
                throw new InvalidOperationException("ShopContent의 세 CSV 연결을 확인하세요.");
            }
            return new ShopSheet(Merchants.text, Stock.text, Texts.text, items, town);
        }
    }
}
