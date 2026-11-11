using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Visual redesign of the arena (look only - no gameplay changes).
//
// "VR Laser Tag > 5. Visual Redesign > A. Lighting" switches the arena to
// baked lighting: the real-time sun and its shadows are replaced by
// lightmaps, light probes (for players and the gun) and one baked
// reflection probe. Baked light costs nothing per frame on the Quest.
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

        CreateBakedLight(root, "Lamp_Center", new Vector3(0f, 3.4f, 0f), CoolWhite, 7f, 9f);
        CreateBakedLight(root, "Lamp_NE", new Vector3(4.2f, 3.2f, 4.2f), CoolWhite, 4.5f, 6f);
        CreateBakedLight(root, "Lamp_NW", new Vector3(-4.2f, 3.2f, 4.2f), CoolWhite, 4.5f, 6f);
        CreateBakedLight(root, "Lamp_SE", new Vector3(4.2f, 3.2f, -4.2f), CoolWhite, 4.5f, 6f);
        CreateBakedLight(root, "Lamp_SW", new Vector3(-4.2f, 3.2f, -4.2f), CoolWhite, 4.5f, 6f);

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
    // Helpers
    // ------------------------------------------------------------------

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
