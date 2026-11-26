using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine.XR.Interaction.Toolkit.UI;

public class SearchStoreUI : MonoBehaviour
{
    public TMP_InputField inputField;
    public DatabaseManager db;
    public Transform planeTransform;
    public GameObject markerPrefab;
    public Unit unit;
    public TMP_Text statusText;

    GameObject currentMarker;
    UnityEngine.Events.UnityAction<string> submitAction;

    // Look & feel
    static readonly Color PanelColour = new Color(0.04f, 0.07f, 0.11f, 0.90f);
    static readonly Color FieldColour = new Color(0.09f, 0.13f, 0.19f, 0.96f);
    static readonly Color AccentColour = new Color(0.10f, 0.80f, 0.98f, 1f);
    static readonly Color MutedText = new Color(0.62f, 0.72f, 0.80f, 1f);
    const int MaxSuggestions = 5;

    List<final_stores_dataset_v3> stores = new List<final_stores_dataset_v3>();
    readonly List<SuggestionRow> suggestionRows = new List<SuggestionRow>();
    RectTransform suggestionPanel;
    TMP_Text detailText;
    bool suppressSuggestions;
    float nextDetailUpdate;

    class SuggestionRow
    {
        public GameObject root;
        public TMP_Text name;
        public TMP_Text floor;
        public final_stores_dataset_v3 store;
    }

    void Awake()
    {
        if (db == null)
            db = FindFirstObjectByType<DatabaseManager>();
        if (unit == null)
            unit = FindFirstObjectByType<Unit>();
        if (inputField == null)
            inputField = FindFirstObjectByType<TMP_InputField>();

        Canvas searchCanvas = inputField != null ? inputField.GetComponentInParent<Canvas>() : null;
        if (searchCanvas != null && searchCanvas.renderMode == RenderMode.WorldSpace)
        {
            if (searchCanvas.GetComponent<TrackedDeviceGraphicRaycaster>() == null)
                searchCanvas.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();

            if (searchCanvas.worldCamera == null)
                searchCanvas.worldCamera = Camera.main;
        }

        CreateStatusDisplay();
        StyleSearchPanel(searchCanvas);

        // The panels float in front of the camera, so near a wall they would be cut off by it.
        // Draw them on top of the mall geometry instead.
        DrawCanvasOnTop(searchCanvas);
        DrawCanvasOnTop(statusText != null ? statusText.GetComponentInParent<Canvas>() : null);

        if (inputField != null)
        {
            submitAction = _ => OnSearchButton();
            inputField.onSubmit.AddListener(submitAction);
            inputField.onValueChanged.AddListener(UpdateSuggestions);
        }
    }

    void OnEnable()
    {
        if (unit != null)
            unit.StatusChanged += SetStatus;
    }

    void OnDisable()
    {
        if (unit != null)
            unit.StatusChanged -= SetStatus;
    }

