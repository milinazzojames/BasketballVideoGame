using UnityEngine;

// Moves the player with the left stick, relative to the court:
// pushing "up" moves toward the hoop (away from the behind-the-player
// camera). Speed follows how far the stick is pushed,
// and R2 pressure blends from run speed up to sprint speed.
[RequireComponent(typeof(Rigidbody))]
public class PlayerMovement : MonoBehaviour
{
    [SerializeField] private PlayerMovementConfig _config;
    [SerializeField] private PlayerInputReader _input;
    [Tooltip("Movement directions come from this camera's court directions (up = toward the hoop). Uses the main camera if empty.")]
    [SerializeField] private Transform _cameraTransform;
    [Tooltip("A jump only starts with the floor within this distance below the capsule, in meters.")]
    [SerializeField] private float _groundCheckDistance = 0.1f;

    private Rigidbody _rigidbody;
    private float _capsuleHalfHeight = 1f;
    private readonly RaycastHit[] _groundHits = new RaycastHit[8];
    private float _slowDownMultiplier = 1f;
    private float _slowDownUntil = float.NegativeInfinity;
    private float _spinDegreesLeft;
    private float _spinSpeed;
    private bool _hasFacingOverride;
    private Vector3 _facingOverride;
    private float _facingOverrideSpeed;

    // Current speed along the floor in m/s (for the debug overlay later).
    public float CurrentSpeed { get; private set; }
    // True while braking hard for a sharp cut or reversal (for the debug overlay later).
    public bool IsPlanting { get; private set; }

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

