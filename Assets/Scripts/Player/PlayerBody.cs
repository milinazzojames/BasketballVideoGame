using UnityEngine;

// A stand-in human body made of primitives (capsules, a sphere for the head,
// boxes for hands and feet), sized like a 6'6" (1.98 m) player. The player's
// capsule still does the moving (feet on the floor, bumping into walls); this
// script poses the body parts around it every physics step.
//
// Each part is its own kinematic Rigidbody, moved with MovePosition and
// MoveRotation, because:
//   - a collider without its own Rigidbody under the player would join the
//     capsule's collider and change how the player moves and bumps into things,
//   - a part moved this way has a real speed, so a ball that hits a swinging
//     arm gets knocked by it instead of just stopping.
// The parts live under their own object, not under the player: the player's
// transform would carry child Rigidbodies along too and fight MovePosition.
//
// The parts are named like Unity's Humanoid bones (LeftUpperArm, RightHand...)
// so a real character model can take their place later. Each part's own Y axis
// points along its bone, from its joint toward the next joint.
//
// Sizes are read when the parts are built. After changing a size, right-click
// this component and pick "Rebuild Body Parts" (or rebuild the scene).
[RequireComponent(typeof(Rigidbody))]
[DefaultExecutionOrder(100)]   // after movement and dribbling, so the body is posed where the player ends up
public class PlayerBody : MonoBehaviour
{
    public enum Bone
    {
        Hips, Spine, Chest, Neck, Head,
        LeftUpperArm, LeftLowerArm, LeftHand,
        RightUpperArm, RightLowerArm, RightHand,
        LeftUpperLeg, LeftLowerLeg, LeftFoot,
        RightUpperLeg, RightLowerLeg, RightFoot,
    }

    [Tooltip("The object the body parts are built under. Keep it at the top of the scene, not under the player.")]
    [SerializeField] private Transform _partsRoot;
    [SerializeField] private Material _material;
    [Tooltip("Bounce and friction of the body parts against the ball.")]
    [SerializeField] private PhysicsMaterial _physicsMaterial;
    [Tooltip("Hide the player's capsule (and its facing marker) while the body is shown.")]
    [SerializeField] private bool _hideCapsule = true;

    [Header("Height (meters, standing straight)")]
    [Tooltip("Height of the shoulder joints. About 1.62 m for a 6'6\" player.")]
    [SerializeField] private float _shoulderHeight = 1.62f;
    [Tooltip("Height of the ankle joints above the soles.")]
    [SerializeField] private float _ankleHeight = 0.08f;
    [Tooltip("Thigh length, hip joint to knee.")]
    [SerializeField] private float _upperLegLength = 0.47f;
    [Tooltip("Shin length, knee to ankle.")]
    [SerializeField] private float _lowerLegLength = 0.46f;
    [Tooltip("Hips joint to spine joint.")]
    [SerializeField] private float _hipsLength = 0.12f;
    [Tooltip("Spine joint to chest joint. The chest runs from there up to the neck.")]
    [SerializeField] private float _spineLength = 0.18f;
    [Tooltip("How far the base of the neck is above the shoulder joints.")]
    [SerializeField] private float _neckBaseAboveShoulders = 0.05f;
    [Tooltip("Neck length. The head sits on top of it.")]
    [SerializeField] private float _neckLength = 0.08f;
    [Tooltip("Head radius. Shoulders + neck base + neck + head (2 x radius) is the player's height: 1.62 + 0.05 + 0.08 + 0.23 = 1.98 m.")]
    [SerializeField] private float _headRadius = 0.115f;

