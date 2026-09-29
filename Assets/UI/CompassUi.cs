using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// A heading-strip compass across the top of the screen: cardinal direction
// letters plus a dot for every active CompassMarker (NPCs, quest givers,
// quest items), positioned by bearing relative to the camera's current
// facing and scrolling as the camera turns. Builds its own Canvas at
// runtime (no scene/prefab wiring needed), matching GenerationDebugLog's
// approach for the same reason: nothing here is hand-placed in a scene.
public class CompassUi : MonoBehaviour
{
    private static CompassUi instance;

    [Tooltip("How many degrees of heading are visible across the compass bar's width at once.")]
    public float visibleDegrees = 180f;
    public float barWidth = 600f;
    public float barHeight = 36f;
    public float markerSize = 14f;
    [Tooltip("Distance at which a marker renders at exactly markerSize — closer scales up, farther scales down.")]
    public float markerReferenceDistance = 20f;
    public float markerMinSize = 6f;
    public float markerMaxSize = 26f;
    [Tooltip("Plain NPC dots farther than this many terrain chunks away are hidden. Highlighted markers (quests, Meet targets) are never hidden by distance.")]
    public float plainNpcMaxChunks = 3f;
    [Tooltip("Smallest size a plain NPC dot shrinks to right before it hits the cutoff distance.")]
    public float plainNpcMinSize = 3f;
    [Tooltip("Fallback chunk size if no TerrainChunkManager is found in the scene.")]
    public float fallbackChunkSize = 100f;

    private class MarkerDot
    {
        public RectTransform rect;
        public Image image;
    }

    private RectTransform bar;
    private Transform cameraTransform;
    private TerrainChunkManager terrainChunkManager;
    private Font font;
    private readonly Dictionary<string, RectTransform> directionLabels = new();
    private readonly Dictionary<CompassMarker, MarkerDot> markerDots = new();

    private static readonly (string label, float degrees)[] Directions =
    {
        ("N", 0f), ("NE", 45f), ("E", 90f), ("SE", 135f),
        ("S", 180f), ("SW", 225f), ("W", 270f), ("NW", 315f),
    };

    public static void EnsureExists()
    {
        if (instance != null) return;
        var go = new GameObject("CompassUi");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<CompassUi>();
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;

        // See GenerationDebugLog's matching comment: UI.Text needs an
        // explicit font (unlike TextMesh, which gets a working default
        // automatically) — try Unity's built-in resource names, then fall
        // back to pulling one straight from the OS.
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        if (font == null) font = Font.CreateDynamicFontFromOSFont(new[] { "Arial", "Segoe UI", "Liberation Sans" }, 24);

        BuildUi();
    }

    private void BuildUi()
    {
        var canvasObject = new GameObject("CompassCanvas");
        canvasObject.transform.SetParent(transform);

        var canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 900;
        canvasObject.AddComponent<CanvasScaler>();

        var barObject = new GameObject("CompassBar");
        barObject.transform.SetParent(canvasObject.transform, false);
        bar = barObject.AddComponent<RectTransform>();
        bar.anchorMin = new Vector2(0.5f, 1f);
        bar.anchorMax = new Vector2(0.5f, 1f);
        bar.pivot = new Vector2(0.5f, 1f);
        bar.sizeDelta = new Vector2(barWidth, barHeight);
        bar.anchoredPosition = new Vector2(0f, -16f);

        var background = barObject.AddComponent<Image>();
        background.color = new Color(0f, 0f, 0f, 0.35f);
        background.raycastTarget = false;

        barObject.AddComponent<RectMask2D>(); // clips anything scrolled past the bar's edges

        foreach ((string label, float _) in Directions)
            directionLabels[label] = BuildLabel(label, label == "N" ? Color.yellow : Color.white);
    }

    private RectTransform BuildLabel(string text, Color color)
    {
        var labelObject = new GameObject($"Dir_{text}");
        labelObject.transform.SetParent(bar, false);

        var rect = labelObject.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(40f, barHeight);

        var label = labelObject.AddComponent<Text>();
        label.font = font;
        label.fontSize = 16;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = color;
        label.text = text;
        label.raycastTarget = false;

        return rect;
    }

    private void LateUpdate()
    {
        if (cameraTransform == null)
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            cameraTransform = cam.transform;
        }

        if (terrainChunkManager == null) terrainChunkManager = FindFirstObjectByType<TerrainChunkManager>();

        float heading = cameraTransform.eulerAngles.y;

