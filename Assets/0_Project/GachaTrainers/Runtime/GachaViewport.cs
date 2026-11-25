using UnityEngine;

namespace GachaTrainers
{
    // Keep the authored world and Canvas in the same 16:9 viewport at every window size.
    [ExecuteAlways, RequireComponent(typeof(Camera)), DefaultExecutionOrder(-500)]
    public sealed class GachaViewport : MonoBehaviour
    {
        [SerializeField] private Canvas _canvas;
        private Camera _camera;

        public void Configure(Canvas canvas)
        {
            _canvas = canvas;
            Refresh();
        }

        private void OnEnable()
        {
            Refresh();
        }

        private void Update()
        {
            Refresh();
        }

        private void Refresh()
        {
            _camera ??= GetComponent<Camera>();
            float width = Mathf.Max(Screen.width, 1);
            float height = Mathf.Max(Screen.height, 1);
            const float targetAspect = 16f / 9f;
            float screenAspect = width / height;
            var viewport = new Rect(0, 0, 1, 1);
            if (screenAspect > targetAspect)
            {
                viewport.width = targetAspect / screenAspect;
                viewport.x = (1f - viewport.width) * 0.5f;
            }
            else
            {
                viewport.height = screenAspect / targetAspect;
                viewport.y = (1f - viewport.height) * 0.5f;
            }
            _camera.rect = viewport;
            _camera.aspect = targetAspect;
            if (_canvas != null)
            {
                // The built-in CanvasScaler uses display size, not the letterboxed viewport.
                _canvas.scaleFactor = height * viewport.height / 900f;
                _canvas.referencePixelsPerUnit = 100f;
            }
        }
    }
}
