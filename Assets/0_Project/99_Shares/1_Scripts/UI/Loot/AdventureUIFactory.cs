using UnityEngine;
using UnityEngine.UI;

namespace Refactor.UI
{
    // 정산/보유 내역의 표시 조립만 공유한다. 입력과 원본 데이터는 포함하지 않는다.
    internal static class AdventureUIFactory
    {
        internal static readonly Color Panel = new(0.075f, 0.085f, 0.09f, 1f);
        internal static readonly Color Gold = new(0.79f, 0.65f, 0.40f);
        internal static readonly Color PaleGold = new(0.94f, 0.84f, 0.62f);
        internal static readonly Color White = new(0.92f, 0.89f, 0.82f);
        internal static readonly Color Muted = new(0.69f, 0.68f, 0.63f);
        internal static readonly Color Error = new(1f, 0.57f, 0.45f);

        internal static RectTransform Rect(string name, Transform parent, float x, float y, float width, float height)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        internal static RectTransform Center(string name, Transform parent, float width, float height)
        {
            var rect = Rect(name, parent, 0, 0, width, height);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            return rect;
        }

        internal static RectTransform Stretch(string name, Transform parent)
        {
            var rect = Rect(name, parent, 0, 0, 0, 0);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }

        internal static Image Image(RectTransform rect, Color color, bool raycast = false)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        internal static Text Text(string name, Transform parent, Font font, float x, float y, float width, float height, int size, Color color)
        {
            var text = Rect(name, parent, x, y, width, height).gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.color = color;
            text.supportRichText = false;
            text.raycastTarget = false;
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.lineSpacing = 1.08f;
            return text;
        }

        internal static Button Button(string name, Transform parent, Font font, float x, float y, float width, float height, bool primary = false)
        {
            var rect = Rect(name, parent, x, y, width, height);
            var image = Image(rect, primary ? new Color(0.30f, 0.25f, 0.17f) : new Color(0.12f, 0.13f, 0.135f), true);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.highlightedColor = new Color(1.3f, 1.25f, 1.15f);
            colors.selectedColor = colors.highlightedColor;
            colors.pressedColor = new Color(0.76f, 0.73f, 0.65f);
            colors.disabledColor = new Color(0.43f, 0.43f, 0.43f, 0.7f);
            button.colors = colors;
            var text = Text("Text", rect, font, 8, 0, width - 16, height, 19, primary ? PaleGold : White);
            text.alignment = TextAnchor.MiddleCenter;
            Image(Rect("Line", rect, 0, 0, width, 1), primary ? Gold : new Color(0.35f, 0.32f, 0.26f));
            return button;
        }

        internal static RectTransform ScrollContent(string name, Transform parent, float x, float y, float width, float height)
        {
            var root = Rect(name, parent, x, y, width, height);
            Image(root, new Color(0.04f, 0.05f, 0.055f), true);
            var viewport = Stretch("Viewport", root);
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = Rect("Content", viewport, 0, 0, width, height);
            var scroll = root.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;
            return content;
        }
    }
}
