using UnityEngine;

// Dribbling. The ball stays a real physics object the whole time: the hand
// pushes it down, the floor bounces it back up, and when it comes back up to
// the hand it gets pushed down again. While it bounces, it's steered sideways
// so it stays at the player's right side (a stand-in for the hand guiding it).
//   - walk up to a loose ball to pick it up (no rebounding yet, just a grab radius)
//   - sprinting (R2) dribbles lower and harder, so the bounces come faster
//   - if the ball gets knocked too far away (say it hits the pole), the dribble is lost
// PlayerShooter calls Gather() to pick the ball up into both hands for a shot,
// and LetGo() when the shot leaves the hands. Right-stick dribble moves come later.
[RequireComponent(typeof(Rigidbody))]
public class PlayerDribbler : MonoBehaviour
{
    private enum State { Loose, Dribbling, Holding }

    [SerializeField] private PlayerInputReader _input;
    [SerializeField] private Basketball _ball;

    [Header("Dribble (heights are the ball's center, in meters)")]
    [Tooltip("How high the ball comes back up to the hand when walking or running. About waist height.")]
    [SerializeField] private float _handHeight = 1f;
    [Tooltip("Hand height with R2 fully pressed. Lower means quicker bounces.")]
    [SerializeField] private float _sprintHandHeight = 0.8f;
    [Tooltip("How fast the hand pushes the ball down, in m/s. The push has to make up for the energy the floor bounce loses: below about 3, the ball won't come back up to the hand.")]
    [SerializeField] private float _pushSpeed = 4f;
    [Tooltip("Push speed with R2 fully pressed.")]
    [SerializeField] private float _sprintPushSpeed = 5f;

    [Header("Where the ball is dribbled (relative to the player)")]
    [Tooltip("How far to the player's right the ball bounces, in meters. The capsule's radius is 0.4 and the ball's 0.12.")]
    [SerializeField] private float _sideOffset = 0.6f;
    [Tooltip("How far in front of the player's center the ball bounces, in meters.")]
    [SerializeField] private float _forwardOffset = 0.25f;
    [Tooltip("How strongly the ball is steered back under the hand, per second. Higher = tighter handle, lower = looser dribble.")]
    [SerializeField] private float _steering = 10f;
    [Tooltip("Largest steering correction, in m/s on top of the player's own speed.")]
    [SerializeField] private float _maxSteeringSpeed = 3f;
    [Tooltip("If the ball ends up this far from the player's center (meters, along the floor), the dribble is lost.")]
    [SerializeField] private float _loseDistance = 1.5f;

    [Header("Picking up a loose ball")]
    [Tooltip("A loose ball this close to the player's center (meters, along the floor) gets picked up.")]
    [SerializeField] private float _pickupRadius = 0.9f;
    [Tooltip("Only pick up balls whose center is lower than this, so a ball flying past overhead isn't grabbed.")]
    [SerializeField] private float _pickupMaxHeight = 1.6f;
    [Tooltip("Seconds after a shot before the player can pick the ball up again, so the shot isn't grabbed right out of the hands.")]
    [SerializeField] private float _pickupDelayAfterShot = 1f;

    [Header("Holding (while charging a shot)")]
    [Tooltip("How far in front of the player's center the ball is held, in meters.")]
    [SerializeField] private float _holdForward = 0.35f;
    [Tooltip("Height of the ball's center while held, in meters. About chest height.")]
    [SerializeField] private float _holdHeight = 1.3f;

    private Rigidbody _body;
    private Collider _collider;
    private State _state = State.Loose;
    private bool _wasRising;
    private float _letGoTime = float.NegativeInfinity;

    public bool HasBall => _state != State.Loose;

    private void Awake()
    {
        _body = GetComponent<Rigidbody>();
        _collider = GetComponent<Collider>();
        if (_input == null) _input = GetComponent<PlayerInputReader>();
    }

    private void FixedUpdate()
    {
        if (_ball == null) return;

        switch (_state)
        {
            case State.Loose:
                if (CanPickUp()) SetState(State.Dribbling);
                break;
            case State.Dribbling:
                Dribble();
                break;
            case State.Holding:
                // Something else (like the R/T test keys) took the ball out of the hands.
                if (!_ball.IsHeld) SetState(State.Loose);
                else _ball.MoveHeld(HoldPoint());
                break;
        }
    }

