using UnityEngine;

// Dribbling. The ball stays a real physics object the whole time: the hand
// pushes it down, the floor bounces it back up, and when it comes back up to
// the hand it gets pushed down again. While it bounces, it's steered sideways
// so it stays at the side of the hand that has it (a stand-in for the hand guiding it).
//   - walk up to a loose ball to pick it up (no rebounding yet, just a grab radius)
//   - sprinting (R2) dribbles lower and harder, so the bounces come faster
//   - standing still (left stick centered) is a low, hard pound dribble
//   - protecting (L2) is a low, tight dribble, with the ball kept on the side
//     away from the hoop (later: away from the defender)
//   - right-stick dribble moves. Each one is still physics: the hand pushes the
//     ball down, guides it to one bounce, and it comes up into the hand it ends in.
//     Directions are relative to the hand that has the ball:
//       flick toward the other hand              = crossover (bounce in front)
//       flick back, diagonally toward other hand = between the legs (bounce under the body)
//       flick straight back                      = behind the back (bounce behind the body)
//       flick forward or toward the ball's side  = hesitation (same hand, quick slowdown)
//       roll the stick through a half circle     = spin move (body spins, ends in the other hand)
//   - if the ball gets knocked too far away (say it hits the pole), the dribble is lost
// Moves take a moment. Flicking again before the ball reaches the new hand, or a
// move that can't get the ball to the new hand (say, while turning hard at full
// speed), fumbles the handle: the hand stops guiding the ball, and unless it comes
// back up near the hand, it rolls away and the dribble is lost.
// PlayerShooter calls Gather() to pick the ball up into both hands for a shot,
// and LetGo() when the shot leaves the hands.
[RequireComponent(typeof(Rigidbody))]
public class PlayerDribbler : MonoBehaviour
{
    private enum State { Loose, Dribbling, Fumbling, Holding }
    private enum Move { None, Crossover, BetweenTheLegs, BehindTheBack, Hesitation, Spin }

    [SerializeField] private PlayerInputReader _input;
    [Tooltip("Hesitations and spin moves slow down and turn the player through this.")]
    [SerializeField] private PlayerMovement _movement;
    [SerializeField] private Basketball _ball;
    [Tooltip("The player's body. While the player has the ball, it passes through every body part, like it passes through the capsule. The ball also has to come up within the arm's reach to be pushed. Optional.")]
    [SerializeField] private PlayerBody _playerBody;
    [Tooltip("Right-stick flicks are read relative to this camera's court directions (up = toward the hoop), like the left stick. Uses the main camera if empty.")]
    [SerializeField] private Transform _cameraTransform;

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
    [Tooltip("Which hand has the ball at the start.")]
    [SerializeField] private bool _startInRightHand = true;
    [Tooltip("How far to the side of the player (toward the hand that has it) the ball bounces, in meters. The capsule's radius is 0.4 and the ball's 0.12.")]
    [SerializeField] private float _sideOffset = 0.6f;
    [Tooltip("How far in front of the player's center the ball bounces, in meters.")]
    [SerializeField] private float _forwardOffset = 0.25f;
    [Tooltip("How strongly the ball is steered back under the hand, per second. Higher = tighter handle, lower = looser dribble.")]
    [SerializeField] private float _steering = 10f;
    [Tooltip("Largest steering correction, in m/s on top of the player's own speed.")]
    [SerializeField] private float _maxSteeringSpeed = 3f;
    [Tooltip("If the ball ends up this far from the player's center (meters, along the floor), the dribble is lost.")]
    [SerializeField] private float _loseDistance = 1.5f;

    [Header("Standing still (pound dribble)")]
    [Tooltip("The pound dribble fades in as the left stick goes below this push (0-1), and is full with the stick centered.")]
    [SerializeField] private float _poundStickThreshold = 0.2f;
    [Tooltip("Hand height of the pound dribble, in meters.")]
    [SerializeField] private float _poundHandHeight = 0.75f;
    [Tooltip("Push speed of the pound dribble, in m/s. Harder than walking, so the ball pops back up quickly.")]
    [SerializeField] private float _poundPushSpeed = 5.5f;

