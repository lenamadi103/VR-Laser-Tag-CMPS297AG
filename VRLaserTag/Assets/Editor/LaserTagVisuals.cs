using System.Collections.Generic;
using System.IO;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

// Visual redesign of the arena (look only - no gameplay changes).
//
// "VR Laser Tag > 5. Visual Redesign > A. Lighting" switches the arena to
// baked lighting: the real-time sun and its shadows are replaced by
// lightmaps, light probes (for players and the gun) and one baked
// reflection probe. Baked light costs nothing per frame on the Quest.
//
// "B. Arena Look" gives the arena its sci-fi look: textured floor, wall
// and cover materials, a ceiling with light fixtures, neon trims, a
// glowing centre core, numbered spawn pads and wall signs. It then
// re-bakes the lighting. Neon parts use the cheap Unlit shader (no
// real lights, no transparency, no post-processing).
//
// Everything new is created under the "ArenaDecor" root object, which has
// no colliders. Gameplay objects (floor, walls, cover, spawn points) are
// only marked static for baking; they are never moved or resized.
// Every step is safe to run again.
public static class LaserTagVisuals
{
    private const string ScenePath = "Assets/ArenaScene.unity";
    private const string LightingSettingsPath = "Assets/Settings/ArenaLighting.lighting";
    private const string MobileRPAssetPath = "Assets/Settings/Mobile_RPAsset.asset";
    private const string DecorRootName = "ArenaDecor";

    // Static arena geometry that receives lightmaps.
    private static readonly string[] StaticArenaObjects =
    {
        "Floor", "Cube1", "Cube2", "Cube3", "Cube4",
        "Cover1", "Cover2", "Cover3", "Cover4", "Cover5",
    };

    // Colour palette shared by all phases (neutral, no team colours).
    public static readonly Color CoolWhite = new Color(0.82f, 0.9f, 1f);
    public static readonly Color AccentBlue = new Color(0.2f, 0.62f, 1f);
    public static readonly Color AccentOrange = new Color(1f, 0.52f, 0.16f);
    public static readonly Color Background = new Color(0.02f, 0.025f, 0.035f);

    // Arena dimensions (the gameplay walls Cube1-4 are 0.5 m thick at +-7.5).
    private const float WallInner = 7.25f;
    private const float WallTop = 3f;
    private const float CeilingY = 4.5f;

    // Ceiling lamps (baked light + visible light fixture).
    private static readonly Vector3[] LampPositions =
    {
        new Vector3(0f, 4.1f, 0f),
        new Vector3(4.2f, 4.1f, 4.2f),
        new Vector3(-4.2f, 4.1f, 4.2f),
        new Vector3(4.2f, 4.1f, -4.2f),
        new Vector3(-4.2f, 4.1f, -4.2f),
    };

    // ------------------------------------------------------------------
    // Menu items and command-line entry points
    // ------------------------------------------------------------------

    [MenuItem("VR Laser Tag/5. Visual Redesign/A. Lighting (bake)")]
    public static void ApplyLighting()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        SetupLighting();
        ConfigureMobileLighting();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Bake(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[LaserTagVisuals] Phase A lighting applied and baked.");
    }

    public static void BatchApplyLighting()
    {
        RunAndExit(ApplyLighting);
    }

    [MenuItem("VR Laser Tag/5. Visual Redesign/B. Arena Look (+ bake)")]
    public static void ApplyArenaLook()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        SetupArenaLook();
        SetupLighting();
        ConfigureMobileLighting();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Bake(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[LaserTagVisuals] Phase B arena look applied and baked.");
    }

    public static void BatchApplyArenaLook()
    {
        RunAndExit(ApplyArenaLook);
    }

