using System.Collections.Generic;
using System.IO;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;

// Fixes for the issues found in the first real headset tests.
//
// "VR Laser Tag > 6. Headset Fixes > Apply All" (or the batch method
// BatchApplyAll) applies, in order:
//
// 1. HUD: the HUD is rebuilt by LaserTagSetup.SetupHud (single source):
//    it is no longer a child of the camera, it floats 1.3 m away and follows
//    the view lazily (ComfortHud). The damage vignette moves to a small
//    head-locked overlay, which also holds the wall fade.
// 2. Walls: HeadCollisionFade (created with the HUD) blacks out the view
//    when the head is inside a wall/cover block or outside the arena.
// 3. Locomotion (teleport-only, as the course spec requires):
//    - the floor can only be selected by dedicated teleport interactors,
//    - each hand gets the XRI Starter Assets "Teleport Interactor",
//      activated by pushing the thumbstick forward and released to teleport
//      (the standard Quest gesture),
//    - the controller input managers no longer deactivate the whole Right
//      Hand (gun, ray, menu clicks) when the thumbstick is pushed forward,
//    - the right UI ray becomes a straight pointer that only draws its line
//      over menus (it was a projectile arc that teleported on grip release),
//    - the XR Origin requests a Floor tracking origin (real player height,
//      no re-centring jumps of a "Device" origin).
// 4. Player avatars: low-poly armoured suit (helmet, torso, legs) built
//    from chamfered boxes, one material with a tiny colour palette, glowing
//    accents in the player's own colour (emission only, no team uniforms).
//    The hitboxes are unchanged.
//
// Every step is safe to run again.
public static class LaserTagHeadsetFixes
{
    private const string ScenePath = "Assets/ArenaScene.unity";
    private const string PlayerPrefabPath = "Assets/Prefabs/NetworkPlayer.prefab";
    private const string StarterAssets = "Assets/Samples/XR Interaction Toolkit/3.6.1/Starter Assets";
    private const string InputActionsPath = StarterAssets + "/XRI Default Input Actions.inputactions";
    private const string TeleportInteractorPrefabPath = StarterAssets + "/Prefabs/Interactors/Teleport Interactor.prefab";
    private const string TeleportInteractorName = "Teleport Interactor";

    private const string MeshesFolder = "Assets/Meshes";
    private const string TexturesFolder = "Assets/Textures/Avatar";
    private const string MaterialsFolder = "Assets/Materials/Avatar";
    private const string PalettePath = TexturesFolder + "/Avatar_Palette.png";
    private const string EmissionMaskPath = TexturesFolder + "/Avatar_EmissionMask.png";
    private const string SuitMaterialPath = MaterialsFolder + "/Avatar_Suit.mat";

    // ------------------------------------------------------------------
    // Menu items and command-line entry points
    // ------------------------------------------------------------------

    [MenuItem("VR Laser Tag/6. Headset Fixes/Apply All")]
    public static void ApplyAll()
    {
        // 1 + 2: HUD, overlay and wall fade (rebuilds scene UI and prefab lists).
        LaserTagVisuals.ApplyHudAndLabels();

        // 3: locomotion.
        ApplyLocomotion();

        // 4: avatars.
        ApplyAvatars();

        AssetDatabase.SaveAssets();
        Debug.Log("[LaserTagHeadsetFixes] All headset fixes applied.");
    }