        foreach ((string label, float degrees) in Directions)
            PositionAtBearing(directionLabels[label], degrees, heading);

        UpdateMarkers(heading);
    }

    private void UpdateMarkers(float heading)
    {
        // Drop dots for markers that were destroyed or disabled since last frame.
        List<CompassMarker> stale = null;
        foreach (CompassMarker marker in markerDots.Keys)
        {
            if (marker != null && CompassMarker.Active.Contains(marker)) continue;
            stale ??= new List<CompassMarker>();
            stale.Add(marker);
        }
        if (stale != null)
        {
            foreach (CompassMarker marker in stale)
            {
                if (markerDots[marker].rect != null) Destroy(markerDots[marker].rect.gameObject);
                markerDots.Remove(marker);
            }
        }

        float chunkSize = terrainChunkManager != null ? terrainChunkManager.chunkSize : fallbackChunkSize;
        float plainNpcMaxDistance = plainNpcMaxChunks * chunkSize;

        foreach (CompassMarker marker in CompassMarker.Active)
        {
            if (marker == null) continue;

            if (!markerDots.TryGetValue(marker, out MarkerDot dot))
            {
                dot = BuildMarkerDot(marker);
                markerDots[marker] = dot;
            }

            if (!marker.IsVisible)
            {
                dot.rect.gameObject.SetActive(false);
                continue;
            }

            Vector3 toMarker = marker.transform.position - cameraTransform.position;
            toMarker.y = 0f;
            float distance = toMarker.magnitude;
            if (distance < 0.01f)
            {
                dot.rect.gameObject.SetActive(false);
                continue;
            }

            bool highlighted = marker.IsHighlighted;
            if (!highlighted && distance > plainNpcMaxDistance)
            {
                dot.rect.gameObject.SetActive(false);
                continue;
            }

            float bearing = Mathf.Atan2(toMarker.x, toMarker.z) * Mathf.Rad2Deg;
            if (bearing < 0f) bearing += 360f;

            dot.rect.gameObject.SetActive(true);
            float size;
            if (highlighted)
            {
                size = Mathf.Clamp(markerSize * (markerReferenceDistance / distance), markerMinSize, markerMaxSize);
            }
            else
            {
                // The inverse-distance curve above bottoms out at markerMinSize
                // within ~50 units, so every farther NPC looked identical. Plain
                // NPCs instead shrink steadily across the whole visible range,
                // down to plainNpcMinSize right at the cutoff.
                float t = Mathf.Clamp01(distance / plainNpcMaxDistance);
                size = Mathf.Lerp(markerMaxSize, plainNpcMinSize, Mathf.Sqrt(t));
            }
            dot.rect.sizeDelta = new Vector2(size, size);
            // A marker's color can change after its dot was first built —
            // e.g. NpcQuestGenerator recoloring a Meet quest's target NPC
            // gold. The dot's Image color used to only ever be set once, at
            // creation time, so that recoloring never actually showed up —
            // exactly the "told to look for a gold marker, found none" bug.
            dot.image.color = marker.color;
            PositionAtBearing(dot.rect, bearing, heading);

            // UI draws later siblings on top — pushing highlighted dots to the
            // end each frame keeps gold/green markers above the blue crowd,
            // including a plain NPC that was just recolored gold.
            if (highlighted) dot.rect.SetAsLastSibling();
        }
    }

    private MarkerDot BuildMarkerDot(CompassMarker marker)
    {
        var dotObject = new GameObject($"Marker_{marker.kind}");
        dotObject.transform.SetParent(bar, false);

        var rect = dotObject.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(markerSize, markerSize);

        var image = dotObject.AddComponent<Image>();
        image.color = marker.color;
        image.raycastTarget = false;

        return new MarkerDot { rect = rect, image = image };
    }

    // Maps a world bearing (0..360, 0 = north/+Z, matching Transform.eulerAngles.y)
    // to a screen X position along the bar, based on angular distance from
    // the camera's current heading — wraps correctly across the 0/360 seam
    // via Mathf.DeltaAngle, and hides anything outside the visible window.
    private void PositionAtBearing(RectTransform rect, float bearing, float heading)
    {
        float delta = Mathf.DeltaAngle(heading, bearing);
        float halfRange = visibleDegrees * 0.5f;

        if (Mathf.Abs(delta) > halfRange)
        {
            rect.gameObject.SetActive(false);
            return;
        }

        rect.gameObject.SetActive(true);
        rect.anchoredPosition = new Vector2((delta / halfRange) * (barWidth * 0.5f), 0f);
    }
}