    [Header("Protecting the ball (L2, blends with trigger pressure)")]
    [Tooltip("Hand height with L2 fully pressed, in meters. Low, so it's hard to reach.")]
    [SerializeField] private float _protectHandHeight = 0.55f;
    [Tooltip("Push speed with L2 fully pressed, in m/s.")]
    [SerializeField] private float _protectPushSpeed = 5f;
    [Tooltip("Side offset with L2 fully pressed, in meters. A bit wider, with the body between the ball and the defense.")]
    [SerializeField] private float _protectSideOffset = 0.7f;
    [Tooltip("Forward offset with L2 fully pressed, in meters. Negative pulls the ball back beside the hip.")]
    [SerializeField] private float _protectForwardOffset = -0.15f;
    [Tooltip("While protecting, the ball is kept on the side away from this: the rim for now, a defender later. Empty = keep it in whichever hand has it.")]
    [SerializeField] private Transform _protectFrom;

    [Header("Dribble moves (right-stick flicks)")]
    [Tooltip("The right stick counts as centered below this push (0-1).")]
    [SerializeField] private float _flickDeadZone = 0.3f;
    [Tooltip("A flick has to push the right stick at least this far (0-1).")]
    [SerializeField] private float _flickThreshold = 0.8f;
    [Tooltip("Seconds from centered to fully pushed for it to count as a flick. Slower pushes are ignored.")]
    [SerializeField] private float _flickTime = 0.15f;
    [Tooltip("A move starts when the ball comes up to the hand. A flick waits this many seconds for that (a walking dribble takes about 0.5 s), then it's dropped.")]
    [SerializeField] private float _moveBufferTime = 0.6f;
    [Tooltip("A flick also starts its move right away while the ball is still on its way down from the hand above this height (meters, ball center), since the hand rides on top of it until about knee height. Lower = moves start sooner after a flick; above the hand height = always wait for the ball to come back up.")]
    [SerializeField] private float _moveStartMinHeight = 0.7f;
    [Tooltip("A fumbled ball can be caught again when it's within this distance below the hand height, in meters (and within the catch radius).")]
    [SerializeField] private float _handReach = 0.3f;
    [Tooltip("How fast the hand pushes the ball down to start a move, in m/s. Softer than a pound dribble, so the ball has time to get across: a 1.2 m crossover takes about 0.4 s at 3.5. Below about 3, it won't come back up to a walking hand height.")]
    [SerializeField] private float _movePushSpeed = 3.5f;
    [Tooltip("During a move, the hand takes the ball when it comes up to at least this height (meters), even from a low pound or protect dribble, so the ball has time to get across.")]
    [SerializeField] private float _moveCatchHeight = 0.8f;
    [Tooltip("Crossover: how far in front of the hands the ball bounces, in meters (halfway between them, side to side).")]
    [SerializeField] private float _crossoverBounceAhead = 0.2f;
    [Tooltip("Between the legs: how far in front of the player's center the ball bounces, in meters (0 = right under the body).")]
    [SerializeField] private float _betweenTheLegsBounceForward = 0f;
    [Tooltip("Behind the back: how far behind the player's center the ball bounces, in meters.")]
    [SerializeField] private float _behindTheBackBounceDistance = 0.45f;
    [Tooltip("Hesitation: the player's top speed is multiplied by this for a moment (0-1).")]
    [SerializeField, Range(0f, 1f)] private float _hesitationSpeed = 0.3f;
    [Tooltip("Hesitation: how long the slowdown lasts, in seconds. Afterwards the player speeds back up at their normal acceleration.")]
    [SerializeField] private float _hesitationTime = 0.35f;
    [Tooltip("Spin move: seconds for the body's full turn.")]
    [SerializeField] private float _spinTime = 0.45f;
    [Tooltip("Spin move: how far in front of the player (as they were facing when the spin started) the ball bounces, in meters.")]
    [SerializeField] private float _spinBounceForward = 0.3f;
    [Tooltip("Spin move: how far the right stick has to be rolled around, in degrees, while pushed out.")]
    [SerializeField] private float _spinStickAngle = 180f;
    [Tooltip("Spin move: the stick has to roll that far within this many seconds of being pushed out.")]
    [SerializeField] private float _spinStickTime = 0.4f;
    [Tooltip("A flick held out counts after this many seconds (or when the stick is let go). If the stick turns more than the flick turn limit first, it's a stick roll (spin), not a flick.")]
    [SerializeField] private float _flickSettleTime = 0.12f;
    [Tooltip("A flick turns into a stick roll once the stick has turned this many degrees while pushed out.")]
    [SerializeField] private float _flickMaxTurn = 60f;
    [Tooltip("Largest speed the hand can give the ball during a move, in m/s on top of the player's own speed. A move that needs more can't get the ball to the other hand in time.")]
    [SerializeField] private float _maxMoveSpeed = 6f;
    [Tooltip("At the end of a move, the ball has to come up within this distance of the new hand (meters, along the floor), or the handle is fumbled.")]
    [SerializeField] private float _catchRadius = 0.3f;
    [Tooltip("A move that hasn't reached the new hand after this many seconds (say, the ball stopped bouncing) is fumbled.")]
    [SerializeField] private float _maxMoveTime = 1.5f;
    [Tooltip("Seconds a fumbled ball can still be caught again, if it comes back up near the hand. After that (or once it's past the lose distance) the dribble is lost.")]
    [SerializeField] private float _fumbleTime = 1f;
    [Tooltip("With a body: when the ball comes up to the hand, its center has to be within the arm's reach (shoulder to palm) plus the ball's radius plus this much, in meters, or the hand can't push it and it's fumbled. Covers the leaning and reaching the primitive body doesn't do.")]
    [SerializeField] private float _reachSlack = 0.1f;

