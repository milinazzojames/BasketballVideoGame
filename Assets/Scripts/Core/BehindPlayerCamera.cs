using UnityEngine;

// "Behind-the-player" camera (the view NBA 2K calls its "2K cam").
// The camera sits behind the player on the line from the hoop through the
// player, so pushing the left stick "up" always moves roughly toward the basket.
public class BehindPlayerCamera : MonoBehaviour
{
    [Header("Targets")]
    [Tooltip("The player the camera follows.")]
    [SerializeField] private Transform _target;
    [Tooltip("What the camera keeps in front of the player, usually the rim.")]
    [SerializeField] private Transform _focus;

    [Header("Placement")]
    [Tooltip("How far behind the player the camera sits, in meters (measured along the floor).")]
    [SerializeField] private float _distance = 7.5f;
    [Tooltip("Camera height above the player's pivot (capsule center), in meters.")]
    [SerializeField] private float _height = 4.5f;

    [Header("Aim")]
    [Tooltip("0 = look straight at the player, 1 = look straight at the focus (rim).")]
    [SerializeField, Range(0f, 1f)] private float _lookTowardFocus = 0.3f;
    [Tooltip("When the player is closer than this to the rim (along the floor), the camera stops swinging around so it doesn't spin while the player is under the basket.")]
    [SerializeField] private float _minFocusDistance = 1.5f;

    [Header("Smoothing")]
    [Tooltip("Roughly how many seconds the camera takes to catch up. Lower = snappier, higher = floatier.")]
    [SerializeField] private float _smoothTime = 0.15f;

    private Vector3 _velocity;
    private Vector3 _backDirection = Vector3.back;

    private void Start()
    {
        if (_target == null) return;

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

    private Vector3 GetDesiredPosition()
    {
        UpdateBackDirection();
        return _target.position + _backDirection * _distance + Vector3.up * _height;
    }

    private Quaternion GetDesiredRotation()
    {
        // Aim at a point part of the way from the player toward the rim,
        // so both the player and the basket stay in view.
        Vector3 lookPoint = _target.position;
        if (_focus != null)
        {
            lookPoint = Vector3.Lerp(_target.position, _focus.position, _lookTowardFocus);
        }

        return Quaternion.LookRotation(lookPoint - transform.position);
    }

    // _backDirection is a flat (no height) unit vector pointing from the rim
    // through the player. The camera sits along it, behind the player.
    private void UpdateBackDirection()
    {
        Vector3 away = _focus != null
            ? _target.position - _focus.position
            : -_target.forward;
        away.y = 0f;

        // Right under the rim the direction changes wildly with tiny moves,
        // so keep the last good direction there.
        float minDistance = _focus != null ? _minFocusDistance : 0.01f;
        if (away.magnitude > minDistance)
        {
            _backDirection = away.normalized;
        }
    }
}
