using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Mirror;
using MultiplayerFishing;

/// <summary>
/// 背包界面 — 以列表形式显示玩家背包内容，支持实时刷新。
/// 代码动态创建 Canvas，沿用 LobbyUI / NameInputUI 风格，使用 TMPro 渲染文本。
/// 需求: 9.1, 9.2, 9.3, 9.4, 9.5
/// </summary>
public class InventoryUI : MonoBehaviour
{
    private Canvas _canvas;
    private GameObject _panel;
    private Transform _contentParent;
    private TMP_Text _emptyHint;
    private ItemRegistry _registry;
    private Scrollbar _scrollbar;
    private RectTransform _contentRect;
    private readonly List<GameObject> _itemRows = new List<GameObject>();
    private GameObject _selectedCard;
    private string _selectedLogicId;
    private GameObject _inspectButton;

    private bool _isInspecting;
    private GameObject _inspectedFishGO;
    private DisplayFishHold _inspectionHold;
    private DisplayFishIK _inspectionIK;
    private GameObject _hintTextGO;

    public bool IsInspecting => _isInspecting;

    /// <summary>
    /// 背包被关闭时触发（关闭按钮或外部调用 Hide）。
    /// </summary>
    public System.Action OnClosed;

    /// <summary>
    /// 打开背包面板，传入当前背包数据和 ItemRegistry 引用。
    /// </summary>
    public void Show(Inventory inventory, ItemRegistry registry)
    {
        _registry = registry;

        if (_canvas == null)
            BuildUI();

        _canvas.gameObject.SetActive(true);
        Refresh(inventory);
    }

    /// <summary>
    /// 关闭背包面板。
    /// </summary>
    public void Hide()
    {
        if (_canvas != null)
            _canvas.gameObject.SetActive(false);
    }

    /// <summary>
    /// 刷新显示（背包数据变化时调用）。
    /// </summary>
    public void Refresh(Inventory inventory)
    {
        ClearItemRows();

        bool isEmpty = inventory == null || inventory.items == null || inventory.items.Count == 0;

        Debug.Log($"[InventoryUI] Refresh: isEmpty={isEmpty}, registryNull={_registry == null}, contentParentNull={_contentParent == null}, canvasNull={_canvas == null}");

        if (_emptyHint != null)
            _emptyHint.gameObject.SetActive(isEmpty);

        if (_scrollbar != null)
            _scrollbar.gameObject.SetActive(!isEmpty);

        if (isEmpty)
            return;

        for (int i = 0; i < inventory.items.Count; i++)
        {
            var item = inventory.items[i];
            ItemRegistryEntry entry = _registry != null ? _registry.FindByLogicId(item.logicId) : null;
            Debug.Log($"[InventoryUI] Creating card for logicId='{item.logicId}' count={item.count} entryNull={entry == null}");
            var card = CreateItemCard(item, entry);
            _itemRows.Add(card);
        }
        Debug.Log($"[InventoryUI] Refresh done, created {_itemRows.Count} cards");
    }


    private void ClearItemRows()
    {
        for (int i = 0; i < _itemRows.Count; i++)
        {
            if (_itemRows[i] != null)
                Destroy(_itemRows[i]);
        }
        _itemRows.Clear();
        _selectedCard = null;
        _selectedLogicId = null;
        if (_inspectButton != null)
            _inspectButton.SetActive(false);
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
        var canvasGo = new GameObject("InventoryUI_Canvas");
        canvasGo.transform.SetParent(transform, false);
        _canvas = canvasGo.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 110; // Above FishingUI_Canvas (100)
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        canvasGo.AddComponent<GraphicRaycaster>();

        // Main panel (dark semi-transparent background)
        _panel = CreatePanel(canvasGo.transform, new Vector2(800f, 700f));

        // Title: "Inventory"
        var title = CreateText(_panel.transform, "Inventory", 32,
            new Vector2(0f, 320f), new Vector2(400f, 50f));
        title.color = Color.white;

        // Close button (top-right corner)
        TMP_Text _;
        var closeBtn = CreateButton(_panel.transform, "X",
            new Vector2(370f, 318f), new Vector2(44f, 44f),
            new Color(0.6f, 0.2f, 0.2f), OnCloseClicked, out _);

        // Empty hint text (centered, hidden by default)
        _emptyHint = CreateText(_panel.transform, "Inventory is empty", 24,
            new Vector2(0f, 0f), new Vector2(400f, 40f));
        _emptyHint.color = new Color(0.6f, 0.6f, 0.6f);
        _emptyHint.gameObject.SetActive(false);

        // Scroll view area
        BuildScrollView(_panel.transform);
    }

