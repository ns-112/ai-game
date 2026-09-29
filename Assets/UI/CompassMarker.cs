using System;
using System.Collections.Generic;
using UnityEngine;

// Attach to anything that should show up as a dot on CompassUi — NPCs, quest
// givers, quest items. Registers itself in a static list CompassUi reads
// every frame to compute bearings; visibility (IsVisible) lets quest-related
// markers hide themselves when not currently relevant (e.g. a quest item
// before its quest is accepted) without CompassUi needing any quest-specific
// logic of its own — it just asks each marker "should you be shown".
public class CompassMarker : MonoBehaviour
{
    public enum MarkerKind { Npc, QuestGiver, QuestItem, QuestLocation }

    // The plain, un-highlighted color every NPC spawns with — shared so
    // whatever temporarily recolors a marker (e.g. NpcQuestGenerator
    // marking a Meet quest's target gold) has a single source of truth to
    // restore instead of hardcoding the same value in two places and
    // risking them drifting apart.
    public static readonly Color DefaultNpcColor = new(0.4f, 0.8f, 1f);

    public MarkerKind kind = MarkerKind.Npc;
    public Color color = Color.white;
    public string label = "";

    // Left null (always visible) for plain NPCs. QuestGiver/QuestItem set
    // this to a QuestManager check in their own Awake().
    public Func<bool> isVisible;

    public static readonly List<CompassMarker> Active = new();

    public bool IsVisible => isVisible == null || isVisible();

    // Anything that isn't a plain, un-highlighted NPC — quest givers, quest
    // items/locations, and Meet targets recolored gold. CompassUi draws these
    // on top of plain NPC dots and never distance-culls them.
    public bool IsHighlighted => kind != MarkerKind.Npc || color != DefaultNpcColor;

    private void OnEnable() => Active.Add(this);
    private void OnDisable() => Active.Remove(this);
}
