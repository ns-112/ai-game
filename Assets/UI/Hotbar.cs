using System.Collections.Generic;
using UnityEngine;

// A minimal inventory/hotbar HUD — picked-up quest items land here. Drawn
// via OnGUI (same reasoning as GenerationDebugLog: simplest option, needs
// no scene wiring, and Canvas rendering here turned out to have never
// actually been broken — the earlier "invisible UI" was just the Editor's
// Game-view zoom cropping the edges — but OnGUI is still the least code for
// a small fixed row of icons).
public class Hotbar : MonoBehaviour
{
    private static Hotbar instance;

    private class Slot
    {
        public string name;
        public Texture2D icon;
        public int count;
        public float expiresIn; // seconds remaining; <= 0 means "never expires"
    }

    public int maxSlots = 8;
    public float slotSize = 56f;
    public float slotSpacing = 6f;

    private readonly List<Slot> slots = new();
    private GUIStyle countStyle;

    // expiresIn > 0 auto-removes the item after that many seconds — quest
    // pickups are trophies confirming "you got it", not persistent
    // inventory, and there was previously no way for anything to ever leave
    // the hotbar at all. General items (expiresIn left at 0) stay until
    // explicitly removed via RemoveItem.
    public static void AddItem(string name, Texture2D icon, float expiresIn = 0f)
    {
        if (instance == null)
        {
            var go = new GameObject("Hotbar");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<Hotbar>();
        }
        instance.Add(name, icon, expiresIn);
    }

    public static void RemoveItem(string name, int count = 1)
    {
        if (instance == null) return;
        instance.Remove(name, count);
    }

    private void Add(string name, Texture2D icon, float expiresIn)
    {
        foreach (Slot slot in slots)
        {
            if (slot.name != name) continue;
            slot.count++;
            if (expiresIn > 0f) slot.expiresIn = expiresIn; // restacking refreshes the timer
            return;
        }

        if (slots.Count >= maxSlots) return; // full — extra items are dropped silently for now
        slots.Add(new Slot { name = name, icon = icon, count = 1, expiresIn = expiresIn });
    }

    private void Remove(string name, int count)
    {
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i].name != name) continue;
            slots[i].count -= count;
            if (slots[i].count <= 0) slots.RemoveAt(i);
            return;
        }
    }

    private void Update()
    {
        for (int i = slots.Count - 1; i >= 0; i--)
        {
            if (slots[i].expiresIn <= 0f) continue;
            slots[i].expiresIn -= Time.deltaTime;
            if (slots[i].expiresIn <= 0f) slots.RemoveAt(i);
        }
    }

    private void OnGUI()
    {
        if (slots.Count == 0) return;

        countStyle ??= new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.LowerRight,
            fontStyle = FontStyle.Bold,
        };
        countStyle.normal.textColor = Color.white;

        float totalWidth = slots.Count * slotSize + Mathf.Max(0, slots.Count - 1) * slotSpacing;
        float x = (Screen.width - totalWidth) * 0.5f;
        float y = Screen.height - slotSize - 16f;

        foreach (Slot slot in slots)
        {
            var rect = new Rect(x, y, slotSize, slotSize);
            GUI.Box(rect, GUIContent.none);

            if (slot.icon != null)
                GUI.DrawTexture(new Rect(rect.x + 4f, rect.y + 4f, rect.width - 8f, rect.height - 8f), slot.icon, ScaleMode.ScaleToFit);

            if (slot.count > 1)
                GUI.Label(rect, slot.count.ToString(), countStyle);

            x += slotSize + slotSpacing;
        }
    }
}
