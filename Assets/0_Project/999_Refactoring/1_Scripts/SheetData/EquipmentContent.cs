using System;
using UnityEngine;

namespace Refactor.Util
{
    [CreateAssetMenu(menuName = "P2/Equipment Content", fileName = "EquipmentContent")]
    public sealed class EquipmentContent : ScriptableObject
    {
        public TextAsset Equipment;
        public TextAsset Consumables;
        public TextAsset Starters;
        public TextAsset Texts;

        public EquipmentSheet Load(LootSheet items, SkillSheet skills, CharacterSheet characters)
        {
            if (Equipment == null || Consumables == null || Starters == null || Texts == null)
            {
                throw new InvalidOperationException("EquipmentContent의 네 시트 연결을 확인하세요.");
            }
            return new EquipmentSheet(Equipment.text, Consumables.text, Starters.text, Texts.text, items, skills, characters);
        }
    }
}
