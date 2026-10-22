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
    private float _slowDownMultiplier = 1f;
    private float _slowDownUntil = float.NegativeInfinity;
    private float _spinDegreesLeft;
    private float _spinSpeed;

    // Current speed along the floor in m/s (for the debug overlay later).
    public float CurrentSpeed { get; private set; }

    // Caps the top speed at this fraction (0-1) for a moment, like a hesitation
    // dribble. Speeding back up afterwards uses the normal acceleration.
    public void SlowDown(float speedMultiplier, float seconds)
    {
        _slowDownMultiplier = speedMultiplier;
        _slowDownUntil = Time.time + seconds;
    }

    // Turns the body a set amount (degrees, + = clockwise seen from above, to
    // the right) over this many seconds, like a spin move. While spinning, the
    // body doesn't turn toward the stick; it still moves where the stick points.
    public void Spin(float degrees, float seconds)
    {
        _spinDegreesLeft = degrees;
        _spinSpeed = Mathf.Abs(degrees) / Mathf.Max(seconds, 0.01f);
    }

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
        if (Time.time < _slowDownUntil) maxSpeed *= _slowDownMultiplier;
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

        // A spin turns the body at its own speed until it's done.
        if (_spinDegreesLeft != 0f)
        {
            float step = Mathf.Sign(_spinDegreesLeft) * Mathf.Min(_spinSpeed * Time.fixedDeltaTime, Mathf.Abs(_spinDegreesLeft));
            _spinDegreesLeft -= step;
            _rigidbody.MoveRotation(_rigidbody.rotation * Quaternion.Euler(0f, step, 0f));
        }
        // Otherwise turn to face where the stick points (not where we're sliding),
        // so the player feels responsive.
        else if (moveDirection.sqrMagnitude > 0.01f)
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
