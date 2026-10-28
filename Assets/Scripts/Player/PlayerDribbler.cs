using UnityEngine;

// Dribbling. The ball stays a real physics object the whole time: the hand
// pushes it down, the floor bounces it back up, and when it comes back up to
// the hand it gets pushed down again. While it bounces, it's steered sideways
// so it's back under the hand that has it by the time it bounces or comes up
// (a stand-in for the hand guiding it), however quickly the player cuts and turns.
//   - walk up to a loose ball to pick it up (no rebounding yet, just a grab radius)
//   - sprinting (R2) dribbles lower and harder, so the bounces come faster
//   - standing still (left stick centered) is a low, hard pound dribble
//   - protecting (L2) is a low, tight dribble, with the ball kept on the side
//     away from the hoop (later: away from the defender)
//   - right-stick dribble moves. Each one is still physics: one hand pushes the
//     ball down, guides it to one bounce, and it comes up into the hand it ends in.
//     A move never picks the ball up, so moves chain into combos.
//     The stick's directions are fixed on the screen (up is always up, whichever
//     way the player or camera faces); left and right are read against the hand
//     with the ball ("toward the ball hand" = right while it's in the right hand):
//       flick up                                    = crossover (bounce in front)
//       flick toward the ball hand                  = hesitation (same hand, quick slowdown)
//       flick toward the other hand                 = between the legs (bounce under the body)
//       flick down                                  = stepback (same hand, yanked back, hop back)
//       flick back diagonally toward the other hand = behind the back (bounce behind the body)
//       flick back diagonally toward the ball hand  = stepback to the ball side (hop back and out)
//       roll the stick through a half circle        = spin move (body spins, ends in the other hand)
//     Holding R2 as the flick is recognized makes it the explosive version:
//     quicker, and the player travels farther (crossover ~1 m sideways, stepback
//     ~1 m back, a burst forward after a hesitation's stop, a spin that keeps
//     going). With R2, toward the other hand is a side step (hop away from the
//     ball hand, ball carried along) instead of between the legs. A short
//     recovery afterwards keeps them from being chained for free.
//     Footwork (hops, shifts, slowdowns) goes by the player's body, not the
//     stick: a stepback always hops the way the player's back faces.
//   - if the ball gets knocked too far away (say it hits the pole), the dribble is lost
// A move starts as soon as the hand can get to the ball (on its way down or
// back up), and a new flick interrupts the move that's going: the ball heads
// for the new move's bounce from wherever it is.
// Fumbles (_allowFumbles, on by default): a move that can't get the ball to the
// new hand, or a ball that comes up out of the arm's reach (say, a sloppy hard
// cut at full sprint), fumbles the handle: the hand stops guiding the ball, and
// unless it comes back up near the hand, it rolls away and the dribble is lost.
// Each fumble logs why, with the numbers (speed, turn rate, reach), so a ball
// lost during normal play can be traced to its cause and tuned.
// Either way, a ball knocked far away (say, off the pole) is lost.
// PlayerShooter calls Gather() to pick the ball up into both hands for a shot
// (only once it's within the hands' reach), and LetGo() when the shot leaves the hands.
[RequireComponent(typeof(Rigidbody))]
public class PlayerDribbler : MonoBehaviour
{
    private enum State { Loose, Dribbling, Fumbling, Holding }
    private enum Move { None, Crossover, BetweenTheLegs, BehindTheBack, Hesitation, Stepback, StepbackBallSide, SideStep, Spin }

    [SerializeField] private PlayerInputReader _input;
    [Tooltip("Hesitations, stepbacks and spin moves slow down, hop and turn the player through this.")]
    [SerializeField] private PlayerMovement _movement;
    [SerializeField] private Basketball _ball;
    [Tooltip("The player's body. While the player has the ball, it passes through every body part, like it passes through the capsule. The ball also has to come up within the arm's reach to be pushed. Optional.")]
    [SerializeField] private PlayerBody _playerBody;

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
    [Tooltip("Largest sideways speed the hand gives the ball to get it back under the hand, in m/s on top of the player's own speed. Too high and the ball slides sideways like a magnet; too low and it can't keep up through hard cuts and turns.")]
    [SerializeField] private float _maxGuideSpeed = 4f;
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

