using UnityEngine;

// The basketball. Bounce and friction come from its Physics Material and the
// materials on whatever it hits (floor, rim, backboard). Mass and air drag are
// on its Rigidbody. This script sets the few physics options Unity doesn't
// show in the Inspector, and gives other code (debug tools now, dribbling and
// shooting later) a simple way to place and launch the ball.
// The ball doesn't know about input or players; they tell it what to do.
[RequireComponent(typeof(Rigidbody))]
public class Basketball : MonoBehaviour
{
    [Tooltip("Top spin speed in radians per second. Unity's default (50) is close to what a ball rolling at 6 m/s needs, so we raise it for fast spins.")]
    [SerializeField] private float _maxSpinSpeed = 150f;
    [Tooltip("Extra solver passes for this ball. More passes = more accurate bounce heights, at a tiny performance cost.")]
    [SerializeField] private int _solverVelocityIterations = 8;

    private Rigidbody _rigidbody;
    private SphereCollider _collider;

    public Rigidbody Body => _rigidbody;
    public float Radius => _collider.radius * transform.lossyScale.x;

    private void Awake()
    {
        _rigidbody = GetComponent<Rigidbody>();
        _collider = GetComponent<SphereCollider>();
        _rigidbody.maxAngularVelocity = _maxSpinSpeed;
        _rigidbody.solverVelocityIterations = _solverVelocityIterations;
    }

    // Teleports the ball to a spot and stops all motion.
    public void Place(Vector3 position)
    {
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
