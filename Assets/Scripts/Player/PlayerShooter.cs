using UnityEngine;

// Hold-and-release shooting. Hold Shoot (Square / Space) to fill the shot
// meter from 0 to 1, and let go to shoot:
//   - inside the green window = a perfect shot, aimed straight at the rim center.
//     It's still a real physics shot (nothing forces it in), so it goes in
//     because the arc is right, and anything in its way can still knock it off.
//   - early = the shot falls short, late = the shot goes long; the further from
//     the green, the bigger the miss, plus a little random left/right scatter
//   - holding until the meter is full shoots automatically (as a late release)
// Every shot picks a random launch angle from a range that depends on distance:
// close shots go up steeper (they start below the rim and need to drop in from
// above), long shots flatter, like real shooters.
// Close to the rim, the shot is a layup instead: released higher (jumping, arm
// extended) and closer to the rim, at a steep angle, and usually off the glass.
// You can only shoot while dribbling: pressing Shoot gathers the ball into the
// hands (PlayerDribbler), and it's released from the release point when you let go.
// With a body (PlayerBody), the meter also lifts the ball from the chest to the
// set point above the forehead, and the player jumps so the top of the jump
// comes at the middle of the green window: the arm pushes the ball from the set
// point up to the release height (2.7 m, or 3.2 m for a layup), which is above
// a 1.98 m player's standing reach. The ball leaves from the shooting hand,
// wherever it is when you let go: release early and it's lower and on the way up.
// Later idea (not built yet): a green shot nobody touches is guaranteed to go in.
[DefaultExecutionOrder(-10)]   // carries the held ball before the dribbler moves it this step
public class PlayerShooter : MonoBehaviour
{
    [SerializeField] private PlayerInputReader _input;
    [SerializeField] private PlayerDribbler _dribbler;
    [SerializeField] private Basketball _ball;
    [Tooltip("The rim to shoot at.")]
    [SerializeField] private Transform _rim;
    [Tooltip("The backboard, used for bank shots.")]
    [SerializeField] private Transform _backboard;
    [Tooltip("The player's body. With one, the shot is lifted into the set point, the player jumps, and the ball leaves from the shooting hand. Optional.")]
    [SerializeField] private PlayerBody _body;
    [Tooltip("Makes the player jump on a shot. Uses the one on this object if empty.")]
    [SerializeField] private PlayerMovement _movement;

    [Header("Shot motion (with a body)")]
    [Tooltip("Shoot with the right hand (off = left hand).")]
    [SerializeField] private bool _shootRightHanded = true;
    [Tooltip("Set point: how far the ball's center is above the center of the head, in meters (above the forehead).")]
    [SerializeField] private float _setPointAboveHead = 0.2f;
    [Tooltip("Set point: how far in front of the head, toward the rim, in meters.")]
    [SerializeField] private float _setPointForward = 0.2f;
    [Tooltip("Set point: how far toward the shooting hand's side, in meters.")]
    [SerializeField] private float _setPointSide = 0.1f;
    [Tooltip("Meter fill (0-1) at which the ball reaches the set point. Before that, it rises from the chest.")]
    [SerializeField, Range(0.05f, 1f)] private float _liftEndFill = 0.6f;

    [Header("Jump (with a body)")]
    [Tooltip("The jump is as high as it needs to be for the hand to get the ball to the release point, up to this many meters.")]
    [SerializeField] private float _maxJumpHeight = 0.8f;
    [Tooltip("Jump shots: how far the arm is stretched at the release, as a fraction of its full reach (shoulder to the ball's center). Below 1, the elbow keeps a little bend.")]
    [SerializeField, Range(0.5f, 1f)] private float _jumpShotArmExtension = 0.95f;
    [Tooltip("Layups: the same, for layups. 1 = arm fully stretched up.")]
    [SerializeField, Range(0.5f, 1f)] private float _layupArmExtension = 1f;
    [Tooltip("The arm pushes the ball from the set point to the release point over at least this many seconds before the top of the jump (more if the jump takes longer to rise).")]
    [SerializeField] private float _minExtendTime = 0.2f;

