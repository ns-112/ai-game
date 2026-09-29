using UnityEngine;

// Keeps a flat sprite quad facing a target on the Y axis only (like a
// classic 2D billboard character), so it reads as a standing figure from
// any horizontal viewing angle instead of looking like a flat card. Faces
// the main camera by default; NpcSocialize temporarily overrides the target
// to the NPC's chat partner instead, for the "turn to face each other"
// beat, then clears the override to resume facing the camera.
public class NpcBillboard : MonoBehaviour
{
    // TextMesh's own readable face is on the opposite side from our custom
    // sprite quad's — the same "forward points at the viewer" rotation that
    // correctly faces the character sprite at the camera shows TextMesh
    // labels from behind (backwards/mirrored text), since TextMesh's glyphs
    // are only readable from its LOCAL -Z side, not +Z. Set true on a label
    // GameObject to flip which way this component points its forward axis.
    public bool flip180;

    private Transform cameraTransform;
    private Transform facingOverride;

    public void SetFacingOverride(Transform target) => facingOverride = target;
    public void ClearFacingOverride() => facingOverride = null;

    private void LateUpdate()
    {
        Transform target = facingOverride;
        if (target == null)
        {
            if (cameraTransform == null)
            {
                Camera cam = Camera.main;
                if (cam == null) return;
                cameraTransform = cam.transform;
            }
            target = cameraTransform;
        }

        // Forward points TOWARD the target (camera, or chat partner) by
        // default, so the sprite's front face is what the target sees; when
        // flipped, forward points AWAY from the target instead, so the
        // opposite (-Z) side — TextMesh's readable side — faces the target.
        Vector3 lookDir = target.position - transform.position;
        if (flip180) lookDir = -lookDir;
        lookDir.y = 0f;
        if (lookDir.sqrMagnitude < 0.0001f) return;
        transform.rotation = Quaternion.LookRotation(lookDir);
    }
}
