using UnityEngine;

// Decides where the hands go, every physics step, and hands that to
// PlayerBody (which bends the arms to get them there with two-bone IK).
// The dribble and the shot are still pure physics in PlayerDribbler and
// PlayerShooter; the arms just follow the ball so it looks like the hands move it:
//   - dribbling: the hand with the ball rides on top of it while it's above
//     knee height (down with the push, and back up to meet it), and waits at
//     the hand height while it's lower. The other arm is up in front, guarding.
//     During a move, the hand switches when the ball bounces toward the new hand.
//   - holding: both hands on the sides of the ball
//   - charging a shot: the shooting hand under and behind the ball, the guide
//     hand on its side, as the shot lifts it
//   - after the shot: the shooting arm follows through, up and out, wrist down
//   - otherwise: arms relaxed at the sides
// It also crouches the body for a low dribble, so the hand can get down to the ball.
[DefaultExecutionOrder(50)]   // after the dribbler and shooter move the ball, before PlayerBody poses the arms
public class PlayerArms : MonoBehaviour
{
    [SerializeField] private PlayerBody _body;
    [SerializeField] private PlayerDribbler _dribbler;
    [SerializeField] private PlayerShooter _shooter;
    [SerializeField] private Basketball _ball;

    [Header("Hand speed")]
    [Tooltip("How fast a hand moves to a new spot, in m/s (from the side to the ball, to a guard pose...).")]
    [SerializeField] private float _handSpeed = 6f;
    [Tooltip("How fast a hand follows the ball it's touching, in m/s. Faster than the ball ever moves, so the hand stays on it.")]
    [SerializeField] private float _onBallHandSpeed = 30f;
    [Tooltip("How fast a hand turns, in degrees per second.")]
    [SerializeField] private float _handTurnSpeed = 900f;

    [Header("Dribbling")]
    [Tooltip("While the ball's center is above this height (meters), the dribbling hand rides on top of it. Below it, the hand waits at the hand height for the ball to come back up. About knee height.")]
    [SerializeField] private float _kneeHeight = 0.55f;
    [Tooltip("Dribbling hand: how much the fingers point out to the side (0 = straight ahead, 1 = 45 degrees out).")]
    [SerializeField] private float _dribbleFingersOut = 0.3f;
    [Tooltip("Dribbling elbow hint, for the right arm (x = out, y = up, z = forward; the left arm is mirrored). Elbows out and a little back.")]
    [SerializeField] private Vector3 _dribbleElbowHint = new Vector3(1f, 0f, -0.6f);
    [Tooltip("The body crouches until the shoulder is within this fraction of the arm's full reach of the dribble, so the arm isn't stretched straight.")]
    [SerializeField, Range(0.5f, 1f)] private float _comfortableReach = 0.9f;

    [Header("Off arm (guarding the ball)")]
    [Tooltip("Where the off hand's palm is, from its shoulder, for the right arm (x = out, y = up, z = forward; mirrored for the left). Forearm out in front.")]
    [SerializeField] private Vector3 _guardHandOffset = new Vector3(0.05f, -0.4f, 0.4f);
    [Tooltip("Off arm elbow hint, for the right arm (mirrored for the left). Out and down.")]
    [SerializeField] private Vector3 _guardElbowHint = new Vector3(1f, -1f, 0f);

    [Header("Holding and shooting")]
    [Tooltip("Holding elbow hint, for the right arm (mirrored for the left). Out and down.")]
    [SerializeField] private Vector3 _holdElbowHint = new Vector3(1f, -1f, -0.3f);
    [Tooltip("Shooting elbow hint, for the shooting arm on the right (mirrored for the left). Under the ball, a little in front.")]
    [SerializeField] private Vector3 _shootElbowHint = new Vector3(0.3f, -1f, 0.6f);
    [Tooltip("Where the shooting hand holds the ball: how far it sits under the ball (0 = straight behind it, 1 = straight under it).")]
    [SerializeField, Range(0f, 1f)] private float _shootHandUnder = 0.6f;
    [Tooltip("Seconds the shooting arm follows through after the release.")]
    [SerializeField] private float _followThroughTime = 0.6f;
    [Tooltip("Follow-through: angle of the shooting arm above horizontal, toward the rim, in degrees.")]
    [SerializeField] private float _followThroughAngle = 65f;

    // Smoothed hand poses (index 0 = left, 1 = right).
    private readonly Vector3[] _palm = new Vector3[2];
    private readonly Quaternion[] _handRotation = new Quaternion[2];
    private bool _started;
    private Rigidbody _rigidbody;

    private void Awake()
    {
        _rigidbody = GetComponent<Rigidbody>();
        if (_body == null) _body = GetComponent<PlayerBody>();
        if (_dribbler == null) _dribbler = GetComponent<PlayerDribbler>();
        if (_shooter == null) _shooter = GetComponent<PlayerShooter>();
    }

    private void FixedUpdate()
    {
        if (_body == null || _dribbler == null || _ball == null) return;

        float crouch = 0f;
        for (int side = -1; side <= 1; side += 2)
        {
            PickTarget(side, out Vector3 palm, out Vector3 fingers, out Vector3 palmFacing, out Vector3 elbowHint, out bool onBall);
            MoveHand(side, palm, fingers, palmFacing, onBall);
            int i = side > 0 ? 1 : 0;
            _body.SetHandTarget(side, _palm[i], _handRotation[i] * Vector3.up, _handRotation[i] * Vector3.forward, elbowHint);
        }

        if (_dribbler.IsDribbling) crouch = GetDribbleCrouch();
        _body.SetCrouch(crouch);
        _started = true;
    }

