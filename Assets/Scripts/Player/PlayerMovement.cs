using UnityEngine;

// Moves the player with the left stick, relative to the camera:
// pushing "up" moves away from the camera (toward the hoop in the
// behind-the-player view). Speed follows how far the stick is pushed,
// and R2 pressure blends from run speed up to sprint speed.
[RequireComponent(typeof(Rigidbody))]
public class PlayerMovement : MonoBehaviour
{
    [SerializeField] private PlayerMovementConfig _config;
    [SerializeField] private PlayerInputReader _input;
    [Tooltip("Movement directions are relative to this camera. Uses the main camera if empty.")]
    [SerializeField] private Transform _cameraTransform;

    private Rigidbody _rigidbody;

    // Current speed along the floor in m/s (for the debug overlay later).
    public float CurrentSpeed { get; private set; }

    private void Awake()
    {
        _rigidbody = GetComponent<Rigidbody>();
        if (_input == null) _input = GetComponent<PlayerInputReader>();
        if (_cameraTransform == null && Camera.main != null) _cameraTransform = Camera.main.transform;
    }

    // Physics runs at a fixed rate in FixedUpdate, so velocity changes here
    // are the same no matter the frame rate.
    private void FixedUpdate()
    {
        Vector2 stick = _input.Move;
        Vector3 moveDirection = GetCameraRelativeDirection(stick);

        // How far the stick is pushed (0..1), shaped by the response curve.
        // With a response of 1.5, a half push gives about 35% speed instead of 50%.
        float stickAmount = Mathf.Pow(stick.magnitude, _config.StickResponse);

        // R2 pressure blends between run and sprint speed.
        float maxSpeed = Mathf.Lerp(_config.RunSpeed, _config.SprintSpeed, _input.Sprint);
        Vector3 targetVelocity = moveDirection * (stickAmount * maxSpeed);

        // Only change the floor (X/Z) part of the velocity; gravity keeps the Y part.
        Vector3 velocity = _rigidbody.linearVelocity;
        Vector3 floorVelocity = new Vector3(velocity.x, 0f, velocity.z);

        // Speeding up uses acceleration; slowing down or cutting uses deceleration.
        // MoveTowards changes velocity by at most (rate * time) this step, which
        // is what gives the player a feeling of weight.
        bool speedingUp = targetVelocity.sqrMagnitude > floorVelocity.sqrMagnitude;
        float rate = speedingUp ? _config.Acceleration : _config.Deceleration;
        floorVelocity = Vector3.MoveTowards(floorVelocity, targetVelocity, rate * Time.fixedDeltaTime);

        _rigidbody.linearVelocity = new Vector3(floorVelocity.x, velocity.y, floorVelocity.z);
        CurrentSpeed = floorVelocity.magnitude;

        // Turn to face where the stick points (not where we're sliding),
        // so the player feels responsive.
        if (moveDirection.sqrMagnitude > 0.01f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(moveDirection);
            _rigidbody.MoveRotation(Quaternion.RotateTowards(
                _rigidbody.rotation, targetRotation, _config.TurnSpeed * Time.fixedDeltaTime));
        }
    }

    // Converts the 2D stick into a flat 3D direction based on where the camera faces.
    private Vector3 GetCameraRelativeDirection(Vector2 stick)
    {
        if (stick.sqrMagnitude < 0.0001f) return Vector3.zero;

        Vector3 forward = _cameraTransform != null ? _cameraTransform.forward : Vector3.forward;
        Vector3 right = _cameraTransform != null ? _cameraTransform.right : Vector3.right;

        // Flatten onto the floor so a camera looking down doesn't push the player into the ground.
        forward.y = 0f;
        right.y = 0f;

        Vector3 direction = forward.normalized * stick.y + right.normalized * stick.x;
        return direction.normalized;
    }
}
