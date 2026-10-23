using UnityEngine;

// Tuning values for player movement, stored as an asset so they can be
// changed in the Inspector (even during Play mode) and keep their values.
// Create one with: Assets > Create > Basketball > Player Movement Config.
[CreateAssetMenu(fileName = "PlayerMovementConfig", menuName = "Basketball/Player Movement Config")]
public class PlayerMovementConfig : ScriptableObject
{
    [Header("Speed (meters per second)")]
    [Tooltip("Top speed with the stick fully pushed and no sprint. A brisk NBA jog is about 4-5 m/s.")]
    [SerializeField] private float _runSpeed = 4.5f;
    [Tooltip("Top speed with the stick fully pushed and R2 fully pressed. NBA players top out around 8-9 m/s.")]
    [SerializeField] private float _sprintSpeed = 7.5f;
    [Tooltip("Shapes how speed follows the stick. 1 = linear. Above 1 = more control at low speeds (light pushes walk slower).")]
    [SerializeField, Range(0.5f, 3f)] private float _stickResponse = 1.5f;
    [Tooltip("Speed for the lightest push past the stick's deadzone, so a tiny push still walks slowly instead of barely moving.")]
    [SerializeField] private float _minWalkSpeed = 0.6f;

    [Header("Acceleration (meters per second, per second)")]
    [Tooltip("How quickly the player speeds up. Higher = snappier starts.")]
    [SerializeField] private float _acceleration = 20f;
    [Tooltip("How quickly the player slows down while the stick is still pushed (e.g. easing off the stick or R2).")]
    [SerializeField] private float _deceleration = 28f;
    [Tooltip("How quickly the player stops when the stick is let go. 35 stops a 7.5 m/s sprint in about 0.2 s and 0.8 m.")]
    [SerializeField] private float _stopDeceleration = 35f;

    [Header("Changing direction")]
    [Tooltip("How fast the direction of movement turns toward the stick, in degrees per second. Left = standing still, right = full sprint speed. Fast when slow (precise), slower at a sprint (a bit of weight).")]
    [SerializeField] private AnimationCurve _turnRateBySpeed = AnimationCurve.Linear(0f, 1080f, 1f, 400f);
    [Tooltip("If the stick points more than this many degrees away from where the player is moving, they plant and brake hard before going the new way, instead of running a wide arc.")]
    [SerializeField, Range(60f, 180f)] private float _plantAngle = 120f;
    [Tooltip("Braking while planting for a hard cut or reversal, in m/s per second.")]
    [SerializeField] private float _plantDeceleration = 40f;
    [Tooltip("Once a plant has slowed the player to this speed (m/s), they push off in the new direction.")]
    [SerializeField] private float _plantEndSpeed = 1f;

    [Header("Facing")]
    [Tooltip("How fast the body turns to face the way it's moving (or the stick when standing), in degrees per second.")]
    [SerializeField] private float _turnSpeed = 720f;

    public float RunSpeed => _runSpeed;
    public float SprintSpeed => _sprintSpeed;
    public float StickResponse => _stickResponse;
    public float MinWalkSpeed => _minWalkSpeed;
    public float Acceleration => _acceleration;
    public float Deceleration => _deceleration;
    public float StopDeceleration => _stopDeceleration;
    public float PlantAngle => _plantAngle;
    public float PlantDeceleration => _plantDeceleration;
    public float PlantEndSpeed => _plantEndSpeed;
    public float TurnSpeed => _turnSpeed;

    // Degrees per second the movement direction can turn at this speed (m/s).
    public float TurnRateAt(float speed)
    {
        return _turnRateBySpeed.Evaluate(speed / Mathf.Max(_sprintSpeed, 0.01f));
    }
}
