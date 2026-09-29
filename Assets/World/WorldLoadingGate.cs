using System.Collections;
using UnityEngine;

// Freezes player movement and shows a loading panel until generation has
// settled, then re-snaps the player onto the final ground height and
// releases control. By default this waits on biome shape AND whichever of
// biomeGenerator's own texture/tree/rock/building generators it has
// assigned — read directly from biomeGenerator rather than needing its own
// separate wiring, so there's only one place to configure which generators
// are active.
//
// Biome generation is the one stage that's ever actually load-bearing for
// safety (terrain height can change out from under the player once
// AI-generated biomes replace the defaults it spawned on, which can drop
// the player through/under the map) — the others are purely cosmetic, so
// waiting on them (waitForDownstreamGenerators) is about wanting a clean
// "reveal" instead of watching the world pop in and change around you, not
// about preventing a fall.
public class WorldLoadingGate : MonoBehaviour
{
    public TerrainChunkManager terrainChunkManager;
    public BiomeAiGenerator biomeGenerator; // if null, the gate releases immediately

    [Tooltip("Wait for biomeGenerator's own texture/tree/rock/building generators too (whichever of those it has assigned), not just biome shape. Turn off to release as soon as biome shape settles, same as the original behavior.")]
    public bool waitForDownstreamGenerators = true;

    [Tooltip("If off, the player can move freely the whole time (no input lock, no loading panel) — but is still teleported onto the correct ground once biome shape settles, so turning this off does not bring back the fall-through-the-map bug.")]
    public bool freezeMovement = true;

    [Tooltip("Player scripts to disable while loading (e.g. ThirdPersonController, StarterAssetsInputs). Only used when Freeze Movement is on.")]
    public MonoBehaviour[] playerControlScripts;

    [Tooltip("A panel/canvas shown while loading, hidden once released.")]
    public GameObject loadingUi;

    [Tooltip("Safety net: force-release the player after this many seconds even if a generation stage never fires OnFinished (e.g. a slow/stuck network call retrying for a while) — the player should never be stuck frozen indefinitely no matter what's slow upstream. 0 disables the timeout.")]
    public float maxWaitSeconds = 20f;

    private CharacterController characterController;
    private int pendingStages;
    private bool released;

    private void Start()
    {
        if (terrainChunkManager != null && terrainChunkManager.player != null)
            characterController = terrainChunkManager.player.GetComponent<CharacterController>();

        if (freezeMovement)
        {
            SetPlayerFrozen(true);
            if (loadingUi != null) loadingUi.SetActive(true);
        }

        pendingStages = 0;
        Watch(biomeGenerator);
        if (waitForDownstreamGenerators && biomeGenerator != null)
        {
            Watch(biomeGenerator.textureGenerator);
            Watch(biomeGenerator.treeGenerator);
            Watch(biomeGenerator.rockGenerator);
            Watch(biomeGenerator.buildingGenerator);
            Watch(biomeGenerator.structureGenerator);
            Watch(biomeGenerator.npcGenerator);
        }

        // Debug HUD logging is deliberately independent of
        // waitForDownstreamGenerators — you still want to see "TreeAiGenerator
        // ready" etc. pop up even when the player isn't frozen waiting on it.
        LogWhenFinished(biomeGenerator);
        if (biomeGenerator != null)
        {
            LogWhenFinished(biomeGenerator.textureGenerator);
            LogWhenFinished(biomeGenerator.treeGenerator);
            LogWhenFinished(biomeGenerator.rockGenerator);
            LogWhenFinished(biomeGenerator.buildingGenerator);
            LogWhenFinished(biomeGenerator.structureGenerator);
            LogWhenFinished(biomeGenerator.npcGenerator);
        }

        if (pendingStages == 0) Release();
        else if (maxWaitSeconds > 0f) StartCoroutine(ForceReleaseAfterTimeout());
    }

    private static void LogWhenFinished(MonoBehaviour behaviour)
    {
        if (behaviour is not IGenerationStage stage) return;

        string name = behaviour.GetType().Name;
        void Handler()
        {
            stage.OnFinished -= Handler;
            GenerationDebugLog.Show($"{name} ready");
        }
        stage.OnFinished += Handler;
    }

    private IEnumerator ForceReleaseAfterTimeout()
    {
        yield return new WaitForSeconds(maxWaitSeconds);
        if (released) yield break;

        Debug.LogWarning($"[WorldLoadingGate] Generation didn't finish within {maxWaitSeconds}s (a network call is likely still retrying/stuck) — releasing the player anyway rather than leaving them frozen.");
        Release();
    }

    private void Watch(MonoBehaviour behaviour)
    {
        if (behaviour is not IGenerationStage stage) return;

        pendingStages++;
        void Handler()
        {
            stage.OnFinished -= Handler;
            if (--pendingStages <= 0) Release();
        }
        stage.OnFinished += Handler;
    }

    private void Release()
    {
        if (released) return;
        released = true;

        GenerationDebugLog.Show("World ready");
        CompassUi.EnsureExists();
        QuestLogUi.EnsureExists();

        if (terrainChunkManager != null)
            terrainChunkManager.SpawnPlayerOnGround();

        if (!freezeMovement) return;

        if (loadingUi != null) loadingUi.SetActive(false);
        SetPlayerFrozen(false);
    }

    private void SetPlayerFrozen(bool frozen)
    {
        if (characterController != null) characterController.enabled = !frozen;

        foreach (MonoBehaviour script in playerControlScripts)
            if (script != null) script.enabled = !frozen;
    }
}
