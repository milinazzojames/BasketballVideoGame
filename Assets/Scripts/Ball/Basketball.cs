using UnityEngine;

// The basketball. Bounce and friction come from its Physics Material and the
// materials on whatever it hits (floor, rim, backboard). Mass and air drag are
// on its Rigidbody. This script sets the few physics options Unity doesn't
// show in the Inspector, and gives other code (debug tools, dribbling and
// shooting) a simple way to place, hold and launch the ball.
// The ball doesn't know about input or players; they tell it what to do.
[RequireComponent(typeof(Rigidbody))]
public class Basketball : MonoBehaviour
{
    [Tooltip("Top spin speed in radians per second. Unity's default (50) is close to what a ball rolling at 6 m/s needs, so we raise it for fast spins.")]
    [SerializeField] private float _maxSpinSpeed = 150f;
    [Tooltip("Extra solver passes for this ball. More passes = more accurate bounce heights, at a tiny performance cost.")]
    [SerializeField] private int _solverVelocityIterations = 8;

    [Header("Safety reset")]
    [Tooltip("If the ball's center ever drops below this height (meters), it fell off the world, so it's put back on the court.")]
    [SerializeField] private float _resetBelowHeight = -2f;
    [Tooltip("Where a ball that fell off the world is put back (the scene builder sets this to the free-throw line).")]
    [SerializeField] private Vector3 _resetPosition = new Vector3(0f, 1f, 8.5f);

    private Rigidbody _rigidbody;
    private SphereCollider _collider;
    private CollisionDetectionMode _collisionMode;

    public Rigidbody Body => _rigidbody;
    public float Radius => _collider.radius * transform.lossyScale.x;
    // True while something holds the ball still (like a player gathering it for a shot).
    public bool IsHeld => _rigidbody.isKinematic;

    private void Awake()
    {
        _rigidbody = GetComponent<Rigidbody>();
        _collider = GetComponent<SphereCollider>();
        _rigidbody.maxAngularVelocity = _maxSpinSpeed;
        _rigidbody.solverVelocityIterations = _solverVelocityIterations;
        _collisionMode = _rigidbody.collisionDetectionMode;
    }

    private void FixedUpdate()
    {
        // The walls around the court should stop this, but just in case.
        if (!IsHeld && _rigidbody.position.y < _resetBelowHeight)
        {
            Debug.Log("Ball fell off the court; putting it back.");
            Place(_resetPosition);
        }
    }

    // Stops physics from moving the ball, so it can be carried with MoveHeld.
    // A held (kinematic) ball can't use Continuous Dynamic collision detection,
    // so it switches to Speculative until it's released.
    public void Hold()
    {
        if (IsHeld) return;
        _rigidbody.linearVelocity = Vector3.zero;
        _rigidbody.angularVelocity = Vector3.zero;
        _rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        _rigidbody.isKinematic = true;
    }

    // Moves a held ball. Call it from FixedUpdate; MovePosition keeps the motion smooth.
    public void MoveHeld(Vector3 position)
    {
        _rigidbody.MovePosition(position);
    }

    // Hands the ball back to physics.
    public void Release()
    {
        if (!IsHeld) return;
        _rigidbody.isKinematic = false;
        _rigidbody.collisionDetectionMode = _collisionMode;
    }

    // Teleports the ball to a spot and stops all motion.
    public void Place(Vector3 position)
    {
        Release();
        _rigidbody.position = position;
        transform.position = position;
        _rigidbody.linearVelocity = Vector3.zero;
        _rigidbody.angularVelocity = Vector3.zero;
    }

    // Starts the ball moving. Spin is in radians per second around the given axis
    // (backspin on a shot is spin around the shooter's left-right axis).
    public void Launch(Vector3 velocity, Vector3 spin)
    {
        _rigidbody.linearVelocity = velocity;
        _rigidbody.angularVelocity = spin;
    }
}
