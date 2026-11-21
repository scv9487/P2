using System;
using UnityEngine;

namespace Refactor.Util
{
    // Unity 에셋 연결만 소유한다. 씬은 이 에셋 하나로 시트와 배경/초상화 의존성을 함께 참조한다.
    [CreateAssetMenu(menuName = "P2/Town Content", fileName = "TownContent")]
    public sealed class TownContent : ScriptableObject
    {
        public TextAsset Locations;
        public TextAsset Commands;
        public TextAsset Characters;
        public TextAsset Texts;
        public Sprite TownBackground;
        public Sprite[] LocationBackgrounds;
        public Sprite[] Portraits;

        public TownSheet Load()
        {
            if (Locations == null || Commands == null || Characters == null || Texts == null)
            {
                throw new InvalidOperationException("TownContent에 장소/명령/인물/문자열 CSV를 연결해야 합니다.");
            }
            var sheet = new TownSheet(Locations.text, Commands.text, Characters.text, Texts.text);
            if (TownBackground == null)
            {
                throw new InvalidOperationException("TownContent에 마을 배경을 연결해야 합니다.");
            }
            foreach (var location in sheet.Locations)
            {
                ValidateSprite(LocationBackgrounds, location.ArtIndex, location.Id);
            }
            foreach (var character in sheet.Characters)
            {
                ValidateSprite(Portraits, character.PortraitIndex, character.Id);
            }
            return sheet;
        }

        private static void ValidateSprite(Sprite[] sprites, int index, string id)
        {
            if (sprites == null || index >= sprites.Length || sprites[index] == null)
            {
                throw new InvalidOperationException($"TownContent: {id}에 해당하는 이미지 {index}를 연결해야 합니다.");
            }
        }
    }
}
