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

    [Header("Acceleration (meters per second, per second)")]
    [Tooltip("How quickly the player speeds up. Higher = snappier starts.")]
    [SerializeField] private float _acceleration = 12f;
    [Tooltip("How quickly the player slows down or changes direction. Higher = sharper stops and cuts.")]
    [SerializeField] private float _deceleration = 18f;

    [Header("Turning")]
    [Tooltip("How fast the player turns to face the stick direction, in degrees per second.")]
    [SerializeField] private float _turnSpeed = 720f;

    public float RunSpeed => _runSpeed;
    public float SprintSpeed => _sprintSpeed;
    public float StickResponse => _stickResponse;
    public float Acceleration => _acceleration;
    public float Deceleration => _deceleration;
    public float TurnSpeed => _turnSpeed;
}
