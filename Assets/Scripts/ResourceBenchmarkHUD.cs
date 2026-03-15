using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Lightweight benchmark HUD.
/// Uses manually assigned UI references and avoids auto-created UI.
/// </summary>
public class ResourceBenchmarkHUD : MonoBehaviour
{
    [Header("UI References (required)")]
    [Tooltip("Canvas used to render the benchmark HUD.")]
    public Canvas canvas;

    [Tooltip("Text component used to display log lines.")]
    public Text text;

    [Tooltip("Background image behind the text.")]
    public Image background;

    [Header("Layout (right third)")]
    [Range(0.0f, 1.0f)]
    [Tooltip("Left anchor of the HUD region (0.66 = right third of screen).")]
    public float leftAnchorX = 0.66f;

    [Tooltip("Padding inside the HUD region in pixels.")]
    public Vector2 padding = new Vector2(20f, 20f);

    [Header("Log buffer")]
    [Tooltip("Maximum number of lines stored internally.")]
    public int maxLines = 128;

    [Tooltip("Approximate line height in pixels used to decide how many lines fit vertically.")]
    public float manualLineHeight = 22f;

    [Header("Style")]
    [Range(0f, 1f)]
    [Tooltip("Alpha of the background rectangle.")]
    public float backgroundAlpha = 0.5f;

    // Rolling line buffer
    private readonly Queue<string> _lines = new Queue<string>(128);

    // Reused string builder to reduce allocations
    private readonly StringBuilder _sb = new StringBuilder(4096);

    // Cached UI rect transforms
    private RectTransform _canvasRt;
    private RectTransform _textRt;
    private RectTransform _bgRt;

    private void Awake()
    {
        CacheReferences();
        ApplyLayoutAndStyle();
        Clear();
        SetVisible(true);
    }

    private void OnValidate()
    {
        CacheReferences();

        if (!Application.isPlaying)
            return;

        ApplyLayoutAndStyle();
        RefreshVisuals();
    }

    private IEnumerator Start()
    {
        // Delay the first full layout pass by one frame
        yield return null;

        ApplyLayoutAndStyle();
        RefreshVisuals();
    }

    private void OnDestroy()
    {
        Clear();
        SetVisible(false);
    }

    /// <summary>
    /// Adds one line to the HUD buffer and refreshes the visible text.
    /// </summary>
    public void PushLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;

        if (!HasValidReferences())
            return;

        _lines.Enqueue(line);

        while (_lines.Count > maxLines)
            _lines.Dequeue();