    [Header("Release")]
    [Tooltip("Height of the ball's center at release, in meters. A 6'6\" player's set shot releases around 2.6-2.8 m. One value for everyone for now; it'll become per-player later. With a body, it's where the arm takes the ball at the top of the jump.")]
    [SerializeField] private float _releaseHeight = 2.7f;
    [Tooltip("How far in front of the player's center the ball is released, in meters. With a body, it's where the arm takes the ball at the top of the jump.")]
    [SerializeField] private float _releaseForward = 0.3f;

    [Header("Arc (launch angle above horizontal, in degrees)")]
    [Tooltip("Jump shots with the player this close to the rim (meters from the player's center to the rim center, along the floor) or closer use the close-shot angle range.")]
    [SerializeField] private float _closeShotDistance = 1.5f;
    [Tooltip("Jump shots with the player this far from the rim (same measure) or farther use the long-shot angle range. In between, the range blends smoothly from one to the other.")]
    [SerializeField] private float _longShotDistance = 6.5f;
    [Tooltip("Lowest and highest launch angle for close shots (x = lowest, y = highest). Below about 58 degrees, shots from 1-1.5 m clip the front rim.")]
    [SerializeField] private Vector2 _closeShotAngles = new Vector2(60f, 66f);
    [Tooltip("Lowest and highest launch angle for long shots (x = lowest, y = highest). Real jump shots and threes leave the hand at roughly 45-52 degrees.")]
    [SerializeField] private Vector2 _longShotAngles = new Vector2(46f, 52f);
    [Tooltip("Backspin in revolutions per second. Real jump shots have about 2-3.")]
    [SerializeField] private float _backspin = 2.5f;

    [Header("Layups")]
    [Tooltip("With the player this close to the rim (meters from the player's center to the rim center, along the floor) or closer, the shot is a layup instead of a jump shot.")]
    [SerializeField] private float _layupRange = 1.8f;
    [Tooltip("Height of the ball's center when a layup leaves the hand, in meters (jumping, arm extended). It's above the rim, so the ball doesn't have to climb over the front of it.")]
    [SerializeField] private float _layupReleaseHeight = 3.2f;
    [Tooltip("How far in front of the player's center, toward the rim, a layup is released, in meters.")]
    [SerializeField] private float _layupReleaseForward = 0.45f;
    [Tooltip("A layup is never released closer to the rim center than this (meters, along the floor), even with the player right under the rim.")]
    [SerializeField] private float _layupMinReleaseDistance = 0.3f;
    [Tooltip("From under or behind the rim, the release point moves out until it's at least this far in front of the rim center (meters, toward the court), so the ball doesn't start under the backboard.")]
    [SerializeField] private float _layupMinInFrontOfRim = 0.2f;
    [Tooltip("Lowest and highest launch angle for layups that go directly at the rim (x = lowest, y = highest).")]
    [SerializeField] private Vector2 _layupAngles = new Vector2(70f, 80f);
    [Tooltip("Lowest and highest launch angle for layups off the glass. Flatter than direct layups: above about 72 degrees, a bank layup from 1 m or more hits the glass too high or flies over the board.")]
    [SerializeField] private Vector2 _layupBankAngles = new Vector2(55f, 65f);
    [Tooltip("Backspin on layups, in revolutions per second. A layup is a soft flip with less spin than a jump shot. In testing, 0-3 made little difference to bank layups; the launch angle matters much more.")]
    [SerializeField] private float _layupBackspin = 1f;

    [Header("Layups off the glass (green layups too)")]
    [Tooltip("Within this many degrees of straight-on, a layup goes directly at the rim.")]
    [SerializeField, Range(0f, 90f)] private float _layupStraightOnAngle = 15f;
    [Tooltip("From the straight-on angle up to this many degrees off straight-on, a layup can use the backboard. Beyond it (along the baseline, or from behind the rim) there's no board to use, so it goes directly at the rim.")]
    [SerializeField, Range(0f, 90f)] private float _layupBankMaxAngle = 80f;
    [Tooltip("Chance (0-1) that a layup in the bank zone uses the backboard.")]
    [SerializeField, Range(0f, 1f)] private float _layupBankChance = 0.95f;

    [Header("Shot meter timing")]
    [Tooltip("Seconds to fill the meter from empty to full while holding Shoot.")]
    [SerializeField] private float _meterFillTime = 1f;
    [Tooltip("Middle of the green window on the meter (0 = bottom, 1 = top). The window grows and shrinks around this point, so it doesn't jump around.")]
    [SerializeField, Range(0f, 1f)] private float _greenCenter = 0.8f;
    [Tooltip("Width of the green window (in meter units) by the player's distance from the rim (meters, along the floor). Wide up close, narrow from deep. Set when the shot starts and kept until the release.")]
    [SerializeField] private AnimationCurve _greenWidthByDistance = AnimationCurve.EaseInOut(1.8f, 0.35f, 10f, 0.08f);

