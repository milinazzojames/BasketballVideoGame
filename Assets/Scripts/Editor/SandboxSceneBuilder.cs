using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Builds the Sandbox half-court scene from code, so nobody has to place
// objects by hand or edit scene files. Run it from the menu:
//   Basketball > Build Sandbox Scene
// Running it again rebuilds the scene from scratch.
//
// Layout: 1 unit = 1 meter. The half-court line is at z = 0 and the baseline
// is at z = +14.325, so moving toward +Z means moving toward the hoop.
public static class SandboxSceneBuilder
{
    private const string ScenePath = "Assets/Scenes/Sandbox.unity";
    private const string MaterialFolder = "Assets/Materials/Sandbox";
    private const string MovementConfigPath = "Assets/Settings/PlayerMovementConfig.asset";
    private const string PhysicsMaterialFolder = "Assets/Settings";

    // Real-world dimensions in meters (see CLAUDE.md).
    private const float CourtWidth = 15.24f;
    private const float HalfCourtLength = 14.325f;     // half of 28.65 m
    private const float Runoff = 2f;                   // floor beyond the lines, so things don't fall off the edge
    private const float BaselineToBoard = 1.22f;       // backboard face is 4 ft inside the baseline
    private const float FreeThrowToBoard = 4.57f;
    private const float RimHeight = 3.048f;            // exactly 10 ft, measured to the top of the rim
    private const float RimInnerRadius = 0.2285f;      // 0.457 m inner diameter
    private const float RimTubeRadius = 0.008f;        // the rim is a ~16 mm steel rod
    private const float RimToBoardGap = 0.15f;         // board face to the rim's nearest inner edge
    private const int RimSegments = 24;                // more segments = rounder collision ring
    private const float BoardWidth = 1.83f;
    private const float BoardHeight = 1.07f;
    private const float BoardThickness = 0.05f;
    private const float BoardBottomBelowRim = 0.15f;   // rim sits 6 in above the board's bottom edge
    private const float PoleBehindBaseline = 1f;
    private const float NetLength = 0.45f;             // real nets hang about 15-18 in
    private const float NetBottomRadius = 0.13f;       // nets taper, so even a swish brushes the strands
    private const int NetColumns = 12;                 // strands around the rim
    private const int NetRows = 4;                     // rows of diamonds top to bottom
    private const float NetStrandWidth = 0.008f;
    private const float LineWidth = 0.05f;

    // Stand-in player: a capsule the size of a 6'6" (1.98 m) player.
    private const float PlayerHeight = 1.98f;
    private const float PlayerRadius = 0.4f;
    private const float PlayerStartZ = 4f;             // a bit beyond the 3-point line, facing the hoop
    private const float PlayerMass = 98f;              // about 215 lb
    // Body parts against the ball: averaged with the ball's 0.85, a ball off an
    // arm or chest bounces about (0.85 + 0.2) / 2 = 0.53, deader than off the floor.
    private const float BodyBounce = 0.2f;
    private const float BodyFriction = 0.5f;

    // Size 7 basketball.
    private const float BallDiameter = 0.24f;
    private const float BallMass = 0.62f;
    // Rigidbody linear damping. Kept at 0 because the shot math (ShotMath) assumes
    // no air drag; any drag here makes every shot fall short and hit the front rim.
    private const float BallAirDrag = 0f;
    private const float BallRollingDrag = 0.3f;        // Rigidbody angular damping; makes a rolling ball slow down

    // Bounciness and friction for each surface. Unity averages the ball's value
    // with the surface's value, so the ball/floor bounce is (0.85 + 0.85) / 2 = 0.85,
    // the ball/rim bounce is (0.85 + 0.45) / 2 = 0.65, and so on.
    // A bounce of 0.85 returns a 1.83 m drop to about 0.85² × 1.83 = 1.32 m.
    private const float BallBounce = 0.85f;
    private const float FloorBounce = 0.85f;
    private const float RimBounce = 0.45f;             // a rim gives a bit, so it's "deader" than the floor
    private const float BackboardBounce = 0.65f;
    private const float BallFriction = 0.6f;
    private const float FloorFriction = 0.6f;
    private const float RimFriction = 0.5f;
    private const float BackboardFriction = 0.4f;
    // The invisible walls use "Minimum" combine, so these are the values the ball
    // actually gets against them (not averaged with the ball's 0.85 bounce).
    private const float BarrierBounce = 0.3f;
    private const float BarrierFriction = 0.3f;

