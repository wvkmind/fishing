using Mirror;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace MultiplayerFishing
{
    /// <summary>
    /// Lobby scene UI — two buttons: Connect / Quit.
    /// Shows connection status. Replaces NetworkManagerHUD.
    /// Only active on clients (not batch mode server).
    /// </summary>
    public class LobbyUI : MonoBehaviour
    {
        private Canvas _canvas;
        private Button _connectBtn;
        private Button _quitBtn;
        private TMP_Text _statusText;
        private TMP_Text _connectBtnText;
        private bool _connecting;

        private void Start()
        {
            // Server doesn't need UI
            if (Application.isBatchMode)
            {
                enabled = false;
                return;
            }

            // Hide Mirror's default HUD if present
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
            // When returning to lobby (offline scene), rebuild UI
            bool isLobby = scene.path != null && scene.path.Contains("LobbyScene");
            if (isLobby && _canvas == null && !Application.isBatchMode)
                ShowLobby();

            // In game scene, hide lobby UI
            if (!isLobby && _canvas != null)
                _canvas.gameObject.SetActive(false);
        }

        private void ShowLobby()
        {
            if (_canvas != null)
            {
                _canvas.gameObject.SetActive(true);
                // Reset state
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
                    // Connection failed or was rejected
                    _statusText.text = "Connection failed, try again";
                    _statusText.color = new Color(1f, 0.3f, 0.3f);
                    _connectBtn.interactable = true;
                    _connectBtnText.text = "CONNECT";
                    _connecting = false;
                }
            }
        }

        private void OnConnect()
        {
            var nm = NetworkManager.singleton;
            if (nm == null) return;

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

            nm.StartHost();
            _statusText.text = "Starting local host...";
            _statusText.color = new Color(0.3f, 0.9f, 0.3f);
        }

        private void BuildUI()
        {
            // Ensure EventSystem exists (required for button clicks)
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

            // Center panel
            var panel = CreatePanel(canvasGo.transform, new Vector2(400f, 360f));

            // Title
            var title = CreateText(panel.transform, "Fishing Online", 36,
                new Vector2(0f, 90f), new Vector2(360f, 50f));
            title.color = Color.white;

            // Status text
            _statusText = CreateText(panel.transform, "", 18,
                new Vector2(0f, 30f), new Vector2(360f, 30f));
            _statusText.color = new Color(0.7f, 0.7f, 0.7f);

            // Connect button
            _connectBtn = CreateButton(panel.transform, "CONNECT",
                new Vector2(0f, -10f), new Vector2(280f, 50f),
                new Color(0.2f, 0.5f, 0.8f), OnConnect, out _connectBtnText);

            // Host local button
            TMP_Text _hostText;
            CreateButton(panel.transform, "HOST (LOCAL)",
                new Vector2(0f, -70f), new Vector2(280f, 50f),
                new Color(0.2f, 0.65f, 0.4f), OnHostLocal, out _hostText);

            // Quit button
            TMP_Text _;
            _quitBtn = CreateButton(panel.transform, "QUIT",
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
