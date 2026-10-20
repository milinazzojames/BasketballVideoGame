using UnityEngine;

// Hold-and-release shooting. Hold Shoot (Square / Space) to fill the shot
// meter from 0 to 1, and let go to shoot:
//   - inside the green window = a perfect shot, straight at the rim center (guaranteed make)
//   - early = the shot falls short, late = the shot goes long; the further from
//     the green, the bigger the miss, plus a little random left/right scatter
//   - holding until the meter is full shoots automatically (as a late release)
// The ball is teleported to the player's hands for now because there's no dribbling yet.
public class PlayerShooter : MonoBehaviour
{
    [SerializeField] private PlayerInputReader _input;
    [SerializeField] private Basketball _ball;
    [Tooltip("The rim to shoot at.")]
    [SerializeField] private Transform _rim;
    [Tooltip("The backboard, used for bank shots.")]
    [SerializeField] private Transform _backboard;

    [Header("Release")]
    [Tooltip("Height of the ball's center at release, in meters. A 6'6\" player's set shot releases around 2.6-2.8 m.")]
    [SerializeField] private float _releaseHeight = 2.7f;
    [Tooltip("How far in front of the player's center the ball is released, in meters.")]
    [SerializeField] private float _releaseForward = 0.3f;

    [Header("Arc")]
    [Tooltip("Launch angle above horizontal, in degrees. Good shooters release around 45-55.")]
    [SerializeField, Range(20f, 75f)] private float _launchAngle = 52f;
    [Tooltip("Backspin in revolutions per second. Real jump shots have about 2-3.")]
    [SerializeField] private float _backspin = 2.5f;

    [Header("Shot meter timing")]
    [Tooltip("Seconds to fill the meter from empty to full while holding Shoot.")]
    [SerializeField] private float _meterFillTime = 1f;
    [Tooltip("Where the green window starts on the meter (0 = bottom, 1 = top).")]
    [SerializeField, Range(0f, 1f)] private float _greenStart = 0.8f;
    [Tooltip("Where the green window ends on the meter. Above this is a late release.")]
    [SerializeField, Range(0f, 1f)] private float _greenEnd = 0.9f;

    [Header("Misses (outside the green)")]
    [Tooltip("How far short or long the shot lands, in meters, per unit of meter missed. With 3, releasing 0.05 outside the green misses by 15 cm.")]
    [SerializeField] private float _missDistancePerMeterError = 3f;
    [Tooltip("Random left/right scatter for non-green shots, in meters per meter of shot distance.")]
    [SerializeField] private float _aimErrorPerMeter = 0.02f;

    [Header("Bank shots (non-green shots only)")]
    [Tooltip("Shots from within this distance of the rim (meters, along the floor) can use the backboard.")]
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
    public float GreenStart => _greenStart;
    public float GreenEnd => _greenEnd;

    private void Awake()
    {
        if (_input == null) _input = GetComponent<PlayerInputReader>();
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

        Shoot(timingError);
    }

    private void Shoot(float timingError)
    {
        if (_ball == null || _rim == null) return;

        // Release in front of the player, at release height.
        Vector3 toRim = _rim.position - transform.position;
        toRim.y = 0f;
        Vector3 shotDirection = toRim.normalized;
        Vector3 release = transform.position + shotDirection * _releaseForward;
        release.y = _releaseHeight;

        Vector3 aimPoint = _rim.position;
        bool green = timingError == 0f;
        if (!green)
        {
            if (ShouldBank(toRim)) aimPoint = GetBankTarget();

            // Early lands short, late lands long (along the shot direction)...
            aimPoint += shotDirection * (timingError * _missDistancePerMeterError);

            // ...plus random left/right scatter that grows with distance.
            Vector3 sideways = Vector3.Cross(Vector3.up, shotDirection);
            aimPoint += sideways * (Random.Range(-1f, 1f) * _aimErrorPerMeter * toRim.magnitude);
        }

        if (!ShotMath.TryGetLaunchVelocity(release, aimPoint, _launchAngle, out Vector3 velocity)) return;

        _ball.Place(release);
        _ball.Launch(velocity, ShotMath.GetBackspin(aimPoint - release, _backspin));
    }

    private bool ShouldBank(Vector3 toRim)
    {
        if (_backboard == null || toRim.magnitude > _bankShotRange) return false;

        // Angle between "straight out from the board" and "from the rim to the player".
        Vector3 boardNormal = -_backboard.forward;
        float angle = Vector3.Angle(Flatten(boardNormal), -toRim);
        return angle >= _bankMinAngle && angle <= _bankMaxAngle && Random.value < _bankShotChance;
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
