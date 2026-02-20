using Mirror;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

namespace MultiplayerFishing
{
    /// <summary>
    /// 客户端 UI 状态机。
    /// 挂在 NetworkManager GameObject 上（DontDestroyOnLoad）。
    /// 用一个 enum 管理所有状态，杜绝 bool 标记。
    /// </summary>
    public class LobbyUI : MonoBehaviour
    {
        // ══════════════════════════════════════════════════════════════
        //  状态定义
        // ══════════════════════════════════════════════════════════════

        public enum ClientState
        {
            /// <summary>未连接，显示连接按钮</summary>
            Offline,
            /// <summary>正在连接服务器</summary>
            Connecting,
            /// <summary>已认证，显示地图选择</summary>
            Lobby,
            /// <summary>正在加载/卸载地图场景</summary>
            Loading,
            /// <summary>在游戏场景中</summary>
            InGame,
        }

        public ClientState State { get; private set; } = ClientState.Offline;

        /// <summary>
        /// 供外部（FishingUI 等）判断玩家是否在游戏中。
        /// </summary>
        public static bool HasPlayerEnteredGame => _instance != null && _instance.State == ClientState.InGame;

        private static LobbyUI _instance;

        // ══════════════════════════════════════════════════════════════
        //  UI 引用
        // ══════════════════════════════════════════════════════════════

        private Canvas _canvas;
        private GameObject _preAuthPanel;
        private GameObject _authPanel;
        private GameObject _loadingPanel;
        private Button _connectBtn;
        private TMP_Text _connectBtnText;
        private TMP_Text _statusText;
        private TMP_Text _loadingText;
        private Image _loadingBarFill;
        private TMP_Text _playerNameText;
        private TMP_Text _mapSelectionText;
        private int _selectedMapIndex;
        private bool _clientHandlersRegistered;

        /// <summary>当前客户端加载的地图场景名（null = 在大厅）</summary>
        private string _currentMapScene;

        private static readonly MapEntry[] AvailableMaps = new MapEntry[]
        {
            new MapEntry { mapName = "Inland Lake", sceneName = "GameScene" }
        };

        // ══════════════════════════════════════════════════════════════
        //  状态切换
        // ══════════════════════════════════════════════════════════════

        private void SetState(ClientState newState)
        {
            if (State == newState) return;
            Debug.Log($"[LobbyUI] {State} → {newState}");
            State = newState;
            ApplyState();
        }

        /// <summary>
        /// 根据当前状态，决定哪些 UI 面板可见。
        /// 单一入口，不散落在各处。
        /// </summary>
        private void ApplyState()
        {
            if (_canvas == null) return;

            bool showCanvas = State != ClientState.InGame;
            _canvas.gameObject.SetActive(showCanvas);

            if (_preAuthPanel != null)
                _preAuthPanel.SetActive(State == ClientState.Offline || State == ClientState.Connecting);
            if (_authPanel != null)
                _authPanel.SetActive(State == ClientState.Lobby);
            if (_loadingPanel != null)
                _loadingPanel.SetActive(State == ClientState.Loading);

            // 光标
            bool freeCursor = State != ClientState.InGame;
            Cursor.lockState = freeCursor ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = freeCursor;
        }

        // ══════════════════════════════════════════════════════════════
        //  生命周期
        // ══════════════════════════════════════════════════════════════

        private void Awake()
        {
            _instance = this;
        }

        private void Start()
        {
            if (Application.isBatchMode) { enabled = false; return; }

            var hud = FindAnyObjectByType<NetworkManagerHUD>();
            if (hud != null) hud.enabled = false;

            BuildUI();
            SetState(ClientState.Offline);
        }

