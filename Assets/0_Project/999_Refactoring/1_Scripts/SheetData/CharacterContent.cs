using System;
using UnityEngine;

namespace Refactor.Util
{
    [CreateAssetMenu(menuName = "P2/Character Content", fileName = "CharacterContent")]
    public sealed class CharacterContent : ScriptableObject
    {
        public TextAsset Characters;
        public TextAsset Settings;
        public TextAsset Texts;
        public TextAsset Growth;
        public TextAsset Levels;
        public TextAsset GrowthSettings;
        public TextAsset CombatStats;
        public TextAsset CombatEnemies;
        public TextAsset CombatAttacks;

        public CharacterSheet Load(TownSheet town)
        {
            if (Characters == null || Settings == null || Texts == null || Growth == null || Levels == null
                || GrowthSettings == null || CombatStats == null || CombatEnemies == null || CombatAttacks == null)
            {
                throw new InvalidOperationException("CharacterContent의 인물·성장·전투 시트 연결을 확인하세요.");
            }
            var growth = new CharacterGrowthSheet(Growth.text, Levels.text, GrowthSettings.text);
            var combat = new CombatStatSheet(CombatStats.text, CombatEnemies.text, CombatAttacks.text);
            return new CharacterSheet(Characters.text, Settings.text, Texts.text, town, growth, combat);
        }
    }
}
