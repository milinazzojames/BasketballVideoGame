using UnityEngine;

// Draws the shot meter next to the player: a white curved bar with a green
// window near the top, and a dark marker that climbs while Shoot is held.
// After a shot, the marker stays where it was released for a moment so you
// can see how close you were. Built from LineRenderers that face the camera.
public class ShotMeterDisplay : MonoBehaviour
{
    [SerializeField] private PlayerShooter _shooter;
    [Tooltip("The meter turns to face this camera. Uses the main camera if empty.")]
    [SerializeField] private Transform _cameraTransform;

    [Header("Look")]
    [SerializeField] private Material _trackMaterial;
    [SerializeField] private Material _greenMaterial;
    [SerializeField] private Material _markerMaterial;
    [Tooltip("Radius of the curve, in meters.")]
    [SerializeField] private float _radius = 0.6f;
    [Tooltip("How much of a circle the bar covers, in degrees.")]
    [SerializeField, Range(30f, 180f)] private float _arcDegrees = 90f;
    [Tooltip("Thickness of the bar, in meters.")]
    [SerializeField] private float _barWidth = 0.06f;
    [Tooltip("Where the meter sits relative to the player's center, in camera space (x = right, y = up).")]
    [SerializeField] private Vector2 _offset = new Vector2(0.75f, 0.3f);
    [Tooltip("How far the meter is moved from the player toward the camera, in meters, so the body doesn't cover it.")]
    [SerializeField] private float _pullTowardCamera = 0.5f;
    [Tooltip("Seconds the meter stays visible after a shot.")]
    [SerializeField] private float _showAfterRelease = 0.6f;

    private const int ArcPoints = 24;
    private const float LayerGap = 0.005f;

    private LineRenderer _track;
    private LineRenderer _green;
    private LineRenderer _marker;

    private void Awake()
    {
        if (_shooter == null) _shooter = GetComponent<PlayerShooter>();
        if (_cameraTransform == null && Camera.main != null) _cameraTransform = Camera.main.transform;

        _track = CreateLine("ShotMeterTrack", _trackMaterial, _barWidth, ArcPoints);
        _green = CreateLine("ShotMeterGreen", _greenMaterial, _barWidth, ArcPoints);
        _marker = CreateLine("ShotMeterMarker", _markerMaterial, _barWidth * 0.5f, 2);
        SetVisible(false);
    }

    private void LateUpdate()
    {
        bool showResult = Time.time - _shooter.LastReleaseTime < _showAfterRelease;
        bool visible = _shooter.IsCharging || showResult;
        SetVisible(visible);
        if (!visible || _cameraTransform == null) return;

        // Build the curve in the plane facing the camera, beside the player.
        Vector3 right = _cameraTransform.right;
        Vector3 up = _cameraTransform.up;
        Vector3 towardCamera = -_cameraTransform.forward;
        // The curve bulges to the right, like ")"; its circle's center is to the left.
        // It's pulled toward the camera so the player's body never hides it.
        Vector3 circleCenter = transform.position + right * (_offset.x - _radius) + up * _offset.y
            + towardCamera * _pullTowardCamera;

        // Each layer sits a hair closer to the camera than the one below it,
        // so green draws over white and the marker over both without flickering.
        DrawArc(_track, circleCenter, right, up, 0f, 1f);
        DrawArc(_green, circleCenter + towardCamera * LayerGap, right, up, _shooter.GreenStart, _shooter.GreenEnd);

        // The marker is a short tick across the bar at the current fill level.
        Vector3 direction = GetArcDirection(_shooter.MeterFill, right, up);
        Vector3 markerCenter = circleCenter + towardCamera * (LayerGap * 2f);
        float tickHalf = _barWidth * 1.2f;
        _marker.SetPosition(0, markerCenter + direction * (_radius - tickHalf));
        _marker.SetPosition(1, markerCenter + direction * (_radius + tickHalf));
    }

    // Fill 0 is the bottom of the bar, fill 1 is the top.
    private Vector3 GetArcDirection(float fill, Vector3 right, Vector3 up)
    {
        float angle = Mathf.Lerp(-_arcDegrees / 2f, _arcDegrees / 2f, fill) * Mathf.Deg2Rad;
        return right * Mathf.Cos(angle) + up * Mathf.Sin(angle);
    }

    private void DrawArc(LineRenderer line, Vector3 center, Vector3 right, Vector3 up, float fromFill, float toFill)
    {
        for (int i = 0; i < ArcPoints; i++)
        {
            float fill = Mathf.Lerp(fromFill, toFill, i / (ArcPoints - 1f));
            line.SetPosition(i, center + GetArcDirection(fill, right, up) * _radius);
        }
    }

    private LineRenderer CreateLine(string name, Material material, float width, int points)
    {
        LineRenderer line = new GameObject(name).AddComponent<LineRenderer>();
        line.transform.SetParent(transform, false);
        line.useWorldSpace = true;
        line.positionCount = points;
        line.widthMultiplier = width;
        line.sharedMaterial = material;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        return line;
    }

    private void SetVisible(bool visible)
    {
        _track.enabled = visible;
        _green.enabled = visible;
        _marker.enabled = visible;
    }
}