    [Header("Dribble moves: reading the right stick")]
    [Tooltip("The right stick counts as centered below this push (0-1). A flick is timed from the last moment the stick was this low.")]
    [SerializeField] private float _flickDeadZone = 0.3f;
    [Tooltip("A flick has to push the right stick at least this far (0-1).")]
    [SerializeField] private float _flickThreshold = 0.6f;
    [Tooltip("Seconds from under the dead zone to past the flick threshold for it to count as a flick. Slower pushes are ignored (and logged).")]
    [SerializeField] private float _flickTime = 0.2f;
    [Tooltip("After a flick, the stick has to come back under this push (0-1) before the next flick counts. Above the dead zone, so fast repeated flicks don't have to go all the way back to center.")]
    [SerializeField] private float _flickRearm = 0.45f;
    [Tooltip("Width of the 'up' direction (crossover), in degrees.")]
    [SerializeField] private float _upSectorAngle = 60f;
    [Tooltip("Width of the 'toward a hand' directions (hesitation, between the legs), in degrees. The front diagonals between up and the sides are split halfway.")]
    [SerializeField] private float _sideSectorAngle = 60f;
    [Tooltip("Width of the 'down' direction (stepback), in degrees.")]
    [SerializeField] private float _downSectorAngle = 60f;
    [Tooltip("Width of each back diagonal (behind the back, stepback to the ball side), in degrees, between down and the side.")]
    [SerializeField] private float _backDiagonalSectorAngle = 30f;

    [Header("Dribble moves: timing")]
    [Tooltip("A move starts as soon as the hand can get to the ball: when the ball is above this height (meters, ball center) on its way down or back up, and within the arm's reach. Lower = less waiting after a flick. In a low dribble it's the hand height minus the hand reach instead.")]
    [SerializeField] private float _moveTakeHeight = 0.6f;
    [Tooltip("A flick waits at most this many seconds for the hand to get to the ball, then it's dropped (and logged).")]
    [SerializeField] private float _moveBufferTime = 0.4f;
    [Tooltip("A fumbled ball can be caught again when it's within this distance below the hand height, in meters (and within the catch radius). Also how far below a low dribble's hand height the hand reaches down to start a move.")]
    [SerializeField] private float _handReach = 0.3f;
    [Tooltip("How fast the hand pushes the ball down to start a move, in m/s (x = normal, y = explosive with R2). With the catch height below, 5 makes a move about 0.3 s from push to catch and 6.5 about 0.2 s. Below about 3, it won't come back up to the hand.")]
    [SerializeField] private Vector2 _movePushSpeed = new Vector2(5f, 6.5f);
    [Tooltip("At the end of a move, the hand takes the ball when it comes up to this height (meters, ball center; x = normal, y = explosive), then dribbles on at the normal height. Explosive moves are caught lower: harder and quicker.")]
    [SerializeField] private Vector2 _moveCatchHeight = new Vector2(0.8f, 0.7f);
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
    [Tooltip("Stepback: the ball is yanked back on one bounce to this far behind where the hand usually dribbles it (meters), beside the feet.")]
    [SerializeField] private float _stepbackPullBack = 0.4f;
    [Tooltip("Stepback: how hard the hand pushes the ball down, in m/s (x = normal, y = explosive). Harder than other moves, so it's a quick yank.")]
    [SerializeField] private Vector2 _stepbackPushSpeed = new Vector2(6f, 7f);
    [Tooltip("Stepback to the ball side: degrees the hop turns out from straight back, toward the ball hand.")]
    [SerializeField] private float _stepbackBallSideAngle = 45f;
    [Tooltip("Spin move: seconds for the body's full turn (x = normal, y = explosive).")]
    [SerializeField] private Vector2 _spinTime = new Vector2(0.45f, 0.3f);

