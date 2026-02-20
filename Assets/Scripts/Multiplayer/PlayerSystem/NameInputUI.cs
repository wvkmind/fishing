using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 名字输入界面 — 首次启动时引导玩家输入角色名字。
/// 代码动态创建 Canvas，沿用 LobbyUI 风格，使用 TMPro 渲染文本。
/// 验收需求: 2.1, 2.2, 2.3, 2.4
/// </summary>
public class NameInputUI : MonoBehaviour
{
    /// <summary>
    /// 玩家确认名字后触发，参数为输入的名字。
    /// </summary>
    public event Action<string> OnNameConfirmed;

    private Canvas _canvas;
    private TMP_InputField _inputField;
    private Button _confirmBtn;
    private TMP_Text _hintText;

    /// <summary>
    /// 验证名字是否合法：长度 2-12 字符，非纯空白。
    /// 设为 public static 以便属性测试直接调用。
    /// </summary>
    public static bool ValidateName(string name)
    {
        if (name == null) return false;
        if (name.Length < 2 || name.Length > 12) return false;
        if (name.Trim().Length == 0) return false;
        return true;
    }

    /// <summary>
    /// 显示名字输入界面。
    /// </summary>
    public void Show()
    {
        if (_canvas == null)
            BuildUI();
        _canvas.gameObject.SetActive(true);

        // Reset state
        if (_inputField != null)
        {
            _inputField.text = "";
            _inputField.interactable = true;
        }
        if (_confirmBtn != null)
            _confirmBtn.interactable = false;
        if (_hintText != null)
            _hintText.text = "";
    }

    /// <summary>
    /// 隐藏名字输入界面。
    /// </summary>
    public void Hide()
    {
        if (_canvas != null)
            _canvas.gameObject.SetActive(false);
    }

    private void OnInputValueChanged(string value)
    {
        if (value == null) value = "";

        if (value.Length == 0)
        {
            // Empty input — no error, just disable button
            _hintText.text = "";
            _confirmBtn.interactable = false;
        }
        else if (value.Trim().Length == 0)
        {
            // All whitespace
            _hintText.text = "Name cannot be blank";
            _hintText.color = new Color(1f, 0.3f, 0.3f);
            _confirmBtn.interactable = false;
        }
        else if (value.Length < 2 || value.Length > 12)
        {
            // Length out of range
            _hintText.text = "Name must be 2-12 characters";
            _hintText.color = new Color(1f, 0.3f, 0.3f);
            _confirmBtn.interactable = false;
        }
        else
        {
            // Valid
            _hintText.text = "";
            _confirmBtn.interactable = true;
        }
    }

    private void OnConfirmClicked()
    {
        string name = _inputField != null ? _inputField.text : "";
        if (!ValidateName(name)) return;

        // Disable button to prevent double submit
        _confirmBtn.interactable = false;
        _inputField.interactable = false;

        OnNameConfirmed?.Invoke(name);
    }

    private void BuildUI()
    {
        // Ensure EventSystem exists (required for UI interaction)
        if (FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            var esGo = new GameObject("EventSystem");
            esGo.AddComponent<UnityEngine.EventSystems.EventSystem>();
            esGo.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }

        // Canvas
        var canvasGo = new GameObject("NameInputUI_Canvas");
        canvasGo.transform.SetParent(transform, false);
        _canvas = canvasGo.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 100;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGo.AddComponent<GraphicRaycaster>();

        // Center panel (dark semi-transparent background)
        var panel = CreatePanel(canvasGo.transform, new Vector2(460f, 300f));

        // Title text
        var title = CreateText(panel.transform, "Enter Your Name", 32,
            new Vector2(0f, 90f), new Vector2(400f, 50f));
        title.color = Color.white;

        // Input field
        _inputField = CreateInputField(panel.transform,
            new Vector2(0f, 20f), new Vector2(360f, 50f));

        // Hint / error text below input
        _hintText = CreateText(panel.transform, "", 18,
            new Vector2(0f, -25f), new Vector2(360f, 30f));
        _hintText.color = new Color(1f, 0.3f, 0.3f);

        // Confirm button
        TMP_Text _;
        _confirmBtn = CreateButton(panel.transform, "Confirm",
            new Vector2(0f, -75f), new Vector2(280f, 50f),
            new Color(0.2f, 0.5f, 0.8f), OnConfirmClicked, out _);
        _confirmBtn.interactable = false;
    }

    private TMP_InputField CreateInputField(Transform parent, Vector2 pos, Vector2 size)
    {
        // Input field root
        var go = new GameObject("InputField");
        go.transform.SetParent(parent, false);
        var rect = go.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = pos;
        rect.sizeDelta = size;

        // Background image
        var bgImg = go.AddComponent<Image>();
        bgImg.color = new Color(0.2f, 0.2f, 0.2f, 1f);

        // Text area (viewport)
        var textArea = new GameObject("Text Area");
        textArea.transform.SetParent(go.transform, false);
        var textAreaRect = textArea.AddComponent<RectTransform>();
        textAreaRect.anchorMin = Vector2.zero;
        textAreaRect.anchorMax = Vector2.one;
        textAreaRect.offsetMin = new Vector2(10f, 5f);
        textAreaRect.offsetMax = new Vector2(-10f, -5f);
        textArea.AddComponent<RectMask2D>();

        // Placeholder text
        var placeholderGo = new GameObject("Placeholder");
        placeholderGo.transform.SetParent(textArea.transform, false);
        var phRect = placeholderGo.AddComponent<RectTransform>();
        phRect.anchorMin = Vector2.zero;
        phRect.anchorMax = Vector2.one;
        phRect.offsetMin = Vector2.zero;
        phRect.offsetMax = Vector2.zero;
        var placeholder = placeholderGo.AddComponent<TextMeshProUGUI>();
        placeholder.text = "Enter name...";
        placeholder.fontSize = 22;
        placeholder.color = new Color(0.5f, 0.5f, 0.5f);
        placeholder.alignment = TextAlignmentOptions.MidlineLeft;

        // Input text
        var textGo = new GameObject("Text");
        textGo.transform.SetParent(textArea.transform, false);
        var textRect = textGo.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        var inputText = textGo.AddComponent<TextMeshProUGUI>();
        inputText.fontSize = 22;
        inputText.color = Color.white;
        inputText.alignment = TextAlignmentOptions.MidlineLeft;

        // TMP_InputField component
        var inputField = go.AddComponent<TMP_InputField>();
        inputField.textViewport = textAreaRect;
        inputField.textComponent = inputText;
        inputField.placeholder = placeholder;
        inputField.characterLimit = 12;
        inputField.onValueChanged.AddListener(OnInputValueChanged);

        return inputField;
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
