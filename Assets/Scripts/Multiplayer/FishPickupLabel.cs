using UnityEngine;
using UnityEngine.UI;

namespace MultiplayerFishing
{
    /// <summary>
    /// Spawns a floating "Press E" label above the fish using a World Space Canvas.
    /// Auto-faces the local camera.
    /// </summary>
    public class FishPickupLabel : MonoBehaviour
    {
        private GameObject _canvasGO;

        private void Start()
        {
            Debug.Log($"[FishPickupLabel] Start() on {gameObject.name} pos={transform.position}");

            // Create World Space Canvas
            _canvasGO = new GameObject("PickupCanvas");
            _canvasGO.layer = 5; // UI layer
            // Parent to fish but use world positioning
            _canvasGO.transform.SetParent(transform, false);
            _canvasGO.transform.localPosition = new Vector3(0f, 2f, 0f);

            var canvas = _canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 100;

            _canvasGO.AddComponent<CanvasScaler>();

            // Set canvas RectTransform size in world units
            var rt = _canvasGO.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(2f, 0.5f);
            // Scale so 1 unit in canvas = small world size
            _canvasGO.transform.localScale = Vector3.one * 0.01f;

            // Create Text child
            var textGO = new GameObject("Text");
            textGO.layer = 5;
            textGO.transform.SetParent(_canvasGO.transform, false);

            var textRT = textGO.AddComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.offsetMin = Vector2.zero;
            textRT.offsetMax = Vector2.zero;

            textGO.AddComponent<CanvasRenderer>();

            var text = textGO.AddComponent<Text>();
            text.text = "Press E";
            text.fontSize = 32;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;

            // Font: try built-in Arial first, then OS font
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
                font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (font == null)
                font = Font.CreateDynamicFontFromOSFont("Arial", 32);
            text.font = font;

            // Add outline for visibility
            var outline = textGO.AddComponent<Outline>();
            outline.effectColor = Color.black;
            outline.effectDistance = new Vector2(2f, 2f);

            Debug.Log($"[FishPickupLabel] Created label font={text.font?.name} worldPos={_canvasGO.transform.position}");
        }

        private void Update()
        {
            if (_canvasGO == null) return;

            var cam = Camera.main;
            if (cam != null)
            {
                _canvasGO.transform.rotation = Quaternion.LookRotation(
                    _canvasGO.transform.position - cam.transform.position);
            }
        }
    }
}