    private void BuildScrollView(Transform parent)
    {
        // ScrollView root (~760×600, centered below title)
        var scrollGo = new GameObject("ScrollView");
        scrollGo.transform.SetParent(parent, false);
        var scrollRt = scrollGo.AddComponent<RectTransform>();
        scrollRt.anchorMin = new Vector2(0.5f, 0.5f);
        scrollRt.anchorMax = new Vector2(0.5f, 0.5f);
        scrollRt.anchoredPosition = new Vector2(0f, -30f);
        scrollRt.sizeDelta = new Vector2(760f, 600f);

        var scroll = scrollGo.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;

        // Viewport (stretch-fill the ScrollView, Mask + Image)
        var viewportGo = new GameObject("Viewport");
        viewportGo.transform.SetParent(scrollGo.transform, false);
        var viewportRect = viewportGo.AddComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = Vector2.zero;
        viewportRect.offsetMax = Vector2.zero;

        var viewportImage = viewportGo.AddComponent<Image>();
        viewportImage.color = Color.white; // Mask requires non-transparent image to show children
        viewportGo.AddComponent<Mask>().showMaskGraphic = false;

        // Content (top-anchored inside Viewport, GridLayoutGroup)
        var contentGo = new GameObject("Content");
        contentGo.transform.SetParent(viewportGo.transform, false);
        _contentRect = contentGo.AddComponent<RectTransform>();
        _contentRect.anchorMin = new Vector2(0f, 1f);
        _contentRect.anchorMax = new Vector2(1f, 1f);
        _contentRect.pivot = new Vector2(0.5f, 1f);
        _contentRect.anchoredPosition = Vector2.zero;
        _contentRect.sizeDelta = new Vector2(0f, 0f);

        var grid = contentGo.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(130f, 150f);
        grid.spacing = new Vector2(10f, 10f);
        grid.constraint = GridLayoutGroup.Constraint.Flexible;
        grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
        grid.childAlignment = TextAnchor.UpperLeft;

        var fitter = contentGo.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Vertical Scrollbar (right side of ScrollView)
        var scrollbarGo = new GameObject("Scrollbar");
        scrollbarGo.transform.SetParent(scrollGo.transform, false);
        var scrollbarRect = scrollbarGo.AddComponent<RectTransform>();
        scrollbarRect.anchorMin = new Vector2(1f, 0f);
        scrollbarRect.anchorMax = new Vector2(1f, 1f);
        scrollbarRect.pivot = new Vector2(1f, 0.5f);
        scrollbarRect.anchoredPosition = Vector2.zero;
        scrollbarRect.sizeDelta = new Vector2(20f, 0f);

        var scrollbarImage = scrollbarGo.AddComponent<Image>();
        scrollbarImage.color = new Color(0.15f, 0.15f, 0.15f, 0.8f);

        _scrollbar = scrollbarGo.AddComponent<Scrollbar>();
        _scrollbar.direction = Scrollbar.Direction.BottomToTop;

        // Scrollbar sliding area
        var slidingAreaGo = new GameObject("Sliding Area");
        slidingAreaGo.transform.SetParent(scrollbarGo.transform, false);
        var slidingRect = slidingAreaGo.AddComponent<RectTransform>();
        slidingRect.anchorMin = Vector2.zero;
        slidingRect.anchorMax = Vector2.one;
        slidingRect.offsetMin = Vector2.zero;
        slidingRect.offsetMax = Vector2.zero;

        // Scrollbar handle
        var handleGo = new GameObject("Handle");
        handleGo.transform.SetParent(slidingAreaGo.transform, false);
        var handleRect = handleGo.AddComponent<RectTransform>();
        handleRect.anchorMin = Vector2.zero;
        handleRect.anchorMax = Vector2.one;
        handleRect.offsetMin = Vector2.zero;
        handleRect.offsetMax = Vector2.zero;

        var handleImage = handleGo.AddComponent<Image>();
        handleImage.color = new Color(0.4f, 0.4f, 0.4f, 0.9f);

        _scrollbar.handleRect = handleRect;
        _scrollbar.targetGraphic = handleImage;

        // Wire everything to ScrollRect
        scroll.viewport = viewportRect;
        scroll.content = _contentRect;
        scroll.verticalScrollbar = _scrollbar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;

        _contentParent = contentGo.transform;
    }