    [Header("Width and arms (meters)")]
    [Tooltip("Distance between the two shoulder joints. Arm span = this + 2 x (upper arm + forearm + hand) = 0.46 + 2 x 0.82 = 2.1 m.")]
    [SerializeField] private float _shoulderWidth = 0.46f;
    [Tooltip("Distance between the two hip joints.")]
    [SerializeField] private float _hipWidth = 0.2f;
    [Tooltip("Upper arm length, shoulder to elbow.")]
    [SerializeField] private float _upperArmLength = 0.34f;
    [Tooltip("Forearm length, elbow to wrist.")]
    [SerializeField] private float _lowerArmLength = 0.29f;
    [Tooltip("Hand length, wrist to fingertips.")]
    [SerializeField] private float _handLength = 0.19f;
    [Tooltip("Hand width (x) and thickness (y).")]
    [SerializeField] private Vector2 _handSize = new Vector2(0.09f, 0.03f);
    [Tooltip("Foot length (x), width (y) and how far the heel sticks out behind the ankle (z).")]
    [SerializeField] private Vector3 _footSize = new Vector3(0.28f, 0.1f, 0.06f);

    [Header("Thickness (capsule radius, meters)")]
    [SerializeField] private float _hipsRadius = 0.15f;
    [SerializeField] private float _spineRadius = 0.14f;
    [SerializeField] private float _chestRadius = 0.17f;
    [SerializeField] private float _neckRadius = 0.06f;
    [SerializeField] private float _upperArmRadius = 0.05f;
    [SerializeField] private float _lowerArmRadius = 0.04f;
    [SerializeField] private float _upperLegRadius = 0.085f;
    [SerializeField] private float _lowerLegRadius = 0.06f;

    [Header("Stance and legs")]
    [Tooltip("How far the hips sit below standing fully straight, in meters. A little bend in the knees, like an athlete ready to move.")]
    [SerializeField] private float _kneeBend = 0.04f;
    [Tooltip("Extra distance the feet are set out to the sides, past the hip joints, in meters.")]
    [SerializeField] private float _stanceWidth = 0.06f;
    [Tooltip("Meters moved per full leg swing (left foot forward, back, and forward again).")]
    [SerializeField] private float _strideLength = 2.2f;
    [Tooltip("How far each foot swings forward and back from under the hip, in meters, at full swing.")]
    [SerializeField] private float _footSwing = 0.35f;
    [Tooltip("How high a foot lifts while it swings forward, in meters, at full swing.")]
    [SerializeField] private float _footLift = 0.12f;
    [Tooltip("Speed (m/s) at which the legs swing fully. Slower than this, the swing is smaller.")]
    [SerializeField] private float _fullSwingSpeed = 4.5f;

    [Header("Relaxed arms")]
    [Tooltip("How far the hands hang out to the sides of the shoulders, in meters.")]
    [SerializeField] private float _relaxedHandOut = 0.08f;
    [Tooltip("How far the hands hang in front of the shoulders, in meters.")]
    [SerializeField] private float _relaxedHandForward = 0.05f;
    [Tooltip("How far the wrists hang below the shoulders, as a fraction of the arm's length (1 = fully straight).")]
    [SerializeField, Range(0.5f, 1f)] private float _relaxedArmStraightness = 0.95f;

    private static readonly int BoneCount = System.Enum.GetValues(typeof(Bone)).Length;

    private Rigidbody _rigidbody;
    private Collider _capsule;
    private float _capsuleHalfHeight;
    private Rigidbody[] _parts;
    private Collider[] _colliders;
    private readonly Vector3[] _position = new Vector3[BoneCount];
    private readonly Quaternion[] _rotation = new Quaternion[BoneCount];
    private bool _snapNext = true;
    private float _stridePhase;

    // The ball's collisions with the parts, while they're turned back on one by one.
    private Collider _ball;
    private bool[] _ignoringBall;
    private bool _turningBallBackOn;

    // Where the hips joint is when standing, above the soles.
    private float LegHeight => _ankleHeight + _upperLegLength + _lowerLegLength;
    private float ArmLength => _upperArmLength + _lowerArmLength;

    private void Awake()
    {
        _rigidbody = GetComponent<Rigidbody>();
        _capsule = GetComponent<Collider>();
        _capsuleHalfHeight = GetCapsuleHalfHeight();
        FindParts();

        // The parts never push the player's own capsule, or each other.
        for (int i = 0; i < _colliders.Length; i++)
        {
            if (_colliders[i] == null) continue;
            if (_capsule != null) Physics.IgnoreCollision(_colliders[i], _capsule, true);
            for (int j = i + 1; j < _colliders.Length; j++)
            {
                if (_colliders[j] != null) Physics.IgnoreCollision(_colliders[i], _colliders[j], true);
            }
        }
    }

