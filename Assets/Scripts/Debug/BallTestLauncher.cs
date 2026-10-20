using UnityEngine;
using UnityEngine.InputSystem;

// Test tool for ball physics, not gameplay. Reads keyboard keys directly
// (instead of going through Input Actions) because it's only for testing.
//   R = drop the ball from 1.83 m in front of the player (real ball rebounds 1.25-1.37 m)
//   T = toss the ball at the rim from the free-throw line, with a little random aim error
// Shows the peak height of the ball's last bounce in the top-left corner.
public class BallTestLauncher : MonoBehaviour
{
    [SerializeField] private Basketball _ball;
    [SerializeField] private Transform _player;
    [SerializeField] private Transform _rim;

    [Header("Drop test (R)")]
    [Tooltip("Height of the bottom of the ball when dropped. 1.83 m (6 ft) is the standard bounce test.")]
    [SerializeField] private float _dropHeight = 1.83f;

    [Header("Toss test (T)")]
    [Tooltip("Distance from the rim center to the release point, along the floor. A free throw is about 4.2 m.")]
    [SerializeField] private float _tossDistance = 4.2f;
    [Tooltip("Release height of the ball's center, in meters.")]
    [SerializeField] private float _releaseHeight = 2.4f;
    [Tooltip("Launch angle above horizontal, in degrees. Good shooters release around 45-55.")]
    [SerializeField, Range(20f, 75f)] private float _launchAngle = 52f;
    [Tooltip("Backspin in revolutions per second. Real jump shots have about 2-3.")]
    [SerializeField] private float _backspin = 2.5f;
    [Tooltip("The toss aims at a random point within this distance of the rim center, so you see makes and rim bounces.")]
    [SerializeField] private float _aimError = 0.15f;

    private float _lastPeakHeight;
    private float _previousVerticalVelocity;

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || _ball == null) return;

        if (keyboard.rKey.wasPressedThisFrame) Drop();
        if (keyboard.tKey.wasPressedThisFrame) Toss();
    }

    // Peak detection runs with physics: the ball is at the top of a bounce
    // when its vertical speed flips from upward to downward.
    private void FixedUpdate()
    {
        if (_ball == null) return;

        float verticalVelocity = _ball.Body.linearVelocity.y;
        if (_previousVerticalVelocity > 0f && verticalVelocity <= 0f)
        {
            // Report the height of the ball's bottom, like the real-world test.
            _lastPeakHeight = _ball.Body.position.y - _ball.Radius;
        }
        _previousVerticalVelocity = verticalVelocity;
    }

    private void Drop()
    {
        Vector3 spot = _player != null ? _player.position + _player.forward * 1f : Vector3.zero;
        spot.y = _dropHeight + _ball.Radius;
        _ball.Place(spot);
        _previousVerticalVelocity = 0f;
    }

    private void Toss()
    {
        if (_rim == null) return;

        // Release point: straight out from the rim toward the court (the rim's -Z side).
        Vector3 courtDirection = -_rim.forward;
        courtDirection.y = 0f;
        courtDirection.Normalize();
        Vector3 release = _rim.position + courtDirection * _tossDistance;
        release.y = _releaseHeight;

        Vector2 error = Random.insideUnitCircle * _aimError;
        Vector3 aimPoint = _rim.position + new Vector3(error.x, 0f, error.y);

        _ball.Place(release);
        _ball.Launch(GetLaunchVelocity(release, aimPoint), GetBackspin(aimPoint - release));
        _previousVerticalVelocity = 0f;
    }

    // Projectile math: find the launch speed that, at the chosen angle, makes
    // the ball's path pass through the target. From the trajectory equation
    //   h = d*tan(a) - g*d^2 / (2*v^2*cos^2(a))
    // solved for v:
    //   v^2 = g*d^2 / (2*cos^2(a) * (d*tan(a) - h))
    // where d = flat distance, h = height difference, a = launch angle, g = gravity.
    private Vector3 GetLaunchVelocity(Vector3 from, Vector3 to)
    {
        Vector3 flat = to - from;
        float height = flat.y;
        flat.y = 0f;
        float distance = flat.magnitude;

        float angle = _launchAngle * Mathf.Deg2Rad;
        float gravity = -Physics.gravity.y;
        float denominator = 2f * Mathf.Cos(angle) * Mathf.Cos(angle) * (distance * Mathf.Tan(angle) - height);
        if (denominator <= 0f) return Vector3.up * 5f; // angle too flat to reach the target; just pop it up

        float speed = Mathf.Sqrt(gravity * distance * distance / denominator);
        Vector3 direction = flat.normalized * Mathf.Cos(angle) + Vector3.up * Mathf.Sin(angle);
        return direction * speed;
    }

    // Backspin: the top of the ball spins back toward the shooter. For a ball
    // flying forward, that's a rotation around the axis pointing to the shooter's left.
    private Vector3 GetBackspin(Vector3 travel)
    {
        travel.y = 0f;
        Vector3 axis = Vector3.Cross(travel.normalized, Vector3.up);
        return axis * (_backspin * 2f * Mathf.PI);
    }

    private void OnGUI()
    {
        GUI.Label(new Rect(10, 10, 400, 60),
            $"R = drop test   T = toss at rim\nLast bounce peak: {_lastPeakHeight:0.00} m (target 1.25-1.37 m from a 1.83 m drop)");
    }
}