    [Header("Dribble moves: footwork (x = normal, y = explosive with R2)")]
    [Tooltip("R2 pressed at least this far (0-1) when a flick is recognized makes it the explosive version.")]
    [SerializeField, Range(0f, 1f)] private float _explosiveTrigger = 0.5f;
    [Tooltip("Seconds a footwork hop or shift takes (stepbacks, crossover, side step, behind the back).")]
    [SerializeField] private Vector2 _footworkTime = new Vector2(0.25f, 0.2f);
    [Tooltip("Crossover: meters the player shifts sideways toward the new ball hand.")]
    [SerializeField] private Vector2 _crossoverShift = new Vector2(0.2f, 1f);
    [Tooltip("Crossover: how much the shift also goes forward (0 = straight sideways, 1 = 45 degrees forward).")]
    [SerializeField, Range(0f, 1f)] private float _crossoverShiftForward = 0.3f;
    [Tooltip("Between the legs (normal only; with R2 it's the side step): meters the player shifts toward the new ball hand.")]
    [SerializeField] private float _betweenTheLegsShift = 0f;
    [Tooltip("Side step (R2 only): meters the player hops sideways, toward the side away from the ball hand. The ball stays in its hand and comes along.")]
    [SerializeField] private float _sideStepHop = 1.1f;
    [Tooltip("Stepbacks: meters the player hops back (straight back, or back and out to the ball side), still facing forward.")]
    [SerializeField] private Vector2 _stepbackHop = new Vector2(0.6f, 1f);
    [Tooltip("Behind the back: meters the player shifts sideways toward the new ball hand.")]
    [SerializeField] private Vector2 _behindTheBackShift = new Vector2(0f, 0.8f);
    [Tooltip("Explosive hesitation: the top speed is multiplied by this (0-1) for the stop, almost a full stop.")]
    [SerializeField, Range(0f, 1f)] private float _explosiveHesitationSpeed = 0.05f;
    [Tooltip("Explosive hesitation: seconds of the stop before the burst forward.")]
    [SerializeField] private float _explosiveHesitationStop = 0.2f;
    [Tooltip("Explosive hesitation: meters of the burst forward, over the explosive footwork time (1.4 m in 0.2 s is 7 m/s, above run speed). The player keeps that speed as it ends and eases back to the stick's speed.")]
    [SerializeField] private float _hesitationBurst = 1.4f;
    [Tooltip("Explosive spin: meters the player keeps going the way they were moving during the spin (forward if standing). Keeps that speed as it ends.")]
    [SerializeField] private float _explosiveSpinCarry = 1.5f;
    [Tooltip("After an explosive move's footwork, seconds of reduced acceleration (see PlayerMovement's recovery acceleration), so they can't be chained for free.")]
    [SerializeField] private float _explosiveRecoveryTime = 0.15f;
    [Tooltip("Spin move: how far in front of the player (as they were facing when the spin started) the ball bounces, in meters.")]
    [SerializeField] private float _spinBounceForward = 0.3f;
    [Tooltip("Spin move: how far the right stick has to be rolled around, in degrees, while pushed out.")]
    [SerializeField] private float _spinStickAngle = 180f;
    [Tooltip("Spin move: the stick has to roll that far within this many seconds of being pushed out.")]
    [SerializeField] private float _spinStickTime = 0.4f;
    [Tooltip("Largest speed the hand can give the ball during a move, in m/s on top of the player's own speed. A move that needs more can't get the ball to the other hand in time.")]
    [SerializeField] private float _maxMoveSpeed = 6f;
    [Tooltip("A new move that interrupts another turns the ball from where the last move was sending it. With fumbles on, a turn that needs a bigger change of the ball's sideways speed than this (m/s) fumbles. A straight reversal (a quick double crossover) is about 8.")]
    [SerializeField] private float _maxRedirectChange = 10f;
    [Tooltip("At the end of a move, the ball has to come up within this distance of the new hand (meters, along the floor), or (with fumbles on) the handle is fumbled. Also how close a fumbled ball has to come back to be caught.")]
    [SerializeField] private float _catchRadius = 0.3f;
    [Tooltip("A move that hasn't reached the new hand after this many seconds (say, the ball stopped bouncing) ends; with fumbles on, it's fumbled.")]
    [SerializeField] private float _maxMoveTime = 1.5f;
    [Tooltip("On: a move that misses the new hand, a move that takes too long, or a ball that comes up out of the arm's reach fumbles the ball (each logs why). Off (for testing only): the hand never loses the ball by itself. A ball knocked past the lose distance is lost either way.")]
    [SerializeField] private bool _allowFumbles = true;
    [Tooltip("Seconds a fumbled ball can still be caught again, if it comes back up near the hand. After that (or once it's past the lose distance) the dribble is lost.")]
    [SerializeField] private float _fumbleTime = 1f;
    [Tooltip("With a body: when the ball comes up to the hand, its center has to be within the arm's reach (shoulder to palm) plus the ball's radius plus this much, in meters, or (with fumbles on) the hand can't push it and it's fumbled. Covers the leaning and reaching the primitive body doesn't do.")]
    [SerializeField] private float _reachSlack = 0.1f;

    [Header("Picking up a loose ball")]
    [Tooltip("A loose ball this close to the player's center (meters, along the floor) gets picked up.")]
    [SerializeField] private float _pickupRadius = 0.9f;
    [Tooltip("Only pick up balls whose center is lower than this, so a ball flying past overhead isn't grabbed.")]
    [SerializeField] private float _pickupMaxHeight = 1.6f;
    [Tooltip("Seconds after a shot (or a lost dribble) before the player can pick the ball up again, so it isn't grabbed right back.")]
    [SerializeField] private float _pickupDelayAfterShot = 1f;
    [Tooltip("At least this many seconds, a shot passes through the shooter's own capsule after it leaves the hand. The hand releases it inside the capsule (near the head), and bumping into it there would knock the shot off its line. After this, the capsule starts bumping the ball again as soon as they no longer overlap.")]
    [SerializeField] private float _shotPassThroughTime = 0.5f;

    [Header("Holding (while charging a shot)")]
    [Tooltip("To gather the ball for a shot, its center has to be within this distance of the hand that has it (meters, along the floor), as well as within the arm's reach. Otherwise the shot waits for it (a buffered Shoot), so the ball never snaps into the hands from far away.")]
    [SerializeField] private float _gatherRadius = 0.45f;
    [Tooltip("How far in front of the player's center the ball is held, in meters.")]
    [SerializeField] private float _holdForward = 0.35f;
    [Tooltip("Height of the ball's center while held, in meters. About chest height.")]
    [SerializeField] private float _holdHeight = 1.3f;

