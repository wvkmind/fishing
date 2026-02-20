using Mirror;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace MultiplayerFishing
{
    /// <summary>
    /// Lobby scene UI — handles both pre-auth (Connect/Host/Quit) and
    /// post-auth (player name, map selection, inventory, quit) views.
    /// Only active on clients (not batch mode server).
    /// 需求: 8.1, 8.2, 8.3, 8.4, 8.5
    /// </summary>
    public class LobbyUI : MonoBehaviour
    {
        private Canvas _canvas;
        private Button _connectBtn;
        private Button _quitBtn;
        private TMP_Text _statusText;
        private TMP_Text _connectBtnText;
        private bool _connecting;

        // Authenticated lobby state
        private bool _showingAuthenticatedLobby;
        private GameObject _preAuthPanel;
        private GameObject _authPanel;
        private int _selectedMapIndex;

        // Available maps — currently only one
        private static readonly MapEntry[] AvailableMaps = new MapEntry[]
        {
            new MapEntry { mapName = "Inland Lake", sceneName = "GameScene" }
        };

        private void Start()
        {
            if (Application.isBatchMode)
            {
                enabled = false;
                return;
            }

            var hud = FindAnyObjectByType<NetworkManagerHUD>();
            if (hud != null) hud.enabled = false;

            ShowLobby();
        }

        private void OnEnable()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            bool isLobby = scene.path != null && scene.path.Contains("LobbyScene");
            if (isLobby && _canvas == null && !Application.isBatchMode)
                ShowLobby();
            if (!isLobby && _canvas != null)
                _canvas.gameObject.SetActive(false);
        }

        private void ShowLobby()
        {
            _showingAuthenticatedLobby = false;

            if (_canvas != null)
            {
                _canvas.gameObject.SetActive(true);
                // Show pre-auth panel, hide auth panel
                if (_preAuthPanel != null) _preAuthPanel.SetActive(true);
                if (_authPanel != null) _authPanel.SetActive(false);
                _connectBtn.interactable = true;
                _connectBtnText.text = "CONNECT";
                _statusText.text = "";
                _connecting = false;
            }
            else
            {
                BuildUI();
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void Update()
        {
            // Check if authentication completed and switch to authenticated lobby
            if (!_showingAuthenticatedLobby && NetworkClient.isConnected
                && PlayerAuthenticator.LocalPlayerData != null)
            {
                ShowAuthenticatedLobby();
                return;
            }

            if (_connecting)
            {
                if (NetworkClient.isConnected)
                {
                    _statusText.text = "Connected, loading...";
                    _statusText.color = new Color(0.3f, 0.9f, 0.3f);
                    _connecting = false;
                }
                else if (!NetworkClient.active)
                {
                    _statusText.text = "Connection failed, try again";
                    _statusText.color = new Color(1f, 0.3f, 0.3f);
                    _connectBtn.interactable = true;
                    _connectBtnText.text = "CONNECT";
                    _connecting = false;
                }
            }
        }

        /// <summary>
        /// Switch to authenticated lobby view showing player name, map selection,
        /// inventory button, and quit button.
        /// </summary>
        private void ShowAuthenticatedLobby()
        {
            _showingAuthenticatedLobby = true;
            _connecting = false;
            _selectedMapIndex = 0;

            // Hide pre-auth panel
            if (_preAuthPanel != null)
                _preAuthPanel.SetActive(false);

            // Build authenticated panel if not yet created
            if (_authPanel == null)
                BuildAuthenticatedPanel();
            else
                _authPanel.SetActive(true);

            // Update player name display
            UpdateAuthPanelPlayerName();
        }

        private TMP_Text _playerNameText;
        private TMP_Text _mapSelectionText;

        private void UpdateAuthPanelPlayerName()
        {
            if (_playerNameText != null && PlayerAuthenticator.LocalPlayerData != null)
            {
                _playerNameText.text = $"Welcome, {PlayerAuthenticator.LocalPlayerData.playerName}";
            }
        }

        private void BuildAuthenticatedPanel()
        {
            _authPanel = CreatePanel(_canvas.transform, new Vector2(400f, 320f));

            // Player name at top
            _playerNameText = CreateText(_authPanel.transform, "", 28,
                new Vector2(0f, 110f), new Vector2(360f, 40f));
            _playerNameText.color = new Color(0.3f, 0.9f, 0.3f);

            // "地图选择" label
            var mapLabel = CreateText(_authPanel.transform, "Select Map", 22,
                new Vector2(0f, 50f), new Vector2(360f, 30f));
            mapLabel.color = new Color(0.8f, 0.8f, 0.8f);

            // Map selection display — clicking a map directly enters the game
            BuildMapList(_authPanel.transform, new Vector2(0f, -10f));

            // "退出" button
            TMP_Text _;
            CreateButton(_authPanel.transform, "Quit",
                new Vector2(0f, -100f), new Vector2(280f, 50f),
                new Color(0.6f, 0.2f, 0.2f), OnDisconnectAndQuit, out _);
        }

        private void BuildMapList(Transform parent, Vector2 startPos)
        {
            for (int i = 0; i < AvailableMaps.Length; i++)
            {
                var map = AvailableMaps[i];
                var yOffset = startPos.y - i * 40f;

                // Map entry button (acts as selectable item)
                var go = new GameObject($"MapEntry_{i}");
                go.transform.SetParent(parent, false);
                var rect = go.AddComponent<RectTransform>();
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = new Vector2(startPos.x, yOffset);
                rect.sizeDelta = new Vector2(280f, 36f);

                var img = go.AddComponent<Image>();
                img.color = i == _selectedMapIndex
                    ? new Color(0.2f, 0.4f, 0.6f, 0.9f)
                    : new Color(0.2f, 0.2f, 0.2f, 0.7f);

                var btn = go.AddComponent<Button>();
                int index = i; // capture for closure
                btn.onClick.AddListener(() => OnSelectMap(index));

                var textGo = new GameObject("Text");
                textGo.transform.SetParent(go.transform, false);
                var textRect = textGo.AddComponent<RectTransform>();
                textRect.anchorMin = Vector2.zero;
                textRect.anchorMax = Vector2.one;
                textRect.offsetMin = Vector2.zero;
                textRect.offsetMax = Vector2.zero;
                var tmp = textGo.AddComponent<TextMeshProUGUI>();
                tmp.text = map.mapName;
                tmp.fontSize = 20;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.color = Color.white;

                if (i == 0)
                    _mapSelectionText = tmp;
            }
        }

        private void OnSelectMap(int index)
        {
            _selectedMapIndex = index;
            // 点击地图直接进入游戏
            OnEnterGame();
        }

        /// <summary>
        /// Player clicks "进入游戏" — request server to change to the selected map scene.
        /// For host mode, directly call ServerChangeScene.
        /// For client-only mode, the server controls scene transitions.
        /// </summary>
        private void OnEnterGame()
        {
            if (_selectedMapIndex < 0 || _selectedMapIndex >= AvailableMaps.Length)
                return;

            string sceneName = AvailableMaps[_selectedMapIndex].sceneName;
            var nm = NetworkManager.singleton;
            if (nm == null) return;

            if (NetworkServer.active)
            {
                // Host mode — directly change scene
                nm.ServerChangeScene(sceneName);
            }
            else if (NetworkClient.isConnected)
            {
                // Client-only mode — 发送场景切换请求给服务器
                NetworkClient.Send(new SceneChangeRequestMessage
                {
                    sceneName = sceneName
                });
                Debug.Log($"[LobbyUI] Sent scene change request: {sceneName}");
            }
        }

        /// <summary>
        /// Open the inventory UI panel.
        /// InventoryUI is created in Task 6.4; find or create it dynamically.
        /// </summary>
        private void OnOpenInventory()
        {
            var inventoryUI = FindAnyObjectByType<InventoryUI>();
            if (inventoryUI == null)
            {
                var go = new GameObject("InventoryUI");
                inventoryUI = go.AddComponent<InventoryUI>();
            }

            var playerData = PlayerAuthenticator.LocalPlayerData;
            if (playerData != null)
            {
                inventoryUI.Show(playerData.inventory, null);
            }
        }

        /// <summary>
        /// Disconnect from server and return to pre-auth lobby.
        /// </summary>
        private void OnDisconnectAndQuit()
        {
            var nm = NetworkManager.singleton;
            if (nm != null)
            {
                if (NetworkServer.active && NetworkClient.isConnected)
                    nm.StopHost();
                else if (NetworkClient.isConnected)
                    nm.StopClient();
            }

            PlayerAuthenticator.LocalPlayerData = null;
            _showingAuthenticatedLobby = false;

            if (_authPanel != null)
                _authPanel.SetActive(false);

            ShowLobby();
        }

        private void OnConnect()
        {
            var nm = NetworkManager.singleton;
            if (nm == null) return;

            // Clear onlineScene so Mirror stays on LobbyScene after auth.
            // Scene transition is handled by OnEnterGame.
            nm.onlineScene = "";

            nm.networkAddress = HeadlessAutoStart.ServerAddress;
            HeadlessAutoStart.LogSpawnPrefabs(nm, "Client");
            nm.StartClient();

            _connectBtn.interactable = false;
            _connectBtnText.text = "Connecting...";
            _statusText.text = $"Connecting to {HeadlessAutoStart.ServerAddress}...";
            _statusText.color = Color.white;
            _connecting = true;
        }

        private void OnQuit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void OnHostLocal()
        {
            var nm = NetworkManager.singleton;
            if (nm == null) return;

            // Clear onlineScene so Mirror stays on LobbyScene after auth.
            // Scene transition is handled by OnEnterGame.
            nm.onlineScene = "";

            nm.StartHost();
            _statusText.text = "Starting local host...";
            _statusText.color = new Color(0.3f, 0.9f, 0.3f);
        }

        private void BuildUI()
        {
            // Ensure EventSystem exists
            if (FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                var esGo = new GameObject("EventSystem");
                esGo.AddComponent<UnityEngine.EventSystems.EventSystem>();
                esGo.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            }

            // Canvas
            var canvasGo = new GameObject("LobbyUI_Canvas");
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 50;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            canvasGo.AddComponent<GraphicRaycaster>();

            // Pre-auth panel (Connect / Host / Quit)
            _preAuthPanel = CreatePanel(canvasGo.transform, new Vector2(400f, 360f));

            var title = CreateText(_preAuthPanel.transform, "Fishing Online", 36,
                new Vector2(0f, 90f), new Vector2(360f, 50f));
            title.color = Color.white;

            _statusText = CreateText(_preAuthPanel.transform, "", 18,
                new Vector2(0f, 30f), new Vector2(360f, 30f));
            _statusText.color = new Color(0.7f, 0.7f, 0.7f);

            _connectBtn = CreateButton(_preAuthPanel.transform, "CONNECT",
                new Vector2(0f, -10f), new Vector2(280f, 50f),
                new Color(0.2f, 0.5f, 0.8f), OnConnect, out _connectBtnText);

            TMP_Text _hostText;
            CreateButton(_preAuthPanel.transform, "HOST (LOCAL)",
                new Vector2(0f, -70f), new Vector2(280f, 50f),
                new Color(0.2f, 0.65f, 0.4f), OnHostLocal, out _hostText);

            TMP_Text _;
            _quitBtn = CreateButton(_preAuthPanel.transform, "QUIT",
                new Vector2(0f, -130f), new Vector2(280f, 50f),
                new Color(0.6f, 0.2f, 0.2f), OnQuit, out _);
        }

        private GameObject CreatePanel(Transform parent, Vector2 size)
        {
            var go = new GameObject("Panel");
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            var img = go.AddComponent<Image>();
            img.color = new Color(0.1f, 0.1f, 0.1f, 0.85f);
            return go;
        }

        private TMP_Text CreateText(Transform parent, string text, int fontSize,
            Vector2 pos, Vector2 size)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.alignment = TextAlignmentOptions.Center;
            return tmp;
        }

        private Button CreateButton(Transform parent, string label,
            Vector2 pos, Vector2 size, Color bgColor,
            UnityEngine.Events.UnityAction onClick, out TMP_Text btnText)
        {
            var go = new GameObject("Button");
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = pos;
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
            btnText = textGo.AddComponent<TextMeshProUGUI>();
            btnText.text = label;
            btnText.fontSize = 22;
            btnText.alignment = TextAlignmentOptions.Center;
            btnText.color = Color.white;

            return btn;
        }
    }
}