    [Header("Picking up a loose ball")]
    [Tooltip("A loose ball this close to the player's center (meters, along the floor) gets picked up.")]
    [SerializeField] private float _pickupRadius = 0.9f;
    [Tooltip("Only pick up balls whose center is lower than this, so a ball flying past overhead isn't grabbed.")]
    [SerializeField] private float _pickupMaxHeight = 1.6f;
    [Tooltip("Seconds after a shot (or a lost dribble) before the player can pick the ball up again, so it isn't grabbed right back.")]
    [SerializeField] private float _pickupDelayAfterShot = 1f;
    [Tooltip("Seconds a shot keeps passing through the shooter's own capsule and body after it leaves the hand. The hand releases it inside the capsule (near the head), and bumping into it there would knock the shot off its line.")]
    [SerializeField] private float _shotPassThroughTime = 0.5f;

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

    private int _hand;   // +1 = right hand, -1 = left hand
    private Move _move = Move.None;
    private bool _moveBounced;
    private float _moveStartTime;
    private Move _pendingMove = Move.None;
    private float _pendingTime;
    private Quaternion _moveStartRotation;
    private bool _stickCentered;
    private float _stickCenteredTime;
    private bool _stickOut;
    private Vector2 _lastStick;
    private float _stickTurned;
    private float _stickOutTime;
    private bool _flickWaiting;
    private Vector2 _flickDirection;
    private float _fumbleStartTime;
    private float _passThroughUntil = float.NegativeInfinity;
    private float _currentHandHeight;
    private Vector3 _heldBallTarget;
    private bool _hasCarryTarget;
    private Vector3 _carryTarget;

    public bool HasBall => _state != State.Loose;
    public bool IsBallInRightHand => _hand > 0;
    // Bouncing the ball (or trying to catch a fumbled one).
    public bool IsDribbling => _state == State.Dribbling || _state == State.Fumbling;
    // Holding the ball in both hands (gathered for a shot).
    public bool IsHolding => _state == State.Holding;
    // The hand the ball belongs to: +1 = right, -1 = left. During a move it's
    // the hand the ball is going to.
    public int BallHand => _hand;
    // The hand touching the ball: during a move, until the ball bounces, it's
    // still the hand that pushed it; then it's the new hand.
    public int TouchHand => _move != Move.None && _move != Move.Hesitation && !_moveBounced ? -_hand : _hand;
    // The height (ball center) the hand meets the ball at right now, in meters.
    public float CurrentHandHeight => _currentHandHeight;
    // Where the held ball is being carried to this step.
    public Vector3 HeldBallTarget => _heldBallTarget;

