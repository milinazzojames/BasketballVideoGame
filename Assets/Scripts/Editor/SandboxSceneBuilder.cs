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
    private const string PlayerPhysicsPath = "Assets/Settings/PlayerPhysics.asset";

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
    private const float LineWidth = 0.05f;

    // Stand-in player: a capsule the size of a 6'6" (1.98 m) player.
    private const float PlayerHeight = 1.98f;
    private const float PlayerRadius = 0.4f;
    private const float PlayerStartZ = 4f;             // a bit beyond the 3-point line, facing the hoop
    private const float PlayerMass = 98f;              // about 215 lb

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

        BuildFloor();
        Transform rim = BuildHoop();
        Transform player = BuildPlayerStandIn();
        SetUpCamera(player, rim);

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
        CreateBox("Floor", court,
            new Vector3(0f, -floorThickness / 2f, HalfCourtLength / 2f),
            new Vector3(CourtWidth + Runoff * 2f, floorThickness, HalfCourtLength + Runoff * 2f),
            GetMaterial("Floor", new Color(0.76f, 0.6f, 0.42f)));

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

    // Returns the rim, which the camera uses as its focus point.
    private static Transform BuildHoop()
    {
        Transform hoop = new GameObject("Hoop").transform;
        float boardFaceZ = HalfCourtLength - BaselineToBoard;

        // Backboard: its front face is at boardFaceZ, its bottom edge 0.15 m below the rim.
        float boardCenterY = RimHeight - BoardBottomBelowRim + BoardHeight / 2f;
        CreateBox("Backboard", hoop,
            new Vector3(0f, boardCenterY, boardFaceZ + BoardThickness / 2f),
            new Vector3(BoardWidth, BoardHeight, BoardThickness),
            GetMaterial("Backboard", new Color(0.95f, 0.95f, 0.95f)));

        // Rim: a ring of short capsule colliders. A ring can't be one primitive
        // collider, and a ring of capsules lets the ball hit the front, back,
        // and sides of the rim realistically.
        Material rimMat = GetMaterial("Rim", new Color(1f, 0.35f, 0f));
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
        }

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
        player.GetComponent<Collider>().sharedMaterial = GetPlayerPhysicsMaterial();

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

    private static PhysicsMaterial GetPlayerPhysicsMaterial()
    {
        PhysicsMaterial material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(PlayerPhysicsPath);
        if (material == null)
        {
            material = new PhysicsMaterial("PlayerPhysics")
            {
                dynamicFriction = 0f,
                staticFriction = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounciness = 0f,
                bounceCombine = PhysicsMaterialCombine.Minimum,
            };
            AssetDatabase.CreateAsset(material, PlayerPhysicsPath);
        }
        return material;
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
    private static Material GetMaterial(string name, Color color)
    {
        EnsureFolder(MaterialFolder);
        string path = $"{MaterialFolder}/{name}.mat";

        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
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
