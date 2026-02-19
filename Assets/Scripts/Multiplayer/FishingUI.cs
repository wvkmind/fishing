using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Mirror;
using FishingGameTool.Example;

namespace MultiplayerFishing
{
    /// <summary>
    /// Self-contained multiplayer fishing UI. Creates all UI elements at runtime.
    /// Reads directly from NetworkFishingController SyncVars.
    /// Replaces SimpleUIManager entirely.
    /// </summary>
    public class FishingUI : MonoBehaviour
    {
        private NetworkFishingController _controller;
        private float _maxCastForce;
        private float _maxLineLoad;
        private float _overLoadDuration;

        // Runtime UI references
        private Canvas _canvas;
        private GameObject _castBarRoot;
        private RectTransform _castBarFill;
        private Image _castBarFillImg;
        private GameObject _lineLoadBarRoot;
        private RectTransform _lineLoadBarFill;
        private Image _lineLoadBarFillImg;
        private Image _lineLoadBarBg;
        private TMP_Text _lootInfoText;

        // ESC Menu
        private GameObject _escMenuRoot;
        private bool _escMenuVisible;

        // Cached references for input blocking
        private CharacterMovement _charMovement;

        // Cast bar colors: green → yellow → red
        private readonly Color _castColorMin = new Color(0.2f, 0.8f, 0.2f);
        private readonly Color _castColorMid = new Color(1f, 0.8f, 0f);
        private readonly Color _castColorMax = new Color(1f, 0.2f, 0.1f);

        // Line load colors: cyan → yellow → red → dark red (overload)
        private readonly Color _loadColorMin = new Color(0.3f, 0.9f, 0.9f);
        private readonly Color _loadColorMid = new Color(1f, 0.8f, 0f);
        private readonly Color _loadColorMax = new Color(1f, 0.2f, 0.1f);
        private readonly Color _loadColorOverload = new Color(0.6f, 0f, 0f);

        private bool _initialized;

        public bool IsMenuOpen => _escMenuVisible;

