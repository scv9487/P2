using UnityEngine;
using UnityEngine.UI;

namespace Refactor.UI
{
    // 회색 쥐의 UI 전용 벡터 실루엣. 전투 데이터나 자동 갱신을 소유하지 않는다.
    public sealed class RatGraphic : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect rect = GetPixelAdjustedRect();
            Vector2 scale = new(rect.width / 100f, rect.height / 100f);
            Vector2 origin = new(rect.xMin, rect.yMin);
            Color fur = color;
            Color ear = new(fur.r * 0.72f, fur.g * 0.72f, fur.b * 0.75f, fur.a);
            PointLine(mesh, origin, scale, new Vector2(17, 30), new Vector2(8, 24), 3, ear);
            PointLine(mesh, origin, scale, new Vector2(8, 24), new Vector2(3, 37), 3, ear);
            PointLine(mesh, origin, scale, new Vector2(3, 37), new Vector2(9, 45), 2, ear);
            Ellipse(mesh, origin, scale, new Vector2(42, 43), new Vector2(29, 23), fur);
            Ellipse(mesh, origin, scale, new Vector2(69, 47), new Vector2(20, 17), fur);
            Ellipse(mesh, origin, scale, new Vector2(62, 64), new Vector2(10, 11), ear);
            Ellipse(mesh, origin, scale, new Vector2(65, 64), new Vector2(5, 6), new Color(0.45f, 0.39f, 0.39f, fur.a));
            Ellipse(mesh, origin, scale, new Vector2(84, 44), new Vector2(7, 6), fur);
            Ellipse(mesh, origin, scale, new Vector2(76, 53), new Vector2(2, 2), new Color(0.05f, 0.05f, 0.05f, fur.a));
            Ellipse(mesh, origin, scale, new Vector2(91, 44), new Vector2(2.5f, 2), ear);
            PointLine(mesh, origin, scale, new Vector2(84, 43), new Vector2(98, 50), 1, ear);
            PointLine(mesh, origin, scale, new Vector2(84, 43), new Vector2(98, 40), 1, ear);
            Ellipse(mesh, origin, scale, new Vector2(32, 23), new Vector2(10, 4), ear);
            Ellipse(mesh, origin, scale, new Vector2(65, 29), new Vector2(10, 4), ear);
        }

        private static void Ellipse(VertexHelper mesh, Vector2 origin, Vector2 scale, Vector2 center, Vector2 radius, Color tint)
        {
            int start = mesh.currentVertCount;
            mesh.AddVert(origin + Vector2.Scale(center, scale), tint, Vector2.zero);
            const int segments = 24;
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                Vector2 point = center + new Vector2(Mathf.Cos(angle) * radius.x, Mathf.Sin(angle) * radius.y);
                mesh.AddVert(origin + Vector2.Scale(point, scale), tint, Vector2.zero);
            }
            for (int i = 0; i < segments; i++)
            {
                mesh.AddTriangle(start, start + 1 + i, start + 1 + (i + 1) % segments);
            }
        }

        private static void PointLine(VertexHelper mesh, Vector2 origin, Vector2 scale, Vector2 from, Vector2 to, float width, Color tint)
        {
            Vector2 normal = new Vector2(-(to - from).y, (to - from).x).normalized * width * 0.5f;
            int start = mesh.currentVertCount;
            mesh.AddVert(origin + Vector2.Scale(from + normal, scale), tint, Vector2.zero);
            mesh.AddVert(origin + Vector2.Scale(to + normal, scale), tint, Vector2.zero);
            mesh.AddVert(origin + Vector2.Scale(to - normal, scale), tint, Vector2.zero);
            mesh.AddVert(origin + Vector2.Scale(from - normal, scale), tint, Vector2.zero);
            mesh.AddTriangle(start, start + 1, start + 2);
            mesh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
