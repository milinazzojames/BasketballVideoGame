using UnityEngine;

// Makes the net move. The net is a grid of knots joined by strands in a
// diamond pattern. Each knot is a tiny point mass that:
//   - springs back toward its resting spot (so the net settles),
//   - is held to its neighbors by the strands (so a push spreads through the net),
//   - gets pushed out of the way, and dragged along, by any ball that touches it.
// Row 0 is tied to the rim and never moves.
// The strands are drawn with LineRenderers that follow the knots.
// This is purely visual; HoopNet handles slowing the ball down.
public class NetSway : MonoBehaviour
{
    [Header("Shape (meters)")]
    [SerializeField] private float _length = 0.45f;
    [SerializeField] private float _topRadius = 0.2365f;
    [SerializeField] private float _bottomRadius = 0.13f;
    [SerializeField] private int _columns = 12;
    [SerializeField] private int _rows = 4;
    [SerializeField] private float _strandWidth = 0.008f;
    [SerializeField] private Material _material;

    [Header("Motion")]
    [Tooltip("How strongly knots spring back to their resting spot. Higher = stiffer, quicker wobble.")]
    [SerializeField] private float _stiffness = 250f;
    [Tooltip("Fraction of knot speed lost each physics step. Higher = the sway dies out sooner.")]
    [SerializeField, Range(0f, 0.5f)] private float _damping = 0.04f;
    [Tooltip("How much a touching ball drags knots along with it (0-1). This is what makes the net 'snap' on a swish.")]
    [SerializeField, Range(0f, 1f)] private float _ballDrag = 0.4f;
    [Tooltip("Passes per step that pull stretched strands back to length. More = less stretchy.")]
    [SerializeField, Range(1, 8)] private int _strandIterations = 3;

    private Vector3[,] _rest;        // resting position of each knot, world space
    private Vector3[,] _position;
    private Vector3[,] _previous;    // last step's position; (position - previous) is the knot's velocity
    private float[] _rowSegmentLength;  // rest length of the strands from row r to row r + 1
    private LineRenderer[] _lines;
    private readonly Collider[] _nearby = new Collider[8];

    private int Knots => _rows + 1;

    private void Awake()
    {
        _lines = GetComponentsInChildren<LineRenderer>();
        if (_lines.Length != _columns * 2) BuildLines();

        _rest = new Vector3[Knots, _columns];
        _position = new Vector3[Knots, _columns];
        _previous = new Vector3[Knots, _columns];
        for (int row = 0; row < Knots; row++)
        {
            for (int k = 0; k < _columns; k++)
            {
                _rest[row, k] = transform.TransformPoint(GetRestKnot(row, k));
                _position[row, k] = _rest[row, k];
                _previous[row, k] = _rest[row, k];
            }
        }

        _rowSegmentLength = new float[_rows];
        for (int row = 0; row < _rows; row++)
        {
            _rowSegmentLength[row] = Vector3.Distance(_rest[row, 0], _rest[row + 1, KnotIndex(row + 1, HalfStep(row, 0) + 1)]);
        }
    }

    private void FixedUpdate()
    {
        float dt2 = Time.fixedDeltaTime * Time.fixedDeltaTime;

        // 1. Move each knot: keep its velocity (minus damping) and spring toward rest.
        //    This is "Verlet integration": velocity is stored as the change since last step.
        for (int row = 1; row < Knots; row++)
        {
            for (int k = 0; k < _columns; k++)
            {
                Vector3 velocity = (_position[row, k] - _previous[row, k]) * (1f - _damping);
                _previous[row, k] = _position[row, k];
                _position[row, k] += velocity + (_rest[row, k] - _position[row, k]) * (_stiffness * dt2);
            }
        }

        // 2. Strands are like string: they can go slack but not stretch much.
        for (int i = 0; i < _strandIterations; i++)
        {
            for (int row = 0; row < _rows; row++)
            {
                for (int k = 0; k < _columns; k++)
                {
                    int a = HalfStep(row, k);
                    KeepStrandLength(row, k, KnotIndex(row + 1, a - 1));
                    KeepStrandLength(row, k, KnotIndex(row + 1, a + 1));
                }
            }
        }

        // 3. Anything round touching the net pushes knots out of its way.
        PushOutOfBalls();
    }