    private void Awake()
    {
        _body = GetComponent<Rigidbody>();
        _collider = GetComponent<Collider>();
        if (_input == null) _input = GetComponent<PlayerInputReader>();
        if (_movement == null) _movement = GetComponent<PlayerMovement>();
        if (_playerBody == null) _playerBody = GetComponent<PlayerBody>();
        if (_cameraTransform == null && Camera.main != null) _cameraTransform = Camera.main.transform;
        _hand = _startInRightHand ? 1 : -1;
    }

    private void FixedUpdate()
    {
        if (_ball == null) return;

        switch (_state)
        {
            case State.Loose:
                // A shot that has had time to get clear starts bumping into the shooter again.
                if (Time.time >= _passThroughUntil && _passThroughUntil > float.NegativeInfinity)
                {
                    _passThroughUntil = float.NegativeInfinity;
                    IgnoreOwnBall(false);
                }
                if (CanPickUp()) PickUp();
                break;
            case State.Dribbling:
                ReadDribbleStick();
                // A move can fumble the ball; then it's no longer dribbled this step.
                if (_state == State.Dribbling) Dribble();
                break;
            case State.Fumbling:
                Fumbling();
                break;
            case State.Holding:
                // Something else (like the R/T test keys) took the ball out of the hands.
                if (!_ball.IsHeld)
                {
                    SetState(State.Loose);
                    break;
                }
                // The shooter carries the ball up into the shot; otherwise it's held at the chest.
                _heldBallTarget = _hasCarryTarget ? _carryTarget : HoldPoint();
                _ball.MoveHeld(_heldBallTarget);
                break;
        }
    }

    // Picks the ball up out of the dribble into both hands. Returns false if
    // the player isn't dribbling (no ball to shoot), or while a dribble move is
    // on its way to the other hand: the ball is under the legs or behind the
    // back, out of reach, so it can't jump into the hands.
    public bool Gather()
    {
        if (_state != State.Dribbling || _move != Move.None) return false;
        _ball.Hold();
        SetState(State.Holding);
        return true;
    }

    // While holding, carries the ball to this point (the shooter lifts it into
    // the shot). It stays there until the next call, or until the ball is let go.
    public void CarryHeldBall(Vector3 point)
    {
        if (_state != State.Holding) return;
        _hasCarryTarget = true;
        _carryTarget = point;
    }

    // Where the ball is held at the chest, before the shot lifts it.
    public Vector3 GetHoldPoint()
    {
        return HoldPoint();
    }

    // Where a hand (+1 = right, -1 = left) wants the ball, at the hand height it's dribbled at now.
    public Vector3 GetHandPoint(int hand)
    {
        Vector3 point = HandPoint(_body.rotation, hand);
        point.y = _currentHandHeight;
        return point;
    }

    // The ball leaves the hands (a shot). It's a normal physics ball again.
    public void LetGo()
    {
        if (_state != State.Holding) return;
        _ball.Release();
        _letGoTime = Time.time;
        SetState(State.Loose);
        // The shot flies through the shooter's own capsule and arms for a moment.
        IgnoreOwnBall(true);
        _passThroughUntil = Time.time + _shotPassThroughTime;
    }