    private Rigidbody _body;
    private Collider _collider;
    private State _state = State.Loose;
    private bool _wasRising;
    private float _letGoTime = float.NegativeInfinity;

    private int _hand;       // +1 = right hand, -1 = left hand
    private int _pushHand;   // the hand that started the current move
    private Move _move = Move.None;
    private bool _moveBounced;
    private float _moveStartTime;
    private float _moveFlickTime;
    private int _moveGesture;
    private bool _moveExplosive;
    private Move _requestedMove = Move.None;
    private float _requestedTime;
    private int _requestedGesture;
    private bool _requestedExplosive;
    private float _hesitationBurstAt = float.PositiveInfinity;
    private Quaternion _moveStartRotation;
    private float _lastYaw;
    private float _turnRate;
    // Right stick: a "gesture" is one push out of the stick, counted so a roll
    // can tell whether the move that's going came from the same push.
    private bool _stickArmed;
    private float _stickLowTime;
    private float _stickPeak;
    private bool _stickOut;
    private Vector2 _lastStick;
    private float _stickTurned;
    private float _stickOutTime;
    private int _gesture;
    private float _fumbleStartTime;
    private float _passThroughUntil = float.NegativeInfinity;
    private float _currentHandHeight;
    private Vector3 _heldBallTarget;
    private bool _hasCarryTarget;
    private Vector3 _carryTarget;

    // Dribbling, holding or trying to catch a fumbled ball (not loose).
    public bool HasBall => _state != State.Loose;
    // Bouncing the ball (or trying to catch a fumbled one).
    public bool IsDribbling => _state == State.Dribbling || _state == State.Fumbling;
    // Holding the ball in both hands (gathered for a shot).
    public bool IsHolding => _state == State.Holding;
    // A dribble move is on its way to the other hand (the ball can't be gathered yet).
    public bool IsDoingMove => _state == State.Dribbling && _move != Move.None;
    // The hand the ball belongs to: +1 = right, -1 = left. During a move it's
    // the hand the ball is going to.
    public int BallHand => _hand;
    // The hand touching the ball: during a move, until the ball bounces, it's
    // still the hand that pushed it; then it's the hand it's going to.
    public int TouchHand => _move != Move.None && !_moveBounced ? _pushHand : _hand;
    // The height (ball center) the hand meets the ball at right now, in meters.
    public float CurrentHandHeight => _currentHandHeight;
    // Where the held ball is being carried to this step.
    public Vector3 HeldBallTarget => _heldBallTarget;
    // The ball can be picked up into both hands for a shot right now (see Gather).
    public bool CanGather => _state == State.Dribbling && _move == Move.None
        && FlatDistance(_ball.Body.position, HandPoint()) <= _gatherRadius && IsWithinReach(_ball.Body.position);

    private void Awake()
    {
        _body = GetComponent<Rigidbody>();
        _collider = GetComponent<Collider>();
        if (_input == null) _input = GetComponent<PlayerInputReader>();
        if (_movement == null) _movement = GetComponent<PlayerMovement>();
        if (_playerBody == null) _playerBody = GetComponent<PlayerBody>();
        _hand = _startInRightHand ? 1 : -1;
    }

