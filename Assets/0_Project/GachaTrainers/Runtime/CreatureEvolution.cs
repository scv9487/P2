using System;
using System.Collections.Generic;
using UnityEngine;

namespace GachaTrainers
{
    // A binary tree per species: 0, A/B, AA/AB/BA/BB. Rarity belongs to the root species.
    public sealed class CreatureForm
    {
        public CreatureKind Kind { get; }
        public int Node { get; }
        public int Rank => Node == 0 ? 1 : Node < 3 ? 2 : 3;
        public string Name { get; }
        public string Path => Node switch
        {
            0 => "",
            1 => "A",
            2 => "B",
            3 => "AA",
            4 => "AB",
            5 => "BA",
            _ => "BB"
        };
        public string Specialty => Node switch
        {
            0 => "기본 형태",
            1 => "맹공 · 공격력 / 공격 속도",
            2 => "수호 · 체력 / 저항 / 가드",
            3 => "추격 · 치명타 / 흡혈 / 명중",
            4 => "비전 · 마법 피해 / 스킬 재사용 감소",
            5 => "역습 · 가드 / 흡혈 / 물리 피해",
            _ => "성역 · 저항 / 재생 / 받는 피해 감소"
        };
        public Color Tint => Node switch
        {
            0 => Color.white,
            1 => new Color(1f, 0.82f, 0.70f),
            2 => new Color(0.72f, 0.92f, 1f),
            3 => new Color(1f, 0.64f, 0.64f),
            4 => new Color(0.84f, 0.72f, 1f),
            5 => new Color(1f, 0.90f, 0.55f),
            _ => new Color(0.62f, 1f, 0.83f)
        };

        internal CreatureForm(CreatureKind kind, int node, string name)
        {
            Kind = kind;
            Node = node;
            Name = name;
        }
    }

    public static class CreatureEvolution
    {
        private static readonly string[][] Names =
        {
            new[] { "잿불여우", "열화여우", "연무여우", "홍련구미", "태양구미", "월영구미", "안개구미" },
            new[] { "이끼거북", "가시거북", "숲거북", "철극현무", "만개현무", "산악현무", "고목현무" },
            new[] { "번개토끼", "섬광토끼", "뇌운토끼", "뇌격왕토끼", "극광토끼", "폭풍토끼", "운해토끼" },
            new[] { "서리올빼미", "빙창올빼미", "설원올빼미", "빙극왕조", "오로라왕조", "설산왕조", "백야왕조" },
            new[] { "물결수달", "해류수달", "샘물수달", "격류해달", "심해해달", "파도수호달", "생명의해달" },
            new[] { "바위멧돼지", "철엄니", "암벽멧돼지", "금강엄니", "화산엄니", "요새멧돼지", "대지수호수" }
        };

        public static IReadOnlyList<CreatureForm> Forms { get; } = BuildForms();

        public static CreatureForm Get(CreatureKind kind, int node = 0)
        {
            if (!Enum.IsDefined(typeof(CreatureKind), kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }
            if (node < 0 || node > 6)
            {
                throw new ArgumentOutOfRangeException(nameof(node));
            }
            return Forms[(int)kind * 7 + node];
        }

        public static CreatureForm Next(PartyCreature creature, int branch)
        {
            if (creature == null || creature.Rank >= 3 || branch < 0 || branch > 1)
            {
                throw new ArgumentException("진화는 랭크 1~2에서 A 또는 B를 선택합니다.");
            }
            return Get(creature.Kind, creature.FormNode * 2 + 1 + branch);
        }

        private static IReadOnlyList<CreatureForm> BuildForms()
        {
            var forms = new List<CreatureForm>();
            for (int kind = 0; kind < Names.Length; kind++)
            {
                for (int node = 0; node < 7; node++)
                {
                    forms.Add(new CreatureForm((CreatureKind)kind, node, Names[kind][node]));
                }
            }
            return forms.AsReadOnly();
        }
    }
}