    [MenuItem("VR Laser Tag/5. Visual Redesign/C. Hands and Gun")]
    public static void ApplyHandsAndGun()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        BuildHandAndGunAssets();
        SetupLocalGunAndHands();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        SetupAvatarHandsAndGun();
        AssetDatabase.SaveAssets();
        Debug.Log("[LaserTagVisuals] Phase C hands and gun applied.");
    }

    public static void BatchApplyHandsAndGun()
    {
        RunAndExit(ApplyHandsAndGun);
    }

    private static void RunAndExit(System.Action action)
    {
        int code = 0;

        try
        {
            action();
        }
        catch (System.Exception exception)
        {
            Debug.LogException(exception);
            code = 1;
        }

        if (Application.isBatchMode)
            EditorApplication.Exit(code);
    }

    // ------------------------------------------------------------------
    // Phase A - lighting
    // ------------------------------------------------------------------

    private static void SetupLighting()
    {
        Transform decor = FindOrCreateRoot(DecorRootName).transform;

        MarkArenaStatic();
        SetupSun();
        SetupBakedLights(decor);
        SetupEnvironment();
        SetupCamera();
        SetupLightProbes(decor);
        SetupReflectionProbe(decor);
        SetupLightingSettings();
    }

    private static void MarkArenaStatic()
    {
        const StaticEditorFlags flags =
            StaticEditorFlags.ContributeGI |
            StaticEditorFlags.BatchingStatic |
            StaticEditorFlags.ReflectionProbeStatic;

        foreach (string name in StaticArenaObjects)
        {
            GameObject go = GameObject.Find(name);
            if (go == null)
            {
                Debug.LogWarning("[LaserTagVisuals] Arena object not found: " + name);
                continue;
            }

            GameObjectUtility.SetStaticEditorFlags(go, flags);

            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.receiveGI = ReceiveGI.Lightmaps;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                EditorUtility.SetDirty(renderer);
            }
        }
    }

    // The sun becomes a baked-only light: it still shapes the lightmaps
    // (soft shadows behind cover) but costs nothing at runtime.
    private static void SetupSun()
    {
        GameObject sunObject = GameObject.Find("Directional Light");
        Light sun = sunObject != null ? sunObject.GetComponent<Light>() : null;
        if (sun == null)
            return;

        sun.lightmapBakeType = LightmapBakeType.Baked;
        sun.shadows = LightShadows.Soft;
        sun.shadowAngle = 2f;
        sun.color = CoolWhite;
        sun.intensity = 0.35f;
        sun.transform.rotation = Quaternion.Euler(62f, -35f, 0f);
        EditorUtility.SetDirty(sun);
    }

    // Baked "ceiling lamps" and low corner accents. Blue and orange
    // alternate diagonally, so no half of the arena reads as a team side.
    private static void SetupBakedLights(Transform decor)
    {
        Transform root = GetOrCreateChild(decor, "BakedLights");

        for (int i = root.childCount - 1; i >= 0; i--)
            Object.DestroyImmediate(root.GetChild(i).gameObject);

        // Lamps sit just under the ceiling light fixtures (see LampPositions).
        CreateBakedLight(root, "Lamp_Center", LampPositions[0], CoolWhite, 16f, 12f);
        CreateBakedLight(root, "Lamp_NE", LampPositions[1], CoolWhite, 11f, 9f);
        CreateBakedLight(root, "Lamp_NW", LampPositions[2], CoolWhite, 11f, 9f);
        CreateBakedLight(root, "Lamp_SE", LampPositions[3], CoolWhite, 11f, 9f);
        CreateBakedLight(root, "Lamp_SW", LampPositions[4], CoolWhite, 11f, 9f);

        CreateBakedLight(root, "Accent_NE", new Vector3(6.6f, 0.5f, 6.6f), AccentBlue, 3f, 4f);
        CreateBakedLight(root, "Accent_SW", new Vector3(-6.6f, 0.5f, -6.6f), AccentBlue, 3f, 4f);
        CreateBakedLight(root, "Accent_NW", new Vector3(-6.6f, 0.5f, 6.6f), AccentOrange, 3f, 4f);
        CreateBakedLight(root, "Accent_SE", new Vector3(6.6f, 0.5f, -6.6f), AccentOrange, 3f, 4f);
    }

    private static void CreateBakedLight(Transform parent, string name, Vector3 position, Color color, float intensity, float range)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = position;

        Light light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.lightmapBakeType = LightmapBakeType.Baked;
        light.color = color;
        light.intensity = intensity;
        light.range = range;
        light.shadows = LightShadows.Soft;
        light.shapeRadius = 0.2f;
    }

    // Dark, slightly blue indoor ambience; no sky.
    private static void SetupEnvironment()
    {
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.10f, 0.12f, 0.16f);
        RenderSettings.ambientEquatorColor = new Color(0.07f, 0.08f, 0.10f);
        RenderSettings.ambientGroundColor = new Color(0.04f, 0.04f, 0.05f);
        RenderSettings.ambientIntensity = 1f;
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
        RenderSettings.customReflectionTexture = null;
        RenderSettings.fog = false;
        RenderSettings.sun = null;
    }

    // A solid colour background is cheaper than drawing a skybox.
    private static void SetupCamera()
    {
        XROrigin origin = Object.FindAnyObjectByType<XROrigin>(FindObjectsInactive.Include);
        Camera camera = origin != null ? origin.Camera : Camera.main;
        if (camera == null)
            return;

        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Background;
        EditorUtility.SetDirty(camera);

        UniversalAdditionalCameraData data = camera.GetComponent<UniversalAdditionalCameraData>();
        if (data != null)
        {
            data.renderShadows = false;
            EditorUtility.SetDirty(data);
        }
    }

    // Grid of probes so players and the gun pick up the baked light.
    // Probes inside cover blocks are skipped (they would bake black).
    private static void SetupLightProbes(Transform decor)
    {
        Transform probesObject = GetOrCreateChild(decor, "LightProbes");
        LightProbeGroup group = GetOrAdd<LightProbeGroup>(probesObject.gameObject);
        probesObject.position = Vector3.zero;

        List<Bounds> blocked = new List<Bounds>();
        foreach (string name in StaticArenaObjects)
        {
            GameObject go = GameObject.Find(name);
            Collider collider = go != null ? go.GetComponent<Collider>() : null;
            if (collider != null && name != "Floor")
            {
                Bounds b = collider.bounds;
                b.Expand(0.1f);
                blocked.Add(b);
            }
        }

        float[] xz = { -6.6f, -4.4f, -2.2f, 0f, 2.2f, 4.4f, 6.6f };
        float[] heights = { 0.3f, 1.3f, 2.6f };
        List<Vector3> positions = new List<Vector3>();

        foreach (float x in xz)
        foreach (float z in xz)
        foreach (float y in heights)
        {
            Vector3 p = new Vector3(x, y, z);
            bool inside = false;
            foreach (Bounds b in blocked)
                inside |= b.Contains(p);

            if (!inside)
                positions.Add(p);
        }

        group.probePositions = positions.ToArray();
        EditorUtility.SetDirty(group);
    }

    // One baked, low-resolution reflection probe gives metal surfaces
    // something to reflect. Sampling it costs the same as the default
    // environment reflection, so it adds no runtime cost.
    private static void SetupReflectionProbe(Transform decor)
    {
        Transform probeObject = GetOrCreateChild(decor, "ReflectionProbe");
        probeObject.position = new Vector3(0f, 1.6f, 0f);

        ReflectionProbe probe = GetOrAdd<ReflectionProbe>(probeObject.gameObject);
        probe.mode = ReflectionProbeMode.Baked;
        probe.size = new Vector3(15.5f, 6f, 15.5f);
        probe.boxProjection = false;
        probe.resolution = 64;
        probe.hdr = false;
        probe.importance = 1;
        EditorUtility.SetDirty(probe);
    }

    private static void SetupLightingSettings()
    {
        LightingSettings settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(LightingSettingsPath);
        if (settings == null)
        {
            settings = new LightingSettings { name = "ArenaLighting" };
            AssetDatabase.CreateAsset(settings, LightingSettingsPath);
        }

        settings.bakedGI = true;
        settings.realtimeGI = false;
        settings.lightmapper = LightingSettings.Lightmapper.UnityComputeGPU;
        settings.lightmapResolution = 12f;
        settings.lightmapPadding = 2;
        settings.lightmapMaxSize = 1024;
        settings.lightmapCompression = LightmapCompression.NormalQuality;
        settings.directionalityMode = LightmapsMode.NonDirectional;
        settings.maxBounces = 2;
        settings.indirectScale = 1.4f;
        settings.directSampleCount = 32;
        settings.indirectSampleCount = 256;
        settings.environmentSampleCount = 256;
        settings.filteringMode = LightingSettings.FilterMode.Auto;
        settings.ao = true;
        settings.aoMaxDistance = 1f;
        settings.aoExponentIndirect = 1f;
        settings.aoExponentDirect = 0.5f;
        EditorUtility.SetDirty(settings);

        Lightmapping.lightingSettings = settings;
    }

    // Everything in the arena is baked, so the Quest renderer needs no
    // real-time shadow pass, mixed lighting or probe blending.
    private static void ConfigureMobileLighting()
    {
        UniversalRenderPipelineAsset mobile =
            AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(MobileRPAssetPath);
        if (mobile == null)
            return;

        SerializedObject so = new SerializedObject(mobile);
        SetBool(so, "m_MainLightShadowsSupported", false);
        SetBool(so, "m_MixedLightingSupported", false);
        SetBool(so, "m_ReflectionProbeBlending", false);
        SetBool(so, "m_ReflectionProbeBoxProjection", false);
        SetBool(so, "m_SupportsLightLayers", false);
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(mobile);
    }

    private static void Bake(UnityEngine.SceneManagement.Scene scene)
    {
        Lightmapping.Clear();

        if (!Lightmapping.Bake())
            throw new System.Exception("Lightmap bake failed or was cancelled.");

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        LightmapData[] maps = LightmapSettings.lightmaps;
        long bytes = 0;
        foreach (LightmapData map in maps)
        {
            if (map.lightmapColor != null)
                bytes += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(map.lightmapColor);
        }

        Debug.Log("[LaserTagVisuals] Baked " + maps.Length + " lightmap(s), about " +
                  (bytes / 1024) + " KB in the Editor.");
    }

    // ------------------------------------------------------------------
    // Phase B - arena look
    // ------------------------------------------------------------------

    private const string TexturesFolder = "Assets/Textures/Arena";
    private const string ArenaMaterialsFolder = "Assets/Materials/Arena";
    private const string MeshesFolder = "Assets/Meshes";

    private const string LitShader = "Universal Render Pipeline/Lit";
    private const string SimpleLitShader = "Universal Render Pipeline/Simple Lit";
    private const string UnlitShader = "Universal Render Pipeline/Unlit";

    private static Material matMetal;
    private static Material matHazard;
    private static Material neonBlue;
    private static Material neonBlueDim;
    private static Material neonOrange;
    private static Material neonWhite;

    private static void SetupArenaLook()
    {
        Directory.CreateDirectory(TexturesFolder);
        Directory.CreateDirectory(ArenaMaterialsFolder);
        Directory.CreateDirectory(MeshesFolder);

        Texture2D floorTex = GetOrCreateTexture("FloorPanels", 512, DrawFloorPanels, 4);
        Texture2D wallTex = GetOrCreateTexture("WallPanels", 512, DrawWallPanels, 2);
        Texture2D coverTex = GetOrCreateTexture("CoverCrate", 256, DrawCoverCrate, 2);
        Texture2D hazardTex = GetOrCreateTexture("HazardStripes", 128, DrawHazardStripes, 1);

        // Floor: the only Lit material, so it can show a soft metallic sheen
        // from the baked reflection probe. Everything else uses Simple Lit.
        Material floor = GetOrCreateMat("Arena_Floor", LitShader);
        SetMap(floor, floorTex, new Vector2(6f, 6f), Color.white);
        floor.SetFloat("_Metallic", 0.3f);
        floor.SetFloat("_Smoothness", 0.55f);

        Material wall = GetOrCreateMat("Arena_Wall", SimpleLitShader);
        SetMap(wall, wallTex, new Vector2(6f, 1f), Color.white);

        Material wallUpper = GetOrCreateMat("Arena_WallUpper", SimpleLitShader);
        SetMap(wallUpper, wallTex, new Vector2(6f, 0.5f), new Color(0.6f, 0.62f, 0.66f));

        Material ceiling = GetOrCreateMat("Arena_Ceiling", SimpleLitShader);
        SetMap(ceiling, wallTex, new Vector2(5f, 5f), new Color(0.45f, 0.47f, 0.5f));

        Material cover = GetOrCreateMat("Arena_Cover", SimpleLitShader);
        SetMap(cover, coverTex, Vector2.one, Color.white);

        matMetal = GetOrCreateMat("Arena_Metal", SimpleLitShader);
        SetMap(matMetal, null, Vector2.one, new Color(0.16f, 0.17f, 0.19f));

        matHazard = GetOrCreateMat("Arena_Hazard", SimpleLitShader);
        SetMap(matHazard, hazardTex, new Vector2(8f, 1f), Color.white);

        neonBlue = GetOrCreateNeon("Neon_Blue", AccentBlue);
        neonBlueDim = GetOrCreateNeon("Neon_BlueDim", AccentBlue * 0.55f);
        neonOrange = GetOrCreateNeon("Neon_Orange", AccentOrange);
        neonWhite = GetOrCreateNeon("Neon_White", new Color(0.88f, 0.94f, 1f));

        AssignMaterial("Floor", floor);
        foreach (string name in new[] { "Cube1", "Cube2", "Cube3", "Cube4" })
            AssignMaterial(name, wall);
        foreach (string name in new[] { "Cover1", "Cover2", "Cover3", "Cover4", "Cover5" })
            AssignMaterial(name, cover);

        Transform decor = FindOrCreateRoot(DecorRootName).transform;

        BuildStructure(RecreateChild(decor, "Structure"), wallUpper, ceiling);
        BuildWallTrims(RecreateChild(decor, "WallTrims"));
        BuildCoverTrims(RecreateChild(decor, "CoverTrims"));
        BuildFloorMarkings(RecreateChild(decor, "FloorMarkings"));
        BuildSigns(RecreateChild(decor, "Signs"));

        AssetDatabase.SaveAssets();
    }

    // Ceiling, upper wall band, roof beams and light fixtures.
    // Structure is static and lightmapped; it has no colliders, so it
    // cannot block shots or teleporting.
    private static void BuildStructure(Transform root, Material wallUpper, Material ceiling)
    {
        float bandHeight = CeilingY - WallTop;
        float bandY = WallTop + bandHeight / 2f;

        CreateBlock(root, "Ceiling", new Vector3(0f, CeilingY + 0.1f, 0f), new Vector3(15.5f, 0.2f, 15.5f), ceiling, true);
        CreateBlock(root, "UpperWall_N", new Vector3(0f, bandY, 7.5f), new Vector3(15.5f, bandHeight, 0.5f), wallUpper, true);
        CreateBlock(root, "UpperWall_S", new Vector3(0f, bandY, -7.5f), new Vector3(15.5f, bandHeight, 0.5f), wallUpper, true);
        CreateBlock(root, "UpperWall_E", new Vector3(7.5f, bandY, 0f), new Vector3(0.5f, bandHeight, 15.5f), wallUpper, true);
        CreateBlock(root, "UpperWall_W", new Vector3(-7.5f, bandY, 0f), new Vector3(0.5f, bandHeight, 15.5f), wallUpper, true);

        // Roof beams between the lamps.
        foreach (float x in new[] { -2.1f, 2.1f })
            CreateBlock(root, "Beam_X" + x, new Vector3(x, CeilingY - 0.2f, 0f), new Vector3(0.3f, 0.4f, 14.5f), matMetal, true);
        foreach (float z in new[] { -2.1f, 2.1f })
            CreateBlock(root, "Beam_Z" + z, new Vector3(0f, CeilingY - 0.15f, z), new Vector3(14.5f, 0.3f, 0.25f), matMetal, true);

        // Light fixtures: dark housing with a glowing panel underneath.
        for (int i = 0; i < LampPositions.Length; i++)
        {
            Vector3 p = LampPositions[i];
            CreateBlock(root, "Fixture_" + i, new Vector3(p.x, CeilingY - 0.06f, p.z), new Vector3(1.5f, 0.1f, 0.45f), matMetal, false);
            CreateStrip(root, "FixtureLight_" + i, new Vector3(p.x, CeilingY - 0.115f, p.z), new Vector3(1.35f, 0.02f, 0.32f), neonWhite);
        }
    }

    // Neon lines along the walls: base and top strips all around, vertical
    // strips every few metres, coloured corners matching the baked accents.
    private static void BuildWallTrims(Transform root)
    {
        const float t = 0.04f;
        float face = WallInner - t / 2f;
        float length = WallInner * 2f;

        // Base and top strips on the four walls.
        CreateStrip(root, "Base_N", new Vector3(0f, 0.12f, face), new Vector3(length, t, t), neonBlue);
        CreateStrip(root, "Base_S", new Vector3(0f, 0.12f, -face), new Vector3(length, t, t), neonBlue);
        CreateStrip(root, "Base_E", new Vector3(face, 0.12f, 0f), new Vector3(t, t, length), neonBlue);
        CreateStrip(root, "Base_W", new Vector3(-face, 0.12f, 0f), new Vector3(t, t, length), neonBlue);

        CreateStrip(root, "Top_N", new Vector3(0f, WallTop, face), new Vector3(length, t, t), neonWhite);
        CreateStrip(root, "Top_S", new Vector3(0f, WallTop, -face), new Vector3(length, t, t), neonWhite);
        CreateStrip(root, "Top_E", new Vector3(face, WallTop, 0f), new Vector3(t, t, length), neonWhite);
        CreateStrip(root, "Top_W", new Vector3(-face, WallTop, 0f), new Vector3(t, t, length), neonWhite);

        // Vertical strips at +-4.5 m on every wall (clear of the scoreboards
        // on the north/south walls and the signs on the east/west walls).
        float h = WallTop / 2f;
        foreach (float s in new[] { -4.5f, 4.5f })
        {
            CreateStrip(root, "Vert_N" + s, new Vector3(s, h, face), new Vector3(t, WallTop, t), neonBlueDim);
            CreateStrip(root, "Vert_S" + s, new Vector3(s, h, -face), new Vector3(t, WallTop, t), neonBlueDim);
            CreateStrip(root, "Vert_E" + s, new Vector3(face, h, s), new Vector3(t, WallTop, t), neonBlueDim);
            CreateStrip(root, "Vert_W" + s, new Vector3(-face, h, s), new Vector3(t, WallTop, t), neonBlueDim);
        }

        // Corners: blue on one diagonal, orange on the other.
        float c = WallInner - 0.05f;
        CreateStrip(root, "Corner_NE", new Vector3(c, h, c), new Vector3(0.08f, WallTop, 0.08f), neonBlue);
        CreateStrip(root, "Corner_SW", new Vector3(-c, h, -c), new Vector3(0.08f, WallTop, 0.08f), neonBlue);
        CreateStrip(root, "Corner_NW", new Vector3(-c, h, c), new Vector3(0.08f, WallTop, 0.08f), neonOrange);
        CreateStrip(root, "Corner_SE", new Vector3(c, h, -c), new Vector3(0.08f, WallTop, 0.08f), neonOrange);
    }

    // Glowing edges on every cover block plus a hazard band at the base,
    // so cover is easy to read. The centre block becomes a glowing core.
    private static void BuildCoverTrims(Transform root)
    {
        foreach (string name in new[] { "Cover1", "Cover2", "Cover3", "Cover4", "Cover5" })
        {
            GameObject cover = GameObject.Find(name);
            Collider collider = cover != null ? cover.GetComponent<Collider>() : null;
            if (collider == null)
                continue;

            Bounds b = collider.bounds;
            bool isCore = Mathf.Abs(b.center.x) < 0.5f && Mathf.Abs(b.center.z) < 0.5f;
            Material edge = isCore || b.center.x * b.center.z > 0f ? neonBlue : neonOrange;

            Transform group = new GameObject(name + "_Trim").transform;
            group.SetParent(root, false);

            const float t = 0.035f;
            const float pad = 0.01f;

            // Vertical edges.
            foreach (float x in new[] { b.min.x, b.max.x })
            foreach (float z in new[] { b.min.z, b.max.z })
                CreateStrip(group, "Edge", new Vector3(x, b.center.y, z), new Vector3(t + pad, b.size.y + pad, t + pad), edge);

            // Top edges.
            foreach (float z in new[] { b.min.z, b.max.z })
                CreateStrip(group, "Top", new Vector3(b.center.x, b.max.y, z), new Vector3(b.size.x + pad, t, t), edge);
            foreach (float x in new[] { b.min.x, b.max.x })
                CreateStrip(group, "Top", new Vector3(x, b.max.y, b.center.z), new Vector3(t, t, b.size.z + pad), edge);

            // Hazard band around the base.
            CreateBlock(group, "HazardBand", new Vector3(b.center.x, 0.09f, b.center.z),
                new Vector3(b.size.x + 0.02f, 0.18f, b.size.z + 0.02f), matHazard, false);

            if (isCore)
            {
                CreateStrip(group, "CoreBand", new Vector3(b.center.x, b.center.y, b.center.z),
                    new Vector3(b.size.x + 0.04f, 0.1f, b.size.z + 0.04f), neonBlue);

                // Halo floating well above head height.
                CreateRing(group, "CoreHalo", new Vector3(b.center.x, 3.4f, b.center.z), 0.85f, 1.0f, neonBlue, true);
            }
        }
    }

    // Floor lines, a ring around the core and five numbered spawn pads.
    private static void BuildFloorMarkings(Transform root)
    {
        const float y = 0.006f;
        float border = WallInner - 0.5f;

        CreateStrip(root, "Border_N", new Vector3(0f, y, border), new Vector3(border * 2f, 0.01f, 0.05f), neonBlueDim);
        CreateStrip(root, "Border_S", new Vector3(0f, y, -border), new Vector3(border * 2f, 0.01f, 0.05f), neonBlueDim);
        CreateStrip(root, "Border_E", new Vector3(border, y, 0f), new Vector3(0.05f, 0.01f, border * 2f), neonBlueDim);
        CreateStrip(root, "Border_W", new Vector3(-border, y, 0f), new Vector3(0.05f, 0.01f, border * 2f), neonBlueDim);

        CreateRing(root, "CoreRing", new Vector3(0f, y, 0f), 1.55f, 1.63f, neonBlue, false);

        for (int i = 1; i <= 5; i++)
        {
            GameObject spawn = GameObject.Find("SpawnPoint_" + i);
            if (spawn == null)
                continue;

            Vector3 p = spawn.transform.position;
            p.y = y;
            float yaw = spawn.transform.eulerAngles.y;

            Transform pad = new GameObject("SpawnPad_" + i).transform;
            pad.SetParent(root, false);

            CreateRing(pad, "Ring", p, 0.46f, 0.52f, neonOrange, false);
            CreateRing(pad, "InnerRing", p + new Vector3(0f, 0.001f, 0f), 0.3f, 0.32f, neonOrange, false);

            // Number on the floor in front of the pad, upright for the
            // player standing on it.
            Vector3 forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            Canvas label = CreateWorldText(pad, "Number", i.ToString("00"), new Vector2(300, 160), 0.003f, 120, AccentOrange);
            label.transform.SetPositionAndRotation(p + forward * 0.85f + Vector3.up * 0.004f, Quaternion.Euler(90f, yaw, 0f));
        }
    }

    // "VR LASER TAG" on the east and west walls (the scoreboards use the
    // north and south walls).
    private static void BuildSigns(Transform root)
    {
        float x = WallInner - 0.02f;

        foreach (int side in new[] { 1, -1 })
        {
            Transform sign = new GameObject(side > 0 ? "Sign_East" : "Sign_West").transform;
            sign.SetParent(root, false);
            sign.SetPositionAndRotation(new Vector3(side * x, 2.15f, 0f), Quaternion.Euler(0f, side * 90f, 0f));

            Canvas title = CreateWorldText(sign, "Title", "VR LASER TAG", new Vector2(1600, 260), 0.0025f, 200, AccentBlue);
            title.GetComponentInChildren<Text>().fontStyle = FontStyle.Bold;
            title.transform.localPosition = new Vector3(0f, 0.2f, 0f);
            title.transform.localRotation = Quaternion.identity;

            Canvas subtitle = CreateWorldText(sign, "Subtitle", "FREE  FOR  ALL  ARENA", new Vector2(1600, 120), 0.0025f, 80, AccentOrange);
            subtitle.transform.localPosition = new Vector3(0f, -0.3f, 0f);
            subtitle.transform.localRotation = Quaternion.identity;

            // Thin neon underline between the two lines.
            CreateStrip(sign, "Underline", Vector3.zero, new Vector3(3.2f, 0.03f, 0.02f), neonBlueDim, true);
        }
    }

    // ---- building blocks ----

    // Solid block: static, batched; lightmapped when it is big enough to matter.
    private static void CreateBlock(Transform parent, string name, Vector3 position, Vector3 scale, Material material, bool lightmapped)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.position = position;
        go.transform.localScale = scale;

        MeshRenderer renderer = go.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;

        StaticEditorFlags flags = StaticEditorFlags.BatchingStatic | StaticEditorFlags.ReflectionProbeStatic;
        if (lightmapped)
        {
            flags |= StaticEditorFlags.ContributeGI;
            renderer.receiveGI = ReceiveGI.Lightmaps;
            renderer.shadowCastingMode = ShadowCastingMode.On;
        }
        else
        {
            renderer.receiveGI = ReceiveGI.LightProbes;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        GameObjectUtility.SetStaticEditorFlags(go, flags);
    }

    // Glowing strip (Unlit): static and batched, no lighting or shadows.
    private static void CreateStrip(Transform parent, string name, Vector3 position, Vector3 scale, Material material, bool local = false)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);

        if (local)
            go.transform.localPosition = position;
        else
            go.transform.position = position;

        go.transform.localScale = scale;
        MakeUnlitStatic(go, material);
    }

    private static void CreateRing(Transform parent, string name, Vector3 position, float inner, float outer, Material material, bool faceDown)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = position;

        go.AddComponent<MeshFilter>().sharedMesh = GetOrCreateRingMesh(inner, outer, faceDown);
        go.AddComponent<MeshRenderer>();
        MakeUnlitStatic(go, material);
    }

    private static void MakeUnlitStatic(GameObject go, Material material)
    {
        MeshRenderer renderer = go.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.receiveGI = ReceiveGI.LightProbes;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
    }

    // Flat ring in the XZ plane, saved as an asset so the scene keeps it.
    private static Mesh GetOrCreateRingMesh(float inner, float outer, bool faceDown)
    {
        string name = "Ring_" + inner.ToString("0.00") + "_" + outer.ToString("0.00") + (faceDown ? "_down" : "_up");
        string path = MeshesFolder + "/" + name + ".asset";

        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (mesh != null)
            return mesh;

        const int segments = 48;
        Vector3[] vertices = new Vector3[segments * 2];
        Vector3[] normals = new Vector3[segments * 2];
        int[] triangles = new int[segments * 6];
        Vector3 normal = faceDown ? Vector3.down : Vector3.up;

        for (int i = 0; i < segments; i++)
        {
            float a = i * Mathf.PI * 2f / segments;
            Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            vertices[i * 2] = dir * inner;
            vertices[i * 2 + 1] = dir * outer;
            normals[i * 2] = normal;
            normals[i * 2 + 1] = normal;

            int next = (i + 1) % segments;
            int t = i * 6;
            int i0 = i * 2, o0 = i * 2 + 1, i1 = next * 2, o1 = next * 2 + 1;

            if (faceDown)
            {
                triangles[t] = i0; triangles[t + 1] = i1; triangles[t + 2] = o0;
                triangles[t + 3] = o0; triangles[t + 4] = i1; triangles[t + 5] = o1;
            }
            else
            {
                triangles[t] = i0; triangles[t + 1] = o0; triangles[t + 2] = i1;
                triangles[t + 3] = o0; triangles[t + 4] = o1; triangles[t + 5] = i1;
            }
        }

        mesh = new Mesh { name = name, vertices = vertices, normals = normals, triangles = triangles };
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    // Non-interactive world-space text (no raycaster, no event camera).
    private static Canvas CreateWorldText(Transform parent, string name, string content, Vector2 sizePixels, float metresPerPixel, int fontSize, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Canvas));
        go.transform.SetParent(parent, false);

        Canvas canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.sizeDelta = sizePixels;
        rect.localScale = Vector3.one * metresPerPixel;

        GameObject textObject = new GameObject("Text", typeof(RectTransform));
        textObject.transform.SetParent(go.transform, false);

        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        Text text = textObject.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = content;
        text.fontSize = fontSize;
        text.color = color;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;

        return canvas;
    }

    // ---- materials ----

    private static Material GetOrCreateMat(string name, string shaderName)
    {
        string path = ArenaMaterialsFolder + "/" + name + ".mat";
        Shader shader = Shader.Find(shaderName);
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }
        else if (material.shader != shader)
        {
            material.shader = shader;
        }

        material.enableInstancing = true;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Material GetOrCreateNeon(string name, Color color)
    {
        Material material = GetOrCreateMat(name, UnlitShader);
        material.SetColor("_BaseColor", color);
        return material;
    }

    private static void SetMap(Material material, Texture2D texture, Vector2 tiling, Color color)
    {
        material.SetTexture("_BaseMap", texture);
        material.SetTextureScale("_BaseMap", tiling);
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Smoothness", 0.25f);
    }

    private static void AssignMaterial(string objectName, Material material)
    {
        GameObject go = GameObject.Find(objectName);
        Renderer renderer = go != null ? go.GetComponent<Renderer>() : null;
        if (renderer == null)
            return;

        renderer.sharedMaterial = material;
        EditorUtility.SetDirty(renderer);
    }

    // ---- generated textures (small, tileable, saved as PNG) ----

    private delegate Color32 PixelPainter(int x, int y, int size, System.Random random);

    private static Texture2D GetOrCreateTexture(string name, int size, PixelPainter painter, int anisoLevel, bool effect = false)
    {
        string path = TexturesFolder + "/" + name + ".png";

        // Always regenerate so tweaks to the painters take effect.
        System.Random random = new System.Random(name.GetHashCode());
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color32[] pixels = new Color32[size * size];

        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
            pixels[y * size + x] = painter(x, y, size, random);

        texture.SetPixels32(pixels);
        texture.Apply();
        File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true;
        importer.mipmapEnabled = true;
        importer.wrapMode = effect ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
        importer.alphaIsTransparency = effect;
        importer.filterMode = FilterMode.Trilinear;
        importer.anisoLevel = anisoLevel;
        importer.maxTextureSize = size;
        importer.SaveAndReimport();

        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static Color32 Shade(int r, int g, int b, int noise)
    {
        return new Color32((byte)Mathf.Clamp(r + noise, 0, 255), (byte)Mathf.Clamp(g + noise, 0, 255),
                           (byte)Mathf.Clamp(b + noise, 0, 255), 255);
    }

    // Dark metal floor plates (2 x 2 plates per tile) with seams and bolts.
    private static Color32 DrawFloorPanels(int x, int y, int size, System.Random random)
    {
        int panel = size / 2;
        int px = x % panel, py = y % panel;
        int noise = random.Next(-3, 4);

        if (px < 3 || py < 3)
            return Shade(14, 15, 18, 0);
        if (px == 3 || py == 3)
            return Shade(52, 56, 63, 0);

        foreach (int bx in new[] { 14, panel - 14 })
        foreach (int by in new[] { 14, panel - 14 })
        {
            int dx = px - bx, dy = py - by;
            if (dx * dx + dy * dy <= 9)
                return Shade(64, 68, 76, noise);
        }

        // Faint diagonal tread pattern.
        int tread = ((px + py) / 6) % 6 == 0 ? 4 : 0;
        return Shade(44 + tread, 47 + tread, 54 + tread, noise);
    }

    // Wall panels: two panels per tile, horizontal seams and a vent band.
    private static Color32 DrawWallPanels(int x, int y, int size, System.Random random)
    {
        int panel = size / 2;
        int px = x % panel;
        int noise = random.Next(-3, 4);

        if (px < 3)
            return Shade(18, 19, 23, 0);
        if (px == 3)
            return Shade(66, 70, 78, 0);

        if ((y >= 96 && y < 99) || (y >= 416 && y < 419))
            return Shade(20, 21, 25, 0);
        if (y == 99 || y == 419)
            return Shade(62, 66, 74, 0);

        bool vent = y >= 30 && y < 86 && px > 36 && px < panel - 36 && (y % 8) < 3;
        if (vent)
            return Shade(22, 24, 28, 0);

        return Shade(60, 64, 72, noise);
    }

    // Crate face: light frame, darker inset panel, two ribs.
    private static Color32 DrawCoverCrate(int x, int y, int size, System.Random random)
    {
        int noise = random.Next(-3, 4);
        int edge = Mathf.Min(Mathf.Min(x, y), Mathf.Min(size - 1 - x, size - 1 - y));

        if (edge < 3)
            return Shade(24, 26, 30, 0);
        if (edge < 20)
            return Shade(100, 106, 117, noise);
        if (edge < 23)
            return Shade(20, 22, 26, 0);

        if ((y >= 84 && y < 90) || (y >= 166 && y < 172))
            return Shade(84, 89, 99, noise);

        return Shade(60, 65, 74, noise);
    }

    // Diagonal orange/dark hazard stripes.
    private static Color32 DrawHazardStripes(int x, int y, int size, System.Random random)
    {
        int noise = random.Next(-4, 5);
        bool orange = ((x + y) / 16) % 2 == 0;
        return orange ? Shade(205, 112, 32, noise) : Shade(24, 24, 27, noise);
    }

    // ------------------------------------------------------------------
    // Phase C - hands and gun
    // ------------------------------------------------------------------

    private const string PlayerPrefabPath = "Assets/Prefabs/NetworkPlayer.prefab";
    private const string ParticlesUnlitShader = "Universal Render Pipeline/Particles/Unlit";

    // The gun handle in gun space (the gun's +Z is the aiming direction).
    // Gloves are modelled around this handle.
    private static readonly Matrix4x4 HandleFrame =
        Matrix4x4.TRS(new Vector3(0f, -0.078f, -0.02f), Quaternion.Euler(20f, 0f, 0f), Vector3.one);

    // Remote avatar hands are spheres scaled 0.12; the remote gun sits
    // under them at scale 2 (0.24 in hand space).
    private const float AvatarHandScale = 0.12f;

    private static Mesh gunMesh;
    private static Mesh gunMeshAvatar;
    private static Mesh gloveRightMesh;
    private static Mesh gloveLeftMesh;
    private static Mesh gloveRightAvatar;
    private static Mesh gloveLeftAvatar;
    private static Mesh fxStarMesh;
    private static Material[] gunMaterials;
    private static Material[] gloveMaterials;
    private static Material fxGlow;
    private static Material fxBeam;

    private static void BuildHandAndGunAssets()
    {
        Directory.CreateDirectory(TexturesFolder);
        Directory.CreateDirectory(ArenaMaterialsFolder);
        Directory.CreateDirectory(MeshesFolder);

        Material neon = GetOrCreateNeon("Neon_Blue", AccentBlue);

        Material gunBody = GetOrCreateMat("Gun_Body", SimpleLitShader);
        SetMap(gunBody, null, Vector2.one, new Color(0.28f, 0.3f, 0.34f));
        Material gunPanel = GetOrCreateMat("Gun_Panel", SimpleLitShader);
        SetMap(gunPanel, null, Vector2.one, new Color(0.62f, 0.66f, 0.72f));
        Material glove = GetOrCreateMat("Glove", SimpleLitShader);
        SetMap(glove, null, Vector2.one, new Color(0.32f, 0.34f, 0.38f));

        gunMaterials = new[] { gunBody, gunPanel, neon };
        gloveMaterials = new[] { glove, neon };

        Texture2D glowTex = GetOrCreateTexture("FX_Glow", 64, DrawGlow, 0, true);
        Texture2D beamTex = GetOrCreateTexture("FX_Beam", 64, DrawBeam, 0, true);
        fxGlow = GetOrCreateAdditive("FX_Glow", glowTex, new Color(0.75f, 0.95f, 1f));
        fxBeam = GetOrCreateAdditive("FX_LaserBeam", beamTex, Color.white);

        // Gun (3 sub-meshes: body, light panels, glowing accents).
        MeshBuilder gun = new MeshBuilder(3);
        AddGun(gun);
        gunMesh = SaveMesh(gun.ToMesh("Gun_Model", 1f, false, false));
        gunMeshAvatar = gunMesh;

        // Gloves (2 sub-meshes: glove, glowing accents).
        MeshBuilder right = new MeshBuilder(2);
        AddGlove(right, true);
        gloveRightMesh = SaveMesh(right.ToMesh("Glove_Right", 1f, false, false));
        gloveRightAvatar = SaveMesh(right.ToMesh("Glove_Right_Avatar", 1f / AvatarHandScale, false, true));

        MeshBuilder left = new MeshBuilder(2);
        AddGlove(left, false);
        gloveLeftMesh = SaveMesh(left.ToMesh("Glove_Left", 1f, true, false));
        gloveLeftAvatar = SaveMesh(left.ToMesh("Glove_Left_Avatar", 1f / AvatarHandScale, true, true));

        fxStarMesh = SaveMesh(CreateStarMesh("FX_Star"));
    }

    // Low-poly sci-fi pistol in gun space: barrel along +Z, the muzzle
    // point (shot origin) is at z = 0.32 and is not moved.
    private static void AddGun(MeshBuilder b)
    {
        Matrix4x4 gun = Matrix4x4.identity;
        const int body = 0, panel = 1, glow = 2;

        b.Box(gun, new Vector3(0f, 0.005f, 0.06f), new Vector3(0.05f, 0.065f, 0.24f), Vector3.zero, body);   // receiver
        b.Box(gun, new Vector3(0f, 0.045f, 0.06f), new Vector3(0.03f, 0.016f, 0.2f), Vector3.zero, body);   // top rail
        b.Box(gun, new Vector3(0f, 0.055f, -0.04f), new Vector3(0.022f, 0.016f, 0.02f), Vector3.zero, body); // rear sight
        b.Box(gun, new Vector3(0f, 0f, 0.235f), new Vector3(0.04f, 0.045f, 0.11f), Vector3.zero, body);      // barrel shroud
        b.Box(gun, new Vector3(0f, 0f, 0.3f), new Vector3(0.028f, 0.028f, 0.022f), Vector3.zero, body);      // emitter housing
        b.Box(gun, new Vector3(0f, -0.04f, 0.045f), new Vector3(0.012f, 0.01f, 0.05f), Vector3.zero, body);  // trigger guard
        b.Box(gun, new Vector3(0f, -0.055f, 0.068f), new Vector3(0.012f, 0.03f, 0.01f), Vector3.zero, body);
        b.Box(HandleFrame, Vector3.zero, new Vector3(0.034f, 0.11f, 0.05f), Vector3.zero, body);            // grip
        b.Box(HandleFrame, new Vector3(0f, -0.058f, -0.004f), new Vector3(0.04f, 0.01f, 0.058f), Vector3.zero, body);

        foreach (float side in new[] { -1f, 1f })
        {
            b.Box(gun, new Vector3(side * 0.026f, 0.003f, 0.06f), new Vector3(0.004f, 0.04f, 0.16f), Vector3.zero, panel);
            b.Box(HandleFrame, new Vector3(side * 0.0175f, 0f, 0f), new Vector3(0.003f, 0.08f, 0.036f), Vector3.zero, panel);
            b.Box(gun, new Vector3(side * 0.0285f, 0.019f, 0.08f), new Vector3(0.002f, 0.006f, 0.12f), Vector3.zero, glow);
            b.Box(gun, new Vector3(side * 0.0205f, 0.008f, 0.235f), new Vector3(0.002f, 0.005f, 0.08f), Vector3.zero, glow);
        }

        b.Box(gun, new Vector3(0f, 0.0535f, 0.07f), new Vector3(0.006f, 0.002f, 0.15f), Vector3.zero, glow); // rail light
        b.Box(gun, new Vector3(0f, 0f, 0.262f), new Vector3(0.044f, 0.049f, 0.006f), Vector3.zero, glow);   // barrel rings
        b.Box(gun, new Vector3(0f, 0f, 0.282f), new Vector3(0.044f, 0.049f, 0.006f), Vector3.zero, glow);
        b.Box(gun, new Vector3(0f, 0f, 0.3115f), new Vector3(0.016f, 0.016f, 0.004f), Vector3.zero, glow);  // emitter
    }

    // Low-poly glove closed around the gun grip (right) or the controller
    // (left, mirrored later). Modelled in the handle frame: Y along the
    // grip, Z forward, X to the right.
    private static void AddGlove(MeshBuilder b, bool holdingGun)
    {
        Matrix4x4 h = HandleFrame;
        const int glove = 0, glow = 1;

        b.Box(h, new Vector3(0.038f, 0f, -0.002f), new Vector3(0.04f, 0.095f, 0.075f), Vector3.zero, glove);   // palm and back of hand
        b.Box(h, new Vector3(0.012f, -0.005f, -0.04f), new Vector3(0.045f, 0.085f, 0.022f), Vector3.zero, glove); // heel
        b.Box(h, new Vector3(-0.01f, 0.035f, -0.03f), new Vector3(0.03f, 0.035f, 0.03f), Vector3.zero, glove);   // thumb base

        float[] fingers = holdingGun ? new[] { 0.022f, 0f, -0.022f } : new[] { 0.044f, 0.022f, 0f, -0.022f };
        foreach (float y in fingers)
        {
            b.Box(h, new Vector3(0f, y, 0.036f), new Vector3(0.062f, 0.019f, 0.022f), Vector3.zero, glove);
            b.Box(h, new Vector3(-0.03f, y, 0.018f), new Vector3(0.02f, 0.019f, 0.034f), Vector3.zero, glove);
        }

        if (holdingGun)
        {
            // Index finger on the trigger, thumb along the left of the frame.
            b.Box(h, new Vector3(0.012f, 0.048f, 0.035f), new Vector3(0.02f, 0.019f, 0.04f), Vector3.zero, glove);
            b.Box(h, new Vector3(0f, 0.045f, 0.058f), new Vector3(0.019f, 0.018f, 0.022f), Vector3.zero, glove);
            b.Box(h, new Vector3(-0.036f, 0.05f, 0.012f), new Vector3(0.018f, 0.02f, 0.06f), new Vector3(0f, 10f, 0f), glove);
        }
        else
        {
            // Thumb resting on top of the controller.
            b.Box(h, new Vector3(-0.012f, 0.066f, 0.01f), new Vector3(0.02f, 0.018f, 0.055f), new Vector3(0f, -10f, 0f), glove);
        }

        b.Box(h, new Vector3(0.025f, -0.055f, -0.045f), new Vector3(0.07f, 0.05f, 0.07f), new Vector3(30f, 0f, 0f), glove); // cuff

        b.Box(h, new Vector3(0.0585f, 0.005f, -0.002f), new Vector3(0.002f, 0.06f, 0.012f), Vector3.zero, glow);          // back-of-hand light
        b.Box(h, new Vector3(0.025f, -0.04f, -0.037f), new Vector3(0.074f, 0.008f, 0.074f), new Vector3(30f, 0f, 0f), glow); // cuff band
    }

    private static void SetupLocalGunAndHands()
    {
        LaserGunShooting gun = Object.FindAnyObjectByType<LaserGunShooting>(FindObjectsInactive.Include);
        if (gun == null)
        {
            Debug.LogWarning("[LaserTagVisuals] LaserGunShooting not found.");
            return;
        }

        Transform gunTransform = gun.transform;

        // Keep the old box gun objects, only stop drawing them.
        foreach (string part in new[] { "GunBody", "GunHandle" })
        {
            Transform t = gunTransform.Find(part);
            Renderer r = t != null ? t.GetComponent<Renderer>() : null;
            if (r != null)
                r.enabled = false;
        }

        CreateMeshObject(RecreateChild(gunTransform, "GunModel"), gunMesh, gunMaterials);

        // Ammo screen on the back of the gun.
        Transform ammo = gunTransform.Find("AmmoDisplay");
        if (ammo != null)
        {
            ammo.localPosition = new Vector3(0f, 0.074f, -0.035f);
            ammo.localRotation = Quaternion.Euler(35f, 0f, 0f);
        }

        if (gun.laserLine != null)
            StyleBeam(gun.laserLine);

        GameObject flash = null;
        if (gun.muzzlePoint != null)
            flash = CreateFx(RecreateChild(gun.muzzlePoint, "FX"), "MuzzleFlash", 0.07f);

        GameObject spark = CreateFx(RecreateChild(gunTransform, "ImpactFX"), "ImpactSpark", 0.05f);

        LaserFX fx = GetOrAdd<LaserFX>(gunTransform.gameObject);
        SerializedObject so = new SerializedObject(fx);
        so.FindProperty("beam").objectReferenceValue = gun.laserLine;
        so.FindProperty("beamStart").objectReferenceValue = null;
        so.FindProperty("muzzleFlash").objectReferenceValue = flash;
        so.FindProperty("impactSpark").objectReferenceValue = spark;
        so.FindProperty("localPlayerGun").boolValue = true;
        so.ApplyModifiedPropertiesWithoutUndo();

        // Gloves on the tracked controllers. The old grey sphere hands
        // (AvatarLeftHand / AvatarRightHand) are no longer drawn.
        foreach (string sphere in new[] { "AvatarLeftHand", "AvatarRightHand" })
        {
            GameObject old = GameObject.Find(sphere);
            Renderer r = old != null ? old.GetComponent<Renderer>() : null;
            if (r != null)
                r.enabled = false;
        }

        Transform rightHand = gunTransform.parent;
        if (rightHand != null)
            CreateMeshObject(RecreateChild(rightHand, "RightGlove"), gloveRightMesh, gloveMaterials);

        XROrigin origin = Object.FindAnyObjectByType<XROrigin>(FindObjectsInactive.Include);
        Transform leftHand = origin != null ? FindDeep(origin.transform, "Left Hand") : null;
        if (leftHand != null)
            CreateMeshObject(RecreateChild(leftHand, "LeftGlove"), gloveLeftMesh, gloveMaterials);
        else
            Debug.LogWarning("[LaserTagVisuals] Left Hand not found.");
    }

    // Other players see the same gloves (in the player's colour) and gun.
    // Existing objects and renderer lists are kept; only meshes change.
    private static void SetupAvatarHandsAndGun()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);

        try
        {
            Transform leftHand = FindDeep(root.transform, "NetworkLeftHand");
            Transform rightHand = FindDeep(root.transform, "NetworkRightHand");
            Transform networkGun = FindDeep(root.transform, "NetworkGun");

            SetMesh(leftHand, gloveLeftAvatar);
            SetMesh(rightHand, gloveRightAvatar);

            // The right-hand sphere was hidden behind the gun; the glove
            // should be visible. (Still hidden for the owner by NetworkVRPlayer.)
            Renderer rightRenderer = rightHand != null ? rightHand.GetComponent<Renderer>() : null;
            if (rightRenderer != null)
                rightRenderer.enabled = true;

            // Tint both gloves with the player's colour.
            PlayerInfo info = root.GetComponent<PlayerInfo>();
            if (info != null)
            {
                SerializedObject infoSO = new SerializedObject(info);
                SerializedProperty list = infoSO.FindProperty("colorRenderers");
                foreach (Transform hand in new[] { leftHand, rightHand })
                {
                    Renderer r = hand != null ? hand.GetComponent<Renderer>() : null;
                    if (r != null && !ContainsObject(list, r))
                    {
                        list.arraySize++;
                        list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = r;
                    }
                }
                infoSO.ApplyModifiedPropertiesWithoutUndo();
            }

            if (networkGun != null)
            {
                float gunScale = networkGun.lossyScale.x / root.transform.lossyScale.x;

                Transform body = networkGun.Find("GunBody");
                if (body != null)
                {
                    body.localPosition = Vector3.zero;
                    body.localRotation = Quaternion.identity;
                    body.localScale = Vector3.one / gunScale;
                    SetMesh(body, gunMeshAvatar);
                    body.GetComponent<Renderer>().sharedMaterials = gunMaterials;
                }

                SetMesh(networkGun.Find("GunHandle"), null);
                SetMesh(FindDeep(root.transform, "Emitter"), null);

                // Visible gun tip for the remote beam and the effects.
                Transform tip = RecreateChild(networkGun, "VisualMuzzle");
                tip.localPosition = new Vector3(0f, 0f, 0.315f / gunScale);
                tip.localRotation = Quaternion.identity;
                tip.localScale = Vector3.one / gunScale;

                GameObject flash = CreateFx(tip, "MuzzleFlash", 0.07f);
                GameObject spark = CreateFx(tip, "ImpactSpark", 0.05f);

                Transform beamObject = FindDeep(root.transform, "RemoteBeam");
                LineRenderer beam = beamObject != null ? beamObject.GetComponent<LineRenderer>() : null;
                if (beam != null)
                    StyleBeam(beam);

                LaserFX fx = GetOrAdd<LaserFX>(networkGun.gameObject);
                SerializedObject so = new SerializedObject(fx);
                so.FindProperty("beam").objectReferenceValue = beam;
                so.FindProperty("beamStart").objectReferenceValue = tip;
                so.FindProperty("muzzleFlash").objectReferenceValue = flash;
                so.FindProperty("impactSpark").objectReferenceValue = spark;
                so.FindProperty("localPlayerGun").boolValue = false;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void StyleBeam(LineRenderer line)
    {
        line.sharedMaterial = fxBeam;
        line.widthMultiplier = 1f;
        line.widthCurve = new AnimationCurve(new Keyframe(0f, 0.022f), new Keyframe(1f, 0.01f));
        line.textureMode = LineTextureMode.Stretch;
        line.alignment = LineAlignment.View;
        line.numCapVertices = 0;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.lightProbeUsage = LightProbeUsage.Off;
        line.reflectionProbeUsage = ReflectionProbeUsage.Off;
        line.startColor = Color.white;
        line.endColor = AccentBlue;
        EditorUtility.SetDirty(line);
    }

    // Small additive "star" (three crossed quads), hidden until a shot.
    private static GameObject CreateFx(Transform parent, string name, float size)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localScale = Vector3.one * size;
        go.AddComponent<MeshFilter>().sharedMesh = fxStarMesh;

        MeshRenderer renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = fxGlow;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

        go.SetActive(false);
        return go;
    }

    private static void CreateMeshObject(Transform t, Mesh mesh, Material[] materials)
    {
        t.localPosition = Vector3.zero;
        t.localRotation = Quaternion.identity;
        t.localScale = Vector3.one;
        t.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;

        MeshRenderer renderer = t.gameObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = materials;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    private static void SetMesh(Transform t, Mesh mesh)
    {
        MeshFilter filter = t != null ? t.GetComponent<MeshFilter>() : null;
        if (filter != null)
            filter.sharedMesh = mesh;
    }

    private static bool ContainsObject(SerializedProperty list, Object value)
    {
        for (int i = 0; i < list.arraySize; i++)
        {
            if (list.GetArrayElementAtIndex(i).objectReferenceValue == value)
                return true;
        }
        return false;
    }

    private static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name)
            return root;

        foreach (Transform child in root)
        {
            Transform found = FindDeep(child, name);
            if (found != null)
                return found;
        }
        return null;
    }

    private static Material GetOrCreateAdditive(string name, Texture2D texture, Color color)
    {
        Material material = GetOrCreateMat(name, ParticlesUnlitShader);
        material.SetTexture("_BaseMap", texture);
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Surface", 1f);            // transparent
        material.SetFloat("_Blend", 2f);              // additive
        material.SetFloat("_Cull", 0f);               // both sides
        BaseShaderGUI.SetupMaterialBlendMode(material);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Mesh SaveMesh(Mesh mesh)
    {
        string path = MeshesFolder + "/" + mesh.name + ".asset";
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);

        if (existing == null)
        {
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        // Update in place so references to the asset stay valid.
        existing.Clear();
        EditorUtility.CopySerialized(mesh, existing);
        Object.DestroyImmediate(mesh);
        EditorUtility.SetDirty(existing);
        return existing;
    }

    private static Mesh CreateStarMesh(string name)
    {
        List<Vector3> vertices = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<int> triangles = new List<int>();

        Quaternion[] planes = { Quaternion.identity, Quaternion.Euler(0f, 90f, 0f), Quaternion.Euler(90f, 0f, 0f) };
        foreach (Quaternion q in planes)
        {
            int start = vertices.Count;
            vertices.Add(q * new Vector3(-0.5f, -0.5f, 0f));
            vertices.Add(q * new Vector3(0.5f, -0.5f, 0f));
            vertices.Add(q * new Vector3(0.5f, 0.5f, 0f));
            vertices.Add(q * new Vector3(-0.5f, 0.5f, 0f));
            uvs.Add(new Vector2(0f, 0f));
            uvs.Add(new Vector2(1f, 0f));
            uvs.Add(new Vector2(1f, 1f));
            uvs.Add(new Vector2(0f, 1f));
            triangles.AddRange(new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
        }

        Mesh mesh = new Mesh { name = name };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Color32 DrawGlow(int x, int y, int size, System.Random random)
    {
        float dx = (x + 0.5f) / size * 2f - 1f;
        float dy = (y + 0.5f) / size * 2f - 1f;
        float a = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy)), 2.2f);
        return new Color32(255, 255, 255, (byte)(a * 255f));
    }

    private static Color32 DrawBeam(int x, int y, int size, System.Random random)
    {
        float v = Mathf.Abs((y + 0.5f) / size * 2f - 1f);
        float a = Mathf.Pow(Mathf.Clamp01(1f - v), 2f);
        return new Color32(255, 255, 255, (byte)(a * 255f));
    }

    // Builds flat-shaded meshes from boxes (used for the gun and gloves).
    private class MeshBuilder
    {
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Vector3> normals = new List<Vector3>();
        private readonly List<int>[] triangles;

        private static readonly Vector3[] FaceNormals =
            { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };

        public MeshBuilder(int subMeshCount)
        {
            triangles = new List<int>[subMeshCount];
            for (int i = 0; i < subMeshCount; i++)
                triangles[i] = new List<int>();
        }

        public void Box(Matrix4x4 frame, Vector3 center, Vector3 size, Vector3 euler, int subMesh)
        {
            Matrix4x4 m = frame * Matrix4x4.TRS(center, Quaternion.Euler(euler), size);
            Matrix4x4 normalMatrix = m.inverse.transpose;

            foreach (Vector3 d in FaceNormals)
            {
                Vector3 u = Mathf.Abs(d.y) > 0.5f ? Vector3.right : Vector3.up;
                Vector3 w = Vector3.Cross(d, u);
                Vector3 n = normalMatrix.MultiplyVector(d).normalized;

                int start = vertices.Count;
                vertices.Add(m.MultiplyPoint3x4(d * 0.5f - u * 0.5f - w * 0.5f));
                vertices.Add(m.MultiplyPoint3x4(d * 0.5f + u * 0.5f - w * 0.5f));
                vertices.Add(m.MultiplyPoint3x4(d * 0.5f + u * 0.5f + w * 0.5f));
                vertices.Add(m.MultiplyPoint3x4(d * 0.5f - u * 0.5f + w * 0.5f));
                for (int i = 0; i < 4; i++)
                    normals.Add(n);

                triangles[subMesh].AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
            }
        }

        // scale: multiplies positions. mirrorX: left-hand copy.
        // singleSubMesh: merge everything for one-material renderers.
        public Mesh ToMesh(string name, float scale, bool mirrorX, bool singleSubMesh)
        {
            Vector3 flip = mirrorX ? new Vector3(-1f, 1f, 1f) : Vector3.one;
            List<Vector3> v = new List<Vector3>(vertices.Count);
            List<Vector3> n = new List<Vector3>(normals.Count);

            for (int i = 0; i < vertices.Count; i++)
            {
                v.Add(Vector3.Scale(vertices[i], flip) * scale);
                n.Add(Vector3.Scale(normals[i], flip));
            }

            List<List<int>> groups = new List<List<int>>();
            if (singleSubMesh)
            {
                List<int> all = new List<int>();
                foreach (List<int> t in triangles)
                    all.AddRange(t);
                groups.Add(all);
            }
            else
            {
                foreach (List<int> t in triangles)
                    groups.Add(new List<int>(t));
            }

            // Make every triangle face along its normal (fixes winding
            // after mirroring and for any face orientation).
            foreach (List<int> t in groups)
            {
                for (int i = 0; i < t.Count; i += 3)
                {
                    Vector3 face = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
                    if (Vector3.Dot(face, n[t[i]]) < 0f)
                    {
                        int swap = t[i + 1];
                        t[i + 1] = t[i + 2];
                        t[i + 2] = swap;
                    }
                }
            }

            Mesh mesh = new Mesh { name = name };
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.subMeshCount = groups.Count;
            for (int i = 0; i < groups.Count; i++)
                mesh.SetTriangles(groups[i], i);
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static Transform RecreateChild(Transform parent, string name)
    {
        Transform existing = parent.Find(name);
        if (existing != null)
            Object.DestroyImmediate(existing.gameObject);

        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    private static void SetBool(SerializedObject so, string property, bool value)
    {
        SerializedProperty p = so.FindProperty(property);
        if (p != null)
            p.boolValue = value;
        else
            Debug.LogWarning("[LaserTagVisuals] URP setting not found: " + property);
    }

    private static GameObject FindOrCreateRoot(string name)
    {
        GameObject go = GameObject.Find(name);
        return go != null ? go : new GameObject(name);
    }

    private static Transform GetOrCreateChild(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        if (child != null)
            return child;

        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    private static T GetOrAdd<T>(GameObject go) where T : Component
    {
        T component = go.GetComponent<T>();
        return component != null ? component : go.AddComponent<T>();
    }
}