    // Turns the body toward a direction for this physics step, at this turn speed
    // (degrees per second), instead of facing the way it's moving. Call it every
    // step while it should apply (the shooter does, to face the hoop). It also
    // cuts a spin move short. The player still moves where the stick points.
    public void FaceDirection(Vector3 direction, float degreesPerSecond)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f) return;
        _hasFacingOverride = true;
        _facingOverride = direction.normalized;
        _facingOverrideSpeed = degreesPerSecond;
    }

    // Jumps straight up at this speed (m/s), if standing on the floor. Gravity
    // brings the player back down; the stick still moves them while in the air.
    // Returns false if they're already in the air.
    public bool Jump(float upSpeed)
    {
        if (!IsOnFloor()) return false;
        Vector3 velocity = _rigidbody.linearVelocity;
        velocity.y = upSpeed;
        _rigidbody.linearVelocity = velocity;
        return true;
    }

    // Looks straight down from the capsule's center for the floor. Things with
    // a Rigidbody (the ball, the player's own body parts) don't count.
    private bool IsOnFloor()
    {
        int count = Physics.RaycastNonAlloc(_rigidbody.position, Vector3.down, _groundHits,
            _capsuleHalfHeight + _groundCheckDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            if (_groundHits[i].collider.attachedRigidbody == null) return true;
        }
        return false;
    }

    private void Awake()
    {
        _rigidbody = GetComponent<Rigidbody>();
        if (TryGetComponent(out CapsuleCollider capsule)) _capsuleHalfHeight = capsule.height * transform.lossyScale.y / 2f;
        if (_input == null) _input = GetComponent<PlayerInputReader>();
        if (_cameraTransform == null && Camera.main != null) _cameraTransform = Camera.main.transform;
    }

    // Physics runs at a fixed rate in FixedUpdate, so velocity changes here
    // are the same no matter the frame rate.
    private void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        Vector2 stick = _input.Move;
        bool stickPushed = stick.sqrMagnitude > 0.0001f;
        Vector3 stickDirection = GetCourtRelativeDirection(stick);

        // Only change the floor (X/Z) part of the velocity; gravity keeps the Y part.
        Vector3 velocity = _rigidbody.linearVelocity;
        Vector3 floorVelocity = new Vector3(velocity.x, 0f, velocity.z);

        // Speed and direction are handled separately. Changing the whole
        // velocity vector at once makes a cut drift the old way and lose speed,
        // which feels like ice.
        float speed = floorVelocity.magnitude;
        Vector3 direction = speed > 0.05f ? floorVelocity / speed : stickDirection;
        IsPlanting = false;

        if (!stickPushed)
        {
            // Stick let go: just brake to a stop.
            speed = Mathf.MoveTowards(speed, 0f, _config.StopDeceleration * dt);
        }
        else
        {
            // How far the stick is pushed (0..1), shaped by the response curve.
            // With a response of 1.5, a half push gives about 35% of the way from
            // a slow walk to top speed instead of 50%. The lightest push still walks.
            float stickAmount = Mathf.Pow(Mathf.Clamp01(stick.magnitude), _config.StickResponse);

            // R2 pressure blends between run and sprint speed.
            float maxSpeed = Mathf.Lerp(_config.RunSpeed, _config.SprintSpeed, _input.Sprint);
            if (Time.time < _slowDownUntil) maxSpeed *= _slowDownMultiplier;
            float targetSpeed = Mathf.Lerp(Mathf.Min(_config.MinWalkSpeed, maxSpeed), maxSpeed, stickAmount);

            if (speed > _config.PlantEndSpeed && Vector3.Angle(direction, stickDirection) > _config.PlantAngle)
            {
                // Hard cut or reversal: plant and brake hard along the old line,
                // then push off the new way once slow (next branch), instead of
                // running a wide arc.
                IsPlanting = true;
                speed = Mathf.MoveTowards(speed, 0f, _config.PlantDeceleration * dt);
            }
            else
            {
                // Done planting (or barely moving): head straight the new way.
                if (speed <= _config.PlantEndSpeed && Vector3.Angle(direction, stickDirection) > _config.PlantAngle)
                {
                    direction = stickDirection;
                }

                // Turn the direction toward the stick, keeping the speed. Slow
                // movement turns fast (precise), a sprint turns slower (weight).
                float maxRadians = _config.TurnRateAt(speed) * Mathf.Deg2Rad * dt;
                direction = Vector3.RotateTowards(direction, stickDirection, maxRadians, 0f);

                float rate = targetSpeed > speed ? _config.Acceleration : _config.Deceleration;
                speed = Mathf.MoveTowards(speed, targetSpeed, rate * dt);
            }
        }

        floorVelocity = direction * speed;
        _rigidbody.linearVelocity = new Vector3(floorVelocity.x, velocity.y, floorVelocity.z);
        CurrentSpeed = speed;

        // Something asked the body to face a set way this step (like the hoop for a shot).
        if (_hasFacingOverride)
        {
            _hasFacingOverride = false;
            _spinDegreesLeft = 0f;
            _rigidbody.MoveRotation(Quaternion.RotateTowards(
                _rigidbody.rotation, Quaternion.LookRotation(_facingOverride), _facingOverrideSpeed * dt));
            return;
        }

        // A spin turns the body at its own speed until it's done.
        if (_spinDegreesLeft != 0f)
        {
            float step = Mathf.Sign(_spinDegreesLeft) * Mathf.Min(_spinSpeed * dt, Mathf.Abs(_spinDegreesLeft));
            _spinDegreesLeft -= step;
            _rigidbody.MoveRotation(_rigidbody.rotation * Quaternion.Euler(0f, step, 0f));
            return;
        }

        // Otherwise face the way we're actually moving, so the feet don't slide
        // sideways. Nearly stopped, face the stick instead, so the player can
        // turn on the spot.
        Vector3 faceDirection = speed > _config.PlantEndSpeed ? direction : stickDirection;
        if (faceDirection.sqrMagnitude > 0.01f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(faceDirection);
            _rigidbody.MoveRotation(Quaternion.RotateTowards(
                _rigidbody.rotation, targetRotation, _config.TurnSpeed * dt));
        }
    }

    // Converts the 2D stick into a flat 3D direction on the court
    // (up = toward the hoop), using the camera's fixed court directions.
    private Vector3 GetCourtRelativeDirection(Vector2 stick)
    {
        if (stick.sqrMagnitude < 0.0001f) return Vector3.zero;
        return BehindPlayerCamera.StickToFloor(stick, _cameraTransform).normalized;
    }
}