        public void Initialize(NetworkFishingController controller, float maxCastForce, float maxLineLoad, float overLoadDuration)
        {
            _controller = controller;
            _maxCastForce = maxCastForce;
            _maxLineLoad = maxLineLoad;
            _overLoadDuration = overLoadDuration;
            _charMovement = controller.GetComponent<CharacterMovement>();
            BuildUI();
            _initialized = true;

            // Lock cursor on start
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        public void UpdateUI(bool isCharging, float currentCastForce)
        {
            if (!_initialized) return;

            HandleEscMenu();
            UpdateCastBar(isCharging, currentCastForce);
            UpdateLineLoadBar();
            UpdateLootInfo();
        }

        // ── Cast Bar: uses localScale.x for progress (like original SimpleUIManager) ──
        private void UpdateCastBar(bool isCharging, float currentCastForce)
        {
            bool show = isCharging && _controller.syncState == FishingState.Idle;
            _castBarRoot.SetActive(show);
            if (!show) return;

            float t = Mathf.InverseLerp(0f, _maxCastForce, currentCastForce);
            // Scale X from 0 to 1
            _castBarFill.localScale = new Vector3(t, 1f, 1f);
            _castBarFillImg.color = t < 0.5f
                ? Color.Lerp(_castColorMin, _castColorMid, t * 2f)
                : Color.Lerp(_castColorMid, _castColorMax, (t - 0.5f) * 2f);
        }

        // ── Line Load Bar ──
        private void UpdateLineLoadBar()
        {
            bool show = _controller.syncState == FishingState.Hooked
                     && !string.IsNullOrEmpty(_controller.syncLootName);
            _lineLoadBarRoot.SetActive(show);
            if (!show) return;

            float t = Mathf.InverseLerp(0f, _maxLineLoad, _controller.syncLineLoad);
            _lineLoadBarFill.localScale = new Vector3(t, 1f, 1f);

            if (_controller.syncOverLoad > 0f)
            {
                float ot = Mathf.InverseLerp(0f, _overLoadDuration, _controller.syncOverLoad);
                _lineLoadBarFillImg.color = Color.Lerp(_loadColorMax, _loadColorOverload, ot);
                float pulse = Mathf.PingPong(Time.time * 4f, 1f);
                _lineLoadBarBg.color = Color.Lerp(new Color(0.3f, 0f, 0f, 0.6f), new Color(0.5f, 0f, 0f, 0.8f), pulse);
            }
            else
            {
                _lineLoadBarBg.color = new Color(0f, 0f, 0f, 0.5f);
                _lineLoadBarFillImg.color = t < 0.5f
                    ? Color.Lerp(_loadColorMin, _loadColorMid, t * 2f)
                    : Color.Lerp(_loadColorMid, _loadColorMax, (t - 0.5f) * 2f);
            }
        }

        // ── Loot Info ──
        private void UpdateLootInfo()
        {
            bool show = _controller.syncState == FishingState.Hooked
                     && !string.IsNullOrEmpty(_controller.syncLootName);
            _lootInfoText.gameObject.SetActive(show);
            if (!show) return;

            _lootInfoText.text = $"{_controller.syncLootName} | {(FishingGameTool.Fishing.LootData.LootTier)_controller.syncLootTier} | {_controller.syncLootDescription}";
        }

        // ── ESC Menu ──
        private void HandleEscMenu()
        {
            if (!Input.GetKeyDown(KeyCode.Escape)) return;

            _escMenuVisible = !_escMenuVisible;
            _escMenuRoot.SetActive(_escMenuVisible);
            SetInputBlocked(_escMenuVisible);
        }

        private void SetInputBlocked(bool blocked)
        {
            if (blocked)
            {
                Cursor.lockState = CursorLockMode.Confined;
                Cursor.visible = true;
            }
            else
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            // Block character movement
            if (_charMovement != null)
                _charMovement.enabled = !blocked;

            // Block camera rotation (TPPCamera / FPPCamera are separate components)
            var tpp = _controller.GetComponentInChildren<TPPCamera>(true);
            if (tpp != null) tpp.enabled = !blocked;

            var fpp = _controller.GetComponentInChildren<FPPCameraSystem>(true);
            if (fpp != null) fpp.enabled = !blocked;
        }

        // ══════════════════════════════════════════════════════════════
        //  Build UI
        // ══════════════════════════════════════════════════════════════

        private void BuildUI()
        {
            // Ensure EventSystem exists (required for ESC menu button clicks)
            if (FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                var esGo = new GameObject("EventSystem");
                esGo.AddComponent<UnityEngine.EventSystems.EventSystem>();
                esGo.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            }

            var canvasGo = new GameObject("FishingUI_Canvas");
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 100;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            canvasGo.AddComponent<GraphicRaycaster>();

            // Cast Bar (bottom center)
            _castBarRoot = CreateBar(canvasGo.transform, "CastBar",
                new Vector2(0f, 60f), new Vector2(300f, 22f),
                out _castBarFill, out _castBarFillImg, out _);
            _castBarRoot.SetActive(false);

            // Line Load Bar (bottom center, same spot)
            _lineLoadBarRoot = CreateBar(canvasGo.transform, "LineLoadBar",
                new Vector2(0f, 60f), new Vector2(300f, 22f),
                out _lineLoadBarFill, out _lineLoadBarFillImg, out _lineLoadBarBg);
            _lineLoadBarRoot.SetActive(false);

            // Loot Info Text (above bar)
            _lootInfoText = CreateText(canvasGo.transform, "LootInfo",
                new Vector2(0.5f, 0f), new Vector2(0f, 95f), new Vector2(500f, 30f),
                18, Color.white);
            _lootInfoText.gameObject.SetActive(false);

            // ESC Menu
            BuildEscMenu(canvasGo.transform);
        }

        private void BuildEscMenu(Transform parent)
        {
            _escMenuRoot = new GameObject("EscMenu");
            _escMenuRoot.transform.SetParent(parent, false);
            var rootRect = _escMenuRoot.AddComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;
            var dimImg = _escMenuRoot.AddComponent<Image>();
            dimImg.color = new Color(0f, 0f, 0f, 0.5f);

            var panel = new GameObject("Panel");
            panel.transform.SetParent(_escMenuRoot.transform, false);
            var panelRect = panel.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(300f, 260f);
            var panelImg = panel.AddComponent<Image>();
            panelImg.color = new Color(0.15f, 0.15f, 0.15f, 0.95f);

            var title = CreateText(panel.transform, "Title",
                new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(280f, 40f),
                24, Color.white);
            title.text = "MENU";

            CreateButton(panel.transform, "ResumeBtn", "RESUME",
                new Vector2(0f, 30f), new Vector2(200f, 45f),
                new Color(0.2f, 0.6f, 0.2f), OnResumeClicked);

            CreateButton(panel.transform, "LobbyBtn", "LOBBY",
                new Vector2(0f, -30f), new Vector2(200f, 45f),
                new Color(0.2f, 0.4f, 0.7f), OnLobbyClicked);

            CreateButton(panel.transform, "ExitBtn", "QUIT",
                new Vector2(0f, -90f), new Vector2(200f, 45f),
                new Color(0.7f, 0.2f, 0.2f), OnExitClicked);

            _escMenuRoot.SetActive(false);
        }

        // ── Helpers ──

        /// <summary>
        /// Creates a bar using localScale for progress (same approach as original SimpleUIManager).
        /// The fill is anchored left so scaling X grows it from left to right.
        /// </summary>
        private GameObject CreateBar(Transform parent, string name,
            Vector2 anchoredPos, Vector2 size,
            out RectTransform fillRect, out Image fillImage, out Image bgImage)
        {
            // Background
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            var rect = root.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.anchoredPosition = anchoredPos;
            rect.sizeDelta = size;
            bgImage = root.AddComponent<Image>();
            bgImage.color = new Color(0f, 0f, 0f, 0.5f);

            // Fill — anchored to left edge, pivot at left, so scaleX grows rightward
            var fill = new GameObject("Fill");
            fill.transform.SetParent(root.transform, false);
            fillRect = fill.AddComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = new Vector2(2f, 2f);
            fillRect.offsetMax = new Vector2(-2f, -2f);
            fillRect.pivot = new Vector2(0f, 0.5f); // pivot left so scale grows right
            // Recalculate anchored position after pivot change
            fillRect.anchoredPosition = new Vector2(fillRect.offsetMin.x, 0f);
            fillImage = fill.AddComponent<Image>();
            fillImage.color = Color.white;
            fillRect.localScale = new Vector3(0f, 1f, 1f);

            return root;
        }

        private TMP_Text CreateText(Transform parent, string name,
            Vector2 anchor, Vector2 anchoredPos, Vector2 size,
            int fontSize, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.anchoredPosition = anchoredPos;
            rect.sizeDelta = size;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.fontSize = fontSize;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = color;
            return tmp;
        }

        private void CreateButton(Transform parent, string name, string label,
            Vector2 anchoredPos, Vector2 size, Color bgColor, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPos;
            rect.sizeDelta = size;
            var img = go.AddComponent<Image>();
            img.color = bgColor;
            var btn = go.AddComponent<Button>();
            btn.onClick.AddListener(onClick);

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(go.transform, false);
            var textRect = textGo.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = 20;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
        }

        private void OnResumeClicked()
        {
            _escMenuVisible = false;
            _escMenuRoot.SetActive(false);
            SetInputBlocked(false);
        }

        private void OnLobbyClicked()
        {
            _escMenuVisible = false;
            _escMenuRoot.SetActive(false);
            SetInputBlocked(false);

            // Disconnect from server → Mirror returns to offlineScene (LobbyScene)
            if (NetworkClient.active)
            {
                if (NetworkServer.active)
                    NetworkManager.singleton.StopHost();
                else
                    NetworkManager.singleton.StopClient();
            }
        }

        private void OnExitClicked()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