    private GameObject CreateItemCard(InventoryItem item, ItemRegistryEntry entry)
    {
        string displayName = (entry != null && !string.IsNullOrEmpty(entry.displayName))
            ? entry.displayName
            : item.logicId;

        // Root card GO
        var cardGo = new GameObject($"ItemCard_{item.logicId}");
        cardGo.transform.SetParent(_contentParent, false);
        var cardRect = cardGo.AddComponent<RectTransform>();

        // Background image
        var bgImage = cardGo.AddComponent<Image>();
        bgImage.color = new Color(0.18f, 0.18f, 0.18f, 0.9f);

        // Button with ColorBlock for hover/selection feedback
        var button = cardGo.AddComponent<Button>();
        button.targetGraphic = bgImage;
        var colors = button.colors;
        colors.normalColor = new Color(0.18f, 0.18f, 0.18f, 0.9f);
        colors.highlightedColor = new Color(0.3f, 0.3f, 0.35f, 1.0f);
        colors.selectedColor = new Color(0.25f, 0.4f, 0.6f, 1.0f);
        colors.pressedColor = new Color(0.15f, 0.3f, 0.5f, 1.0f);
        colors.colorMultiplier = 1f;
        button.colors = colors;

        bool hasIcon = entry != null && entry.icon != null;

        if (hasIcon)
        {
            // Icon — upper portion
            var iconGo = new GameObject("Icon");
            iconGo.transform.SetParent(cardGo.transform, false);
            var iconRect = iconGo.AddComponent<RectTransform>();
            iconRect.anchorMin = new Vector2(0.1f, 0.4f);
            iconRect.anchorMax = new Vector2(0.9f, 0.95f);
            iconRect.offsetMin = Vector2.zero;
            iconRect.offsetMax = Vector2.zero;
            var iconImg = iconGo.AddComponent<Image>();
            iconImg.sprite = entry.icon;
            iconImg.preserveAspect = true;
        }

        // "名字 x数量" — below icon
        string nameCountStr = item.count > 1 ? $"{displayName} x{item.count}" : displayName;
        var nameGo = new GameObject("Name");
        nameGo.transform.SetParent(cardGo.transform, false);
        var nameRect = nameGo.AddComponent<RectTransform>();
        nameRect.anchorMin = new Vector2(0f, 0.15f);
        nameRect.anchorMax = new Vector2(1f, hasIcon ? 0.4f : 0.6f);
        nameRect.offsetMin = Vector2.zero;
        nameRect.offsetMax = Vector2.zero;
        var nameTmp = nameGo.AddComponent<TextMeshProUGUI>();
        nameTmp.text = nameCountStr;
        nameTmp.fontSize = 13;
        nameTmp.alignment = TextAlignmentOptions.Center;
        nameTmp.color = Color.white;

        // Total weight — bottom line
        if (item.totalWeight > 0f)
        {
            var weightGo = new GameObject("Weight");
            weightGo.transform.SetParent(cardGo.transform, false);
            var weightRect = weightGo.AddComponent<RectTransform>();
            weightRect.anchorMin = new Vector2(0f, 0f);
            weightRect.anchorMax = new Vector2(1f, 0.18f);
            weightRect.offsetMin = Vector2.zero;
            weightRect.offsetMax = Vector2.zero;
            var weightTmp = weightGo.AddComponent<TextMeshProUGUI>();
            weightTmp.text = $"{item.totalWeight:F2} kg";
            weightTmp.fontSize = 12;
            weightTmp.alignment = TextAlignmentOptions.Center;
            weightTmp.color = new Color(0.7f, 0.9f, 1f);
        }

        // Wire click handler for selection and inspect
        string logicId = item.logicId;
        button.onClick.AddListener(() => OnCardClicked(cardGo, logicId));

        return cardGo;
    }