    void Start()
    {
        if (db == null || db.Connection == null)
        {
            SetStatus("Store database could not be loaded.");
            return;
        }

        try
        {
            stores = db.Connection.Table<final_stores_dataset_v3>().ToList()
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.store_name))
                .ToList();
        }
        catch (System.Exception exception)
        {
            Debug.LogException(exception, this);
        }
        SetStatus($"Welcome to Phoenix Mall of Asia. Search {stores.Count} stores and follow the glowing path.");
    }

    void Update()
    {
        if (detailText == null || Time.unscaledTime < nextDetailUpdate)
            return;
        nextDetailUpdate = Time.unscaledTime + 0.25f;

        if (unit == null)
            return;
        string floor = InterFloorController.FloorName(unit.currentFloor);
        if (unit.IsInLift)
            detailText.text = "In the glass lift";
        else if (unit.IsNavigating && unit.RemainingDistance > 0.5f)
        {
            float seconds = unit.RemainingDistance / Mathf.Max(0.5f, unit.speed);
            string eta = seconds < 60f ? $"{Mathf.CeilToInt(seconds / 5f) * 5} s" : $"{Mathf.CeilToInt(seconds / 60f)} min";
            string via = unit.HeadingToLift ? " to the lift" : "";
            detailText.text = $"{floor}   ·   {unit.RemainingDistance:0} m{via}   ·   about {eta}";
        }
        else
            detailText.text = floor;
    }

    string NormalizeForSearch(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return Regex.Replace(value.ToLowerInvariant(), @"[^\p{L}\p{Nd}]", "");
    }

    public void OnSearchButton()
    {
        if (inputField == null || db == null || db.Connection == null)
        {
            SetStatus("Search is unavailable because the store database did not load.");
            return;
        }

        string query = NormalizeForSearch(inputField.text);
        if (query.Length == 0)
        {
            SetStatus("Enter a store name to plan a route.");
            inputField.ActivateInputField();
            return;
        }

        if (unit == null)
        {
            SetStatus("Navigation is unavailable because the seeker is missing.");
            return;
        }

        try
        {
            // Compare in C# so punctuation, spaces, and capitalization behave consistently
            // across SQLite versions and with the dataset's normalized store names.
            var store = db.Connection.Table<final_stores_dataset_v3>()
                .ToList()
                .FirstOrDefault(item =>
                    item != null &&
                    (NormalizeForSearch(item.normalized_name) == query || NormalizeForSearch(item.store_name) == query));

            if (store == null)
            {
                // Fall back to the best partial match, as shown in the suggestions.
                store = FindSuggestions(query).FirstOrDefault();
            }

            if (store == null)
            {
                SetStatus("No store matched “" + inputField.text.Trim() + "”. Check the spelling and try again.");
                return;
            }

            NavigateTo(store);
        }
        catch (System.Exception exception)
        {
            Debug.LogException(exception, this);
            SetStatus("The store search failed. The database may be damaged or out of date.");
        }
    }

    void NavigateTo(final_stores_dataset_v3 store)
    {
        HideSuggestions();
        if (InterFloorController.Instance == null || !InterFloorController.Instance.IsValidFloor(store.floor_number))
        {
            SetStatus("“" + store.store_name + "” has an invalid floor assignment in the store database.");
            return;
        }

        Vector3 storePosition = ConvertCentroidToUnity(store.Centroid_X, store.Centroid_Y, planeTransform, store.floor_number);
        // The route guide's beacon marks the destination; the legacy marker is only a fallback.
        if (markerPrefab != null && unit.Guide == null)
        {
            if (currentMarker != null)
                Destroy(currentMarker);
            currentMarker = Instantiate(markerPrefab, storePosition, Quaternion.identity);
        }

        SetStatus("Planning a route to " + store.store_name + "…");
        unit.SetTarget(storePosition, store.floor_number, store.store_name);
    }

    IEnumerable<final_stores_dataset_v3> FindSuggestions(string query)
    {
        if (query.Length == 0)
            return Enumerable.Empty<final_stores_dataset_v3>();

        return stores
            .Select(item => new { item, key = NormalizeForSearch(item.store_name), alt = NormalizeForSearch(item.normalized_name) })
            .Select(x => new
            {
                x.item,
                rank = x.key.StartsWith(query) || x.alt.StartsWith(query) ? 0
                     : x.key.Contains(query) || x.alt.Contains(query) ? 1
                     : NormalizeForSearch(x.item.category).Contains(query) ? 2 : 3
            })
            .Where(x => x.rank < 3)
            .OrderBy(x => x.rank)
            .ThenBy(x => x.item.store_name.Length)
            .ThenBy(x => x.item.floor_number)
            .Select(x => x.item)
            .GroupBy(item => item.store_name + "|" + item.floor_number)
            .Select(group => group.First());
    }

    // ------------------------------------------------------------------ search panel styling

    void StyleSearchPanel(Canvas canvas)
    {
        if (canvas == null || inputField == null)
            return;

        // Lift the panel above the line of sight so it no longer blocks the view ahead.
        Transform anchor = canvas.transform.parent;
        if (anchor != null && anchor.name == "UIAnchor")
            anchor.localPosition = new Vector3(0f, 0.17f, 0.75f);

        RectTransform field = inputField.GetComponent<RectTransform>();
        field.anchoredPosition = new Vector2(-24f, 30f);
        field.sizeDelta = new Vector2(144f, 26f);
        var fieldImage = inputField.GetComponent<Image>();
        if (fieldImage != null)
            fieldImage.color = FieldColour;
        if (inputField.textComponent != null)
        {
            inputField.textComponent.color = Color.white;
            inputField.textComponent.fontSize = 12f;
        }
        if (inputField.placeholder is TMP_Text placeholder)
        {
            placeholder.text = "Where to? e.g. Nike, Starbucks";
            placeholder.color = MutedText;
            placeholder.fontStyle = FontStyles.Normal;
            placeholder.fontSize = 11f;
        }
        inputField.caretColor = AccentColour;
        inputField.selectionColor = new Color(AccentColour.r, AccentColour.g, AccentColour.b, 0.35f);

        Button searchButton = canvas.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.name == "SearchButton");
        if (searchButton != null)
        {
            var buttonRect = searchButton.GetComponent<RectTransform>();
            buttonRect.anchoredPosition = new Vector2(74f, 30f);
            buttonRect.sizeDelta = new Vector2(46f, 26f);
            var colours = searchButton.colors;
            colours.normalColor = AccentColour;
            colours.highlightedColor = Color.Lerp(AccentColour, Color.white, 0.25f);
            colours.pressedColor = Color.Lerp(AccentColour, Color.black, 0.2f);
            colours.selectedColor = AccentColour;
            searchButton.colors = colours;
            var buttonImage = searchButton.GetComponent<Image>();
            if (buttonImage != null)
                buttonImage.color = Color.white;
            var label = searchButton.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
            {
                label.text = "Go";
                label.fontSize = 13f;
                label.fontStyle = FontStyles.Bold;
                label.color = new Color(0.02f, 0.08f, 0.12f);
            }
            searchButton.onClick.RemoveListener(OnSearchButton);
            searchButton.onClick.AddListener(OnSearchButton);
        }

        // Soft backdrop behind the search bar.
        var backdrop = CreateImage("Search Backdrop", field.parent, PanelColour);
        backdrop.rectTransform.SetAsFirstSibling();
        backdrop.rectTransform.anchoredPosition = new Vector2(0f, 36f);
        backdrop.rectTransform.sizeDelta = new Vector2(196f, 50f);

        var title = CreateText("Search Title", field.parent, "SAARATHI  ·  FIND A STORE", 7f, MutedText, TextAlignmentOptions.Left);
        title.rectTransform.anchoredPosition = new Vector2(-4f, 52f);
        title.rectTransform.sizeDelta = new Vector2(180f, 10f);
        title.characterSpacing = 6f;

        // Suggestion list below the bar.
        suggestionPanel = CreateImage("Suggestions", field.parent, PanelColour).rectTransform;
        suggestionPanel.pivot = new Vector2(0.5f, 1f);
        suggestionPanel.anchoredPosition = new Vector2(0f, 9f);
        for (int i = 0; i < MaxSuggestions; i++)
            suggestionRows.Add(CreateSuggestionRow(suggestionPanel, i));
        HideSuggestions();
    }

    SuggestionRow CreateSuggestionRow(RectTransform parent, int index)
    {
        var row = new SuggestionRow();
        Image background = CreateImage("Suggestion " + index, parent, Color.white);
        background.rectTransform.anchorMin = background.rectTransform.anchorMax = new Vector2(0.5f, 1f);
        background.rectTransform.pivot = new Vector2(0.5f, 1f);
        background.rectTransform.anchoredPosition = new Vector2(0f, -4f - index * 20f);
        background.rectTransform.sizeDelta = new Vector2(188f, 19f);
        background.raycastTarget = true;
        row.root = background.gameObject;

        var button = row.root.AddComponent<Button>();
        var colours = button.colors;
        colours.normalColor = new Color(1f, 1f, 1f, 0f);
        colours.highlightedColor = new Color(AccentColour.r, AccentColour.g, AccentColour.b, 0.22f);
        colours.pressedColor = new Color(AccentColour.r, AccentColour.g, AccentColour.b, 0.4f);
        colours.selectedColor = colours.highlightedColor;
        button.colors = colours;
        button.targetGraphic = background;
        button.onClick.AddListener(() =>
        {
            if (row.store == null)
                return;
            suppressSuggestions = true;
            inputField.text = row.store.store_name;
            suppressSuggestions = false;
            NavigateTo(row.store);
        });

        row.name = CreateText("Name", row.root.transform, "", 10f, Color.white, TextAlignmentOptions.Left);
        row.name.rectTransform.anchorMin = new Vector2(0f, 0f);
        row.name.rectTransform.anchorMax = new Vector2(0.68f, 1f);
        row.name.rectTransform.offsetMin = new Vector2(8f, 0f);
        row.name.rectTransform.offsetMax = Vector2.zero;
        row.name.textWrappingMode = TextWrappingModes.NoWrap;
        row.name.overflowMode = TextOverflowModes.Ellipsis;

        row.floor = CreateText("Floor", row.root.transform, "", 8f, AccentColour, TextAlignmentOptions.Right);
        row.floor.rectTransform.anchorMin = new Vector2(0.62f, 0f);
        row.floor.rectTransform.anchorMax = new Vector2(1f, 1f);
        row.floor.rectTransform.offsetMin = Vector2.zero;
        row.floor.rectTransform.offsetMax = new Vector2(-8f, 0f);
        return row;
    }

    void UpdateSuggestions(string text)
    {
        if (suppressSuggestions || suggestionPanel == null)
            return;

        string query = NormalizeForSearch(text);
        var matches = FindSuggestions(query).Take(MaxSuggestions).ToList();
        if (matches.Count == 0)
        {
            HideSuggestions();
            return;
        }

        suggestionPanel.gameObject.SetActive(true);
        suggestionPanel.sizeDelta = new Vector2(196f, 8f + matches.Count * 20f);
        for (int i = 0; i < suggestionRows.Count; i++)
        {
            SuggestionRow row = suggestionRows[i];
            bool visible = i < matches.Count;
            row.root.SetActive(visible);
            if (!visible)
                continue;
            row.store = matches[i];
            row.name.text = matches[i].store_name;
            row.floor.text = InterFloorController.FloorName(matches[i].floor_number);
        }
        DrawCanvasOnTop(suggestionPanel.GetComponentInParent<Canvas>());
    }

    void HideSuggestions()
    {
        if (suggestionPanel != null)
            suggestionPanel.gameObject.SetActive(false);
    }

    static Image CreateImage(string name, Transform parent, Color colour)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.color = colour;
        image.raycastTarget = false;
        // Reuse the rounded sprite of the stock UI so panels get soft corners.
        image.sprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/UISprite.psd");
        image.type = Image.Type.Sliced;
        return image;
    }

    static TMP_Text CreateText(string name, Transform parent, string text, float size, Color colour, TextAlignmentOptions alignment)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var label = go.GetComponent<TextMeshProUGUI>();
        label.font = TMP_Settings.defaultFontAsset;
        label.text = text;
        label.fontSize = size;
        label.color = colour;
        label.alignment = alignment;
        label.raycastTarget = false;
        return label;
    }

    Vector3 ConvertCentroidToUnity(float centroidX, float centroidY, Transform plane, float floorNumber)
    {
        const float pixelToUnity = MallWorldScale.WorldUnitsPerPixel;
        float height = unit != null ? unit.floorHeight : MallWorldScale.FloorHeight;
        return new Vector3(-centroidX * pixelToUnity, height * floorNumber, centroidY * pixelToUnity);
    }

    void CreateStatusDisplay()
    {
        if (statusText != null)
            return;

        GameObject canvasObject = new GameObject("Navigation Status Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        Camera mainCamera = Camera.main;
        canvas.renderMode = mainCamera != null ? RenderMode.WorldSpace : RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        if (mainCamera != null)
        {
            canvas.worldCamera = mainCamera;
            canvasObject.transform.SetParent(mainCamera.transform, false);
            canvasObject.transform.localPosition = new Vector3(0f, -0.34f, 0.85f);
            canvasObject.transform.localRotation = Quaternion.identity;
            canvasObject.transform.localScale = Vector3.one * 0.001f;
            RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(1000f, 250f);
        }

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = mainCamera != null
            ? CanvasScaler.ScaleMode.ConstantPixelSize
            : CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        GameObject panelObject = new GameObject("Navigation Status Panel", typeof(RectTransform), typeof(Image));
        panelObject.transform.SetParent(canvasObject.transform, false);
        RectTransform panelRect = panelObject.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 1f);
        panelRect.anchorMax = new Vector2(0.5f, 1f);
        panelRect.pivot = new Vector2(0.5f, 1f);
        panelRect.anchoredPosition = new Vector2(0f, -24f);
        panelRect.sizeDelta = new Vector2(860f, 118f);
        Image panelImage = panelObject.GetComponent<Image>();
        panelImage.color = new Color(0.03f, 0.06f, 0.10f, 0.88f);
        panelImage.sprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/UISprite.psd");
        panelImage.type = Image.Type.Sliced;
        panelImage.pixelsPerUnitMultiplier = 0.25f; // larger corner radius
        panelImage.raycastTarget = false;

        // Thin accent line along the top of the panel.
        GameObject accentObject = new GameObject("Accent", typeof(RectTransform), typeof(Image));
        accentObject.transform.SetParent(panelObject.transform, false);
        RectTransform accentRect = accentObject.GetComponent<RectTransform>();
        accentRect.anchorMin = new Vector2(0.5f, 1f);
        accentRect.anchorMax = new Vector2(0.5f, 1f);
        accentRect.pivot = new Vector2(0.5f, 1f);
        accentRect.anchoredPosition = new Vector2(0f, -8f);
        accentRect.sizeDelta = new Vector2(120f, 4f);
        accentObject.GetComponent<Image>().color = AccentColour;
        accentObject.GetComponent<Image>().raycastTarget = false;

        GameObject textObject = new GameObject("Navigation Status", typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(panelObject.transform, false);
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(28f, 40f);
        textRect.offsetMax = new Vector2(-28f, -14f);

        statusText = textObject.GetComponent<TextMeshProUGUI>();
        statusText.font = TMP_Settings.defaultFontAsset;
        statusText.fontSize = 26f;
        statusText.color = Color.white;
        statusText.alignment = TextAlignmentOptions.Center;
        statusText.textWrappingMode = TextWrappingModes.Normal;
        statusText.enableAutoSizing = true;
        statusText.fontSizeMin = 18f;
        statusText.fontSizeMax = 26f;
        statusText.raycastTarget = false;

        // Second line: current floor, distance and time to go.
        detailText = CreateText("Navigation Detail", panelObject.transform, "", 19f, AccentColour, TextAlignmentOptions.Center);
        RectTransform detailRect = detailText.rectTransform;
        detailRect.anchorMin = new Vector2(0f, 0f);
        detailRect.anchorMax = new Vector2(1f, 0f);
        detailRect.pivot = new Vector2(0.5f, 0f);
        detailRect.anchoredPosition = new Vector2(0f, 10f);
        detailRect.sizeDelta = new Vector2(-56f, 30f);
        detailText.characterSpacing = 2f;
    }

    static Material overlayUIMaterial;

    static void DrawCanvasOnTop(Canvas canvas)
    {
        if (canvas == null || canvas.renderMode != RenderMode.WorldSpace)
            return;

        const float alwaysPass = (float)UnityEngine.Rendering.CompareFunction.Always;
        foreach (Graphic graphic in canvas.GetComponentsInChildren<Graphic>(true))
        {
            if (graphic is TMP_Text text)
            {
                // fontMaterial returns a per-object instance, so this does not touch the shared font asset.
                Material fontMaterial = text.fontMaterial;
                if (fontMaterial != null)
                    fontMaterial.SetFloat("unity_GUIZTestMode", alwaysPass);
                continue;
            }

            if (graphic.material != graphic.defaultMaterial)
                continue;

            if (overlayUIMaterial == null)
            {
                overlayUIMaterial = new Material(graphic.defaultMaterial) { name = "UI Always On Top" };
                overlayUIMaterial.SetFloat("unity_GUIZTestMode", alwaysPass);
            }
            graphic.material = overlayUIMaterial;
        }
    }

    void SetStatus(string message)
    {
        if (statusText != null)
            statusText.text = message;
    }
}