    // Where one hand should go this step.
    private void PickTarget(int side, out Vector3 palm, out Vector3 fingers, out Vector3 palmFacing, out Vector3 elbowHint, out bool onBall)
    {
        // The physics rotation, not the smoothed transform, like the dribbler.
        Quaternion facing = _rigidbody.rotation;
        Vector3 up = Vector3.up;
        Vector3 forward = facing * Vector3.forward;
        Vector3 right = facing * Vector3.right;
        float radius = _ball.Radius;
        onBall = false;

        // The ball where it'll be at the end of this step, like the body.
        Vector3 ball = _ball.Body.position + _ball.Body.linearVelocity * Time.fixedDeltaTime;
        int shootingSide = _shooter != null ? _shooter.ShootingSide : 1;

        if (_dribbler.IsDribbling)
        {
            if (side == _dribbler.TouchHand || side == _dribbler.BallHand)
            {
                // Palm down, fingers forward and a little out.
                palmFacing = -up;
                fingers = forward + right * (side * _dribbleFingersOut);
                elbowHint = Mirror(_dribbleElbowHint, side);
                if (side == _dribbler.TouchHand && ball.y >= _kneeHeight)
                {
                    // Riding on top of the ball.
                    palm = ball + up * radius;
                    onBall = true;
                }
                else
                {
                    // Waiting where the top of the ball will come up to.
                    palm = _dribbler.GetHandPoint(side) + up * radius;
                }
                return;
            }
            // The off arm guards: forearm out in front, palm facing out.
            palm = _body.GetShoulder(side) + Mirror(_guardHandOffset, side);
            fingers = up + forward;
            palmFacing = forward + right * side;
            elbowHint = Mirror(_guardElbowHint, side);
            return;
        }

        if (_dribbler.IsHolding)
        {
            Vector3 held = _dribbler.HeldBallTarget;
            onBall = true;
            if (_shooter != null && _shooter.IsCharging)
            {
                Vector3 toRim = _shooter.ShotDirection;
                if (side == shootingSide)
                {
                    // Under and behind the ball, palm facing it, fingers up.
                    palmFacing = Vector3.Slerp(toRim, up, _shootHandUnder);
                    palm = held - palmFacing * radius;
                    fingers = up - toRim;
                    elbowHint = Mirror(_shootElbowHint, side);
                    return;
                }
                // The guide hand on the side of the ball.
                palmFacing = -right * side;
                palm = held - palmFacing * radius;
                fingers = up;
                elbowHint = Mirror(_holdElbowHint, side);
                return;
            }

            // Both hands on the sides of the ball, fingers up and forward.
            palmFacing = -right * side;
            palm = held - palmFacing * radius;
            fingers = up + forward;
            elbowHint = Mirror(_holdElbowHint, side);
            return;
        }

        if (_shooter != null && side == shootingSide && Time.time - _shooter.LastReleaseTime < _followThroughTime)
        {
            // Follow-through: the arm stays up toward the rim, wrist snapped down.
            Vector3 toRim = _shooter.ShotDirection;
            float angle = _followThroughAngle * Mathf.Deg2Rad;
            Vector3 armDirection = toRim * Mathf.Cos(angle) + up * Mathf.Sin(angle);
            palm = _body.GetShoulder(side) + up * _body.ShoulderLiftOverhead + armDirection * _body.ArmReach;
            fingers = toRim - up;
            palmFacing = -up - toRim;
            elbowHint = Mirror(_shootElbowHint, side);
            return;
        }

        _body.GetRelaxedHand(side, out palm, out fingers, out palmFacing, out elbowHint);
    }

    // Eases the hand toward its target: quickly when it's touching the ball, so
    // it stays on it, and more slowly when it's moving to a new spot.
    private void MoveHand(int side, Vector3 palm, Vector3 fingers, Vector3 palmFacing, bool onBall)
    {
        int i = side > 0 ? 1 : 0;
        Quaternion rotation = Quaternion.LookRotation(palmFacing, fingers);
        if (!_started)
        {
            _palm[i] = palm;
            _handRotation[i] = rotation;
            return;
        }

        float speed = onBall ? _onBallHandSpeed : _handSpeed;
        _palm[i] = Vector3.MoveTowards(_palm[i], palm, speed * Time.fixedDeltaTime);
        _handRotation[i] = Quaternion.RotateTowards(_handRotation[i], rotation, _handTurnSpeed * Time.fixedDeltaTime);
    }

    // How far the hips drop so the hand can get down to the dribble: enough that
    // the shoulder is within a comfortable reach of where the hand meets the ball.
    private float GetDribbleCrouch()
    {
        int side = _dribbler.BallHand;
        Vector3 handPoint = _dribbler.GetHandPoint(side) + Vector3.up * _ball.Radius;
        Vector3 shoulder = _body.GetShoulder(side);
        float shoulderStanding = _body.SolesHeight + _body.StandingShoulderHeight;

        Vector3 flat = handPoint - shoulder;
        flat.y = 0f;
        float reach = _body.ArmReach * _comfortableReach;
        // How high the shoulder can be with the hand still on the ball: the
        // rest of the reach after covering the distance along the floor (Pythagoras).
        float shoulderHeight = handPoint.y + Mathf.Sqrt(Mathf.Max(0f, reach * reach - flat.sqrMagnitude));
        return Mathf.Clamp(shoulderStanding - shoulderHeight, 0f, _body.MaxCrouch);
    }

    // A right-arm offset in the player's frame, mirrored for the left arm and turned into the world.
    private Vector3 Mirror(Vector3 rightArmOffset, int side)
    {
        rightArmOffset.x *= side;
        return _rigidbody.rotation * rightArmOffset;
    }
}