    private void OnCloseClicked()
    {
        Hide();
        OnClosed?.Invoke();
    }

    private void OnCardClicked(GameObject card, string logicId)
    {
        // Deselect previous card
        if (_selectedCard != null && _selectedCard != card)
        {
            var prevBtn = _selectedCard.GetComponent<Button>();
            if (prevBtn != null)
            {
                var prevColors = prevBtn.colors;
                prevColors.normalColor = new Color(0.18f, 0.18f, 0.18f, 0.9f);
                prevBtn.colors = prevColors;
                // Reset the Image color to normal
                var prevImg = _selectedCard.GetComponent<Image>();
                if (prevImg != null)
                    prevImg.color = new Color(0.18f, 0.18f, 0.18f, 0.9f);
            }
        }

        // Select new card
        _selectedCard = card;
        _selectedLogicId = logicId;

        var btn = card.GetComponent<Button>();
        if (btn != null)
        {
            var colors = btn.colors;
            colors.normalColor = new Color(0.25f, 0.4f, 0.6f, 1.0f);
            btn.colors = colors;
            var img = card.GetComponent<Image>();
            if (img != null)
                img.color = new Color(0.25f, 0.4f, 0.6f, 1.0f);
        }

        // Check if item is Fish type — show/hide inspect button
        bool isFish = false;
        if (_registry != null)
        {
            var entry = _registry.FindByLogicId(logicId);
            if (entry != null)
                isFish = entry.itemType == ItemType.Fish;
        }

        ShowInspectButton(isFish, card);
    }

    private void ShowInspectButton(bool show, GameObject nearCard)
    {
        if (!show)
        {
            if (_inspectButton != null)
                _inspectButton.SetActive(false);
            return;
        }

        if (_inspectButton == null)
        {
            // Create the inspect button once, reuse it
            _inspectButton = new GameObject("InspectButton");
            _inspectButton.transform.SetParent(_panel.transform, false);
            var rect = _inspectButton.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(120f, 40f);
            var img = _inspectButton.AddComponent<Image>();
            img.color = new Color(0.2f, 0.5f, 0.3f, 0.95f);
            var btn = _inspectButton.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(EnterInspection);

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(_inspectButton.transform, false);
            var textRect = textGo.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            tmp.text = "Inspect";
            tmp.fontSize = 18;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
        }

        _inspectButton.SetActive(true);

        // Position the inspect button below the panel center (fixed position)
        var inspectRect = _inspectButton.GetComponent<RectTransform>();
        inspectRect.anchorMin = new Vector2(0.5f, 0.5f);
        inspectRect.anchorMax = new Vector2(0.5f, 0.5f);
        inspectRect.anchoredPosition = new Vector2(0f, -310f); // near bottom of 700-height panel
    }

