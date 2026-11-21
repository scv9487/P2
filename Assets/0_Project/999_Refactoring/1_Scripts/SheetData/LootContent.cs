using System;
using UnityEngine;

namespace Refactor.Util
{
    [CreateAssetMenu(menuName = "P2/Loot Content", fileName = "LootContent")]
    public sealed class LootContent : ScriptableObject
    {
        public TextAsset Items;
        public TextAsset Drops;
        public TextAsset Shares;
        public TextAsset Settings;
        public TextAsset Texts;

        public LootSheet Load(DungeonSheet dungeonSheet)
        {
            if (Items == null || Drops == null || Shares == null || Settings == null || Texts == null)
            {
                throw new InvalidOperationException("LootContent의 다섯 CSV 연결을 확인하세요.");
            }
            return new LootSheet(Items.text, Drops.text, Shares.text, Settings.text, Texts.text, dungeonSheet);
        }
    }
}