    private void OnEnable()
    {
        SetCapsuleVisible(!_hideCapsule);
    }

    private void OnDisable()
    {
        SetCapsuleVisible(true);
    }

    private void FixedUpdate()
    {
        if (_parts == null) return;

        // Where the capsule will be at the end of this physics step, so the body
        // isn't a step behind it.
        Vector3 rootPosition = _rigidbody.position + _rigidbody.linearVelocity * Time.fixedDeltaTime;
        ComputePose(rootPosition, _rigidbody.rotation, _rigidbody.linearVelocity, Time.fixedDeltaTime);
        MoveParts();
        TurnBallCollisionsBackOn();
    }

    // While the player has the ball, it passes through all of their body parts
    // (like it passes through the capsule). When it's loose again, each part
    // starts bumping it again as soon as the ball isn't overlapping that part,
    // so a ball that leaves the hand isn't shoved out of the hand's box.
    public void SetBallIgnored(Collider ball, bool ignored)
    {
        if (_colliders == null || ball == null) return;
        _ball = ball;
        if (_ignoringBall == null) _ignoringBall = new bool[BoneCount];

        for (int i = 0; i < BoneCount; i++)
        {
            if (_colliders[i] == null) continue;
            if (ignored)
            {
                Physics.IgnoreCollision(_colliders[i], ball, true);
                _ignoringBall[i] = true;
            }
        }
        _turningBallBackOn = !ignored;
    }

    private void TurnBallCollisionsBackOn()
    {
        if (!_turningBallBackOn || _ball == null) return;

        bool allOn = true;
        Rigidbody ballBody = _ball.attachedRigidbody;
        for (int i = 0; i < BoneCount; i++)
        {
            if (!_ignoringBall[i] || _colliders[i] == null) continue;

            // Test against where the part will be this step (it hasn't moved yet).
            Transform shape = _colliders[i].transform;
            Vector3 shapePosition = _position[i] + _rotation[i] * shape.localPosition;
            Quaternion shapeRotation = _rotation[i] * shape.localRotation;
            bool overlapping = Physics.ComputePenetration(
                _colliders[i], shapePosition, shapeRotation,
                _ball, ballBody != null ? ballBody.position : _ball.transform.position, _ball.transform.rotation,
                out _, out _);

            if (overlapping)
            {
                allOn = false;
                continue;
            }
            Physics.IgnoreCollision(_colliders[i], _ball, false);
            _ignoringBall[i] = false;
        }
        if (allOn) _turningBallBackOn = false;
    }