    [Header("Misses (outside the green)")]
    [Tooltip("How far short or long the shot lands, in meters, per unit of meter missed. With 3, releasing 0.05 outside the green misses by 15 cm.")]
    [SerializeField] private float _missDistancePerMeterError = 3f;
    [Tooltip("Random left/right scatter for non-green shots, in meters per meter of shot distance.")]
    [SerializeField] private float _aimErrorPerMeter = 0.02f;

    [Header("Bank jump shots (non-green shots only)")]
    [Tooltip("Jump shots with the player within this distance of the rim (meters, along the floor) can use the backboard.")]
    [SerializeField] private float _bankShotRange = 3.5f;
    [Tooltip("Only bank when at least this many degrees off straight-on. Straight in front of the rim, players shoot directly.")]
    [SerializeField, Range(0f, 90f)] private float _bankMinAngle = 20f;
    [Tooltip("Only bank when at most this many degrees off straight-on. From near the baseline there's no angle to use the board.")]
    [SerializeField, Range(0f, 90f)] private float _bankMaxAngle = 70f;
    [Tooltip("Chance (0-1) that an eligible shot uses the backboard.")]
    [SerializeField, Range(0f, 1f)] private float _bankShotChance = 0.6f;

    private float _holdStartTime;

    // The jump for the shot being charged.
    private bool _chargingLayup;
    private float _jumpHeight;
    private float _jumpSpeed;
    private float _takeoffTime;
    private float _peakTime;
    private float _extendStartTime;
    private float _groundSolesHeight;
    private bool _jumped;

    // Read by the shot meter display.
    public bool IsCharging { get; private set; }
    public float MeterFill => IsCharging ? Mathf.Clamp01((Time.time - _holdStartTime) / _meterFillTime) : LastReleaseFill;
    public float LastReleaseFill { get; private set; }
    public float LastReleaseTime { get; private set; } = float.NegativeInfinity;
    public bool LastReleaseWasGreen { get; private set; }
    public float LastLaunchAngle { get; private set; }
    public float GreenStart { get; private set; }
    public float GreenEnd { get; private set; }

    // Read by PlayerArms. +1 = right hand, -1 = left hand.
    public int ShootingSide => _shootRightHanded ? 1 : -1;
    // Flat direction from the player toward the rim.
    public Vector3 ShotDirection => _rim != null ? Flatten(_rim.position - transform.position) : transform.forward;

    private void Awake()
    {
        if (_input == null) _input = GetComponent<PlayerInputReader>();
        if (_dribbler == null) _dribbler = GetComponent<PlayerDribbler>();
        if (_movement == null) _movement = GetComponent<PlayerMovement>();
    }

    private void OnEnable()
    {
        _input.ShootPressed += StartCharging;
        _input.ShootReleased += Release;
    }

    private void OnDisable()
    {
        _input.ShootPressed -= StartCharging;
        _input.ShootReleased -= Release;
    }

    private void Update()
    {
        // Holding all the way to the top lets the shot go by itself.
        if (IsCharging && MeterFill >= 1f) Release();
    }

    // While charging: the ball rises with the meter from the chest to the set
    // point, the player jumps, and the arm pushes the ball up to the release
    // point by the top of the jump.
    private void FixedUpdate()
    {
        if (!IsCharging || _body == null || _rim == null) return;

        if (!_jumped && Time.time >= _takeoffTime)
        {
            _jumped = true;
            if (_jumpHeight > 0.01f && _movement != null) _movement.Jump(_jumpSpeed);
        }

        float lift = Mathf.SmoothStep(0f, 1f, MeterFill / _liftEndFill);
        Vector3 point = Vector3.Lerp(_dribbler.GetHoldPoint(), GetSetPoint(), lift);

        float extend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(_extendStartTime, _peakTime, Time.time));
        float belowPeak = _groundSolesHeight + _jumpHeight - _body.SolesHeight;
        point = Vector3.Lerp(point, GetReleaseAim(belowPeak), extend);