    [MenuItem("VR Laser Tag/6. Headset Fixes/Locomotion Only")]
    public static void ApplyLocomotion()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        FixLocomotion();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[LaserTagHeadsetFixes] Locomotion fixed.");
    }

    [MenuItem("VR Laser Tag/6. Headset Fixes/Player Avatars Only")]
    public static void ApplyAvatars()
    {
        BuildAvatarAssets();
        SetupAvatarPrefab();
        AssetDatabase.SaveAssets();
        Debug.Log("[LaserTagHeadsetFixes] Player avatars applied.");
    }

    public static void BatchApplyAll()
    {
        int code = 0;
        try
        {
            ApplyAll();
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
    // 3. Locomotion
    // ------------------------------------------------------------------

    private static void FixLocomotion()
    {
        XROrigin origin = Object.FindAnyObjectByType<XROrigin>(FindObjectsInactive.Include);
        if (origin == null)
            throw new System.Exception("XR Origin not found in " + ScenePath);

        // Standing room-scale game: the floor is the tracking origin.
        origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
        EditorUtility.SetDirty(origin);

        Transform left = FindDeep(origin.transform, "Left Hand");
        Transform right = FindDeep(origin.transform, "Right Hand");
        if (left == null || right == null)
            throw new System.Exception("Left Hand / Right Hand not found under the XR Origin.");

        InteractionLayerMask teleportLayer = InteractionLayerMask.GetMask("Teleport");
        InteractionLayerMask defaultLayer = InteractionLayerMask.GetMask("Default");
        if (teleportLayer.value == 0)
            throw new System.Exception("Interaction layer 'Teleport' is missing.");

        // The arena floor only reacts to teleport interactors.
        foreach (ValidatedTeleportationArea area in Object.FindObjectsByType<ValidatedTeleportationArea>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            area.interactionLayers = teleportLayer;
            EditorUtility.SetDirty(area);
        }

        // The hand rays are for menus only: they can no longer select the
        // floor (the right one teleported on grip release), and the right
        // ray is a straight pointer instead of a projectile arc.
        foreach (Transform hand in new[] { left, right })
        {
            XRRayInteractor ray = hand.GetComponent<XRRayInteractor>();
            if (ray == null)
                continue;

            ray.interactionLayers = defaultLayer;
            ray.lineType = XRRayInteractor.LineType.StraightLine;
            EditorUtility.SetDirty(ray);
        }

        // Draw the right pointer only while it points at a menu (valid UI
        // hit); during the match it would be a distracting line from the gun.
        XRInteractorLineVisual pointer = right.GetComponent<XRInteractorLineVisual>();
        if (pointer != null)
        {
            pointer.invalidColorGradient = Transparent();
            EditorUtility.SetDirty(pointer);
        }

        XRRayInteractor leftTeleport = EnsureTeleportInteractor(left, "XRI Left Locomotion", teleportLayer);
        XRRayInteractor rightTeleport = EnsureTeleportInteractor(right, "XRI Right Locomotion", teleportLayer);

        ConfigureControllerManager(left, leftTeleport, "XRI Left Locomotion");
        ConfigureControllerManager(right, rightTeleport, "XRI Right Locomotion");
    }

    private static XRRayInteractor EnsureTeleportInteractor(Transform hand, string locomotionMap, InteractionLayerMask teleportLayer)
    {
        Transform existing = hand.Find(TeleportInteractorName);
        GameObject go;

        if (existing != null)
        {
            go = existing.gameObject;
        }
        else
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TeleportInteractorPrefabPath);
            if (prefab == null)
                throw new System.Exception("Missing " + TeleportInteractorPrefabPath);

            go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, hand);
            go.name = TeleportInteractorName;
        }

        go.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        go.transform.localScale = Vector3.one;

        XRRayInteractor ray = go.GetComponent<XRRayInteractor>();
        ray.interactionLayers = teleportLayer;

        // Ignore avatar hitboxes and the local rig (same rule as the other rays).
        int ignore = (1 << LayerMask.NameToLayer(GameLayers.PlayerHitboxName)) |
                     (1 << LayerMask.NameToLayer(GameLayers.LocalRigName));
        ray.raycastMask = ray.raycastMask & ~ignore;

        // Select = thumbstick pushed forward ("Teleport Mode"); releasing the
        // stick ends the selection and teleports (TeleportTrigger OnSelectExited).
        InputActionReference teleportMode = FindAction(locomotionMap, "Teleport Mode");
        SerializedObject so = new SerializedObject(ray);
        so.FindProperty("m_SelectInput.m_InputSourceMode").intValue = 2; // InputActionReference
        so.FindProperty("m_SelectInput.m_InputActionReferencePerformed").objectReferenceValue = teleportMode;
        so.FindProperty("m_SelectInput.m_InputActionReferenceValue").objectReferenceValue = teleportMode;
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(ray);
        PrefabUtility.RecordPrefabInstancePropertyModifications(ray);
        PrefabUtility.RecordPrefabInstancePropertyModifications(go.transform);
        return ray;
    }

    // ControllerInputActionManager shows the teleport interactor while the
    // thumbstick is pushed forward. Its "Ray Interactor" (deactivated during
    // teleport aiming) must be empty here: the hand ray sits on the hand
    // object itself, so deactivating it hid the gun and also disabled the
    // manager, which then never re-activated the hand.
    private static void ConfigureControllerManager(Transform hand, XRRayInteractor teleport, string locomotionMap)
    {
        ControllerInputActionManager manager = hand.GetComponent<ControllerInputActionManager>();
        if (manager == null)
            manager = hand.gameObject.AddComponent<ControllerInputActionManager>();

        SerializedObject so = new SerializedObject(manager);
        so.FindProperty("m_RayInteractor").objectReferenceValue = null;
        so.FindProperty("m_NearFarInteractor").objectReferenceValue = null;
        so.FindProperty("m_TeleportInteractor").objectReferenceValue = teleport;
        so.FindProperty("m_TeleportMode").objectReferenceValue = FindAction(locomotionMap, "Teleport Mode");
        so.FindProperty("m_TeleportModeCancel").objectReferenceValue = FindAction(locomotionMap, "Teleport Mode Cancel");
        so.FindProperty("m_Turn").objectReferenceValue = null;
        so.FindProperty("m_SnapTurn").objectReferenceValue = null;
        so.FindProperty("m_Move").objectReferenceValue = null;
        so.FindProperty("m_SmoothMotionEnabled").boolValue = false;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(manager);
    }

    private static InputActionReference FindAction(string mapName, string actionName)
    {
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(InputActionsPath))
        {
            InputActionReference reference = asset as InputActionReference;
            if (reference != null && reference.action != null &&
                reference.action.actionMap.name == mapName && reference.action.name == actionName)
                return reference;
        }

        throw new System.Exception("Input action " + mapName + "/" + actionName + " not found.");
    }

    private static Gradient Transparent()
    {
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0f, 1f) });
        return gradient;
    }

    // ------------------------------------------------------------------
    // 4. Player avatars
    // ------------------------------------------------------------------

    // Palette entries (one texel each). All suit parts share one material;
    // each box picks its colour through its UV.
    private const int Armor = 0, ArmorShade = 1, Undersuit = 2, Visor = 3, Accent = 4, Metal = 5;
    private static readonly Color32[] Palette =
    {
        new Color32(212, 217, 226, 255), // armour (light ceramic)
        new Color32(118, 126, 140, 255), // armour shade / frames
        new Color32(28, 31, 37, 255),    // undersuit
        new Color32(10, 12, 18, 255),    // visor glass
        new Color32(30, 30, 34, 255),    // accent (lit colour; the glow is emission)
        new Color32(70, 76, 86, 255),    // metal
        new Color32(0, 0, 0, 255),
        new Color32(0, 0, 0, 255),
    };

    // Head and avatar root sizes from the prefab.
    private const float HeadScale = 0.3f;

    private static Mesh helmetMesh, torsoMesh, legsMesh;
    private static Material suitMaterial;

    private static void BuildAvatarAssets()
    {
        Directory.CreateDirectory(TexturesFolder);
        Directory.CreateDirectory(MaterialsFolder);
        Directory.CreateDirectory(MeshesFolder);

        Texture2D palette = SavePaletteTexture(PalettePath, i => Palette[i]);
        Texture2D mask = SavePaletteTexture(EmissionMaskPath, i => i == Accent ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 255));

        suitMaterial = AssetDatabase.LoadAssetAtPath<Material>(SuitMaterialPath);
        if (suitMaterial == null)
        {
            suitMaterial = new Material(Shader.Find("Universal Render Pipeline/Simple Lit"));
            AssetDatabase.CreateAsset(suitMaterial, SuitMaterialPath);
        }

        Shader simpleLit = Shader.Find("Universal Render Pipeline/Simple Lit");
        if (suitMaterial.shader != simpleLit)
            suitMaterial.shader = simpleLit;
        suitMaterial.SetTexture("_BaseMap", palette);
        suitMaterial.SetColor("_BaseColor", Color.white);
        suitMaterial.SetColor("_SpecColor", new Color(0.32f, 0.32f, 0.34f));
        suitMaterial.SetFloat("_Smoothness", 0.55f);
        suitMaterial.SetFloat("_SpecularHighlights", 0f); // 0 = SpecularTextureAndColor (on)
        suitMaterial.SetFloat("_SmoothnessSource", 0f);
        suitMaterial.SetTexture("_EmissionMap", mask);
        suitMaterial.SetColor("_EmissionColor", Color.white); // replaced by the player colour at runtime
        // URP enables _EMISSION only when the material is flagged emissive.
        // The avatars are dynamic objects, so this adds nothing to any bake.
        suitMaterial.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        suitMaterial.enableInstancing = false;

        // Let URP derive the keywords (_EMISSION, _SPECULAR_COLOR) the same way
        // its material inspector and import validation do, so they persist.
        BaseShaderGUI.SetMaterialKeywords(suitMaterial,
            UnityEditor.Rendering.Universal.ShaderGUI.SimpleLitGUI.SetMaterialKeywords);
        EditorUtility.SetDirty(suitMaterial);
        AssetDatabase.SaveAssetIfDirty(suitMaterial);

        Debug.Log("[LaserTagHeadsetFixes] Suit material keywords: " + string.Join(",", suitMaterial.shaderKeywords));

        AvatarMesh helmet = new AvatarMesh();
        BuildHelmet(helmet);
        helmetMesh = SaveMesh(helmet.ToMesh("Avatar_Helmet", 1f / HeadScale));

        AvatarMesh torso = new AvatarMesh();
        BuildTorso(torso);
        torsoMesh = SaveMesh(torso.ToMesh("Avatar_Torso", 1f));

        AvatarMesh legs = new AvatarMesh();
        BuildLegs(legs);
        legsMesh = SaveMesh(legs.ToMesh("Avatar_Legs", 1f));
    }

    // Helmet in head space: origin at the eyes, +Z = view direction (metres).
    private static void BuildHelmet(AvatarMesh m)
    {
        m.Box(new Vector3(0f, 0.02f, -0.015f), new Vector3(0.235f, 0.25f, 0.27f), 0.05f, Armor);      // shell
        m.Box(new Vector3(0f, 0.148f, -0.025f), new Vector3(0.05f, 0.022f, 0.21f), 0.008f, Accent);    // crown light
        m.Box(new Vector3(0f, 0.005f, 0.105f), new Vector3(0.214f, 0.125f, 0.08f), 0.022f, ArmorShade); // visor frame
        m.Box(new Vector3(0f, 0.012f, 0.143f), new Vector3(0.188f, 0.072f, 0.02f), 0.008f, Visor);     // visor glass
        m.Box(new Vector3(0f, 0.056f, 0.142f), new Vector3(0.17f, 0.009f, 0.014f), 0.003f, Accent);    // visor rim light
        m.Box(new Vector3(0f, -0.088f, 0.075f), new Vector3(0.165f, 0.05f, 0.1f), 0.018f, Armor);      // jaw guard
        m.Box(new Vector3(0f, -0.075f, -0.115f), new Vector3(0.18f, 0.07f, 0.06f), 0.02f, ArmorShade); // neck guard

        foreach (float s in new[] { -1f, 1f })
        {
            m.Box(new Vector3(s * 0.124f, -0.005f, -0.01f), new Vector3(0.035f, 0.105f, 0.115f), 0.012f, ArmorShade); // ear pod
            m.Box(new Vector3(s * 0.1425f, -0.005f, -0.01f), new Vector3(0.006f, 0.045f, 0.045f), 0.002f, Accent);    // ear light
            m.Box(new Vector3(s * 0.09f, -0.06f, 0.125f), new Vector3(0.02f, 0.012f, 0.03f), 0.004f, Metal);          // breather
        }
    }

    // Torso in body space: origin at the base of the neck, +Z = forward,
    // the torso hangs down to the hips at y = -0.58.
    private static void BuildTorso(AvatarMesh m)
    {
        m.Box(new Vector3(0f, 0.045f, 0f), new Vector3(0.095f, 0.11f, 0.095f), 0.02f, Undersuit);                          // neck
        m.Box(new Vector3(0f, -0.15f, 0.005f), new Vector3(0.38f, 0.27f, 0.22f), 0.05f, Armor, new Vector2(0.84f, 0.9f)); // chest plate
        m.Box(new Vector3(0f, -0.13f, 0.112f), new Vector3(0.06f, 0.06f, 0.012f), 0.006f, Accent, Vector2.one, new Vector3(0f, 0f, 45f)); // chest core
        m.Box(new Vector3(0f, -0.13f, 0.106f), new Vector3(0.1f, 0.1f, 0.012f), 0.01f, Metal, Vector2.one, new Vector3(0f, 0f, 45f));    // core housing
        m.Box(new Vector3(0f, -0.35f, 0f), new Vector3(0.285f, 0.2f, 0.17f), 0.035f, Undersuit, new Vector2(0.9f, 0.95f)); // abdomen
        m.Box(new Vector3(0f, -0.335f, 0.07f), new Vector3(0.15f, 0.15f, 0.05f), 0.02f, ArmorShade);                     // ab plate
        m.Box(new Vector3(0f, -0.47f, 0f), new Vector3(0.32f, 0.06f, 0.2f), 0.015f, Metal);                               // belt
        m.Box(new Vector3(0f, -0.47f, 0.101f), new Vector3(0.07f, 0.026f, 0.01f), 0.004f, Accent);                        // belt light
        m.Box(new Vector3(0f, -0.54f, 0f), new Vector3(0.3f, 0.1f, 0.18f), 0.03f, Undersuit);                             // pelvis
        m.Box(new Vector3(0f, -0.2f, -0.145f), new Vector3(0.24f, 0.27f, 0.08f), 0.025f, ArmorShade);                    // power pack
        m.Box(new Vector3(0f, -0.15f, -0.187f), new Vector3(0.13f, 0.012f, 0.008f), 0.003f, Accent);                      // pack lights
        m.Box(new Vector3(0f, -0.19f, -0.187f), new Vector3(0.13f, 0.012f, 0.008f), 0.003f, Accent);

        foreach (float s in new[] { -1f, 1f })
        {
            Vector3 tilt = new Vector3(0f, 0f, s * -16f);
            m.Box(new Vector3(s * 0.235f, -0.055f, 0f), new Vector3(0.14f, 0.085f, 0.175f), 0.03f, Armor, Vector2.one, tilt);  // shoulder pad
            m.Box(new Vector3(s * 0.245f, -0.013f, 0f), new Vector3(0.08f, 0.008f, 0.12f), 0.003f, Accent, Vector2.one, tilt); // shoulder stripe
            m.Box(new Vector3(s * 0.225f, -0.115f, 0f), new Vector3(0.085f, 0.06f, 0.1f), 0.025f, Undersuit);                  // deltoid
            m.Box(new Vector3(s * 0.12f, -0.07f, 0.112f), new Vector3(0.09f, 0.012f, 0.01f), 0.003f, ArmorShade);              // chest seam
        }
    }

    // Legs in hip space: origin at the hips, +Z = forward. Modelled for a
    // hip height of 0.86 m; AvatarBodyVisual stretches them in Y.
    private static void BuildLegs(AvatarMesh m)
    {
        foreach (float s in new[] { -1f, 1f })
        {
            float x = s * 0.095f;
            m.Box(new Vector3(x, -0.2f, 0f), new Vector3(0.145f, 0.38f, 0.155f), 0.035f, Undersuit, new Vector2(0.82f, 0.85f)); // thigh
            m.Box(new Vector3(x, -0.17f, 0.068f), new Vector3(0.11f, 0.24f, 0.04f), 0.015f, Armor, new Vector2(0.85f, 1f));      // thigh plate
            m.Box(new Vector3(x + s * 0.068f, -0.17f, 0f), new Vector3(0.012f, 0.16f, 0.05f), 0.004f, Accent);                    // side light
            m.Box(new Vector3(x, -0.42f, 0.055f), new Vector3(0.11f, 0.1f, 0.07f), 0.025f, ArmorShade);                           // knee pad
            m.Box(new Vector3(x, -0.62f, 0f), new Vector3(0.12f, 0.33f, 0.13f), 0.03f, Armor, new Vector2(0.88f, 0.9f));          // shin
            m.Box(new Vector3(x, -0.82f, 0.035f), new Vector3(0.13f, 0.08f, 0.25f), 0.025f, Metal);                               // boot
            m.Box(new Vector3(x, -0.785f, 0.15f), new Vector3(0.09f, 0.01f, 0.012f), 0.003f, Accent);                             // boot light
        }
    }

    private static void SetupAvatarPrefab()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);

        try
        {
            Transform head = FindDeep(root.transform, "NetworkHead");
            Transform body = FindDeep(root.transform, "PlayerBody");
            if (head == null || body == null)
                throw new System.Exception("NetworkHead / PlayerBody not found in the player prefab.");

            // The capsule and sphere stay as hitboxes (colliders, synced
            // sizes); only their placeholder meshes are no longer drawn.
            foreach (Transform t in new[] { head, body, head.Find("Visor") })
            {
                Renderer r = t != null ? t.GetComponent<Renderer>() : null;
                if (r != null)
                    r.enabled = false;
            }

            Renderer helmet = CreatePart(head, "Helmet", helmetMesh);
            Renderer torso = CreatePart(root.transform, "AvatarTorso", torsoMesh);
            Renderer legs = CreatePart(root.transform, "AvatarLegs", legsMesh);
            torso.transform.localPosition = new Vector3(0f, 1.4f, -0.07f);
            legs.transform.localPosition = new Vector3(0f, 0.82f, -0.07f);

            AvatarBodyVisual bodyVisual = root.GetComponent<AvatarBodyVisual>();
            if (bodyVisual == null)
                bodyVisual = root.AddComponent<AvatarBodyVisual>();
            bodyVisual.head = head;
            bodyVisual.torso = torso.transform;
            bodyVisual.legs = legs.transform;
            bodyVisual.visibilityRenderer = torso;
            EditorUtility.SetDirty(bodyVisual);

            Renderer[] parts = { helmet, torso, legs };

            // Owner: hidden (the player never sees their own suit from inside).
            NetworkVRPlayer vrPlayer = root.GetComponent<NetworkVRPlayer>();
            SerializedObject vrSO = new SerializedObject(vrPlayer);
            AddUnique(vrSO.FindProperty("localHiddenRenderers"), parts);
            vrSO.ApplyModifiedPropertiesWithoutUndo();

            // Hidden while eliminated, like the rest of the avatar.
            PlayerCombat combat = root.GetComponent<PlayerCombat>();
            SerializedObject combatSO = new SerializedObject(combat);
            AddUnique(combatSO.FindProperty("avatarRenderers"), parts);
            combatSO.ApplyModifiedPropertiesWithoutUndo();

            // Glowing accents in the player's own colour.
            PlayerInfo info = root.GetComponent<PlayerInfo>();
            SerializedObject infoSO = new SerializedObject(info);
            SerializedProperty accents = infoSO.FindProperty("accentRenderers");
            accents.arraySize = 0;
            AddUnique(accents, parts);
            infoSO.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static Renderer CreatePart(Transform parent, string name, Mesh mesh)
    {
        Transform existing = parent.Find(name);
        if (existing != null)
            Object.DestroyImmediate(existing.gameObject);

        GameObject go = new GameObject(name);
        go.layer = 0;
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;

        MeshRenderer renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = suitMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.BlendProbes;   // lit by the baked arena light
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        return renderer;
    }

    private static void AddUnique(SerializedProperty list, Renderer[] values)
    {
        foreach (Renderer value in values)
        {
            bool found = false;
            for (int i = 0; i < list.arraySize; i++)
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == value)
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                list.arraySize++;
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = value;
            }
        }

        // Drop entries whose objects were replaced when re-running.
        for (int i = list.arraySize - 1; i >= 0; i--)
        {
            if (list.GetArrayElementAtIndex(i).objectReferenceValue == null)
                list.DeleteArrayElementAtIndex(i);
        }
    }

    private static Texture2D SavePaletteTexture(string path, System.Func<int, Color32> colorOf)
    {
        Texture2D texture = new Texture2D(Palette.Length, 1, TextureFormat.RGBA32, false);
        for (int i = 0; i < Palette.Length; i++)
            texture.SetPixel(i, 0, colorOf(i));
        texture.Apply();
        File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.DestroyImmediate(texture);

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Point;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.SaveAndReimport();

        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
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

        existing.Clear();
        EditorUtility.CopySerialized(mesh, existing);
        Object.DestroyImmediate(mesh);
        EditorUtility.SetDirty(existing);
        return existing;
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

    // Flat-shaded low-poly mesh made of chamfered (bevelled) boxes. Each
    // box takes one palette colour through its UV. Optional taper scales
    // the box's bottom face in X/Z (wider chest, narrower waist).
    private class AvatarMesh
    {
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Vector3> normals = new List<Vector3>();
        private readonly List<Vector2> uvs = new List<Vector2>();
        private readonly List<int> triangles = new List<int>();

        public void Box(Vector3 center, Vector3 size, float chamfer, int color)
        {
            Box(center, size, chamfer, color, Vector2.one, Vector3.zero);
        }

        public void Box(Vector3 center, Vector3 size, float chamfer, int color, Vector2 bottomScale)
        {
            Box(center, size, chamfer, color, bottomScale, Vector3.zero);
        }

        public void Box(Vector3 center, Vector3 size, float chamfer, int color, Vector2 bottomScale, Vector3 euler)
        {
            Vector3 h = size * 0.5f;
            float c = Mathf.Min(chamfer, Mathf.Min(h.x, Mathf.Min(h.y, h.z)) * 0.95f);
            Quaternion rotation = Quaternion.Euler(euler);
            Vector2 uv = new Vector2((color + 0.5f) / Palette.Length, 0.5f);

            // Three points per corner: one on each of the corner's faces.
            Vector3 P(int sx, int sy, int sz, int axis)
            {
                Vector3 p = new Vector3(
                    sx * (axis == 0 ? h.x : h.x - c),
                    sy * (axis == 1 ? h.y : h.y - c),
                    sz * (axis == 2 ? h.z : h.z - c));

                // Taper: scale X/Z towards the bottom of the box.
                float t = Mathf.InverseLerp(h.y, -h.y, p.y);
                p.x *= Mathf.Lerp(1f, bottomScale.x, t);
                p.z *= Mathf.Lerp(1f, bottomScale.y, t);

                return center + rotation * p;
            }

            Vector3 worldCenter = center;

            // Main faces.
            for (int axis = 0; axis < 3; axis++)
            {
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    Vector3 a, b, d, e;
                    if (axis == 0)
                    {
                        a = P(sign, -1, -1, 0); b = P(sign, 1, -1, 0); d = P(sign, 1, 1, 0); e = P(sign, -1, 1, 0);
                    }
                    else if (axis == 1)
                    {
                        a = P(-1, sign, -1, 1); b = P(1, sign, -1, 1); d = P(1, sign, 1, 1); e = P(-1, sign, 1, 1);
                    }
                    else
                    {
                        a = P(-1, -1, sign, 2); b = P(1, -1, sign, 2); d = P(1, 1, sign, 2); e = P(-1, 1, sign, 2);
                    }
                    Quad(a, b, d, e, worldCenter, uv);
                }
            }

            // Chamfer strips along the 12 edges.
            for (int s1 = -1; s1 <= 1; s1 += 2)
            {
                for (int s2 = -1; s2 <= 1; s2 += 2)
                {
                    // Edges along Z (between X and Y faces).
                    Quad(P(s1, s2, -1, 0), P(s1, s2, 1, 0), P(s1, s2, 1, 1), P(s1, s2, -1, 1), worldCenter, uv);
                    // Edges along X (between Y and Z faces).
                    Quad(P(-1, s1, s2, 1), P(1, s1, s2, 1), P(1, s1, s2, 2), P(-1, s1, s2, 2), worldCenter, uv);
                    // Edges along Y (between X and Z faces).
                    Quad(P(s1, -1, s2, 0), P(s1, 1, s2, 0), P(s1, 1, s2, 2), P(s1, -1, s2, 2), worldCenter, uv);
                }
            }

            // Corner triangles.
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sy = -1; sy <= 1; sy += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                        Triangle(P(sx, sy, sz, 0), P(sx, sy, sz, 1), P(sx, sy, sz, 2), worldCenter, uv);
        }

        private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 inside, Vector2 uv)
        {
            Triangle(a, b, c, inside, uv);
            Triangle(a, c, d, inside, uv);
        }

        // Flat-shaded triangle, wound to face away from the box centre.
        private void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 inside, Vector2 uv)
        {
            Vector3 normal = Vector3.Cross(b - a, c - a);
            if (normal.sqrMagnitude < 1e-12f)
                return;

            if (Vector3.Dot(normal, (a + b + c) / 3f - inside) < 0f)
            {
                Vector3 swap = b;
                b = c;
                c = swap;
                normal = -normal;
            }

            normal.Normalize();
            int start = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            for (int i = 0; i < 3; i++)
            {
                normals.Add(normal);
                uvs.Add(uv);
            }
            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
        }

        public Mesh ToMesh(string name, float scale)
        {
            List<Vector3> scaled = new List<Vector3>(vertices.Count);
            foreach (Vector3 v in vertices)
                scaled.Add(v * scale);

            Mesh mesh = new Mesh { name = name };
            mesh.SetVertices(scaled);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(false);
            return mesh;
        }
    }
}