    private void LateUpdate()
    {
        // Each line follows one diagonal strand from the rim to the bottom of the net.
        for (int c = 0; c < _columns; c++)
        {
            for (int row = 0; row < Knots; row++)
            {
                _lines[c].SetPosition(row, _position[row, KnotIndex(row, c * 2 + row)]);
                _lines[c + _columns].SetPosition(row, _position[row, KnotIndex(row, c * 2 - row)]);
            }
        }
    }

    // Pulls two connected knots back together if the strand between them is
    // stretched. Rim knots (row 0) are fixed, so only the lower knot moves then.
    private void KeepStrandLength(int upperRow, int upperK, int lowerK)
    {
        Vector3 upper = _position[upperRow, upperK];
        Vector3 lower = _position[upperRow + 1, lowerK];
        Vector3 delta = lower - upper;
        float length = delta.magnitude;
        float restLength = _rowSegmentLength[upperRow];
        if (length <= restLength) return;

        Vector3 correction = delta * ((length - restLength) / length);
        if (upperRow == 0)
        {
            _position[upperRow + 1, lowerK] -= correction;
        }
        else
        {
            _position[upperRow, upperK] += correction * 0.5f;
            _position[upperRow + 1, lowerK] -= correction * 0.5f;
        }
    }

    private void PushOutOfBalls()
    {
        Vector3 center = transform.TransformPoint(new Vector3(0f, -_length / 2f, 0f));
        int count = Physics.OverlapSphereNonAlloc(center, _length + _topRadius, _nearby, ~0, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            // Only moving round things (the ball). The rim and floor never touch the net.
            if (!(_nearby[i] is SphereCollider sphere) || sphere.attachedRigidbody == null) continue;

            Vector3 ballCenter = sphere.transform.TransformPoint(sphere.center);
            float ballRadius = sphere.radius * sphere.transform.lossyScale.x + _strandWidth;
            Vector3 ballMove = sphere.attachedRigidbody.linearVelocity * Time.fixedDeltaTime;

            for (int row = 1; row < Knots; row++)
            {
                for (int k = 0; k < _columns; k++)
                {
                    Vector3 fromBall = _position[row, k] - ballCenter;
                    float distance = fromBall.magnitude;
                    if (distance >= ballRadius || distance < 0.0001f) continue;

                    // Move the knot to the ball's surface, plus part of the ball's own motion.
                    _position[row, k] = ballCenter + fromBall / distance * ballRadius + ballMove * _ballDrag;
                }
            }
        }
    }

    // Creates one LineRenderer per diagonal strand (two directions x columns).
    // Called by the scene builder so the net is visible in the editor too.
    public void BuildLines()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            GameObject child = transform.GetChild(i).gameObject;
            if (Application.isPlaying) Destroy(child);
            else DestroyImmediate(child);
        }

        _lines = new LineRenderer[_columns * 2];
        for (int i = 0; i < _lines.Length; i++)
        {
            LineRenderer line = new GameObject($"Strand{i:00}").AddComponent<LineRenderer>();
            line.transform.SetParent(transform, false);
            line.useWorldSpace = true;
            line.positionCount = Knots;
            line.widthMultiplier = _strandWidth;
            line.sharedMaterial = _material;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            int c = i % _columns;
            int direction = i < _columns ? 1 : -1;
            for (int row = 0; row < Knots; row++)
            {
                line.SetPosition(row, transform.TransformPoint(GetRestKnot(row, KnotIndex(row, c * 2 + direction * row))));
            }
            _lines[i] = line;
        }
    }

    // Knots in odd rows sit half a column over from even rows, which makes the
    // diamond pattern. Positions are counted in half-columns ("half steps"):
    // knot k in row r is at half step 2k + (r % 2).
    private int HalfStep(int row, int k) => k * 2 + row % 2;

    private int KnotIndex(int row, int halfStep)
    {
        int wrapped = ((halfStep % (_columns * 2)) + _columns * 2) % (_columns * 2);
        return (wrapped - row % 2) / 2;
    }

    // Resting position of a knot, relative to the rim center (this object).
    private Vector3 GetRestKnot(int row, int k)
    {
        float t = (float)row / _rows;
        float radius = Mathf.Lerp(_topRadius, _bottomRadius, t);
        float angle = HalfStep(row, k) * Mathf.PI / _columns;
        return new Vector3(Mathf.Cos(angle) * radius, -_length * t, Mathf.Sin(angle) * radius);
    }
}
