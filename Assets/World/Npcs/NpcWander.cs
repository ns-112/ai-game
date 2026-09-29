using UnityEngine;

// Wanders the NPC in slow loops around its spawn point, alternating between
// picking a random nearby destination and pausing there a while. Height at
// both the current position (while moving) and any candidate destination is
// read via TerrainChunkManager.GetGroundHeight — the raycast-based query
// against the actual streamed-in collider, not the smooth analytic height —
// so the NPC stays glued to real slopes/ledges instead of the idealized
// terrain function.
public class NpcWander : MonoBehaviour
{
    public float wanderRadius = 20f;
    public float moveSpeed = 1.5f;
    public float pauseDurationMin = 1f;
    public float pauseDurationMax = 4f;

    private TerrainChunkManager terrainChunkManager;
    private Vector3 anchor;
    private Vector3 destination;
    private float pauseTimer;
    private bool paused = true; // starts paused so a freshly spawned NPC doesn't immediately dash off

    // A count rather than a bool: both NpcSocialize (while "chatting") and
    // NpcInteractable (while the player is in talk range) call this
    // independently, each pairing one true with one later false. A plain
    // bool would let one system's "resume" incorrectly override the other's
    // still-active "pause" — e.g. the player walks up mid-chat, then walks
    // away first, which shouldn't resume wandering while the chat is still
    // going. Only reaching zero actually resumes movement.
    private int externalPauseCount;

    public void Initialize(TerrainChunkManager manager, Vector3 spawnPosition)
    {
        terrainChunkManager = manager;
        anchor = spawnPosition;
        destination = spawnPosition;
        pauseTimer = Random.Range(pauseDurationMin, pauseDurationMax);
    }

    public void SetExternallyPaused(bool value) =>
        externalPauseCount = Mathf.Max(0, externalPauseCount + (value ? 1 : -1));

    private void Update()
    {
        if (terrainChunkManager == null || externalPauseCount > 0) return;

        if (paused)
        {
            pauseTimer -= Time.deltaTime;
            if (pauseTimer <= 0f) PickNewDestination();
            return;
        }

        Vector3 toDestination = destination - transform.position;
        toDestination.y = 0f;
        if (toDestination.magnitude < 0.2f)
        {
            paused = true;
            pauseTimer = Random.Range(pauseDurationMin, pauseDurationMax);
            return;
        }

        Vector3 step = toDestination.normalized * (moveSpeed * Time.deltaTime);
        Vector3 newPos = transform.position + step;
        newPos.y = terrainChunkManager.GetGroundHeight(newPos.x, newPos.z);
        transform.position = newPos;
    }

    private void PickNewDestination()
    {
        Vector2 offset = Random.insideUnitCircle * wanderRadius;
        Vector3 candidate = anchor + new Vector3(offset.x, 0f, offset.y);
        candidate.y = terrainChunkManager.GetGroundHeight(candidate.x, candidate.z);
        destination = candidate;
        paused = false;
    }
}