    private void FixedUpdate()
    {
        if (_ball == null) return;

        // How fast the body is turning, for the fumble log (degrees per second).
        float yaw = _body.rotation.eulerAngles.y;
        _turnRate = Mathf.Abs(Mathf.DeltaAngle(_lastYaw, yaw)) / Time.fixedDeltaTime;
        _lastYaw = yaw;

        switch (_state)
        {
            case State.Loose:
                // A shot that has had time to get clear, and isn't inside the
                // capsule anymore, starts bumping into the shooter again. (Turning
                // it back on while they overlap would shove the ball out.)
                if (Time.time >= _passThroughUntil && _passThroughUntil > float.NegativeInfinity && !IsBallInCapsule())
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
    // the player isn't dribbling (no ball to shoot), while a dribble move is on
    // its way to the other hand (the ball is under the legs or behind the back),
    // or while the ball is out of the hands' reach: it can't jump into the hands.
    public bool Gather()
    {
        if (!CanGather) return false;
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
        // At the end of a move the hand takes the ball at the catch height.
        if (_move != Move.None) handHeight = Pick(_moveCatchHeight);
        StartHesitationBurst();
        _currentHandHeight = handHeight;

        // A flick that waited too long for the hand to get to the ball is dropped.
        if (_requestedMove != Move.None && Time.time - _requestedTime > _moveBufferTime)
        {
            Debug.Log($"Dribble move {_requestedMove} dropped: the hand couldn't get to the ball within {_moveBufferTime:0.00} s.");
            _requestedMove = Move.None;
        }

        // Up and down: gravity and the floor bounce do the work. The hand only
        // pushes the ball back down when it comes up to the hand, or when it
        // tops out below the hand (the hand reaches down to meet it).
        float vertical = _ball.Body.linearVelocity.y;
        bool reachedHand = vertical > 0f && ballPosition.y >= handHeight;
        bool toppedOutLow = vertical <= 0f && _wasRising;
        if (_move != Move.None && !_moveBounced && vertical > 0f) _moveBounced = true;

        if (_requestedMove != Move.None && CanTakeForMove(ballPosition, handHeight))
        {
            // A flicked move starts as soon as the hand can get to the ball, on
            // its way down or back up, even in the middle of another move.
            vertical = StartMove(ballPosition, vertical);
            // Turning the ball for an interrupting move can be more than the hand can do.
            if (_state != State.Dribbling) return;
            _ball.Body.angularVelocity = Vector3.zero;
        }
        else if (reachedHand || toppedOutLow)
        {
            // The end of a move: the ball has to come up to the new hand.
            if (_move != Move.None && _moveBounced)
            {
                Vector3 catchPoint = MoveCatchPoint();
                // Without fumbles, a miss is just guided back to the hand on the next bounce.
                if (_allowFumbles && FlatDistance(ballPosition, catchPoint) > _catchRadius)
                {
                    Fumble($"Fumbled the move: the ball came up {FlatDistance(ballPosition, catchPoint):0.00} m from the new hand (catch radius {_catchRadius:0.00}).");
                    return;
                }
                Debug.Log($"Dribble move {_move} reached the hand {Time.time - _moveFlickTime:0.00} s after the flick ({Time.time - _moveStartTime:0.00} s after it started).");
                _move = Move.None;
            }

            // The hand is on the end of an arm: if the ball comes up too far
            // from the shoulder (say, it drifted out while turning hard at full
            // speed), the hand can't get on top of it to push it.
            if (_allowFumbles && !IsWithinReach(ballPosition))
            {
                Fumble($"Fumbled: the ball came up out of the hand's reach ({ReachInfo(ballPosition)}).");
                return;
            }

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

        // A move that never finishes (say, the ball stopped bouncing) is a fumble.
        if (_move != Move.None && Time.time - _moveStartTime > _maxMoveTime)
        {
            if (_allowFumbles)
            {
                Fumble($"Fumbled the move: it took too long (over {_maxMoveTime:0.00} s).");
                return;
            }
            _move = Move.None;
        }

        // Sideways: the ball moves with the player, plus a correction toward
        // where the hand wants it. Clamping the correction keeps it from snapping.
        Vector3 playerVelocity = _body.linearVelocity;
        playerVelocity.y = 0f;
        Vector3 correction = _move == Move.None ? SteerToHand(ballPosition, vertical, handHeight) : SteerMove(ballPosition, vertical, handHeight);
        Vector3 horizontal = playerVelocity + correction;

        _ball.Body.linearVelocity = new Vector3(horizontal.x, vertical, horizontal.z);
    }

    // Normal dribble: the hand sends the ball back under it in time: by the
    // bounce on the way down, by the hand height on the way up. When the player
    // turns or cuts, the hand swings around the body, and the ball has to get
    // there before it comes back up. (A gentle pull that ignored the time left
    // let the ball come up out of reach on hard turns, and it was fumbled.)
    private Vector3 SteerToHand(Vector3 ballPosition, float vertical, float handHeight)
    {
        Vector3 toHand = HandPoint() - ballPosition;
        toHand.y = 0f;
        float time = vertical > 0f
            ? TimeToRiseTo(handHeight - ballPosition.y, vertical)
            : TimeToFall(ballPosition.y - _ball.Radius, -vertical);
        return Vector3.ClampMagnitude(toHand / Mathf.Max(time, 0.05f), _maxGuideSpeed);
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
            case Move.SideStep:
                // Same hand: just a quick bounce while the feet stop or hop.
                return HandPoint();
            case Move.Stepback:
            case Move.StepbackBallSide:
                // Same hand, yanked back from in front to beside the feet as the player hops.
                return HandPoint() - _body.rotation * Vector3.forward * _stepbackPullBack;
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

    // Reads the right stick. A flick is a quick push from near the center to
    // past the flick threshold; it picks its move the moment it's recognized.
    // A roll is the stick going on around while pushed out: half a circle soon
    // enough is a spin, which replaces the move the same push started.
    private void ReadDribbleStick()
    {
        Vector2 stick = _input.DribbleStick;
        float push = stick.magnitude;

        // The flick is timed from the last moment the stick was near the center
        // (or from when it came back under the re-arm level after the last flick).
        if (push < _flickDeadZone || (!_stickArmed && push < _flickRearm)) _stickLowTime = Time.time;

        if (push < _flickRearm)
        {
            if (_stickArmed && _stickPeak >= _flickRearm)
            {
                Debug.Log($"Flick missed: the stick only got to {_stickPeak:0.00} (a flick needs {_flickThreshold:0.00}).");
            }
            // Back in: ready for the next flick.
            _stickArmed = true;
            _stickPeak = push;
            _stickOut = false;
            return;
        }

        if (_stickArmed)
        {
            _stickPeak = Mathf.Max(_stickPeak, push);
            if (push < _flickThreshold) return;

            // Pushed out. From here, watch for a roll.
            _stickArmed = false;
            _stickOut = true;
            _lastStick = stick;
            _stickTurned = 0f;
            _stickOutTime = Time.time;
            _gesture++;

            float pushTime = Time.time - _stickLowTime;
            if (pushTime <= _flickTime) OnFlick(stick);
            else Debug.Log($"Flick too slow: {pushTime:0.00} s from under {_flickDeadZone:0.00} to {_flickThreshold:0.00} (limit {_flickTime:0.00} s).");
            return;
        }
        if (!_stickOut) return;

        // Rolling the stick around: half a circle (either way) is a spin move.
        _stickTurned += Vector2.SignedAngle(_lastStick, stick);
        _lastStick = stick;
        if (Mathf.Abs(_stickTurned) >= _spinStickAngle && Time.time - _stickOutTime <= _spinStickTime)
        {
            _stickOut = false;
            OnSpinRoll();
        }
    }

    // R2 is read at the moment the flick is recognized.
    private void OnFlick(Vector2 stick)
    {
        bool explosive = _input.Sprint >= _explosiveTrigger;
        Move move = GetFlickMove(stick, explosive);
        float stickAngle = Mathf.Atan2(stick.x, stick.y) * Mathf.Rad2Deg;
        Debug.Log($"Flick: stick at {stickAngle:0} deg (0 = up, 90 = right), ball in the {HandName(_hand)} hand: {MoveName(move, explosive)}.");
        RequestMove(move, _gesture, explosive);
    }

    // The stick kept rolling into a spin. If the flick that started the roll has
    // already started its move, that move becomes the spin, as if the spin had
    // been picked from the start: the ball goes to the other hand from the one
    // that pushed it, and any footwork the flick started stops.
    private void OnSpinRoll()
    {
        bool explosive = _input.Sprint >= _explosiveTrigger;
        if (_move != Move.None && _move != Move.Spin && _moveGesture == _gesture)
        {
            Debug.Log($"Dribble move {MoveName(_move, _moveExplosive)} turned into a {MoveName(Move.Spin, explosive)} (the stick kept rolling).");
            _move = Move.Spin;
            _moveExplosive = explosive;
            _hand = -_pushHand;
            StartFootwork();
            return;
        }
        Debug.Log($"Stick roll: {MoveName(Move.Spin, explosive)}.");
        RequestMove(Move.Spin, _gesture, explosive);
    }

    // Which move a flick is. The stick's directions are fixed (up is always up),
    // with left and right read against the hand that has the ball. With R2,
    // toward the other hand is the side step instead of between the legs.
    private Move GetFlickMove(Vector2 stick, bool explosive)
    {
        // 0 = up, +90 = toward the ball hand, -90 = toward the other hand, 180 = down.
        float angle = Mathf.Atan2(stick.x * _hand, stick.y) * Mathf.Rad2Deg;
        float fromUp = Mathf.Abs(angle);
        bool towardBallHand = angle > 0f;

        float downStart = 180f - _downSectorAngle / 2f;
        float backDiagonalStart = downStart - _backDiagonalSectorAngle;
        // The front diagonals are split halfway between up and the side.
        float frontSplit = (_upSectorAngle / 2f + 90f - _sideSectorAngle / 2f) / 2f;

        if (fromUp >= downStart) return Move.Stepback;
        if (fromUp >= backDiagonalStart) return towardBallHand ? Move.StepbackBallSide : Move.BehindTheBack;
        if (fromUp > frontSplit)
        {
            if (towardBallHand) return Move.Hesitation;
            return explosive ? Move.SideStep : Move.BetweenTheLegs;
        }
        return Move.Crossover;
    }

    // Asks for a move. It starts as soon as the hand can get to the ball (see
    // Dribble). A newer request replaces one that's still waiting. The gesture
    // is the stick push it came from (-1 for none).
    private void RequestMove(Move move, int gesture, bool explosive)
    {
        _requestedMove = move;
        _requestedTime = Time.time;
        _requestedGesture = gesture;
        _requestedExplosive = explosive;
    }

    // The hand can push the ball to start a move: it's high enough (on its way
    // down or back up) and within the arm's reach of the hand it's in.
    private bool CanTakeForMove(Vector3 ballPosition, float handHeight)
    {
        float takeHeight = Mathf.Min(_moveTakeHeight, handHeight - _handReach);
        return ballPosition.y >= takeHeight && IsWithinReach(ballPosition, _hand);
    }

    // Starts the requested move and returns the ball's new up-down speed: the
    // hand pushes it down (the sideways part comes from SteerMove). The hand the
    // ball is in (or heading to, mid-move) pushes it, from wherever it is.
    private float StartMove(Vector3 ballPosition, float vertical)
    {
        Move interrupted = _move;
        float interruptedAge = Time.time - _moveStartTime;

        _move = _requestedMove;
        _requestedMove = Move.None;
        _moveBounced = false;
        _moveStartTime = Time.time;
        _moveFlickTime = _requestedTime;
        _moveGesture = _requestedGesture;
        _moveExplosive = _requestedExplosive;
        _moveStartRotation = _body.rotation;
        _pushHand = _hand;

        // These moves end in the other hand; the rest stay in the same hand.
        if (_move == Move.Crossover || _move == Move.BetweenTheLegs || _move == Move.BehindTheBack || _move == Move.Spin)
        {
            _hand = -_hand;
        }
        StartFootwork();

        float push = Pick(_move == Move.Stepback || _move == Move.StepbackBallSide ? _stepbackPushSpeed : _movePushSpeed);
        // On its way down, the ball keeps whichever is faster: its own speed or the push.
        float newVertical = Mathf.Min(vertical, -push);

        string interrupt = interrupted != Move.None ? $", interrupting {interrupted} {interruptedAge:0.00} s in" : "";
        Debug.Log($"Dribble move: {MoveName(_move, _moveExplosive)}, {HandName(_pushHand)} hand to {HandName(_hand)}, started {Time.time - _moveFlickTime:0.00} s after the flick{interrupt}.");

        if (interrupted != Move.None) CheckRedirect(ballPosition, newVertical);
        return newVertical;
    }

    // The move's footwork, which goes by the player's body, not the stick: x =
    // their right, z = their forward. Whatever footwork the last move started
    // stops first. Explosive (R2) footwork travels farther and quicker, with a
    // short recovery afterwards. Sideways moves keep the player's running speed
    // and add their shift on top; stepbacks replace it. _pushHand is the hand
    // the ball started in and _hand the one it ends in.
    private void StartFootwork()
    {
        _hesitationBurstAt = float.PositiveInfinity;
        if (_movement == null) return;
        _movement.CancelMoveFootwork();

        float time = Pick(_footworkTime);
        float recovery = _moveExplosive ? _explosiveRecoveryTime : 0f;
        switch (_move)
        {
            case Move.Crossover:
                // Sideways toward the new ball hand, a little forward.
                _movement.Burst(new Vector3(_hand, 0f, _crossoverShiftForward), Pick(_crossoverShift), time, 0f, recovery, true);
                break;
            case Move.BetweenTheLegs:
                _movement.Burst(new Vector3(_hand, 0f, 0f), _betweenTheLegsShift, time, 0f, 0f, true);
                break;
            case Move.SideStep:
                // A hop toward the side away from the ball hand; the ball comes along.
                _movement.Burst(new Vector3(-_pushHand, 0f, 0f), _sideStepHop, time, 0f, recovery, true);
                break;
            case Move.BehindTheBack:
                _movement.Burst(new Vector3(_hand, 0f, 0f), Pick(_behindTheBackShift), time, 0f, recovery, true);
                break;
            case Move.Stepback:
                // Straight back, still facing forward.
                _movement.Burst(Vector3.back, Pick(_stepbackHop), time, 0f, recovery);
                break;
            case Move.StepbackBallSide:
                float radians = _stepbackBallSideAngle * Mathf.Deg2Rad;
                _movement.Burst(new Vector3(Mathf.Sin(radians) * _pushHand, 0f, -Mathf.Cos(radians)), Pick(_stepbackHop), time, 0f, recovery);
                break;
            case Move.Hesitation:
                if (_moveExplosive)
                {
                    // Almost a full stop, then a burst forward (started in Dribble).
                    _movement.SlowDown(_explosiveHesitationSpeed, _explosiveHesitationStop);
                    _hesitationBurstAt = Time.time + _explosiveHesitationStop;
                }
                else
                {
                    _movement.SlowDown(_hesitationSpeed, _hesitationTime);
                }
                break;
            case Move.Spin:
                // A spin turns away from the ball's side (right hand = spin to the left).
                float spinTime = Pick(_spinTime);
                _movement.Spin(-360f * _pushHand, spinTime);
                if (_moveExplosive)
                {
                    // Keeps going the way the player was moving (forward if standing),
                    // in the body's frame as it was when the spin started.
                    Vector3 moving = _body.linearVelocity;
                    moving.y = 0f;
                    Vector3 local = moving.magnitude > 0.5f ? Quaternion.Inverse(_body.rotation) * moving : Vector3.forward;
                    _movement.Burst(local, _explosiveSpinCarry, spinTime, 1f, recovery);
                }
                break;
        }
    }

    // A value with a normal (x) and an explosive (y) version, for the current move.
    private float Pick(Vector2 normalAndExplosive)
    {
        return _moveExplosive ? normalAndExplosive.y : normalAndExplosive.x;
    }

    private static string MoveName(Move move, bool explosive)
    {
        return explosive ? $"Explosive {move}" : move.ToString();
    }

    // An interrupting move turns the ball from wherever the last move was
    // sending it, toward the new bounce. The faster it was going the other way
    // and the less time is left to get there, the bigger the change of speed;
    // with fumbles on, more than the hand can give it fumbles it.
    private void CheckRedirect(Vector3 ballPosition, float vertical)
    {
        Vector3 toBounce = MoveBouncePoint() - ballPosition;
        toBounce.y = 0f;
        float time = TimeToFall(ballPosition.y - _ball.Radius, -vertical);
        Vector3 needed = Vector3.ClampMagnitude(toBounce / Mathf.Max(time, 0.05f), _maxMoveSpeed);

        Vector3 current = _ball.Body.linearVelocity - _body.linearVelocity;
        current.y = 0f;
        float change = (needed - current).magnitude;
        Debug.Log($"Redirect: {change:0.0} m/s change of the ball's speed (max {_maxRedirectChange:0.0}).");
        if (_allowFumbles && change > _maxRedirectChange)
        {
            Fumble($"Fumbled the move: turning the ball for the {_move} took {change:0.0} m/s (max redirect {_maxRedirectChange:0.0}).");
        }
    }

    private static string HandName(int hand)
    {
        return hand > 0 ? "right" : "left";
    }

    // An explosive hesitation bursts forward once its stop is over, unless
    // another move has taken over since.
    private void StartHesitationBurst()
    {
        if (Time.time < _hesitationBurstAt) return;
        _hesitationBurstAt = float.PositiveInfinity;
        if (_move != Move.Hesitation || _movement == null) return;
        _movement.Burst(Vector3.forward, _hesitationBurst, _footworkTime.y, 1f, _explosiveRecoveryTime);
    }

    // While protecting, if the ball is on the side facing the hoop (later: the
    // defender), cross it over to the other hand.
    private void KeepBallAwayFromDefense()
    {
        if (_protectFrom == null || _input.Protect < 0.5f || _move != Move.None || _requestedMove != Move.None) return;

        Vector3 local = Quaternion.Inverse(_body.rotation) * (_protectFrom.position - _body.position);
        // Only when the hoop is clearly off to one side; straight ahead either hand is fine.
        if (Mathf.Abs(local.x) > 0.5f && Mathf.Sign(local.x) == _hand) RequestMove(Move.Crossover, -1, false);
    }

    // The hand stops guiding the ball: it flies on with whatever speed it has.
    private void Fumble(string reason)
    {
        Vector3 speed = _body.linearVelocity;
        speed.y = 0f;
        string move = _move != Move.None ? $", {_move} {Time.time - _moveStartTime:0.00} s in" : "";
        Debug.Log($"{reason} Speed {speed.magnitude:0.0} m/s, sprint {_input.Sprint:0.00}, turning {_turnRate:0} deg/s{move}.");
        _move = Move.None;
        _requestedMove = Move.None;
        _hesitationBurstAt = float.PositiveInfinity;
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
        return IsWithinReach(ballPosition, _hand);
    }

    private bool IsWithinReach(Vector3 ballPosition, int hand)
    {
        if (_playerBody == null) return true;
        Vector3 shoulder = _body.position + _body.rotation * _playerBody.GetShoulderLocal(hand);
        return Vector3.Distance(shoulder, ballPosition) <= _playerBody.ArmReach + _ball.Radius + _reachSlack;
    }

    // For the fumble log: how far the ball is from the shoulder, and how far the hand can reach.
    private string ReachInfo(Vector3 ballPosition)
    {
        if (_playerBody == null) return "no body";
        Vector3 shoulder = _body.position + _body.rotation * _playerBody.GetShoulderLocal(_hand);
        float limit = _playerBody.ArmReach + _ball.Radius + _reachSlack;
        return $"{Vector3.Distance(shoulder, ballPosition):0.00} m from the shoulder, reach {limit:0.00} m with slack {_reachSlack:0.00}";
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
        _requestedMove = Move.None;
        _hesitationBurstAt = float.PositiveInfinity;
        _hasCarryTarget = false;
        _passThroughUntil = float.NegativeInfinity;
        // While the player has the ball, it passes through their capsule and body
        // parts, the way a real ball moves around a player's legs and body instead
        // of bumping off. A loose ball bounces off them.
        IgnoreOwnBall(state != State.Loose);
    }

    private bool IsBallInCapsule()
    {
        Collider ballCollider = _ball.GetComponent<Collider>();
        return Physics.ComputePenetration(
            _collider, _body.position, _body.rotation,
            ballCollider, _ball.Body.position, _ball.Body.rotation,
            out _, out _);
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
