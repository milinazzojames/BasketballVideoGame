using UnityEngine;

// "Behind-the-player" camera (the view NBA 2K calls its "2K cam").
// The camera stays mostly still behind the court, always facing the hoop,
// and only drifts a fraction of the way when the player moves. Pushing the
// left stick "up" always means "toward the basket".
public class BehindPlayerCamera : MonoBehaviour
{
    [Header("Targets")]
    [Tooltip("The player the camera follows.")]
    [SerializeField] private Transform _target;
    [Tooltip("What the camera faces, usually the rim. Its blue (Z) axis should point from the court toward the baseline.")]
    [SerializeField] private Transform _focus;

    [Header("Placement")]
    [Tooltip("How far behind the player's starting spot the camera sits, in meters (measured along the floor).")]
    [SerializeField] private float _distance = 7.5f;
    [Tooltip("Camera height above the player's pivot (capsule center), in meters.")]
    [SerializeField] private float _height = 4.5f;

    [Header("Follow")]
    [Tooltip("How much the camera follows the player toward and away from the hoop. 0 = not at all, 1 = fully.")]
    [SerializeField, Range(0f, 1f)] private float _followForward = 0.35f;
    [Tooltip("How much the camera follows the player side to side. 0 = not at all, 1 = fully.")]
    [SerializeField, Range(0f, 1f)] private float _followSideways = 0.25f;

    [Header("Aim")]
    [Tooltip("0 = look at the player's (followed) position, 1 = look straight at the rim.")]
    [SerializeField, Range(0f, 1f)] private float _lookTowardFocus = 0.3f;

    [Header("Smoothing")]
    [Tooltip("Roughly how many seconds the camera takes to catch up. Lower = snappier, higher = floatier.")]
    [SerializeField] private float _smoothTime = 0.15f;

    private Vector3 _velocity;
    private Vector3 _restPoint;        // the player's starting spot; the camera is "at rest" when the player stands here
    private Vector3 _courtForward;     // flat direction from the court toward the hoop
    private Vector3 _courtRight;

    // Flat directions of the court: toward the hoop, and to the right of that.
    // They never change, unlike the camera's own facing, which drifts a little
    // as it follows the player.
    public Vector3 CourtForward => _courtForward;
    public Vector3 CourtRight => _courtRight;

    // Turns a stick (x = right, y = up) into a flat direction on the floor:
    // up = toward the hoop. Uses the camera's court directions so holding the
    // stick still gives a straight line; without a BehindPlayerCamera it falls
    // back to the camera's facing. Not normalized: a half push stays half as long.
    public static Vector3 StickToFloor(Vector2 stick, Transform cameraTransform)
    {
        Vector3 forward = Vector3.forward;
        Vector3 right = Vector3.right;
        if (cameraTransform != null)
        {
            if (cameraTransform.TryGetComponent(out BehindPlayerCamera court))
            {
                forward = court.CourtForward;
                right = court.CourtRight;
            }
            else
            {
                forward = Flatten(cameraTransform.forward);
                right = Flatten(cameraTransform.right);
            }
        }
        return forward * stick.y + right * stick.x;
    }

    // The court directions are set in Awake so the player scripts can use them
    // from their very first physics step.
    private void Awake()
    {
        _courtForward = _focus != null ? Flatten(_focus.forward) : Vector3.forward;
        _courtRight = Vector3.Cross(Vector3.up, _courtForward);
    }

    private void Start()
    {
        if (_target == null) return;

        _restPoint = _target.position;

        // Snap into place on the first frame instead of gliding in from wherever the camera started.
        transform.position = GetDesiredPosition();
        transform.rotation = GetDesiredRotation();
    }

    // LateUpdate runs after all movement code, so the camera uses the player's
    // final position for this frame. That avoids jitter.
    private void LateUpdate()
    {
        if (_target == null) return;

        transform.position = Vector3.SmoothDamp(transform.position, GetDesiredPosition(), ref _velocity, _smoothTime);
        transform.rotation = GetDesiredRotation();
    }

    // The camera follows a stand-in point that moves only a fraction as far as
    // the player does. With _followForward = 0.35, walking 10 m toward the hoop
    // moves the camera 3.5 m.
    private Vector3 GetFollowPoint()
    {
        Vector3 offset = _target.position - _restPoint;
        float forward = Vector3.Dot(offset, _courtForward);
        float sideways = Vector3.Dot(offset, _courtRight);

        return _restPoint
            + _courtForward * (forward * _followForward)
            + _courtRight * (sideways * _followSideways);
    }

    private Vector3 GetDesiredPosition()
    {
        return GetFollowPoint() - _courtForward * _distance + Vector3.up * _height;
    }

    private Quaternion GetDesiredRotation()
    {
        // Aim part of the way from the follow point toward the rim, so the
        // basket stays in view and the view barely turns as the player moves.
        Vector3 lookPoint = GetFollowPoint();
        if (_focus != null)
        {
            lookPoint = Vector3.Lerp(lookPoint, _focus.position, _lookTowardFocus);
        }

        return Quaternion.LookRotation(lookPoint - transform.position);
    }

    private static Vector3 Flatten(Vector3 direction)
    {
        direction.y = 0f;
        return direction.normalized;
    }
}
