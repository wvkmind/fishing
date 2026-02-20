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
        private ItemRegistry _itemRegistry;

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

        // Catch Info UI (shown during Displaying state)
        private GameObject _catchInfoRoot;
        private TMP_Text _catchNameText;
        private TMP_Text _catchDescText;
        private TMP_Text _catchWeightText;
        private TMP_Text _catchRecordText;
        private TMP_Text _catchNewRecordText;

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
            _itemRegistry = controller.ItemRegistry;
            BuildUI();
            _initialized = true;
            Debug.Log("[FishingUI] Initialized successfully");

            // Lock cursor on start
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        public void UpdateUI(bool isCharging, float currentCastForce)
        {
            if (!_initialized) return;

            HandleInspectionInput();
            HandleEscMenu();
            HandleInventoryKey();
            UpdateCastBar(isCharging, currentCastForce);
            UpdateLineLoadBar();
            UpdateLootInfo();
            UpdateCatchInfo();
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
                    // Hidden: don't reveal fish identity until caught — more fun
                    _lootInfoText.gameObject.SetActive(false);
                }

        // ── Catch Info (Displaying state) ──
        private void UpdateCatchInfo()
        {
            // Auto-hide when leaving Displaying state
            if (_catchInfoRoot != null && _catchInfoRoot.activeSelf
                && _controller.syncState != FishingState.Displaying)
            {
                _catchInfoRoot.SetActive(false);
            }
        }

        /// <summary>
        /// 由 NetworkFishingController.TargetShowCatchInfo 调用，展示钓获信息。
        /// </summary>
        public void ShowCatchInfo(string fishName, string description, float weight, float previousRecord, bool isNewRecord)
        {
            if (_catchInfoRoot == null)
                BuildCatchInfoUI();

            _catchNameText.text = fishName;
            _catchDescText.text = description;
            _catchWeightText.text = $"{weight:F2} kg";

            if (isNewRecord)
            {
                _catchRecordText.text = previousRecord > 0f
                    ? $"Previous: {previousRecord:F2} kg"
                    : "";
                _catchNewRecordText.text = "NEW RECORD!";
                _catchNewRecordText.gameObject.SetActive(true);
            }
            else
            {
                _catchRecordText.text = $"Record: {previousRecord:F2} kg";
                _catchNewRecordText.gameObject.SetActive(false);
            }

            _catchInfoRoot.SetActive(true);
        }

        private void BuildCatchInfoUI()
        {
            _catchInfoRoot = new GameObject("CatchInfo");
            _catchInfoRoot.transform.SetParent(_canvas.transform, false);
            var rootRect = _catchInfoRoot.AddComponent<RectTransform>();
            rootRect.anchorMin = new Vector2(0.5f, 0.5f);
            rootRect.anchorMax = new Vector2(0.5f, 0.5f);
            rootRect.anchoredPosition = new Vector2(0f, -200f);
            rootRect.sizeDelta = new Vector2(360f, 180f);

            var bg = _catchInfoRoot.AddComponent<Image>();
            bg.color = new Color(0.05f, 0.05f, 0.1f, 0.85f);

            // Fish name (top)
            _catchNameText = CreateText(_catchInfoRoot.transform, "CatchName",
                new Vector2(0.5f, 0.5f), new Vector2(0f, 60f), new Vector2(340f, 36f),
                26, Color.white);
            _catchNameText.fontStyle = TMPro.FontStyles.Bold;

            // Weight
            _catchWeightText = CreateText(_catchInfoRoot.transform, "CatchWeight",
                new Vector2(0.5f, 0.5f), new Vector2(0f, 25f), new Vector2(340f, 28f),
                22, new Color(0.7f, 0.9f, 1f));

            // Description
            _catchDescText = CreateText(_catchInfoRoot.transform, "CatchDesc",
                new Vector2(0.5f, 0.5f), new Vector2(0f, -10f), new Vector2(340f, 24f),
                16, new Color(0.7f, 0.7f, 0.7f));

            // Record info
            _catchRecordText = CreateText(_catchInfoRoot.transform, "CatchRecord",
                new Vector2(0.5f, 0.5f), new Vector2(0f, -40f), new Vector2(340f, 24f),
                16, new Color(0.6f, 0.6f, 0.6f));

            // NEW RECORD! (flashy)
            _catchNewRecordText = CreateText(_catchInfoRoot.transform, "NewRecord",
                new Vector2(0.5f, 0.5f), new Vector2(0f, -65f), new Vector2(340f, 30f),
                22, new Color(1f, 0.85f, 0.1f));
            _catchNewRecordText.fontStyle = TMPro.FontStyles.Bold;
            _catchNewRecordText.gameObject.SetActive(false);

            _catchInfoRoot.SetActive(false);
        }


        // ── ESC Menu ──
        private InventoryUI _inventoryUI;
        private bool _inventoryVisible;

        private void HandleInspectionInput()
        {
            if (_inventoryUI == null || !_inventoryUI.IsInspecting) return;
            if (!Input.GetKeyDown(KeyCode.E)) return;

            _inventoryUI.ExitInspection();
            _inventoryUI.Hide();
            _inventoryVisible = false;
            SetInputBlocked(false);
        }

        private void HandleInventoryKey()
        {
            if (!Input.GetKeyDown(KeyCode.I)) return;
            if (_escMenuVisible) return; // Don't toggle inventory while ESC menu is open
            if (_inventoryUI != null && _inventoryUI.IsInspecting) return; // Block I key during inspection

            _inventoryVisible = !_inventoryVisible;

            if (_inventoryVisible)
            {
                if (_inventoryUI == null)
                {
                    var go = new GameObject("InventoryUI");
                    _inventoryUI = go.AddComponent<InventoryUI>();
                }
                var playerData = PlayerAuthenticator.LocalPlayerData;
                if (playerData != null)
                {
                    Debug.Log($"[FishingUI] Opening inventory, items count={playerData.inventory?.items?.Count ?? 0}, registryNull={_itemRegistry == null}");
                    _inventoryUI.Show(playerData.inventory, _itemRegistry);
                }
                else
                {
                    Debug.LogWarning("[FishingUI] Cannot open inventory: LocalPlayerData is null");
                }
                SetInputBlocked(true);
            }
            else
            {
                if (_inventoryUI != null)
                    _inventoryUI.Hide();
                SetInputBlocked(false);
            }
        }

        private void HandleEscMenu()
        {
            if (!Input.GetKeyDown(KeyCode.Escape)) return;

            // If inspecting a fish, exit inspection first (consume ESC press)
            if (_inventoryUI != null && _inventoryUI.IsInspecting)
            {
                _inventoryUI.ExitInspection();
                _inventoryVisible = false;
                SetInputBlocked(false);
                return;
            }

            // If inventory is open, close it first instead of toggling ESC menu
            if (_inventoryVisible)
            {
                _inventoryVisible = false;
                if (_inventoryUI != null) _inventoryUI.Hide();
                SetInputBlocked(false);
                return;
            }

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