        private void Update()
        {
            switch (State)
            {
                case ClientState.Connecting:
                    if (NetworkClient.isConnected && PlayerAuthenticator.LocalPlayerData != null)
                    {
                        OnAuthenticated();
                    }
                    else if (!NetworkClient.active)
                    {
                        _statusText.text = "Connection failed, try again";
                        _statusText.color = new Color(1f, 0.3f, 0.3f);
                        _connectBtn.interactable = true;
                        _connectBtnText.text = "CONNECT";
                        SetState(ClientState.Offline);
                    }
                    break;

                case ClientState.Offline:
                    // 可能是 Host 模式，连接后直接跳到认证
                    if (NetworkClient.isConnected && PlayerAuthenticator.LocalPlayerData != null)
                        OnAuthenticated();
                    break;
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  状态转换逻辑
        // ══════════════════════════════════════════════════════════════

        private void OnAuthenticated()
        {
            if (!_clientHandlersRegistered)
            {
                NetworkClient.RegisterHandler<AdditiveSceneManager.LoadSceneMessage>(OnServerTellLoadScene);
                NetworkClient.RegisterHandler<AdditiveSceneManager.UnloadSceneMessage>(OnServerTellUnloadScene);
                _clientHandlersRegistered = true;
            }

            _selectedMapIndex = 0;
            if (_authPanel == null) BuildAuthenticatedPanel();
            UpdateAuthPanelPlayerName();
            SetState(ClientState.Lobby);
        }

        // ── 服务器消息处理 ──────────────────────────────────────────

        private void OnServerTellLoadScene(AdditiveSceneManager.LoadSceneMessage msg)
        {
            // 已经在这个场景了，忽略
            if (State == ClientState.InGame && _currentMapScene == msg.sceneName) return;
            // 正在切换中，忽略
            if (State == ClientState.Loading) return;

            Debug.Log($"[LobbyUI] Server says load scene '{msg.sceneName}'");
            StartCoroutine(CoLoadMap(msg.sceneName));
        }

        private void OnServerTellUnloadScene(AdditiveSceneManager.UnloadSceneMessage msg)
        {
            if (State == ClientState.Loading) return;

            Debug.Log($"[LobbyUI] Server says unload scene '{msg.sceneName}'");
            StartCoroutine(CoUnloadMap(msg.sceneName));
        }

        // ── 加载地图 ────────────────────────────────────────────────

        private System.Collections.IEnumerator CoLoadMap(string sceneName)
        {
            SetState(ClientState.Loading);
            ShowLoading("Loading map...");

            // 1. 加载场景
            var existing = SceneManager.GetSceneByName(sceneName);
            if (!existing.IsValid() || !existing.isLoaded)
            {
                var op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
                if (op == null)
                {
                    Debug.LogError($"[LobbyUI] Failed to load scene '{sceneName}'");
                    SetState(ClientState.Lobby);
                    yield break;
                }
                while (!op.isDone)
                {
                    UpdateLoading(Mathf.Clamp01(op.progress / 0.9f) * 0.7f,
                        $"Loading map... {(int)(op.progress / 0.9f * 70)}%");
                    yield return null;
                }
            }

            // 2. 卸载旧地图
            UpdateLoading(0.7f, "Preparing scene...");
            if (!string.IsNullOrEmpty(_currentMapScene) && _currentMapScene != sceneName)
            {
                var oldScene = SceneManager.GetSceneByName(_currentMapScene);
                if (oldScene.IsValid() && oldScene.isLoaded)
                {
                    var unOp = SceneManager.UnloadSceneAsync(oldScene);
                    if (unOp != null) while (!unOp.isDone) yield return null;
                }
            }
            _currentMapScene = sceneName;

            // 3. 切换 active scene，处理 EventSystem
            var targetScene = SceneManager.GetSceneByName(sceneName);
            if (targetScene.IsValid())
            {
                SceneManager.SetActiveScene(targetScene);

                // 客户端把本地玩家移到目标场景（服务器已经移了，客户端也要同步）
                var localPlayer = NetworkClient.localPlayer;
                if (localPlayer != null && localPlayer.gameObject.scene != targetScene)
                {
                    SceneManager.MoveGameObjectToScene(localPlayer.gameObject, targetScene);
                    Debug.Log($"[LobbyUI] Moved local player to '{sceneName}'");
                }

                // 将玩家传送到场景的出生点
                if (localPlayer != null)
                {
                    var spawnPos = FindSpawnPosition(targetScene);
                    // 先禁用 CharacterController，否则无法直接设置 position
                    var cc = localPlayer.GetComponent<CharacterController>();
                    bool ccWasEnabled = cc != null && cc.enabled;
                    if (cc != null) cc.enabled = false;

                    localPlayer.transform.position = spawnPos;
                    Debug.Log($"[LobbyUI] Teleported player to spawn {spawnPos}");

                    if (cc != null) cc.enabled = ccWasEnabled;
                }
            }

            SetLobbySceneEventSystem(false);
            HideLobbySceneVisuals(true);

            // 禁用 GameScene 自带的 EventSystem（避免重复）
            DisableExtraEventSystems(targetScene);

            // 4. 等几帧让 shader 编译
            UpdateLoading(0.85f, "Compiling shaders...");
            for (int i = 0; i < 5; i++) yield return null;

            // 5. 进入游戏 — 先显式关掉 Loading 面板再切状态
            if (_loadingPanel != null) _loadingPanel.SetActive(false);
            Debug.Log($"[LobbyUI] Canvas null? {_canvas == null}, LoadingPanel null? {_loadingPanel == null}");
            SetState(ClientState.InGame);
            Debug.Log($"[LobbyUI] After SetState(InGame): canvas active={_canvas?.gameObject.activeSelf}, loading active={_loadingPanel?.activeSelf}");

            var enterPlayer = NetworkClient.localPlayer;
            if (enterPlayer != null)
            {
                var nfc = enterPlayer.GetComponent<NetworkFishingController>();
                if (nfc != null) nfc.EnterGameMode();
            }
        }

        // ── 卸载地图（回大厅）────────────────────────────────────────

        private System.Collections.IEnumerator CoUnloadMap(string sceneName)
        {
            SetState(ClientState.Loading);
            ShowLoading("Returning to lobby...");

            // 先退出游戏模式
            var localPlayer = NetworkClient.localPlayer;
            if (localPlayer != null)
            {
                var nfc = localPlayer.GetComponent<NetworkFishingController>();
                if (nfc != null) nfc.ExitGameMode();

                // 把玩家移回 LobbyScene（在卸载地图之前）
                var lobbyScene = SceneManager.GetSceneByName("LobbyScene");
                if (lobbyScene.IsValid() && localPlayer.gameObject.scene.name != "LobbyScene")
                {
                    SceneManager.MoveGameObjectToScene(localPlayer.gameObject, lobbyScene);
                    Debug.Log("[LobbyUI] Moved local player back to LobbyScene");
                }
            }

            UpdateLoading(0.3f, "Unloading map...");
            var scene = SceneManager.GetSceneByName(sceneName);
            if (scene.IsValid() && scene.isLoaded)
            {
                var op = SceneManager.UnloadSceneAsync(scene);
                if (op != null)
                {
                    while (!op.isDone)
                    {
                        UpdateLoading(0.3f + Mathf.Clamp01(op.progress / 0.9f) * 0.5f, "Unloading map...");
                        yield return null;
                    }
                }
            }
            _currentMapScene = null;

            // 恢复大厅
            var returnLobbyScene = SceneManager.GetSceneByName("LobbyScene");
            if (returnLobbyScene.IsValid())
                SceneManager.SetActiveScene(returnLobbyScene);

            HideLobbySceneVisuals(false);
            SetLobbySceneEventSystem(true);

            yield return null;

            SetState(ClientState.Lobby);
        }

        // ══════════════════════════════════════════════════════════════
        //  玩家操作
        // ══════════════════════════════════════════════════════════════

        private void OnSelectMap(int index)
        {
            if (State != ClientState.Lobby) return;
            _selectedMapIndex = index;

            if (NetworkServer.active)
            {
                var asm = AdditiveSceneManager.Instance;
                if (asm == null)
                {
                    var nm = NetworkManager.singleton;
                    if (nm != null)
                    {
                        asm = nm.GetComponent<AdditiveSceneManager>();
                        if (asm == null)
                            asm = nm.gameObject.AddComponent<AdditiveSceneManager>();
                        asm.ServerSetup();
                    }
                }
            }

            string sceneName = AvailableMaps[_selectedMapIndex].sceneName;
            NetworkClient.Send(new AdditiveSceneManager.EnterSceneMessage { sceneName = sceneName });
            Debug.Log($"[LobbyUI] Sent EnterSceneMessage: {sceneName}");
        }

        /// <summary>
        /// 从游戏回到大厅（由 FishingUI ESC 菜单调用）。不断开连接。
        /// </summary>
        public void ReturnToLobby()
        {
            if (!NetworkClient.isConnected) { DisconnectToLobby(); return; }
            Debug.Log("[LobbyUI] ReturnToLobby — staying connected");
            NetworkClient.Send(new AdditiveSceneManager.LeaveSceneMessage());
        }

        /// <summary>真正断开连接（退出按钮用）</summary>
        public void DisconnectToLobby()
        {
            _currentMapScene = null;
            _clientHandlersRegistered = false;

            var nm = NetworkManager.singleton;
            if (nm != null)
            {
                if (NetworkServer.active && NetworkClient.isConnected) nm.StopHost();
                else if (NetworkClient.isConnected) nm.StopClient();
            }

            PlayerAuthenticator.LocalPlayerData = null;
            StartCoroutine(CoDelayedOffline());
        }

        private System.Collections.IEnumerator CoDelayedOffline()
        {
            yield return null;
            yield return null;
            var active = SceneManager.GetActiveScene();
            if (!active.name.Contains("LobbyScene"))
            {
                SceneManager.LoadScene("LobbyScene");
                yield return null;
            }
            if (_authPanel != null) _authPanel.SetActive(false);
            SetState(ClientState.Offline);
            _connectBtn.interactable = true;
            _connectBtnText.text = "CONNECT";
            _statusText.text = "";
        }

        // ══════════════════════════════════════════════════════════════
        //  连接 / Host / 退出
        // ══════════════════════════════════════════════════════════════

        private void OnConnect()
        {
            if (State != ClientState.Offline) return;
            var nm = NetworkManager.singleton;
            if (nm == null) return;

            nm.onlineScene = "";
            nm.networkAddress = HeadlessAutoStart.ServerAddress;
            HeadlessAutoStart.LogSpawnPrefabs(nm, "Client");
            nm.StartClient();

            _connectBtn.interactable = false;
            _connectBtnText.text = "Connecting...";
            _statusText.text = $"Connecting to {HeadlessAutoStart.ServerAddress}...";
            _statusText.color = Color.white;
            SetState(ClientState.Connecting);
        }

        private void OnHostLocal()
        {
            if (State != ClientState.Offline) return;
            var nm = NetworkManager.singleton;
            if (nm == null) return;

            nm.onlineScene = "";
            nm.StartHost();

            var asm = nm.GetComponent<AdditiveSceneManager>();
            if (asm == null) asm = nm.gameObject.AddComponent<AdditiveSceneManager>();
            asm.ServerSetup();

            NetworkServer.OnDisconnectedEvent += (conn) =>
            {
                if (AdditiveSceneManager.Instance != null)
                    AdditiveSceneManager.Instance.OnPlayerDisconnected(conn);
            };

            _statusText.text = "Starting local host...";
            _statusText.color = new Color(0.3f, 0.9f, 0.3f);
            SetState(ClientState.Connecting);
        }

        private void OnQuit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

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
                inventoryUI.Show(playerData.inventory, null);
        }

        // ══════════════════════════════════════════════════════════════
        //  场景辅助
        // ══════════════════════════════════════════════════════════════

        private void HideLobbySceneVisuals(bool hide)
        {
            var lobbyScene = SceneManager.GetSceneByName("LobbyScene");
            if (!lobbyScene.IsValid()) return;
            var nmGo = NetworkManager.singleton != null ? NetworkManager.singleton.gameObject : null;

            foreach (var rootGo in lobbyScene.GetRootGameObjects())
            {
                if (rootGo == nmGo) continue;
                if (rootGo.GetComponent<UnityEngine.EventSystems.EventSystem>() != null) continue;
                rootGo.SetActive(!hide);
            }
        }

        private void SetLobbySceneEventSystem(bool active)
        {
            var lobbyScene = SceneManager.GetSceneByName("LobbyScene");
            if (!lobbyScene.IsValid()) return;
            foreach (var rootGo in lobbyScene.GetRootGameObjects())
            {
                if (rootGo.GetComponent<UnityEngine.EventSystems.EventSystem>() != null)
                {
                    rootGo.SetActive(active);
                    return;
                }
            }
        }

        /// <summary>
        /// 禁用目标场景中自带的 EventSystem，避免和 LobbyScene 的冲突。
        /// </summary>
        private void DisableExtraEventSystems(Scene targetScene)
        {
            if (!targetScene.IsValid()) return;
            foreach (var rootGo in targetScene.GetRootGameObjects())
            {
                var es = rootGo.GetComponent<UnityEngine.EventSystems.EventSystem>();
                if (es != null)
                {
                    Debug.Log($"[LobbyUI] Disabling extra EventSystem in '{targetScene.name}'");
                    rootGo.SetActive(false);
                }
            }
        }

        /// <summary>
        /// 在目标场景中查找 NetworkStartPosition 作为出生点。
        /// 找不到则返回默认位置。
        /// </summary>
        private Vector3 FindSpawnPosition(Scene targetScene)
        {
            if (!targetScene.IsValid()) return Vector3.up;
            foreach (var rootGo in targetScene.GetRootGameObjects())
            {
                var nsp = rootGo.GetComponent<NetworkStartPosition>();
                if (nsp != null)
                    return nsp.transform.position;
            }
            // 没找到 NetworkStartPosition，用 Mirror 全局的
            var positions = NetworkManager.startPositions;
            if (positions != null && positions.Count > 0)
                return positions[0].position;
            return new Vector3(98.1f, 4.215f, 66.77f); // GameScene 默认出生点
        }

        // ══════════════════════════════════════════════════════════════
        //  Loading Screen
        // ══════════════════════════════════════════════════════════════

        private void ShowLoading(string message)
        {
            if (_loadingPanel == null) BuildLoadingScreen();
            _loadingText.text = message;
            _loadingBarFill.fillAmount = 0f;
            _loadingPanel.SetActive(true);
        }

        private void UpdateLoading(float progress, string message = null)
        {
            if (_loadingBarFill != null)
                _loadingBarFill.fillAmount = Mathf.Clamp01(progress);
            if (message != null && _loadingText != null)
                _loadingText.text = message;
        }

        private void BuildLoadingScreen()
        {
            var go = new GameObject("LoadingScreen");
            go.transform.SetParent(_canvas.transform, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            go.AddComponent<Image>().color = new Color(0.05f, 0.05f, 0.08f, 0.95f);
            _loadingPanel = go;

            _loadingText = CreateText(go.transform, "Loading...", 28,
                new Vector2(0f, 30f), new Vector2(400f, 40f));
            _loadingText.color = new Color(0.85f, 0.85f, 0.85f);

            var barBg = new GameObject("BarBg");
            barBg.transform.SetParent(go.transform, false);
            var barBgRect = barBg.AddComponent<RectTransform>();
            barBgRect.anchorMin = new Vector2(0.5f, 0.5f);
            barBgRect.anchorMax = new Vector2(0.5f, 0.5f);
            barBgRect.anchoredPosition = new Vector2(0f, -20f);
            barBgRect.sizeDelta = new Vector2(320f, 12f);
            barBg.AddComponent<Image>().color = new Color(0.2f, 0.2f, 0.2f, 1f);

            var barFill = new GameObject("BarFill");
            barFill.transform.SetParent(barBg.transform, false);
            var barFillRect = barFill.AddComponent<RectTransform>();
            barFillRect.anchorMin = Vector2.zero;
            barFillRect.anchorMax = Vector2.one;
            barFillRect.offsetMin = Vector2.zero;
            barFillRect.offsetMax = Vector2.zero;
            _loadingBarFill = barFill.AddComponent<Image>();
            _loadingBarFill.color = new Color(0.3f, 0.7f, 1f, 1f);
            _loadingBarFill.type = Image.Type.Filled;
            _loadingBarFill.fillMethod = Image.FillMethod.Horizontal;
            _loadingBarFill.fillAmount = 0f;

            go.SetActive(false);
        }

        // ══════════════════════════════════════════════════════════════
        //  UI 构建
        // ══════════════════════════════════════════════════════════════

        private void UpdateAuthPanelPlayerName()
        {
            if (_playerNameText != null && PlayerAuthenticator.LocalPlayerData != null)
                _playerNameText.text = $"Welcome, {PlayerAuthenticator.LocalPlayerData.playerName}";
        }

        private void BuildAuthenticatedPanel()
        {
            _authPanel = CreatePanel(_canvas.transform, new Vector2(400f, 320f));

            _playerNameText = CreateText(_authPanel.transform, "", 28,
                new Vector2(0f, 110f), new Vector2(360f, 40f));
            _playerNameText.color = new Color(0.3f, 0.9f, 0.3f);

            var mapLabel = CreateText(_authPanel.transform, "Select Map", 22,
                new Vector2(0f, 50f), new Vector2(360f, 30f));
            mapLabel.color = new Color(0.8f, 0.8f, 0.8f);

            BuildMapList(_authPanel.transform, new Vector2(0f, -10f));

            TMP_Text _;
            CreateButton(_authPanel.transform, "Quit",
                new Vector2(0f, -100f), new Vector2(280f, 50f),
                new Color(0.6f, 0.2f, 0.2f), DisconnectToLobby, out _);
        }

        private void BuildMapList(Transform parent, Vector2 startPos)
        {
            for (int i = 0; i < AvailableMaps.Length; i++)
            {
                var map = AvailableMaps[i];
                var yOffset = startPos.y - i * 40f;

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
                int index = i;
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

                if (i == 0) _mapSelectionText = tmp;
            }
        }

        private void BuildUI()
        {
            if (FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                var esGo = new GameObject("EventSystem");
                esGo.AddComponent<UnityEngine.EventSystems.EventSystem>();
                esGo.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            }

            var canvasGo = new GameObject("LobbyUI_Canvas");
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 50;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            canvasGo.AddComponent<GraphicRaycaster>();

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
            CreateButton(_preAuthPanel.transform, "QUIT",
                new Vector2(0f, -130f), new Vector2(280f, 50f),
                new Color(0.6f, 0.2f, 0.2f), OnQuit, out _);
        }

        // ══════════════════════════════════════════════════════════════
        //  UI Helpers
        // ══════════════════════════════════════════════════════════════

        private GameObject CreatePanel(Transform parent, Vector2 size)
        {
            var go = new GameObject("Panel");
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            go.AddComponent<Image>().color = new Color(0.1f, 0.1f, 0.1f, 0.85f);
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
            go.AddComponent<Image>().color = bgColor;
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