        // Never farther than the hand can hold it.
        _dribbler.CarryHeldBall(_body.ClampToReach(ShootingSide, point, _ball.Radius));
    }

    // Above the forehead, a little in front and toward the shooting hand.
    private Vector3 GetSetPoint()
    {
        Vector3 toRim = ShotDirection;
        Vector3 toShootingSide = Vector3.Cross(Vector3.up, toRim) * ShootingSide;
        return _body.GetHeadCenter() + Vector3.up * _setPointAboveHead + toRim * _setPointForward + toShootingSide * _setPointSide;
    }

    // With a body, the ball leaves from the shooting hand: it sits on the palm,
    // so its center is one ball radius out from it.
    private Vector3 GetHandRelease()
    {
        _body.GetPalm(ShootingSide, out Vector3 palm, out Vector3 palmFacing);
        return KeepInFrontOfBoard(palm + palmFacing * _ball.Radius);
    }

    // From under the rim, the hand can be under or behind the backboard; the
    // ball has to start in front of it (the same limit layups use).
    private Vector3 KeepInFrontOfBoard(Vector3 release)
    {
        if (_backboard == null || _rim == null) return release;
        Vector3 boardNormal = Flatten(-_backboard.forward);   // points from the board toward the court
        float inFront = Vector3.Dot(release - _rim.position, boardNormal);
        if (inFront < _layupMinInFrontOfRim) release += boardNormal * (_layupMinInFrontOfRim - inFront);
        return release;
    }

    private void StartCharging()
    {
        // No ball, no shot.
        if (_dribbler == null || !_dribbler.Gather()) return;

        IsCharging = true;
        _holdStartTime = Time.time;

        // The green window is sized once, when the shot starts, so it can't
        // change under the player while they're timing the release.
        float width = _greenWidthByDistance.Evaluate(GetDistanceToRim());
        GreenStart = Mathf.Clamp01(_greenCenter - width / 2f);
        GreenEnd = Mathf.Clamp01(_greenCenter + width / 2f);

        if (_body != null && _ball != null && _rim != null) PlanJump();
    }

    // Works out how high to jump and when to leave the floor, so the top of
    // the jump comes at the middle of the green window, with the shooting
    // hand able to reach the release point there.
    private void PlanJump()
    {
        _chargingLayup = GetDistanceToRim() <= _layupRange;
        _groundSolesHeight = _body.SolesHeight;
        _jumped = false;

        // Standing, with the arm raised (which lifts the shoulder), how high can
        // the hand get the ball at the release point's distance along the floor?
        // Pythagoras: the arm's reach is the long side, the floor distance one short side.
        Vector3 aim = GetReleaseAim(0f);
        Vector3 shoulder = _body.GetShoulder(ShootingSide);
        Vector3 flat = aim - shoulder;
        flat.y = 0f;
        float extension = _chargingLayup ? _layupArmExtension : _jumpShotArmExtension;
        float reach = (_body.ArmReach + _ball.Radius) * extension;
        float shoulderHeight = _groundSolesHeight + _body.StandingShoulderHeight + _body.ShoulderLiftOverhead;
        float standingBallHeight = shoulderHeight + Mathf.Sqrt(Mathf.Max(0f, reach * reach - flat.sqrMagnitude));
        _jumpHeight = Mathf.Clamp(aim.y - standingBallHeight, 0f, _maxJumpHeight);

        // Jumping h meters high takes an up speed of sqrt(2 * g * h), and reaches
        // the top after (up speed / g) seconds.
        float gravity = -Physics.gravity.y;
        _jumpSpeed = Mathf.Sqrt(2f * gravity * _jumpHeight);
        _peakTime = _holdStartTime + _greenCenter * _meterFillTime;
        _takeoffTime = _peakTime - _jumpSpeed / gravity;
        _extendStartTime = Mathf.Min(_takeoffTime, _peakTime - _minExtendTime);
    }

    // Where the arm takes the ball at the top of the jump (the old fixed release
    // point), lowered by however far the body is below the top of the jump.
    // heightBelowPeak = how far the soles are below where they'll be at the top.
    private Vector3 GetReleaseAim(float heightBelowPeak)
    {
        Vector3 rimToPlayer = transform.position - _rim.position;
        rimToPlayer.y = 0f;
        Vector3 aim = _chargingLayup ? GetLayupRelease(rimToPlayer) : GetJumpShotRelease(rimToPlayer);
        aim.y -= heightBelowPeak;
        return aim;
    }

    private void Release()
    {
        if (!IsCharging) return;

        float fill = MeterFill;
        IsCharging = false;
        LastReleaseFill = fill;
        LastReleaseTime = Time.time;
        LastReleaseWasGreen = fill >= GreenStart && fill <= GreenEnd;

        // How far outside the green the release was: negative = early, positive = late, 0 = green.
        float timingError = 0f;
        if (fill < GreenStart) timingError = fill - GreenStart;
        else if (fill > GreenEnd) timingError = fill - GreenEnd;

        Shoot(LastReleaseWasGreen, timingError);
    }

    private void Shoot(bool green, float timingError)
    {
        // Let go first, so the ball is a physics object again even if the shot
        // can't be worked out below (it just drops out of the hands).
        _dribbler.LetGo();
        if (_ball == null || _rim == null) return;

        // Shot distance is always the player's center to the rim center, along the floor.
        Vector3 rimToPlayer = transform.position - _rim.position;
        rimToPlayer.y = 0f;
        float distance = rimToPlayer.magnitude;
        bool layup = distance <= _layupRange;

        Vector3 release = _body != null ? GetHandRelease()
            : layup ? GetLayupRelease(rimToPlayer) : GetJumpShotRelease(rimToPlayer);
        Vector3 shotDirection = Flatten(_rim.position - release);

        // Green layups can bank too; green jump shots always go straight at the rim.
        bool bank = layup ? ShouldBankLayup(rimToPlayer) : !green && ShouldBankJumpShot(rimToPlayer);

        // The miss is worked out around the real rim first, so early always
        // means short and late always means long. Only then is it mirrored
        // through the board for a bank shot: mirroring first would flip the
        // part of the miss that points into the board, and a late bank shot
        // from an angle could land short.
        Vector3 target = _rim.position;
        if (!green)
        {
            // Early lands short, late lands long (along the shot direction)...
            target += shotDirection * (timingError * _missDistancePerMeterError);

            // ...plus random left/right scatter that grows with distance.
            Vector3 sideways = Vector3.Cross(Vector3.up, shotDirection);
            target += sideways * (Random.Range(-1f, 1f) * _aimErrorPerMeter * distance);
        }
        Vector3 aimPoint = bank ? GetBankTarget(target) : target;

        float launchAngle = layup ? PickLayupAngle(bank) : PickLaunchAngle(distance);
        // A release from the hand can be low or high (early or late, mid-jump),
        // and a flat angle can't reach a target well above the release. Then
        // the shot goes up steeper instead, so it always heads for the hoop.
        Vector3 velocity;
        while (!ShotMath.TryGetLaunchVelocity(release, aimPoint, launchAngle, out velocity))
        {
            launchAngle += 5f;
            if (launchAngle >= 89f) return;
        }

        LastLaunchAngle = launchAngle;
        _ball.Place(release);
        _ball.Launch(velocity, ShotMath.GetBackspin(aimPoint - release, layup ? _layupBackspin : _backspin));

        string shotType = layup ? (bank ? "Layup off the glass" : "Layup") : (bank ? "Bank jump shot" : "Jump shot");
        Debug.Log($"{shotType}: {distance:0.0} m from the rim, {launchAngle:0.0} deg, green window {GreenEnd - GreenStart:0.00}, {(green ? "GREEN" : $"timing off by {timingError:+0.00;-0.00}")}");
    }

    // The player's center to the rim center, along the floor, in meters.
    private float GetDistanceToRim()
    {
        if (_rim == null) return 0f;
        Vector3 rimToPlayer = transform.position - _rim.position;
        rimToPlayer.y = 0f;
        return rimToPlayer.magnitude;
    }

    // A jump shot leaves the hands a little in front of the player, at release height.
    private Vector3 GetJumpShotRelease(Vector3 rimToPlayer)
    {
        Vector3 release = transform.position - rimToPlayer.normalized * _releaseForward;
        release.y = _releaseHeight;
        return release;
    }

    // A layup leaves the hand up high, a step in front of the player toward the rim.
    private Vector3 GetLayupRelease(Vector3 rimToPlayer)
    {
        // From under or behind the rim, the ball would start under the backboard,
        // so the release moves out in front of the rim. (A reverse layup off the
        // glass can come later.)
        if (_backboard != null)
        {
            Vector3 boardNormal = Flatten(-_backboard.forward);   // points from the board toward the court
            float inFront = Vector3.Dot(rimToPlayer, boardNormal);
            if (inFront < _layupMinInFrontOfRim) rimToPlayer += boardNormal * (_layupMinInFrontOfRim - inFront);
        }

        float releaseDistance = Mathf.Max(rimToPlayer.magnitude - _layupReleaseForward, _layupMinReleaseDistance);
        Vector3 release = _rim.position + rimToPlayer.normalized * releaseDistance;
        release.y = _layupReleaseHeight;
        return release;
    }

    // Picks a random launch angle for a jump shot with the player this far from
    // the rim (meters, along the floor). Close and long shots each have their own
    // range; in between, both ends of the range slide smoothly from the close
    // values to the long values.
    private float PickLaunchAngle(float distance)
    {
        float t = Mathf.InverseLerp(_closeShotDistance, _longShotDistance, distance);   // 0 = close, 1 = long
        float lowest = Mathf.Lerp(_closeShotAngles.x, _longShotAngles.x, t);
        float highest = Mathf.Lerp(_closeShotAngles.y, _longShotAngles.y, t);
        return Random.Range(lowest, highest);
    }

    private float PickLayupAngle(bool bank)
    {
        Vector2 range = bank ? _layupBankAngles : _layupAngles;
        return Random.Range(range.x, range.y);
    }

    private bool ShouldBankJumpShot(Vector3 rimToPlayer)
    {
        if (_backboard == null || rimToPlayer.magnitude > _bankShotRange) return false;

        float angle = GetAngleOffStraightOn(rimToPlayer);
        return angle >= _bankMinAngle && angle <= _bankMaxAngle && Random.value < _bankShotChance;
    }

    // Layups go off the glass almost every time. Straight on, they go directly in;
    // along the baseline (or from behind the rim) there's no board to use.
    private bool ShouldBankLayup(Vector3 rimToPlayer)
    {
        if (_backboard == null) return false;

        float angle = GetAngleOffStraightOn(rimToPlayer);
        return angle > _layupStraightOnAngle && angle <= _layupBankMaxAngle && Random.value < _layupBankChance;
    }

    // Angle between "straight out from the board" and "from the rim to the player",
    // in degrees: 0 = straight on, 90 = along the baseline, over 90 = behind the rim.
    private float GetAngleOffStraightOn(Vector3 rimToPlayer)
    {
        return Vector3.Angle(Flatten(-_backboard.forward), rimToPlayer);
    }

    // Mirror trick: a ball bouncing off a flat board travels as if it passed
    // through the board and kept going to a "mirror point" behind it. So we aim
    // at the mirror of the point we want to hit (the rim, or a spot a little
    // short or long of it for a miss), and the board bounces the ball into it.
    // The board takes away some speed into the board (bounciness below 1), so
    // after the bounce the ball covers less distance; we put the mirror point
    // farther back by dividing by the bounciness to make up for it.
    private Vector3 GetBankTarget(Vector3 point)
    {
        Vector3 boardNormal = Flatten(-_backboard.forward);   // points from the board toward the court
        // The ball's center can only get within one ball radius of the board face.
        Vector3 boardFace = _backboard.position + boardNormal * (_backboard.lossyScale.z / 2f);
        Vector3 bouncePlane = boardFace + boardNormal * _ball.Radius;

        float pointToPlane = Vector3.Dot(point - bouncePlane, boardNormal);
        float mirrorDepth = pointToPlane / GetBoardBounciness();

        // Move the point straight back through the plane to the mirror spot.
        return point - boardNormal * (pointToPlane + mirrorDepth);
    }

    // Unity averages the ball's and board's bounciness (both use "Average" combine).
    private float GetBoardBounciness()
    {
        PhysicsMaterial ballMaterial = _ball.GetComponent<Collider>().sharedMaterial;
        PhysicsMaterial boardMaterial = _backboard.GetComponent<Collider>().sharedMaterial;
        if (ballMaterial == null || boardMaterial == null) return 1f;

        return Mathf.Max(0.1f, (ballMaterial.bounciness + boardMaterial.bounciness) / 2f);
    }

    private static Vector3 Flatten(Vector3 direction)
    {
        direction.y = 0f;
        return direction.normalized;
    }
}
