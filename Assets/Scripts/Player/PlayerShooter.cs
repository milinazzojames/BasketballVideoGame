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
// Later idea (not built yet): a green shot nobody touches is guaranteed to go in.
public class PlayerShooter : MonoBehaviour
{
    [SerializeField] private PlayerInputReader _input;
    [SerializeField] private PlayerDribbler _dribbler;
    [SerializeField] private Basketball _ball;
    [Tooltip("The rim to shoot at.")]
    [SerializeField] private Transform _rim;
    [Tooltip("The backboard, used for bank shots.")]
    [SerializeField] private Transform _backboard;

    [Header("Release")]
    [Tooltip("Height of the ball's center at release, in meters. A 6'6\" player's set shot releases around 2.6-2.8 m. One value for everyone for now; it'll become per-player later.")]
    [SerializeField] private float _releaseHeight = 2.7f;
    [Tooltip("How far in front of the player's center the ball is released, in meters.")]
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
    [Tooltip("Where the green window starts on the meter (0 = bottom, 1 = top). Wide for now to make testing easy; narrow it later for challenge.")]
    [SerializeField, Range(0f, 1f)] private float _greenStart = 0.6f;
    [Tooltip("Where the green window ends on the meter. Above this is a late release.")]
    [SerializeField, Range(0f, 1f)] private float _greenEnd = 0.95f;

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

    // Read by the shot meter display.
    public bool IsCharging { get; private set; }
    public float MeterFill => IsCharging ? Mathf.Clamp01((Time.time - _holdStartTime) / _meterFillTime) : LastReleaseFill;
    public float LastReleaseFill { get; private set; }
    public float LastReleaseTime { get; private set; } = float.NegativeInfinity;
    public bool LastReleaseWasGreen { get; private set; }
    public float LastLaunchAngle { get; private set; }
    public float GreenStart => _greenStart;
    public float GreenEnd => _greenEnd;

    private void Awake()
    {
        if (_input == null) _input = GetComponent<PlayerInputReader>();
        if (_dribbler == null) _dribbler = GetComponent<PlayerDribbler>();
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

    private void StartCharging()
    {
        // No ball, no shot.
        if (_dribbler == null || !_dribbler.Gather()) return;

        IsCharging = true;
        _holdStartTime = Time.time;
    }

    private void Release()
    {
        if (!IsCharging) return;

        float fill = MeterFill;
        IsCharging = false;
        LastReleaseFill = fill;
        LastReleaseTime = Time.time;
        LastReleaseWasGreen = fill >= _greenStart && fill <= _greenEnd;

        // How far outside the green the release was: negative = early, positive = late, 0 = green.
        float timingError = 0f;
        if (fill < _greenStart) timingError = fill - _greenStart;
        else if (fill > _greenEnd) timingError = fill - _greenEnd;

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

        Vector3 release = layup ? GetLayupRelease(rimToPlayer) : GetJumpShotRelease(rimToPlayer);
        Vector3 shotDirection = Flatten(_rim.position - release);

        // Green layups can bank too; green jump shots always go straight at the rim.
        bool bank = layup ? ShouldBankLayup(rimToPlayer) : !green && ShouldBankJumpShot(rimToPlayer);
        Vector3 aimPoint = bank ? GetBankTarget() : _rim.position;
        if (!green)
        {
            // Early lands short, late lands long (along the shot direction)...
            aimPoint += shotDirection * (timingError * _missDistancePerMeterError);

            // ...plus random left/right scatter that grows with distance.
            Vector3 sideways = Vector3.Cross(Vector3.up, shotDirection);
            aimPoint += sideways * (Random.Range(-1f, 1f) * _aimErrorPerMeter * distance);
        }

        float launchAngle = layup ? PickLayupAngle(bank) : PickLaunchAngle(distance);
        if (!ShotMath.TryGetLaunchVelocity(release, aimPoint, launchAngle, out Vector3 velocity)) return;

        LastLaunchAngle = launchAngle;
        _ball.Place(release);
        _ball.Launch(velocity, ShotMath.GetBackspin(aimPoint - release, layup ? _layupBackspin : _backspin));

        string shotType = layup ? (bank ? "Layup off the glass" : "Layup") : (bank ? "Bank jump shot" : "Jump shot");
        Debug.Log($"{shotType}: {distance:0.0} m from the rim, {launchAngle:0.0} deg, {(green ? "GREEN" : $"timing off by {timingError:+0.00;-0.00}")}");
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
    // through the board and kept going to a "mirror rim" behind it. So we aim at
    // that mirror rim, and the board bounces the ball into the real one.
    // The board takes away some speed into the board (bounciness below 1), so
    // after the bounce the ball covers less distance; we put the mirror rim
    // farther back by dividing by the bounciness to make up for it.
    private Vector3 GetBankTarget()
    {
        Vector3 boardNormal = Flatten(-_backboard.forward);   // points from the board toward the court
        // The ball's center can only get within one ball radius of the board face.
        Vector3 boardFace = _backboard.position + boardNormal * (_backboard.lossyScale.z / 2f);
        Vector3 bouncePlane = boardFace + boardNormal * _ball.Radius;

        float rimToPlane = Vector3.Dot(_rim.position - bouncePlane, boardNormal);
        float mirrorDepth = rimToPlane / GetBoardBounciness();

        // Move the rim center straight back through the plane to the mirror spot.
        return _rim.position - boardNormal * (rimToPlane + mirrorDepth);
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