    // Invisible walls around the edge of the floor, so the ball and player can't fall off.
    private const float BarrierHeight = 10f;           // at 6 m, a hard lob (peaking at 6.5 m) went over; 10 m costs nothing since they are invisible
    private const float BarrierThickness = 0.5f;
    // If the ball still ends up below this height (say it slips through), it's put back on the court.
    private const float BallResetBelowY = -2f;

    // Project-wide physics settings.
    private const float FixedTimestep = 0.01f;         // 100 physics steps per second, so a fast ball can't skip past the thin rim
    private const float BounceThreshold = 0.5f;        // impacts slower than this (m/s) don't bounce; Unity's default of 2 would kill soft dribbles

    [MenuItem("Basketball/Build Sandbox Scene")]
    public static void Build()
    {
        if (System.IO.File.Exists(ScenePath) &&
            !EditorUtility.DisplayDialog("Rebuild Sandbox scene?",
                $"This replaces {ScenePath}. Any changes made to it by hand will be lost.",
                "Rebuild", "Cancel"))
        {
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        // DefaultGameObjects gives us a Main Camera and a Directional Light.
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        ApplyProjectPhysicsSettings();
        BuildFloor();
        BuildBarriers();
        Transform rim = BuildHoop();
        Transform player = BuildPlayerStandIn();
        SetUpCamera(player, rim);
        Basketball ball = BuildBall();
        BuildBallTester(ball, player, rim);
        PlayerDribbler dribbler = AddDribbler(player, ball, rim);
        AddShooter(player, dribbler, ball, rim);
        AddBody(player, dribbler, ball);

        EditorSceneManager.SaveScene(scene, ScenePath);
        AddSceneToBuildSettings();
        AssetDatabase.SaveAssets();

        Debug.Log($"Sandbox scene built and saved to {ScenePath}.");
    }

    private static void BuildFloor()
    {
        Transform court = new GameObject("Court").transform;

        // The floor is a thin box whose top surface sits exactly at y = 0.
        const float floorThickness = 0.1f;
        GameObject floor = CreateBox("Floor", court,
            new Vector3(0f, -floorThickness / 2f, HalfCourtLength / 2f),
            new Vector3(CourtWidth + Runoff * 2f, floorThickness, HalfCourtLength + Runoff * 2f),
            GetMaterial("Floor", new Color(0.76f, 0.6f, 0.42f)));
        floor.GetComponent<Collider>().sharedMaterial =
            GetPhysicsMaterial("FloorPhysics", FloorBounce, FloorFriction, PhysicsMaterialCombine.Average);

        // Painted lines are visual only (no colliders), raised 1 mm to avoid flickering against the floor.
        Material lineMat = GetMaterial("Lines", Color.white);
        const float lineY = 0.001f;
        float boardFaceZ = HalfCourtLength - BaselineToBoard;

        CreateLine("Baseline", court, new Vector3(0f, lineY, HalfCourtLength), new Vector3(CourtWidth, 0.002f, LineWidth), lineMat);
        CreateLine("HalfCourtLine", court, new Vector3(0f, lineY, 0f), new Vector3(CourtWidth, 0.002f, LineWidth), lineMat);
        CreateLine("SidelineLeft", court, new Vector3(-CourtWidth / 2f, lineY, HalfCourtLength / 2f), new Vector3(LineWidth, 0.002f, HalfCourtLength), lineMat);
        CreateLine("SidelineRight", court, new Vector3(CourtWidth / 2f, lineY, HalfCourtLength / 2f), new Vector3(LineWidth, 0.002f, HalfCourtLength), lineMat);
        CreateLine("FreeThrowLine", court, new Vector3(0f, lineY, boardFaceZ - FreeThrowToBoard), new Vector3(3.66f, 0.002f, LineWidth), lineMat);
    }

    // Four invisible walls (colliders only, no mesh) standing on the outer edge of
    // the floor, so the ball bounces softly back instead of falling off the world.
    // Later, real out-of-bounds rules replace these during games.
    private static void BuildBarriers()
    {
        Transform barriers = new GameObject("Barriers").transform;
        PhysicsMaterial bounce = GetPhysicsMaterial("BarrierPhysics", BarrierBounce, BarrierFriction, PhysicsMaterialCombine.Minimum);

        // Same footprint as the floor in BuildFloor. Each wall sits just outside
        // the edge, and the side walls run past the corners so there are no gaps.
        float halfWidth = CourtWidth / 2f + Runoff;
        float nearZ = -Runoff;
        float farZ = HalfCourtLength + Runoff;
        float centerZ = (nearZ + farZ) / 2f;
        float length = farZ - nearZ + BarrierThickness * 2f;
        float width = halfWidth * 2f + BarrierThickness * 2f;
        float y = BarrierHeight / 2f;
        float offset = BarrierThickness / 2f;

        CreateBarrier("BarrierLeft", barriers, new Vector3(-halfWidth - offset, y, centerZ), new Vector3(BarrierThickness, BarrierHeight, length), bounce);
        CreateBarrier("BarrierRight", barriers, new Vector3(halfWidth + offset, y, centerZ), new Vector3(BarrierThickness, BarrierHeight, length), bounce);
        CreateBarrier("BarrierHalfCourt", barriers, new Vector3(0f, y, nearZ - offset), new Vector3(width, BarrierHeight, BarrierThickness), bounce);
        CreateBarrier("BarrierBaseline", barriers, new Vector3(0f, y, farZ + offset), new Vector3(width, BarrierHeight, BarrierThickness), bounce);
    }

    private static void CreateBarrier(string name, Transform parent, Vector3 position, Vector3 size, PhysicsMaterial material)
    {
        GameObject wall = new GameObject(name);
        wall.transform.SetParent(parent, false);
        wall.transform.localPosition = position;
        BoxCollider collider = wall.AddComponent<BoxCollider>();
        collider.size = size;
        collider.sharedMaterial = material;
    }

    // Returns the rim, which the camera uses as its focus point.
    private static Transform BuildHoop()
    {
        Transform hoop = new GameObject("Hoop").transform;
        float boardFaceZ = HalfCourtLength - BaselineToBoard;

        // Backboard: its front face is at boardFaceZ, its bottom edge 0.15 m below the rim.
        float boardCenterY = RimHeight - BoardBottomBelowRim + BoardHeight / 2f;
        GameObject backboard = CreateBox("Backboard", hoop,
            new Vector3(0f, boardCenterY, boardFaceZ + BoardThickness / 2f),
            new Vector3(BoardWidth, BoardHeight, BoardThickness),
            GetMaterial("Backboard", new Color(0.95f, 0.95f, 0.95f)));
        backboard.GetComponent<Collider>().sharedMaterial =
            GetPhysicsMaterial("BackboardPhysics", BackboardBounce, BackboardFriction, PhysicsMaterialCombine.Average);

        // Rim: a ring of short capsule colliders. A ring can't be one primitive
        // collider, and a ring of capsules lets the ball hit the front, back,
        // and sides of the rim realistically.
        Material rimMat = GetMaterial("Rim", new Color(1f, 0.35f, 0f));
        PhysicsMaterial rimPhysics = GetPhysicsMaterial("RimPhysics", RimBounce, RimFriction, PhysicsMaterialCombine.Average);
        float ringRadius = RimInnerRadius + RimTubeRadius;          // radius of the rod's centerline
        float rimCenterZ = boardFaceZ - RimToBoardGap - RimInnerRadius;
        Transform rim = new GameObject("Rim").transform;
        rim.SetParent(hoop, false);
        rim.localPosition = new Vector3(0f, RimHeight - RimTubeRadius, rimCenterZ);

        // Each segment spans one chord of the circle. Capsules have rounded ends,
        // so making them a rod-width longer than the chord closes the gaps between them.
        float chord = 2f * ringRadius * Mathf.Sin(Mathf.PI / RimSegments);
        float segmentLength = chord + RimTubeRadius * 2f;
        for (int i = 0; i < RimSegments; i++)
        {
            float angle = (i + 0.5f) * Mathf.PI * 2f / RimSegments;
            Vector3 onCircle = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            Vector3 tangent = new Vector3(-Mathf.Sin(angle), 0f, Mathf.Cos(angle));

            // Unity's cylinder is 2 units tall with radius 0.5, and its collider is a
            // capsule along its Y axis, so we scale it and turn Y to face along the ring.
            GameObject segment = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            segment.name = $"RimSegment{i:00}";
            segment.transform.SetParent(rim, false);
            segment.transform.localPosition = onCircle * ringRadius;
            segment.transform.localRotation = Quaternion.FromToRotation(Vector3.up, tangent);
            segment.transform.localScale = new Vector3(RimTubeRadius * 2f, segmentLength / 2f, RimTubeRadius * 2f);
            segment.GetComponent<Renderer>().sharedMaterial = rimMat;
            segment.GetComponent<Collider>().sharedMaterial = rimPhysics;
        }

        BuildNet(rim, ringRadius);

        // Bracket joining the back of the rim to the backboard.
        float rimBackZ = rimCenterZ + RimInnerRadius + RimTubeRadius * 2f;
        float bracketLength = boardFaceZ - rimBackZ;
        CreateBox("RimBracket", hoop,
            new Vector3(0f, RimHeight - RimTubeRadius, rimBackZ + bracketLength / 2f),
            new Vector3(0.12f, 0.03f, bracketLength),
            rimMat);

        // Support pole behind the baseline, with an arm out to the back of the board.
        Material poleMat = GetMaterial("Pole", new Color(0.25f, 0.25f, 0.28f));
        float poleZ = HalfCourtLength + PoleBehindBaseline;
        CreateBox("Pole", hoop,
            new Vector3(0f, boardCenterY / 2f, poleZ),
            new Vector3(0.2f, boardCenterY, 0.2f),
            poleMat);

        float boardBackZ = boardFaceZ + BoardThickness;
        CreateBox("PoleArm", hoop,
            new Vector3(0f, boardCenterY, (boardBackZ + poleZ) / 2f),
            new Vector3(0.15f, 0.15f, poleZ - boardBackZ),
            poleMat);

        return rim;
    }

    // The net is two parts on one object at the rim center: NetSway draws the
    // strands and makes them move when the ball touches them, and a trigger
    // volume with HoopNet slows the ball down as it drops through.
    private static void BuildNet(Transform rim, float topRadius)
    {
        GameObject net = new GameObject("Net");
        net.transform.SetParent(rim, false);

        // Trigger volume filling the inside of the net. A capsule's height includes its
        // rounded ends, so it's a little taller than the net to cover the whole length.
        // It's shifted a bit lower than the net so a ball bouncing on top of the rim
        // isn't slowed; it only catches the ball once it has started dropping through.
        CapsuleCollider trigger = net.AddComponent<CapsuleCollider>();
        trigger.isTrigger = true;
        trigger.radius = NetBottomRadius + 0.05f;
        trigger.height = NetLength + 0.1f;
        trigger.center = new Vector3(0f, -NetLength / 2f - 0.1f, 0f);
        net.AddComponent<HoopNet>();

        NetSway sway = net.AddComponent<NetSway>();
        SerializedObject serialized = new SerializedObject(sway);
        serialized.FindProperty("_length").floatValue = NetLength;
        serialized.FindProperty("_topRadius").floatValue = topRadius;
        serialized.FindProperty("_bottomRadius").floatValue = NetBottomRadius;
        serialized.FindProperty("_columns").intValue = NetColumns;
        serialized.FindProperty("_rows").intValue = NetRows;
        serialized.FindProperty("_strandWidth").floatValue = NetStrandWidth;
        serialized.FindProperty("_material").objectReferenceValue = GetMaterial("Net", Color.white);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        sway.BuildLines();
    }

    private static Transform BuildPlayerStandIn()
    {
        GameObject player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        player.name = "Player";
        // Unity's capsule is 2 m tall with radius 0.5, so scaling X/Z sets the radius and Y sets the height.
        player.transform.position = new Vector3(0f, PlayerHeight / 2f, PlayerStartZ);
        player.transform.localScale = new Vector3(PlayerRadius * 2f, PlayerHeight / 2f, PlayerRadius * 2f);
        player.GetComponent<Renderer>().sharedMaterial = GetMaterial("Player", new Color(0.2f, 0.4f, 0.9f));

        // A small "nose" box on the front so you can see which way the player faces.
        GameObject nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
        nose.name = "FacingIndicator";
        Object.DestroyImmediate(nose.GetComponent<Collider>());
        nose.transform.position = player.transform.position + new Vector3(0f, 0.6f, PlayerRadius);
        nose.transform.localScale = new Vector3(0.15f, 0.1f, 0.2f);
        nose.transform.SetParent(player.transform, true);
        nose.GetComponent<Renderer>().sharedMaterial = GetMaterial("Lines", Color.white);

        // Physics body. Rotation is frozen so collisions can't tip the capsule over;
        // PlayerMovement turns it itself. Interpolation smooths motion between physics steps.
        Rigidbody body = player.AddComponent<Rigidbody>();
        body.mass = PlayerMass;
        body.constraints = RigidbodyConstraints.FreezeRotation;
        body.interpolation = RigidbodyInterpolation.Interpolate;

        // No friction: PlayerMovement sets the speed directly, and floor friction would fight it.
        player.GetComponent<Collider>().sharedMaterial =
            GetPhysicsMaterial("PlayerPhysics", 0f, 0f, PhysicsMaterialCombine.Minimum);

        player.AddComponent<PlayerInputReader>();
        PlayerMovement movement = player.AddComponent<PlayerMovement>();
        SerializedObject serialized = new SerializedObject(movement);
        serialized.FindProperty("_config").objectReferenceValue = GetMovementConfig();
        serialized.FindProperty("_input").objectReferenceValue = player.GetComponent<PlayerInputReader>();
        serialized.ApplyModifiedPropertiesWithoutUndo();

        return player.transform;
    }

    // Created once and then reused, so tuning changes survive rebuilding the scene.
    private static PlayerMovementConfig GetMovementConfig()
    {
        PlayerMovementConfig config = AssetDatabase.LoadAssetAtPath<PlayerMovementConfig>(MovementConfigPath);
        if (config == null)
        {
            config = ScriptableObject.CreateInstance<PlayerMovementConfig>();
            AssetDatabase.CreateAsset(config, MovementConfigPath);
        }
        return config;
    }

    // Physics materials are created once and then reused, so bounce and friction
    // tuning done in the Inspector survives rebuilding the scene.
    private static PhysicsMaterial GetPhysicsMaterial(string name, float bounciness, float friction, PhysicsMaterialCombine combine)
    {
        string path = $"{PhysicsMaterialFolder}/{name}.asset";
        PhysicsMaterial material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
        if (material == null)
        {
            material = new PhysicsMaterial(name)
            {
                dynamicFriction = friction,
                staticFriction = friction,
                frictionCombine = combine,
                bounciness = bounciness,
                bounceCombine = combine,
            };
            AssetDatabase.CreateAsset(material, path);
        }
        return material;
    }

    private static Basketball BuildBall()
    {
        GameObject ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ball.name = "Basketball";
        // Unity's sphere has radius 0.5, so a uniform scale of 0.24 gives a 0.24 m ball.
        ball.transform.localScale = Vector3.one * BallDiameter;
        // Start resting on the floor at the player's right hand, so the dribble starts right away.
        ball.transform.position = new Vector3(0.6f, BallDiameter / 2f, PlayerStartZ + 0.25f);
        ball.GetComponent<Renderer>().sharedMaterial = GetMaterial("Ball", new Color(0.8f, 0.3f, 0.08f));
        ball.GetComponent<Collider>().sharedMaterial =
            GetPhysicsMaterial("BallPhysics", BallBounce, BallFriction, PhysicsMaterialCombine.Average);

        Rigidbody body = ball.AddComponent<Rigidbody>();
        body.mass = BallMass;
        body.linearDamping = BallAirDrag;
        body.angularDamping = BallRollingDrag;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        // Continuous Dynamic checks the whole path between physics steps, so a
        // fast ball can't pass through the thin rim between two steps.
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        Basketball basketball = ball.AddComponent<Basketball>();
        // A ball that falls off the world is dropped back in at the free-throw line.
        float freeThrowZ = HalfCourtLength - BaselineToBoard - FreeThrowToBoard;
        SerializedObject serialized = new SerializedObject(basketball);
        serialized.FindProperty("_resetBelowHeight").floatValue = BallResetBelowY;
        serialized.FindProperty("_resetPosition").vector3Value = new Vector3(0f, 1f, freeThrowZ);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return basketball;
    }

    private static void BuildBallTester(Basketball ball, Transform player, Transform rim)
    {
        BallTestLauncher tester = new GameObject("BallTester").AddComponent<BallTestLauncher>();
        SerializedObject serialized = new SerializedObject(tester);
        serialized.FindProperty("_ball").objectReferenceValue = ball;
        serialized.FindProperty("_player").objectReferenceValue = player;
        serialized.FindProperty("_rim").objectReferenceValue = rim;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static PlayerDribbler AddDribbler(Transform player, Basketball ball, Transform rim)
    {
        PlayerDribbler dribbler = player.gameObject.AddComponent<PlayerDribbler>();
        SerializedObject serialized = new SerializedObject(dribbler);
        serialized.FindProperty("_input").objectReferenceValue = player.GetComponent<PlayerInputReader>();
        serialized.FindProperty("_ball").objectReferenceValue = ball;
        serialized.FindProperty("_movement").objectReferenceValue = player.GetComponent<PlayerMovement>();
        serialized.FindProperty("_cameraTransform").objectReferenceValue = Object.FindAnyObjectByType<Camera>().transform;
        // Protecting keeps the ball away from the hoop until there's a defender to keep it away from.
        serialized.FindProperty("_protectFrom").objectReferenceValue = rim;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        return dribbler;
    }

    private static void AddShooter(Transform player, PlayerDribbler dribbler, Basketball ball, Transform rim)
    {
        PlayerShooter shooter = player.gameObject.AddComponent<PlayerShooter>();
        SerializedObject serialized = new SerializedObject(shooter);
        serialized.FindProperty("_input").objectReferenceValue = player.GetComponent<PlayerInputReader>();
        serialized.FindProperty("_dribbler").objectReferenceValue = dribbler;
        serialized.FindProperty("_ball").objectReferenceValue = ball;
        serialized.FindProperty("_rim").objectReferenceValue = rim;
        serialized.FindProperty("_backboard").objectReferenceValue = rim.parent.Find("Backboard");
        serialized.ApplyModifiedPropertiesWithoutUndo();

        ShotMeterDisplay meter = player.gameObject.AddComponent<ShotMeterDisplay>();
        SerializedObject meterSettings = new SerializedObject(meter);
        meterSettings.FindProperty("_shooter").objectReferenceValue = shooter;
        meterSettings.FindProperty("_trackMaterial").objectReferenceValue = GetMaterial("MeterTrack", Color.white, unlit: true);
        meterSettings.FindProperty("_greenMaterial").objectReferenceValue = GetMaterial("MeterGreen", new Color(0.1f, 0.85f, 0.25f), unlit: true);
        meterSettings.FindProperty("_markerMaterial").objectReferenceValue = GetMaterial("MeterMarker", new Color(0.1f, 0.1f, 0.1f), unlit: true);
        meterSettings.ApplyModifiedPropertiesWithoutUndo();
    }

    // The primitive body: its parts are built under their own top-level
    // "PlayerBody" object (see PlayerBody for why they aren't under the player).
    private static void AddBody(Transform player, PlayerDribbler dribbler, Basketball ball)
    {
        PlayerBody body = player.gameObject.AddComponent<PlayerBody>();
        SerializedObject serialized = new SerializedObject(body);
        serialized.FindProperty("_material").objectReferenceValue = GetMaterial("PlayerBody", new Color(0.2f, 0.4f, 0.9f));
        serialized.FindProperty("_physicsMaterial").objectReferenceValue =
            GetPhysicsMaterial("BodyPhysics", BodyBounce, BodyFriction, PhysicsMaterialCombine.Average);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        body.BuildParts();

        SerializedObject dribblerSettings = new SerializedObject(dribbler);
        dribblerSettings.FindProperty("_playerBody").objectReferenceValue = body;
        dribblerSettings.ApplyModifiedPropertiesWithoutUndo();

        PlayerShooter shooter = player.GetComponent<PlayerShooter>();
        SerializedObject shooterSettings = new SerializedObject(shooter);
        shooterSettings.FindProperty("_body").objectReferenceValue = body;
        shooterSettings.ApplyModifiedPropertiesWithoutUndo();

        // The arms reach for the ball.
        PlayerArms arms = player.gameObject.AddComponent<PlayerArms>();
        SerializedObject armSettings = new SerializedObject(arms);
        armSettings.FindProperty("_body").objectReferenceValue = body;
        armSettings.FindProperty("_dribbler").objectReferenceValue = dribbler;
        armSettings.FindProperty("_shooter").objectReferenceValue = shooter;
        armSettings.FindProperty("_ball").objectReferenceValue = ball;
        armSettings.ApplyModifiedPropertiesWithoutUndo();
    }

    // These live in Edit > Project Settings (Time and Physics). Setting them here,
    // through Unity's own settings objects, keeps them in sync with this scene.
    private static void ApplyProjectPhysicsSettings()
    {
        // Unity 6 stores Fixed Timestep as a fraction rather than a plain float, so
        // setting it through SerializedObject silently fails. Outside Play mode,
        // Time.fixedDeltaTime writes straight to the project setting instead;
        // marking TimeManager dirty makes SaveAssets write it to disk.
        Time.fixedDeltaTime = FixedTimestep;
        EditorUtility.SetDirty(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TimeManager.asset")[0]);
        SetProjectSetting("ProjectSettings/DynamicsManager.asset", "m_BounceThreshold", BounceThreshold);
    }

    private static void SetProjectSetting(string settingsPath, string propertyName, float value)
    {
        SerializedObject settings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath(settingsPath)[0]);
        SerializedProperty property = settings.FindProperty(propertyName);
        if (property == null)
        {
            Debug.LogWarning($"Couldn't find '{propertyName}' in {settingsPath}; set it by hand in Project Settings.");
            return;
        }
        property.floatValue = value;
        settings.ApplyModifiedProperties();
    }

    private static void SetUpCamera(Transform player, Transform rim)
    {
        Camera camera = Object.FindAnyObjectByType<Camera>();
        BehindPlayerCamera follow = camera.gameObject.AddComponent<BehindPlayerCamera>();

        // The camera's fields are private (tunable in the Inspector), so we fill them
        // through SerializedObject, the same way the Inspector does.
        SerializedObject serialized = new SerializedObject(follow);
        serialized.FindProperty("_target").objectReferenceValue = player;
        serialized.FindProperty("_focus").objectReferenceValue = rim;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject movement = new SerializedObject(player.GetComponent<PlayerMovement>());
        movement.FindProperty("_cameraTransform").objectReferenceValue = camera.transform;
        movement.ApplyModifiedPropertiesWithoutUndo();

        // Rough starting spot behind the player so the Scene view thumbnail looks right.
        // The script snaps to its exact spot when Play starts.
        camera.transform.position = player.position + new Vector3(0f, 4.5f, -7.5f);
        camera.transform.LookAt(Vector3.Lerp(player.position, rim.position, 0.3f));
    }

    private static GameObject CreateBox(string name, Transform parent, Vector3 position, Vector3 size, Material material)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.SetParent(parent, false);
        box.transform.localPosition = position;
        box.transform.localScale = size;
        box.GetComponent<Renderer>().sharedMaterial = material;
        return box;
    }

    private static void CreateLine(string name, Transform parent, Vector3 position, Vector3 size, Material material)
    {
        GameObject line = CreateBox(name, parent, position, size, material);
        Object.DestroyImmediate(line.GetComponent<Collider>());
    }

    // Materials are saved as asset files; materials that only exist in memory
    // wouldn't be saved with the scene.
    // Unlit materials ignore lighting and show their exact color, which suits UI like the shot meter.
    private static Material GetMaterial(string name, Color color, bool unlit = false)
    {
        EnsureFolder(MaterialFolder);
        string path = $"{MaterialFolder}/{name}.mat";

        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, path);
        }

        material.color = color;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;

        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }

    // Puts Sandbox first in File > Build Profiles so it's the scene that loads in a build.
    private static void AddSceneToBuildSettings()
    {
        List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        scenes.RemoveAll(s => s.path == ScenePath);
        scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