    // Works out where every part goes, from the capsule's position and facing.
    private void ComputePose(Vector3 rootPosition, Quaternion rootRotation, Vector3 velocity, float deltaTime)
    {
        Vector3 up = rootRotation * Vector3.up;
        Vector3 forward = rootRotation * Vector3.forward;
        Vector3 right = rootRotation * Vector3.right;
        // The point between the feet at sole level: the bottom of the capsule.
        // Measured from the capsule (not the floor), so the legs leave the floor with it in a jump.
        Vector3 soles = rootPosition - up * _capsuleHalfHeight;

        // Torso: straight up from the hips.
        Vector3 hips = soles + up * (LegHeight - _kneeBend);
        Vector3 spine = hips + up * _hipsLength;
        Vector3 chest = spine + up * _spineLength;
        Vector3 shoulders = hips + up * (_shoulderHeight - LegHeight);
        Vector3 neck = shoulders + up * _neckBaseAboveShoulders;
        Vector3 head = neck + up * _neckLength;

        SetBone(Bone.Hips, hips, rootRotation);
        SetBone(Bone.Spine, spine, rootRotation);
        SetBone(Bone.Chest, chest, rootRotation);
        SetBone(Bone.Neck, neck, rootRotation);
        SetBone(Bone.Head, head, rootRotation);

        PoseLegs(hips, soles, up, forward, right, velocity, deltaTime);

        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 shoulder = shoulders + right * (side * _shoulderWidth / 2f);
            // Relaxed: hanging down, a little out to the side and forward.
            Vector3 wrist = shoulder - up * (ArmLength * _relaxedArmStraightness)
                + right * (side * _relaxedHandOut) + forward * _relaxedHandForward;
            Vector3 elbowHint = right * side - forward * 0.5f;
            // Fingers down, palm facing the leg.
            PoseArm(side, shoulder, wrist, elbowHint, -up, -right * side, forward);
        }
    }

    // A simple stand/run pose: the feet stay under the hips (a knee bend comes
    // from the hips sitting a little low), and swing forward and back with speed.
    // It's not a real walk cycle; the feet slide.
    private void PoseLegs(Vector3 hips, Vector3 soles, Vector3 up, Vector3 forward, Vector3 right, Vector3 velocity, float deltaTime)
    {
        Vector3 flatVelocity = Vector3.ProjectOnPlane(velocity, up);
        float speed = flatVelocity.magnitude;
        // One full swing per stride length travelled.
        _stridePhase = Mathf.Repeat(_stridePhase + speed / _strideLength * 2f * Mathf.PI * deltaTime, 2f * Mathf.PI);
        float swing = Mathf.Clamp01(speed / _fullSwingSpeed);
        Vector3 swingDirection = speed > 0.01f ? flatVelocity / speed : forward;

        for (int side = -1; side <= 1; side += 2)
        {
            // The two legs swing opposite each other.
            float phase = _stridePhase + (side > 0 ? Mathf.PI : 0f);
            Vector3 hip = hips + right * (side * _hipWidth / 2f);
            Vector3 foot = soles + right * (side * (_hipWidth / 2f + _stanceWidth))
                + swingDirection * (Mathf.Sin(phase) * _footSwing * swing);
            // A foot moving forward (cos > 0) is off the floor; moving back, it's on it.
            Vector3 ankle = foot + up * (_ankleHeight + Mathf.Max(0f, Mathf.Cos(phase)) * _footLift * swing);

            // Knees bend forward.
            TwoBoneIK.Solve(hip, ankle, forward, _upperLegLength, _lowerLegLength, out Vector3 knee, out ankle);

            Bone upper = side > 0 ? Bone.RightUpperLeg : Bone.LeftUpperLeg;
            Bone lower = side > 0 ? Bone.RightLowerLeg : Bone.LeftLowerLeg;
            Bone footBone = side > 0 ? Bone.RightFoot : Bone.LeftFoot;
            SetBone(upper, hip, AlongBone(knee - hip, forward));
            SetBone(lower, knee, AlongBone(ankle - knee, forward));
            // The foot bone points toward the toes, with its Z axis up.
            SetBone(footBone, ankle, Quaternion.LookRotation(up, forward));
        }
    }

    // Solves one arm and sets its three parts. The hand's rotation comes from
    // where its fingers point and which way its palm faces.
    private void PoseArm(int side, Vector3 shoulder, Vector3 wristTarget, Vector3 elbowHint,
        Vector3 fingerDirection, Vector3 palmDirection, Vector3 boneForward)
    {
        TwoBoneIK.Solve(shoulder, wristTarget, elbowHint, _upperArmLength, _lowerArmLength, out Vector3 elbow, out Vector3 wrist);

        Bone upper = side > 0 ? Bone.RightUpperArm : Bone.LeftUpperArm;
        Bone lower = side > 0 ? Bone.RightLowerArm : Bone.LeftLowerArm;
        Bone hand = side > 0 ? Bone.RightHand : Bone.LeftHand;
        SetBone(upper, shoulder, AlongBone(elbow - shoulder, boneForward));
        SetBone(lower, elbow, AlongBone(wrist - elbow, boneForward));
        SetBone(hand, wrist, HandRotation(fingerDirection, palmDirection));
    }

    // A hand's rotation: Y along the fingers, Z out of the palm.
    private static Quaternion HandRotation(Vector3 fingerDirection, Vector3 palmDirection)
    {
        return AlongBone(fingerDirection, palmDirection);
    }

    // A rotation whose Y axis points along the bone, with its Z axis turned as
    // close to forwardHint as it can be while staying square to the bone.
    private static Quaternion AlongBone(Vector3 boneDirection, Vector3 forwardHint)
    {
        if (boneDirection.sqrMagnitude < 0.000001f) boneDirection = Vector3.up;
        Vector3 forward = Vector3.ProjectOnPlane(forwardHint, boneDirection);
        if (forward.sqrMagnitude < 0.000001f) forward = Vector3.ProjectOnPlane(Vector3.forward, boneDirection);
        if (forward.sqrMagnitude < 0.000001f) forward = Vector3.ProjectOnPlane(Vector3.right, boneDirection);
        return Quaternion.LookRotation(forward, boneDirection);
    }

    private void SetBone(Bone bone, Vector3 position, Quaternion rotation)
    {
        _position[(int)bone] = position;
        _rotation[(int)bone] = rotation;
    }

    private void MoveParts()
    {
        for (int i = 0; i < BoneCount; i++)
        {
            Rigidbody part = _parts[i];
            if (part == null) continue;

            if (_snapNext)
            {
                // First step: jump straight there instead of sweeping in from wherever the parts were.
                part.position = _position[i];
                part.rotation = _rotation[i];
                part.transform.SetPositionAndRotation(_position[i], _rotation[i]);
            }
            else
            {
                // MovePosition and MoveRotation give the part a real velocity for this step.
                part.MovePosition(_position[i]);
                part.MoveRotation(_rotation[i]);
            }
        }
        _snapNext = false;
    }

    private void FindParts()
    {
        _parts = new Rigidbody[BoneCount];
        _colliders = new Collider[BoneCount];
        if (_partsRoot == null)
        {
            Debug.LogWarning("PlayerBody has no parts root; rebuild the scene or use \"Rebuild Body Parts\".");
            _parts = null;
            return;
        }

        for (int i = 0; i < BoneCount; i++)
        {
            Transform part = _partsRoot.Find(((Bone)i).ToString());
            if (part == null) continue;
            _parts[i] = part.GetComponent<Rigidbody>();
            _colliders[i] = part.GetComponentInChildren<Collider>();
        }
    }

    private float GetCapsuleHalfHeight()
    {
        CapsuleCollider capsule = GetComponent<CapsuleCollider>();
        if (capsule == null) return 1f;
        return capsule.height * transform.lossyScale.y / 2f;
    }

    private void SetCapsuleVisible(bool visible)
    {
        Renderer capsule = GetComponent<Renderer>();
        if (capsule != null) capsule.enabled = visible;
        Transform facing = transform.Find("FacingIndicator");
        if (facing != null && facing.TryGetComponent(out Renderer marker)) marker.enabled = visible;
    }

    // Builds (or rebuilds) the body parts from the sizes above, and puts them
    // in the standing pose around the player. The scene builder calls this.
    [ContextMenu("Rebuild Body Parts")]
    public void BuildParts()
    {
        if (_partsRoot == null) _partsRoot = new GameObject("PlayerBody").transform;
        for (int i = _partsRoot.childCount - 1; i >= 0; i--)
        {
            DestroyImmediate(_partsRoot.GetChild(i).gameObject);
        }

        for (int i = 0; i < BoneCount; i++)
        {
            Bone bone = (Bone)i;
            GameObject part = new GameObject(bone.ToString());
            part.transform.SetParent(_partsRoot, false);

            // Kinematic: moved by this script, not pushed around by physics.
            Rigidbody body = part.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            // Speculative contacts catch a fast ball against a moving part without tunneling.
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            CreateShape(bone, part.transform);
        }

        // Stand the new parts around the player so they look right in the editor.
        _rigidbody = GetComponent<Rigidbody>();
        _capsuleHalfHeight = GetCapsuleHalfHeight();
        ComputePose(transform.position, transform.rotation, Vector3.zero, 0f);
        for (int i = 0; i < BoneCount; i++)
        {
            _partsRoot.GetChild(i).SetPositionAndRotation(_position[i], _rotation[i]);
        }
    }

    // The visible, colliding shape of a part: a child of the part, offset so
    // it runs along the bone (the part itself sits on the joint).
    private void CreateShape(Bone bone, Transform part)
    {
        switch (bone)
        {
            case Bone.Hips:
                // Lying across, from one hip joint to the other.
                AddCapsule(part, _hipsRadius, _hipWidth + _hipsRadius * 2f, Vector3.up * 0.03f, Quaternion.Euler(0f, 0f, 90f));
                break;
            case Bone.Spine:
                AddCapsule(part, _spineRadius, _spineLength + _spineRadius, Vector3.up * (_spineLength / 2f), Quaternion.identity);
                break;
            case Bone.Chest:
                float chestLength = _shoulderHeight + _neckBaseAboveShoulders - (LegHeight + _hipsLength + _spineLength);
                AddCapsule(part, _chestRadius, chestLength + _chestRadius, Vector3.up * (chestLength / 2f), Quaternion.identity);
                break;
            case Bone.Neck:
                AddCapsule(part, _neckRadius, _neckLength + _neckRadius, Vector3.up * (_neckLength / 2f), Quaternion.identity);
                break;
            case Bone.Head:
                AddShape(PrimitiveType.Sphere, part, Vector3.up * _headRadius, Quaternion.identity, Vector3.one * (_headRadius * 2f));
                break;
            case Bone.LeftUpperArm:
            case Bone.RightUpperArm:
                AddLimb(part, _upperArmRadius, _upperArmLength);
                break;
            case Bone.LeftLowerArm:
            case Bone.RightLowerArm:
                AddLimb(part, _lowerArmRadius, _lowerArmLength);
                break;
            case Bone.LeftUpperLeg:
            case Bone.RightUpperLeg:
                AddLimb(part, _upperLegRadius, _upperLegLength);
                break;
            case Bone.LeftLowerLeg:
            case Bone.RightLowerLeg:
                AddLimb(part, _lowerLegRadius, _lowerLegLength);
                break;
            case Bone.LeftHand:
            case Bone.RightHand:
                // X = width, Y = along the fingers, Z = thickness (out of the palm).
                AddShape(PrimitiveType.Cube, part, Vector3.up * (_handLength / 2f), Quaternion.identity,
                    new Vector3(_handSize.x, _handLength, _handSize.y));
                break;
            case Bone.LeftFoot:
            case Bone.RightFoot:
                // The foot's Y points toward the toes and its Z is up; the sole sits on the floor.
                AddShape(PrimitiveType.Cube, part,
                    new Vector3(0f, _footSize.x / 2f - _footSize.z, -_ankleHeight / 2f), Quaternion.identity,
                    new Vector3(_footSize.y, _footSize.x, _ankleHeight));
                break;
        }
    }

    // A limb capsule running from the joint along the bone. It's one radius
    // longer than the bone, so neighboring parts overlap a little at the joints.
    private void AddLimb(Transform part, float radius, float length)
    {
        AddCapsule(part, radius, length + radius, Vector3.up * (length / 2f), Quaternion.identity);
    }

    // Unity's capsule is 2 m tall with radius 0.5 (along its Y axis), so X/Z
    // scale sets the radius and Y scale sets the height.
    private void AddCapsule(Transform part, float radius, float height, Vector3 center, Quaternion rotation)
    {
        height = Mathf.Max(height, radius * 2f);
        AddShape(PrimitiveType.Capsule, part, center, rotation, new Vector3(radius * 2f, height / 2f, radius * 2f));
    }

    private void AddShape(PrimitiveType type, Transform part, Vector3 localPosition, Quaternion localRotation, Vector3 scale)
    {
        GameObject shape = GameObject.CreatePrimitive(type);
        shape.name = "Shape";
        shape.transform.SetParent(part, false);
        shape.transform.localPosition = localPosition;
        shape.transform.localRotation = localRotation;
        shape.transform.localScale = scale;
        if (_material != null) shape.GetComponent<Renderer>().sharedMaterial = _material;
        if (_physicsMaterial != null) shape.GetComponent<Collider>().sharedMaterial = _physicsMaterial;
    }
}
