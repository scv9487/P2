using System;
using UnityEngine;

namespace Refactor.Util
{
    [CreateAssetMenu(menuName = "P2/Skill Content", fileName = "SkillContent")]
    public sealed class SkillContent : ScriptableObject
    {
        public TextAsset Skills;
        public TextAsset Classes;
        public TextAsset CharacterClasses;
        public TextAsset Weapons;
        public TextAsset Settings;
        public TextAsset Texts;

        public SkillSheet Load()
        {
            if (Skills == null || Classes == null || CharacterClasses == null || Weapons == null || Settings == null || Texts == null)
            {
                throw new InvalidOperationException("SkillContent의 여섯 시트 연결을 확인하세요.");
            }
            return new SkillSheet(Skills.text, Classes.text, CharacterClasses.text, Weapons.text, Settings.text, Texts.text);
        }
    }
}