        RebuildVisibleText();
        RefreshVisuals();
    }

    /// <summary>
    /// Clears all HUD text and hides the background.
    /// </summary>
    public void Clear()
    {
        _lines.Clear();
        _sb.Clear();

        if (text != null)
            text.text = string.Empty;

        if (background != null)
            background.enabled = false;
    }

    /// <summary>
    /// Shows or hides the entire HUD canvas.
    /// </summary>
    public void SetVisible(bool visible)
    {
        if (canvas != null)
            canvas.enabled = visible;

        if (!visible && background != null)
            background.enabled = false;
    }

    /// <summary>
    /// Caches required RectTransform references.
    /// </summary>
    private void CacheReferences()
    {
        if (canvas != null)
            _canvasRt = canvas.GetComponent<RectTransform>();

        if (text != null)
            _textRt = text.GetComponent<RectTransform>();

        if (background != null)
            _bgRt = background.GetComponent<RectTransform>();
    }

    /// <summary>
    /// Returns true when all required UI references are valid.
    /// </summary>
    private bool HasValidReferences()
    {
        return canvas != null &&
               text != null &&
               background != null &&
               _canvasRt != null &&
               _textRt != null &&
               _bgRt != null;
    }

    /// <summary>
    /// Applies layout anchors and visual style.
    /// </summary>
    private void ApplyLayoutAndStyle()
    {
        if (!HasValidReferences())
            return;

        if (canvas.renderMode != RenderMode.ScreenSpaceCamera)
            canvas.renderMode = RenderMode.ScreenSpaceCamera;

        if (canvas.worldCamera == null)
        {
            Camera parentCamera = GetComponentInParent<Camera>();
            if (parentCamera != null)
                canvas.worldCamera = parentCamera;
        }

        if (canvas.planeDistance <= 0f)
            canvas.planeDistance = 1f;

        // Text occupies the right part of the screen
        _textRt.anchorMin = new Vector2(leftAnchorX, 0f);
        _textRt.anchorMax = new Vector2(1f, 1f);
        _textRt.pivot = new Vector2(0f, 1f);

        _textRt.offsetMin = new Vector2(padding.x, padding.y);
        _textRt.offsetMax = new Vector2(-padding.x, -padding.y);

        // Background uses the same anchor region
        _bgRt.anchorMin = _textRt.anchorMin;
        _bgRt.anchorMax = _textRt.anchorMax;
        _bgRt.pivot = _textRt.pivot;

        Color c = background.color;
        c.r = 0f;
        c.g = 0f;
        c.b = 0f;
        c.a = Mathf.Clamp01(backgroundAlpha);
        background.color = c;

        text.raycastTarget = false;
        background.raycastTarget = false;
    }

    /// <summary>
    /// Rebuilds the visible text according to available screen height.
    /// </summary>
    private void RebuildVisibleText()
    {
        if (!HasValidReferences())
            return;

        Canvas.ForceUpdateCanvases();

        float canvasHeight = _canvasRt.rect.height;
        if (canvasHeight <= 0f)
            return;

        float availableHeight = canvasHeight - padding.y * 2f;
        if (availableHeight <= 0f)
            return;

        float lineHeight = manualLineHeight;
        if (lineHeight < 1f)
            lineHeight = 20f;

        int maxVisibleLines = Mathf.Max(1, Mathf.FloorToInt(availableHeight / lineHeight));

        List<string> allLines = new List<string>(_lines);
        int startIndex = Mathf.Max(0, allLines.Count - maxVisibleLines);

        _sb.Clear();

        bool first = true;
        for (int i = startIndex; i < allLines.Count; i++)
        {
            if (!first)
                _sb.Append('\n');

            _sb.Append(allLines[i]);
            first = false;
        }

        text.text = _sb.ToString();
    }

    /// <summary>
    /// Resizes the background box to match current text content.
    /// </summary>
    private void RefreshVisuals()
    {
        if (!HasValidReferences())
            return;

        Canvas.ForceUpdateCanvases();

        float canvasWidth = _canvasRt.rect.width;
        float canvasHeight = _canvasRt.rect.height;

        if (canvasWidth <= 0f || canvasHeight <= 0f)
            return;

        float textWidth = canvasWidth * (1f - leftAnchorX) - padding.x * 2f;
        if (textWidth < 10f)
            textWidth = 10f;

        float availableHeight = canvasHeight - padding.y * 2f;
        if (availableHeight < 10f)
            availableHeight = 10f;

        // Force the text area to use the available region
        _textRt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, textWidth);
        _textRt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, availableHeight);

        // Measure rendered text height
        var settings = text.GetGenerationSettings(new Vector2(textWidth, 0f));
        float preferredHeight =
            text.cachedTextGeneratorForLayout.GetPreferredHeight(text.text ?? string.Empty, settings) /
            text.pixelsPerUnit;

        preferredHeight = Mathf.Min(preferredHeight, availableHeight);

        const float bgMargin = 6f;

        _bgRt.anchorMin = new Vector2(leftAnchorX, 1f);
        _bgRt.anchorMax = new Vector2(leftAnchorX, 1f);
        _bgRt.pivot = new Vector2(0f, 1f);
        _bgRt.anchoredPosition = new Vector2(padding.x - bgMargin, -padding.y + bgMargin);

        _bgRt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, textWidth + bgMargin * 2f);
        _bgRt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, preferredHeight + bgMargin * 2f);

        background.enabled = !string.IsNullOrEmpty(text.text);
    }
}