    private void EnterInspection()
    {
        if (_registry == null || string.IsNullOrEmpty(_selectedLogicId))
            return;

        var entry = _registry.FindByLogicId(_selectedLogicId);
        if (entry == null || entry.prefab == null)
        {
            Debug.LogWarning("[InventoryUI] Cannot inspect: prefab is null for " + _selectedLogicId);
            return;
        }

        // Find local player GameObject via Mirror
        var playerObj = NetworkClient.localPlayer?.gameObject;
        if (playerObj == null)
        {
            Debug.LogError("[InventoryUI] Cannot inspect: no local player object");
            return;
        }

        var animator = playerObj.GetComponent<Animator>();
        if (animator == null)
        {
            Debug.LogError("[InventoryUI] Cannot inspect: player has no Animator");
            return;
        }

        Debug.Log($"[InventoryUI] EnterInspection: logicId='{_selectedLogicId}' prefab='{entry.prefab.name}' player='{playerObj.name}' animatorEnabled={animator.enabled}");

        // Hide inventory panel
        _panel.SetActive(false);

        // Instantiate fish prefab locally (no NetworkServer.Spawn)
        _inspectedFishGO = Object.Instantiate(entry.prefab);
        Debug.Log($"[InventoryUI] Fish instantiated: '{_inspectedFishGO.name}' active={_inspectedFishGO.activeSelf} scale={_inspectedFishGO.transform.localScale}");

        // Strip physics and network components that interfere with local display
        var rb = _inspectedFishGO.GetComponent<Rigidbody>();
        if (rb != null) Object.Destroy(rb);
        foreach (var col in _inspectedFishGO.GetComponents<Collider>())
            Object.Destroy(col);
        var netId = _inspectedFishGO.GetComponent<Mirror.NetworkIdentity>();
        if (netId != null) Object.Destroy(netId);

        // Remove any existing DisplayFishHold before adding a new one
        var existingHold = playerObj.GetComponent<DisplayFishHold>();
        if (existingHold != null)
        {
            Debug.LogWarning("[InventoryUI] Removing existing DisplayFishHold before inspection");
            existingHold.Cleanup();
            Object.Destroy(existingHold);
        }

        // Add DisplayFishHold and set up (handles both fish positioning and left hand IK)
        _inspectionHold = playerObj.AddComponent<DisplayFishHold>();
        _inspectionHold.Setup(_inspectedFishGO);

        _isInspecting = true;
        Debug.Log($"[InventoryUI] Inspection started, holdActive={_inspectionHold != null}");

        // Show hint text
        ShowHintText(true);
    }

    public void ExitInspection()
    {
        if (!_isInspecting) return;

        // Clean up DisplayFishHold
        if (_inspectionHold != null)
        {
            _inspectionHold.Cleanup();
            Object.Destroy(_inspectionHold);
            _inspectionHold = null;
        }

        // Destroy the fish GameObject
        if (_inspectedFishGO != null)
        {
            Object.Destroy(_inspectedFishGO);
            _inspectedFishGO = null;
        }

        _isInspecting = false;

        // Restore panel visibility (EnterInspection hides it)
        if (_panel != null)
            _panel.SetActive(true);

        // Hide hint text
        ShowHintText(false);
    }
    private void OnDisable()
    {
        ExitInspection();
    }


    private void ShowHintText(bool show)
    {
        if (show)
        {
            if (_hintTextGO == null)
            {
                // Parent to canvas (not panel, since panel is hidden during inspection)
                _hintTextGO = new GameObject("InspectionHint");
                _hintTextGO.transform.SetParent(_canvas.transform, false);
                var rect = _hintTextGO.AddComponent<RectTransform>();
                rect.anchorMin = new Vector2(0.5f, 0f);
                rect.anchorMax = new Vector2(0.5f, 0f);
                rect.anchoredPosition = new Vector2(0f, 80f);
                rect.sizeDelta = new Vector2(400f, 50f);
                var tmp = _hintTextGO.AddComponent<TextMeshProUGUI>();
                tmp.text = "Press E to put back";
                tmp.fontSize = 24;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.color = Color.white;
            }
            _hintTextGO.SetActive(true);
        }
        else
        {
            if (_hintTextGO != null)
                _hintTextGO.SetActive(false);
        }
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
