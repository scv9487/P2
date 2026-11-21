using System;
using UnityEngine;

namespace Refactor.Util
{
    [CreateAssetMenu(menuName = "P2/Quest Content", fileName = "QuestContent")]
    public sealed class QuestContent : ScriptableObject
    {
        public TextAsset Quests;
        public TextAsset RescueNodes;
        public TextAsset Texts;

        public QuestSheet Load(DungeonSheet dungeons, LootSheet items)
        {
            if (Quests == null || RescueNodes == null || Texts == null)
            {
                throw new InvalidOperationException("QuestContent의 세 시트 연결을 확인하세요.");
            }
            return new QuestSheet(Quests.text, RescueNodes.text, Texts.text, dungeons, items);
        }
    }
}
