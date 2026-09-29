using StarterAssets;
using UnityEngine;

// Adds a walking head-bob (vertical, driven by distance traveled) and a
// strafe tilt (roll) to the first-person camera target — tilts right while
// holding right, left while holding left, and eases back to level when
// neither is held, rather than a continuous side-to-side sway.
//
// This has to share cameraTarget's rotation with FirstPersonController,
// which OVERWRITES localRotation with a fresh pitch-only value every frame
// there's mouse/stick look input (and leaves it untouched otherwise). To
// avoid fighting that: each frame, check whether the rotation changed since
// the last value *we* wrote. If it did, FirstPersonController just
// overwrote it (our old tilt is already gone, so don't try to remove it
// again). If it didn't, our old tilt is still baked in and needs removing
// before adding the new one. Either way we end up with exactly
// (current pitch) * (this frame's tilt), never compounding.
public class HeadBob : MonoBehaviour
{
    public CharacterController controller;
    public Transform cameraTarget; // FirstPersonController's CinemachineCameraTarget
    public StarterAssetsInputs input;

    [Header("Bob")]
    public float strideLength = 2f; // world units per full bob cycle
    public float bobHeight = 0.05f;

    [Header("Strafe tilt")]
    public float maxTiltAngle = 4f; // degrees of roll while fully holding left/right
    public float tiltSpeed = 8f; // how quickly the tilt eases toward its target

    [Header("Smoothing")]
    public float blendSpeed = 8f; // how quickly bob fades in/out when starting/stopping moving

    private Vector3 basePosition;
    private float distanceTraveled;
    private float blend;
    private float currentTilt;
    private float lastAppliedTilt;
    private Quaternion lastAppliedRotation;
    private bool initialized;

    private void Start()
    {
        if (cameraTarget != null) basePosition = cameraTarget.localPosition;
    }

    private void LateUpdate()
    {
        if (controller == null || cameraTarget == null) return;

        Vector3 horizontalVelocity = controller.velocity;
        horizontalVelocity.y = 0f;
        float speed = horizontalVelocity.magnitude;
        bool moving = controller.isGrounded && speed > 0.15f;

        blend = Mathf.MoveTowards(blend, moving ? 1f : 0f, blendSpeed * Time.deltaTime);
        if (moving) distanceTraveled += speed * Time.deltaTime;

        float phase = distanceTraveled / strideLength * Mathf.PI * 2f;
        float bob = Mathf.Abs(Mathf.Sin(phase)) * bobHeight * blend; // always bobs up from rest, one bump per step

        cameraTarget.localPosition = basePosition + new Vector3(0f, bob, 0f);

        // strafeInput is -1 while holding left, +1 while holding right, 0 otherwise.
        // If tilt comes out backwards (leaning left while holding right), flip this sign.
        float strafeInput = input != null ? -input.move.x : 0f;
        float targetTilt = strafeInput * maxTiltAngle;
        currentTilt = Mathf.MoveTowards(currentTilt, targetTilt, maxTiltAngle * tiltSpeed * Time.deltaTime);

        bool overwrittenSinceLastFrame = !initialized || Quaternion.Angle(cameraTarget.localRotation, lastAppliedRotation) > 0.01f;
        Quaternion pitchOnly = overwrittenSinceLastFrame
            ? cameraTarget.localRotation
            : cameraTarget.localRotation * Quaternion.Euler(0f, 0f, -lastAppliedTilt);

        lastAppliedTilt = currentTilt;
        cameraTarget.localRotation = pitchOnly * Quaternion.Euler(0f, 0f, currentTilt);
        lastAppliedRotation = cameraTarget.localRotation;
        initialized = true;
    }
}