    private void Dribble()
    {
        Vector3 ballPosition = _ball.Body.position;
        if (FlatDistance(ballPosition, _body.position) > _loseDistance)
        {
            LoseDribble("Lost the dribble.");
            return;
        }

        GetDribbleHeightAndPush(out float handHeight, out float pushSpeed);
        KeepBallAwayFromDefense();
        // A move's ball comes up higher than a low pound or protect dribble before the hand takes it.
        if (_move != Move.None) handHeight = Mathf.Max(handHeight, _moveCatchHeight);
        _currentHandHeight = handHeight;

        // A flick that waited too long for the ball to come up to the hand is dropped.
        if (_pendingMove != Move.None && Time.time - _pendingTime > _moveBufferTime) _pendingMove = Move.None;

        // Up and down: gravity and the floor bounce do the work. The hand only
        // pushes the ball back down when it comes up to the hand, or when it
        // tops out below the hand (the hand reaches down to meet it).
        float vertical = _ball.Body.linearVelocity.y;
        bool reachedHand = vertical > 0f && ballPosition.y >= handHeight;
        bool toppedOutLow = vertical <= 0f && _wasRising;
        if (_move != Move.None && !_moveBounced && vertical > 0f) _moveBounced = true;

        if (reachedHand || toppedOutLow)
        {
            // The end of a move: the ball has to come up to the new hand.
            if (_move != Move.None && _moveBounced)
            {
                Vector3 catchPoint = MoveCatchPoint();
                _move = Move.None;
                if (FlatDistance(ballPosition, catchPoint) > _catchRadius)
                {
                    Fumble("Fumbled the move: the ball didn't come up to the hand.");
                    return;
                }
            }

            // The hand is on the end of an arm: if the ball comes up too far
            // from the shoulder (say, it drifted out while turning hard at full
            // speed), the hand can't get on top of it to push it.
            if (!IsWithinReach(ballPosition))
            {
                Fumble("Fumbled: the ball came up out of the hand's reach.");
                return;
            }

            // A flicked move starts here, as the ball comes up to the hand;
            // otherwise it's a normal push back down.
            vertical = _pendingMove != Move.None ? StartMove() : -pushSpeed;
            // A real hand also stops the ball spinning from its last floor contact.
            _ball.Body.angularVelocity = Vector3.zero;
        }
        else if (_pendingMove != Move.None && _move == Move.None && vertical <= 0f && ballPosition.y >= _moveStartMinHeight)
        {
            // Flicked while the ball is still under the hand on its way down: the
            // hand redirects it now instead of waiting a whole bounce. It keeps
            // whichever is faster, its current speed down or the move's push.
            vertical = Mathf.Min(vertical, StartMove());
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

        // A move that never finishes (say, the ball stopped bouncing) is a fumble.
        if (_move != Move.None && Time.time - _moveStartTime > _maxMoveTime)
        {
            Fumble("Fumbled the move: it took too long.");
            return;
        }

        // Sideways: the ball moves with the player, plus a correction toward
        // where the hand wants it. Clamping the correction keeps it from snapping.
        Vector3 playerVelocity = _body.linearVelocity;
        playerVelocity.y = 0f;
        Vector3 correction = _move == Move.None ? SteerToHand(ballPosition) : SteerMove(ballPosition, vertical, handHeight);
        Vector3 horizontal = playerVelocity + correction;

        _ball.Body.linearVelocity = new Vector3(horizontal.x, vertical, horizontal.z);
    }

    // Normal dribble: pulled back under the hand, harder the farther it is.
    private Vector3 SteerToHand(Vector3 ballPosition)
    {
        Vector3 toHand = HandPoint() - ballPosition;
        toHand.y = 0f;
        return Vector3.ClampMagnitude(toHand * _steering, _maxSteeringSpeed);
    }

    // During a move the hand sends the ball to a target in time: first the spot
    // where it should bounce (it gets there as it lands), then the new hand (it
    // gets there as it comes up to hand height).
    private Vector3 SteerMove(Vector3 ballPosition, float vertical, float handHeight)
    {
        Vector3 target = _moveBounced ? MoveCatchPoint() : MoveBouncePoint();
        float time = _moveBounced
            ? TimeToRiseTo(handHeight - ballPosition.y, vertical)
            : TimeToFall(ballPosition.y - _ball.Radius, -vertical);

        Vector3 toTarget = target - ballPosition;
        toTarget.y = 0f;
        return Vector3.ClampMagnitude(toTarget / Mathf.Max(time, 0.05f), _maxMoveSpeed);
    }

    // Where the ball bounces during the current move, in world space.
    private Vector3 MoveBouncePoint()
    {
        switch (_move)
        {
            case Move.BetweenTheLegs:
                return _body.position + _body.rotation * new Vector3(0f, 0f, _betweenTheLegsBounceForward);
            case Move.BehindTheBack:
                return _body.position + _body.rotation * new Vector3(0f, 0f, -_behindTheBackBounceDistance);
            case Move.Hesitation:
                // Same hand: just a slower, higher bounce while the feet stop.
                return HandPoint();
            case Move.Spin:
                // The ball is planted in front while the body spins around it. Uses
                // the facing from the start of the spin, since the body is turning.
                return _body.position + _moveStartRotation * new Vector3(0f, 0f, _spinBounceForward);
            default:
                // Crossover: halfway between the hands, a little in front of them.
                float handForward = Mathf.Lerp(_forwardOffset, _protectForwardOffset, _input.Protect);
                return _body.position + _body.rotation * new Vector3(0f, 0f, handForward + _crossoverBounceAhead);
        }
    }

    // Reads the right stick. A flick is a quick push from centered to far out;
    // a roll is the stick going around while pushed out. A roll starts with a
    // push out too, so a flick only counts once the stick is let go or has held
    // still for a moment without turning.
    private void ReadDribbleStick()
    {
        Vector2 stick = _input.DribbleStick;
        if (stick.magnitude < _flickDeadZone)
        {
            if (_flickWaiting) ConfirmFlick();
            _stickCentered = true;
            _stickOut = false;
            _stickCenteredTime = Time.time;
            return;
        }

        if (_stickCentered && stick.magnitude >= _flickThreshold)
        {
            // Pushed out. The stick has to come back to center before the next flick.
            _stickCentered = false;
            _stickOut = true;
            _lastStick = stick;
            _stickTurned = 0f;
            _stickOutTime = Time.time;
            _flickDirection = stick;
            _flickWaiting = Time.time - _stickCenteredTime <= _flickTime;
            return;
        }
        if (!_stickOut) return;

        // Rolling the stick around: half a circle (either way) is a spin move.
        _stickTurned += Vector2.SignedAngle(_lastStick, stick);
        _lastStick = stick;
        if (Mathf.Abs(_stickTurned) > _flickMaxTurn) _flickWaiting = false;
        if (Mathf.Abs(_stickTurned) >= _spinStickAngle && Time.time - _stickOutTime <= _spinStickTime)
        {
            _stickOut = false;
            OnMoveInput(Move.Spin);
            return;
        }

        if (_flickWaiting && Time.time - _stickOutTime >= _flickSettleTime) ConfirmFlick();
    }

    private void ConfirmFlick()
    {
        _flickWaiting = false;
        OnMoveInput(GetFlickMove(_flickDirection));
    }

    // Which move a flick is, from its direction relative to the hand with the ball.
    private Move GetFlickMove(Vector2 stick)
    {
        Vector2 local = StickToPlayerFrame(stick);   // x = player's right, y = player's forward
        float towardBallSide = local.x * _hand;       // + = toward the hand with the ball
        if (local.y < -0.5f) return towardBallSide < -0.3f ? Move.BetweenTheLegs : Move.BehindTheBack;
        if (towardBallSide < -0.5f) return Move.Crossover;
        return Move.Hesitation;
    }

    private void OnMoveInput(Move move)
    {
        // Too quick: a new move while the last one is still on its way.
        if (_move != Move.None)
        {
            Fumble("Fumbled: moves too quick.");
            return;
        }
        RequestMove(move);
    }

    private void RequestMove(Move move)
    {
        _pendingMove = move;
        _pendingTime = Time.time;
    }

    // Starts the waiting move and returns the ball's new up-down speed: the hand
    // pushes it down (the sideways part comes from SteerMove).
    private float StartMove()
    {
        _move = _pendingMove;
        _pendingMove = Move.None;
        _moveBounced = false;
        _moveStartTime = Time.time;
        _moveStartRotation = _body.rotation;
        Debug.Log($"Dribble move: {_move}");

        if (_move == Move.Hesitation)
        {
            if (_movement != null) _movement.SlowDown(_hesitationSpeed, _hesitationTime);
        }
        else
        {
            // A spin turns away from the ball's side (right hand = spin to the left).
            if (_move == Move.Spin && _movement != null) _movement.Spin(-360f * _hand, _spinTime);
            // Every other move ends in the other hand.
            _hand = -_hand;
        }
        return -_movePushSpeed;
    }

    // While protecting, if the ball is on the side facing the hoop (later: the
    // defender), cross it over to the other hand.
    private void KeepBallAwayFromDefense()
    {
        if (_protectFrom == null || _input.Protect < 0.5f || _move != Move.None || _pendingMove != Move.None) return;

        Vector3 local = Quaternion.Inverse(_body.rotation) * (_protectFrom.position - _body.position);
        // Only when the hoop is clearly off to one side; straight ahead either hand is fine.
        if (Mathf.Abs(local.x) > 0.5f && Mathf.Sign(local.x) == _hand) RequestMove(Move.Crossover);
    }

    // The hand stops guiding the ball: it flies on with whatever speed it has.
    private void Fumble(string reason)
    {
        Debug.Log(reason);
        _move = Move.None;
        _pendingMove = Move.None;
        _fumbleStartTime = Time.time;
        _state = State.Fumbling;
    }

    // A fumbled ball isn't steered. If it comes back up near the hand, it's
    // caught again; if it gets away (the lose distance) or takes too long, it's lost.
    private void Fumbling()
    {
        Vector3 ballPosition = _ball.Body.position;
        if (FlatDistance(ballPosition, _body.position) > _loseDistance || Time.time - _fumbleStartTime > _fumbleTime)
        {
            LoseDribble("Lost the dribble.");
            return;
        }

        GetDribbleHeightAndPush(out float handHeight, out _);
        _currentHandHeight = handHeight;
        if (FlatDistance(ballPosition, HandPoint()) <= _catchRadius && IsBallAtHand(handHeight))
        {
            Debug.Log("Recovered the dribble.");
            SetState(State.Dribbling);
        }
    }

    private void LoseDribble(string reason)
    {
        Debug.Log(reason);
        // Short wait before it can be picked up again, so a fumble isn't instantly grabbed back.
        _letGoTime = Time.time;
        SetState(State.Loose);
    }

    // Hand height and push speed blend between walking, sprinting (R2), standing
    // still (pound dribble), and protecting (L2).
    private void GetDribbleHeightAndPush(out float handHeight, out float pushSpeed)
    {
        float stillness = 1f - Mathf.Clamp01(_input.Move.magnitude / _poundStickThreshold);
        float protect = _input.Protect;

        handHeight = Mathf.Lerp(_handHeight, _sprintHandHeight, _input.Sprint);
        handHeight = Mathf.Lerp(handHeight, _poundHandHeight, stillness);
        handHeight = Mathf.Lerp(handHeight, _protectHandHeight, protect);

        pushSpeed = Mathf.Lerp(_pushSpeed, _sprintPushSpeed, _input.Sprint);
        pushSpeed = Mathf.Lerp(pushSpeed, _poundPushSpeed, stillness);
        pushSpeed = Mathf.Lerp(pushSpeed, _protectPushSpeed, protect);
    }

    private bool IsBallAtHand(float handHeight)
    {
        return _ball.Body.position.y >= handHeight - _handReach;
    }

    // Can the hand that has the ball get on top of it? Measured from the body's
    // shoulder to the ball's center. Without a body, the hand reaches anywhere.
    private bool IsWithinReach(Vector3 ballPosition)
    {
        if (_playerBody == null) return true;
        Vector3 shoulder = _body.position + _body.rotation * _playerBody.GetShoulderLocal(_hand);
        return Vector3.Distance(shoulder, ballPosition) <= _playerBody.ArmReach + _ball.Radius + _reachSlack;
    }

    private bool CanPickUp()
    {
        if (_ball.IsHeld || Time.time - _letGoTime < _pickupDelayAfterShot) return false;

        Vector3 fromPlayer = _ball.Body.position - _body.position;
        fromPlayer.y = 0f;
        return fromPlayer.magnitude <= _pickupRadius && _ball.Body.position.y <= _pickupMaxHeight;
    }

    // A loose ball is picked up by the hand on the side it's on.
    private void PickUp()
    {
        Vector3 local = Quaternion.Inverse(_body.rotation) * (_ball.Body.position - _body.position);
        _hand = local.x >= 0f ? 1 : -1;
        SetState(State.Dribbling);
    }

    private void SetState(State state)
    {
        _state = state;
        _wasRising = false;
        _move = Move.None;
        _pendingMove = Move.None;
        _hasCarryTarget = false;
        _passThroughUntil = float.NegativeInfinity;
        // While the player has the ball, it passes through their capsule and body
        // parts, the way a real ball moves around a player's legs and body instead
        // of bumping off. A loose ball bounces off them.
        IgnoreOwnBall(state != State.Loose);
    }

    private void IgnoreOwnBall(bool ignore)
    {
        Collider ballCollider = _ball.GetComponent<Collider>();
        Physics.IgnoreCollision(_collider, ballCollider, ignore);
        if (_playerBody != null) _playerBody.SetBallIgnored(ballCollider, ignore);
    }

    // Where the hand that has the ball wants it. Uses the physics position and
    // rotation (not the smoothed transform) so the ball follows where the player
    // really is this physics step.
    private Vector3 HandPoint()
    {
        return HandPoint(_body.rotation, _hand);
    }

    // Where a hand (+1 = right, -1 = left) would want the ball with the body turned this way.
    private Vector3 HandPoint(Quaternion bodyRotation, int hand)
    {
        float protect = _input.Protect;
        float side = Mathf.Lerp(_sideOffset, _protectSideOffset, protect);
        float forward = Mathf.Lerp(_forwardOffset, _protectForwardOffset, protect);
        return _body.position + bodyRotation * new Vector3(side * hand, 0f, forward);
    }

    // Where the hand takes the ball at the end of a move. During a spin the hand
    // whips around much faster than the ball can follow, so the ball goes straight
    // to where the hand will be when the full turn is done (facing the same way
    // as at the start) while the body spins around it.
    private Vector3 MoveCatchPoint()
    {
        return _move == Move.Spin ? HandPoint(_moveStartRotation, _hand) : HandPoint();
    }

    private Vector3 HoldPoint()
    {
        Vector3 point = _body.position + _body.rotation * Vector3.forward * _holdForward;
        point.y = _holdHeight;
        return point;
    }

    // The right stick, turned from court directions (up = toward the hoop, like
    // the left stick) into the player's own directions (x = player's right,
    // y = player's forward).
    private Vector2 StickToPlayerFrame(Vector2 stick)
    {
        Vector3 world = BehindPlayerCamera.StickToFloor(stick, _cameraTransform);
        Vector3 local = Quaternion.Inverse(_body.rotation) * world;
        return new Vector2(local.x, local.z).normalized;
    }

    // Seconds for the ball to drop a height (meters) when already moving down at
    // downSpeed (m/s). From height = downSpeed * t + g * t^2 / 2.
    private static float TimeToFall(float height, float downSpeed)
    {
        float g = -Physics.gravity.y;
        height = Mathf.Max(height, 0f);
        return (-downSpeed + Mathf.Sqrt(downSpeed * downSpeed + 2f * g * height)) / g;
    }

    // Seconds for a rising ball to climb a height (meters) at upSpeed (m/s). If it
    // won't make it that high, the time to the top of its bounce instead.
    private static float TimeToRiseTo(float height, float upSpeed)
    {
        float g = -Physics.gravity.y;
        if (upSpeed <= 0f) return 0f;
        float discriminant = upSpeed * upSpeed - 2f * g * Mathf.Max(height, 0f);
        return discriminant < 0f ? upSpeed / g : (upSpeed - Mathf.Sqrt(discriminant)) / g;
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }
}