    // Picks the ball up out of the dribble into both hands. Returns false if
    // the player isn't dribbling (no ball to shoot).
    public bool Gather()
    {
        if (_state != State.Dribbling) return false;
        _ball.Hold();
        SetState(State.Holding);
        return true;
    }

    // The ball leaves the hands (a shot). It's a normal physics ball again.
    public void LetGo()
    {
        if (_state != State.Holding) return;
        _ball.Release();
        _letGoTime = Time.time;
        SetState(State.Loose);
    }

    private void Dribble()
    {
        Vector3 ballPosition = _ball.Body.position;
        Vector3 fromPlayer = ballPosition - _body.position;
        fromPlayer.y = 0f;
        if (fromPlayer.magnitude > _loseDistance)
        {
            Debug.Log("Lost the dribble.");
            SetState(State.Loose);
            return;
        }

        float handHeight = Mathf.Lerp(_handHeight, _sprintHandHeight, _input.Sprint);
        float pushSpeed = Mathf.Lerp(_pushSpeed, _sprintPushSpeed, _input.Sprint);

        // Sideways: the ball moves with the player, plus a correction that pulls it
        // back under the hand. Clamping the correction keeps it from snapping.
        Vector3 playerVelocity = _body.linearVelocity;
        playerVelocity.y = 0f;
        Vector3 toHand = HandPoint() - ballPosition;
        toHand.y = 0f;
        Vector3 horizontal = playerVelocity + Vector3.ClampMagnitude(toHand * _steering, _maxSteeringSpeed);

        // Up and down: gravity and the floor bounce do the work. The hand only
        // pushes the ball back down when it comes up to the hand, or when it
        // tops out below the hand (the hand reaches down to meet it).
        float vertical = _ball.Body.linearVelocity.y;
        bool reachedHand = vertical > 0f && ballPosition.y >= handHeight;
        bool toppedOutLow = vertical <= 0f && _wasRising;
        if (reachedHand || toppedOutLow)
        {
            vertical = -pushSpeed;
            // A real hand also stops the ball spinning from its last floor contact.
            _ball.Body.angularVelocity = Vector3.zero;
        }
        else if (ballPosition.y < _ball.Radius + 0.02f && Mathf.Abs(vertical) < 0.2f)
        {
            // Resting or rolling on the floor (like a ball just picked up): scoop it
            // up. Rising to height h needs a speed of sqrt(2 * g * h); 10% extra so
            // it reaches the hand instead of stopping just short of it.
            float rise = handHeight - ballPosition.y;
            vertical = Mathf.Sqrt(2f * -Physics.gravity.y * rise) * 1.1f;
        }
        _wasRising = vertical > 0f;

        _ball.Body.linearVelocity = new Vector3(horizontal.x, vertical, horizontal.z);
    }

    private bool CanPickUp()
    {
        if (_ball.IsHeld || Time.time - _letGoTime < _pickupDelayAfterShot) return false;

        Vector3 fromPlayer = _ball.Body.position - _body.position;
        fromPlayer.y = 0f;
        return fromPlayer.magnitude <= _pickupRadius && _ball.Body.position.y <= _pickupMaxHeight;
    }

    private void SetState(State state)
    {
        _state = state;
        _wasRising = false;
        // While the player has the ball, it passes through their capsule, the way
        // a real ball moves around a player's legs and body instead of bumping off.
        Physics.IgnoreCollision(_collider, _ball.GetComponent<Collider>(), state != State.Loose);
    }

    // The physics position and rotation (not the smoothed transform) so the
    // ball follows where the player really is this physics step.
    private Vector3 HandPoint()
    {
        return _body.position + _body.rotation * new Vector3(_sideOffset, 0f, _forwardOffset);
    }

    private Vector3 HoldPoint()
    {
        Vector3 point = _body.position + _body.rotation * Vector3.forward * _holdForward;
        point.y = _holdHeight;
        return point;
    }
}
