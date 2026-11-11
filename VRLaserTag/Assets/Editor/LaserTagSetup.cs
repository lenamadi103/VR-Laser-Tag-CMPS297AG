using System.Collections.Generic;
using System.IO;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.UI;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Interactions;
using UnityEngine.XR.OpenXR.Features.MetaQuestSupport;

// One-click project setup for the multiplayer laser tag game.
//
// "VR Laser Tag > 1. Setup Scene and Prefab" integrates the gameplay
// scripts into ArenaScene and NetworkPlayer.prefab (layers, hitboxes,
// match manager, audio, VR menu, HUD, scoreboards, teleport rules).
//
// "VR Laser Tag > 2. Configure Meta Quest" sets the Android player
// settings, XR Plug-in Management (OpenXR) and Quest features.
//
// Every step is safe to run again: existing objects are reused.
public static class LaserTagSetup
{
    private const string ScenePath = "Assets/ArenaScene.unity";
    private const string PlayerPrefabPath = "Assets/Prefabs/NetworkPlayer.prefab";
    private const string AudioFolder = "Assets/Audio";
    private const string MaterialsFolder = "Assets/Materials";
    private const string LaserMaterialPath = "Assets/LaserMaterial.mat";
    private const string MobileRPAssetPath = "Assets/Settings/Mobile_RPAsset.asset";

    private const int PlayerHitboxLayerIndex = 6;
    private const int LocalRigLayerIndex = 7;

    // ------------------------------------------------------------------
    // Menu items
    // ------------------------------------------------------------------

    [MenuItem("VR Laser Tag/1. Setup Scene and Prefab")]
    public static void SetupSceneAndPrefab()
    {
        SetupLayers();
        SetupPlayerPrefab();
        SetupScene();
        AssetDatabase.SaveAssets();
        Debug.Log("[LaserTagSetup] Scene and prefab setup complete.");
    }

    [MenuItem("VR Laser Tag/2. Configure Meta Quest (Android + OpenXR)")]
    public static void ConfigureQuest()
    {
        ConfigureAndroidPlayerSettings();
        ConfigureXRManagement();
        ConfigureOpenXRFeatures();
        ConfigureMobileRendering();
        AssetDatabase.SaveAssets();
        Debug.Log("[LaserTagSetup] Meta Quest configuration complete.");
    }

    [MenuItem("VR Laser Tag/3. Build Quest APK")]
    public static void BuildQuestApk()
    {
        Build(BuildTarget.Android, "Builds/Quest/VRLaserTag.apk");
    }

    [MenuItem("VR Laser Tag/4. Build Windows Test Version")]
    public static void BuildWindows()
    {
        Build(BuildTarget.StandaloneWindows64, "Builds/Windows/VRLaserTag.exe");
    }

    // Command-line entry points (Unity -executeMethod ...).
    public static void BatchSetup()
    {
        RunAndExit(() =>
        {
            SetupSceneAndPrefab();
            ConfigureQuest();
        });
    }

    public static void BatchBuildQuest()
    {
        RunAndExit(BuildQuestApk);
    }

    public static void BatchBuildWindows()
    {
        RunAndExit(BuildWindows);
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

    private static void Build(BuildTarget target, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = path,
            target = target,
            targetGroup = BuildPipeline.GetBuildTargetGroup(target),
            options = BuildOptions.None,
        };

        var report = BuildPipeline.BuildPlayer(options);
        Debug.Log("[LaserTagSetup] Build " + target + ": " + report.summary.result +
                  " (" + report.summary.totalErrors + " errors) -> " + path);

        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            throw new BuildFailedException("Build failed: " + report.summary.result);
    }

    // ------------------------------------------------------------------
    // Layers and physics
    // ------------------------------------------------------------------

    private static void SetupLayers()
    {
        SerializedObject tagManager = new SerializedObject(
            AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);

        SerializedProperty layers = tagManager.FindProperty("layers");
        EnsureLayer(layers, PlayerHitboxLayerIndex, GameLayers.PlayerHitboxName);
        EnsureLayer(layers, LocalRigLayerIndex, GameLayers.LocalRigName);
        tagManager.ApplyModifiedPropertiesWithoutUndo();

        int hitbox = LayerMask.NameToLayer(GameLayers.PlayerHitboxName);
        int rig = LayerMask.NameToLayer(GameLayers.LocalRigName);

        // Hitboxes are only for shot raycasts: they never collide physically.
        for (int i = 0; i < 32; i++)
            Physics.IgnoreLayerCollision(hitbox, i, true);

        // The local rig still collides with walls (Default), not with hitboxes.
        Physics.IgnoreLayerCollision(rig, hitbox, true);

        Object dynamicsManager = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/DynamicsManager.asset")[0];
        EditorUtility.SetDirty(dynamicsManager);
        EditorUtility.SetDirty(tagManager.targetObject);
    }

    private static void EnsureLayer(SerializedProperty layers, int index, string name)
    {
        for (int i = 0; i < layers.arraySize; i++)
        {
            if (layers.GetArrayElementAtIndex(i).stringValue == name)
                return;
        }

        SerializedProperty slot = layers.GetArrayElementAtIndex(index);

        if (!string.IsNullOrEmpty(slot.stringValue))
            Debug.LogWarning("[LaserTagSetup] Layer " + index + " was '" + slot.stringValue + "', renaming to " + name);

        slot.stringValue = name;
    }

    // ------------------------------------------------------------------
    // NetworkPlayer prefab
    // ------------------------------------------------------------------

    private static void SetupPlayerPrefab()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);

        try
        {
            int hitboxLayer = LayerMask.NameToLayer(GameLayers.PlayerHitboxName);

            Transform body = root.transform.Find("PlayerBody");
            Transform head = root.transform.Find("NetworkHead");
            Transform leftHand = root.transform.Find("NetworkLeftHand");
            Transform rightHand = root.transform.Find("NetworkRightHand");
            Transform gun = rightHand.Find("NetworkGun");
            Transform gunBody = gun.Find("GunBody");

            // Hitboxes: body capsule (already present) and head sphere.
            body.gameObject.layer = hitboxLayer;
            head.gameObject.layer = hitboxLayer;

            if (head.GetComponent<SphereCollider>() == null)
                head.gameObject.AddComponent<SphereCollider>();

            // Hands and gun should not block shots aimed at the body.
            RemoveCollider(leftHand);
            RemoveCollider(rightHand);
            foreach (Collider c in gun.GetComponentsInChildren<Collider>(true))
                Object.DestroyImmediate(c);

            // Visor so other players can see where someone is looking.
            Transform visor = head.Find("Visor");
            if (visor == null)
            {
                visor = CreatePrimitiveChild(head, "Visor", PrimitiveType.Cube,
                    new Vector3(0f, 0.08f, 0.38f), new Vector3(0.8f, 0.3f, 0.35f));
                visor.GetComponent<Renderer>().sharedMaterial =
                    GetOrCreateMaterial("AvatarVisor", new Color(0.08f, 0.08f, 0.1f), 0.8f);
            }
            visor.gameObject.layer = 0;

            // Glowing emitter at the front of the networked gun.
            Transform emitter = gun.Find("Emitter");
            if (emitter == null)
            {
                emitter = CreatePrimitiveChild(gun, "Emitter", PrimitiveType.Cylinder,
                    Vector3.zero, Vector3.one);
                emitter.position = gunBody.TransformPoint(new Vector3(0f, 0f, 0.5f));
                emitter.rotation = gunBody.rotation * Quaternion.Euler(90f, 0f, 0f);
                emitter.localScale = new Vector3(0.05f, 0.02f, 0.05f);
                emitter.GetComponent<Renderer>().sharedMaterial =
                    AssetDatabase.LoadAssetAtPath<Material>(LaserMaterialPath);
            }

            // Muzzle point used for remote beams.
            Transform muzzle = gun.Find("Muzzle");
            if (muzzle == null)
            {
                muzzle = new GameObject("Muzzle").transform;
                muzzle.SetParent(gun, false);
            }
            muzzle.position = gunBody.TransformPoint(new Vector3(0f, 0f, 0.6f));
            muzzle.rotation = gunBody.rotation;

            // Beam drawn when other players see this player shoot.
            Transform beamTransform = root.transform.Find("RemoteBeam");
            if (beamTransform == null)
            {
                beamTransform = new GameObject("RemoteBeam").transform;
                beamTransform.SetParent(root.transform, false);
            }

            LineRenderer beam = beamTransform.GetComponent<LineRenderer>();
            if (beam == null)
                beam = beamTransform.gameObject.AddComponent<LineRenderer>();

            beam.useWorldSpace = true;
            beam.positionCount = 2;
            beam.startWidth = 0.012f;
            beam.endWidth = 0.012f;
            beam.shadowCastingMode = ShadowCastingMode.Off;
            beam.receiveShadows = false;
            beam.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(LaserMaterialPath);
            beam.enabled = false;

            // Gameplay components.
            PlayerInfo info = GetOrAdd<PlayerInfo>(root);
            PlayerCombat combat = GetOrAdd<PlayerCombat>(root);

            List<Renderer> avatarRenderers = new List<Renderer>();
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!(r is LineRenderer))
                    avatarRenderers.Add(r);
            }

            SerializedObject combatSO = new SerializedObject(combat);
            combatSO.FindProperty("networkHead").objectReferenceValue = head;
            combatSO.FindProperty("gunMuzzle").objectReferenceValue = muzzle;
            combatSO.FindProperty("remoteBeam").objectReferenceValue = beam;
            SetObjectArray(combatSO.FindProperty("avatarRenderers"), avatarRenderers.ToArray());
            combatSO.ApplyModifiedPropertiesWithoutUndo();

            SerializedObject infoSO = new SerializedObject(info);
            SetObjectArray(infoSO.FindProperty("colorRenderers"), new Object[]
            {
                body.GetComponent<Renderer>(),
                head.GetComponent<Renderer>(),
                leftHand.GetComponent<Renderer>(),
            });
            infoSO.ApplyModifiedPropertiesWithoutUndo();

            // The owner must not see their own visor or gun emitter.
            NetworkVRPlayer vrPlayer = root.GetComponent<NetworkVRPlayer>();
            SerializedObject vrSO = new SerializedObject(vrPlayer);
            SerializedProperty hidden = vrSO.FindProperty("localHiddenRenderers");
            AddToObjectArray(hidden, visor.GetComponent<Renderer>());
            AddToObjectArray(hidden, emitter.GetComponent<Renderer>());
            foreach (Renderer r in gun.GetComponentsInChildren<Renderer>(true))
                AddToObjectArray(hidden, r);
            vrSO.ApplyModifiedPropertiesWithoutUndo();

            // The root keeps an identity rotation and scale: sync position only.
            NetworkTransform networkTransform = root.GetComponent<NetworkTransform>();
            if (networkTransform != null)
            {
                networkTransform.SyncRotAngleX = false;
                networkTransform.SyncRotAngleY = false;
                networkTransform.SyncRotAngleZ = false;
                networkTransform.SyncScaleX = false;
                networkTransform.SyncScaleY = false;
                networkTransform.SyncScaleZ = false;
            }

            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // ------------------------------------------------------------------
    // ArenaScene
    // ------------------------------------------------------------------

    private static void SetupScene()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        XROrigin origin = Object.FindAnyObjectByType<XROrigin>(FindObjectsInactive.Include);
        Camera headCamera = origin != null ? origin.Camera : Camera.main;

        SetupNetworkManager();
        SetupMatchManager();
        SetupGameAudio();
        SetupLocalRig(origin);
        SetupTeleportArea();
        SetupEventSystem();
        SetupArena();
        SetupLocalGun(origin);
        SetupMenu(headCamera);
        SetupHud(headCamera);
        SetupScoreboards(headCamera);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private static void SetupNetworkManager()
    {
        NetworkManager networkManager = Object.FindAnyObjectByType<NetworkManager>(FindObjectsInactive.Include);

        GetOrAdd<LanConnectionManager>(networkManager.gameObject);

        UnityTransport transport = networkManager.GetComponent<UnityTransport>();
        UnityTransport.ConnectionAddressData data = transport.ConnectionData;
        data.ServerListenAddress = "0.0.0.0";
        transport.ConnectionData = data;

        networkManager.NetworkConfig.ConnectionApproval = true;

        EditorUtility.SetDirty(transport);
        EditorUtility.SetDirty(networkManager);
    }

    private static void SetupMatchManager()
    {
        GameObject go = FindOrCreateRoot("MatchManager");
        GetOrAdd<NetworkObject>(go);
        GetOrAdd<MatchManager>(go);
    }

    private static void SetupGameAudio()
    {
        GameObject go = FindOrCreateRoot("GameAudio");
        GameAudio audio = GetOrAdd<GameAudio>(go);

        audio.laserShot = LoadClip("laser_shot");
        audio.hitConfirm = LoadClip("hit_confirm");
        audio.reload = LoadClip("reload");
        audio.emptyClick = LoadClip("empty_click");
        audio.damageTaken = LoadClip("damage_taken");
        audio.eliminated = LoadClip("eliminated");
        audio.respawn = LoadClip("respawn");
        audio.denied = LoadClip("denied");
        audio.countdownBeep = LoadClip("countdown_beep");
        audio.matchStart = LoadClip("match_start");
        audio.matchEnd = LoadClip("match_end");

        EditorUtility.SetDirty(audio);
    }

    private static void SetupLocalRig(XROrigin origin)
    {
        int hitbox = LayerMask.NameToLayer(GameLayers.PlayerHitboxName);
        int rig = LayerMask.NameToLayer(GameLayers.LocalRigName);

        // The CharacterController on the XR Origin root must not block shots.
        if (origin != null)
            origin.gameObject.layer = rig;

        // Rays (teleport, UI) ignore avatar hitboxes and the local rig, so a
        // player's own invisible body never blocks their teleport ray.
        int ignore = (1 << hitbox) | (1 << rig);

        foreach (XRRayInteractor ray in Object.FindObjectsByType<XRRayInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            ray.raycastMask = ray.raycastMask & ~ignore;
            EditorUtility.SetDirty(ray);
            PrefabUtility.RecordPrefabInstancePropertyModifications(ray);
            AssignUIPressInput(ray);
        }

        // Keep the XR Device Simulator for Editor/desktop testing only.
        foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (t.parent == null && t.name.StartsWith("XR Device Simulator"))
                GetOrAdd<DisableWhenHeadsetActive>(t.gameObject);
        }
    }

    // A ray only clicks UI buttons through its "UI Press" input. If that is
    // empty, the trigger never presses menu buttons. Use the UI Press action
    // from the same action map as the ray's Select (e.g. "XRI Right Interaction").
    private static void AssignUIPressInput(XRRayInteractor ray)
    {
        SerializedObject so = new SerializedObject(ray);
        SerializedProperty select = so.FindProperty("m_SelectInput.m_InputActionReferencePerformed");
        SerializedProperty pressPerformed = so.FindProperty("m_UIPressInput.m_InputActionReferencePerformed");
        SerializedProperty pressValue = so.FindProperty("m_UIPressInput.m_InputActionReferenceValue");

        InputActionReference selectRef = select != null ? select.objectReferenceValue as InputActionReference : null;
        if (selectRef == null || selectRef.action == null || pressPerformed == null || pressPerformed.objectReferenceValue != null)
            return;

        string mapName = selectRef.action.actionMap.name;

        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(selectRef)))
        {
            InputActionReference reference = asset as InputActionReference;
            if (reference == null || reference.action == null || reference.action.actionMap.name != mapName)
                continue;

            if (reference.action.name == "UI Press")
                pressPerformed.objectReferenceValue = reference;
            else if (reference.action.name == "UI Press Value" && pressValue != null)
                pressValue.objectReferenceValue = reference;
        }

        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetupTeleportArea()
    {
        foreach (TeleportationArea area in Object.FindObjectsByType<TeleportationArea>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (area is ValidatedTeleportationArea)
                continue;

            // Replace the plain area with the validated one, keeping all settings.
            GameObject go = area.gameObject;
            string json = EditorJsonUtility.ToJson(area);
            Object.DestroyImmediate(area);

            ValidatedTeleportationArea validated = go.AddComponent<ValidatedTeleportationArea>();
            EditorJsonUtility.FromJsonOverwrite(json, validated);

            Collider floorCollider = go.GetComponent<Collider>();
            if (floorCollider != null)
                validated.arenaCenter = new Vector3(floorCollider.bounds.center.x, 0f, floorCollider.bounds.center.z);

            EditorUtility.SetDirty(validated);
        }

        // Object references do not survive the JSON copy reliably:
        // assign the floor collider and the teleportation provider explicitly.
        TeleportationProvider provider = Object.FindAnyObjectByType<TeleportationProvider>(FindObjectsInactive.Include);

        foreach (ValidatedTeleportationArea area in Object.FindObjectsByType<ValidatedTeleportationArea>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            SerializedObject so = new SerializedObject(area);

            Collider floorCollider = area.GetComponent<Collider>();
            SetObjectArray(so.FindProperty("m_Colliders"), floorCollider != null ? new Object[] { floorCollider } : new Object[0]);

            if (provider != null)
                so.FindProperty("m_TeleportationProvider").objectReferenceValue = provider;

            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static void SetupEventSystem()
    {
        EventSystem eventSystem = Object.FindAnyObjectByType<EventSystem>(FindObjectsInactive.Include);

        if (eventSystem == null)
            eventSystem = new GameObject("EventSystem").AddComponent<EventSystem>();

        foreach (BaseInputModule module in eventSystem.GetComponents<BaseInputModule>())
        {
            if (!(module is XRUIInputModule))
                Object.DestroyImmediate(module);
        }

        GetOrAdd<XRUIInputModule>(eventSystem.gameObject);
    }

    private static void SetupArena()
    {
        // Remove a leftover physics test capsule that falls from the sky.
        GameObject capsule = GameObject.Find("Capsule");
        if (capsule != null && capsule.transform.parent == null && capsule.GetComponent<Rigidbody>() != null)
            Object.DestroyImmediate(capsule);

        // Face every spawn point towards the arena centre.
        for (int i = 1; i <= 5; i++)
        {
            GameObject spawn = GameObject.Find("SpawnPoint_" + i);
            if (spawn == null)
                continue;

            Vector3 toCentre = -spawn.transform.position;
            toCentre.y = 0f;

            if (toCentre.sqrMagnitude > 0.01f)
                spawn.transform.rotation = Quaternion.LookRotation(toCentre, Vector3.up);
        }

        // Two cover blocks become tall walls (full cover when standing);
        // the others stay as crouch cover.
        MakeTallCover("Cover2", new Vector3(-3.5f, 1.1f, 3f), new Vector3(2.5f, 2.2f, 0.5f));
        MakeTallCover("Cover5", new Vector3(3.5f, 1.1f, -3f), new Vector3(2.5f, 2.2f, 0.5f));

        Material coverMaterial = GetOrCreateMaterial("Cover", new Color(0.2f, 0.45f, 0.75f), 0.3f);
        foreach (string name in new[] { "Cover1", "Cover2", "Cover3", "Cover4", "Cover5" })
        {
            GameObject cover = GameObject.Find(name);
            if (cover != null && cover.GetComponent<Renderer>() != null)
                cover.GetComponent<Renderer>().sharedMaterial = coverMaterial;
        }
    }

    private static void MakeTallCover(string name, Vector3 position, Vector3 scale)
    {
        GameObject cover = GameObject.Find(name);
        if (cover == null)
            return;

        cover.transform.SetPositionAndRotation(position, Quaternion.identity);
        cover.transform.localScale = scale;
    }

    private static void SetupLocalGun(XROrigin origin)
    {
        LaserGunShooting gun = Object.FindAnyObjectByType<LaserGunShooting>(FindObjectsInactive.Include);
        if (gun == null)
            return;

        if (gun.uiRayInteractor == null)
            gun.uiRayInteractor = gun.GetComponentInParent<XRRayInteractor>();

        // Small ammo display on top of the gun, tilted towards the player.
        Transform display = gun.transform.Find("AmmoDisplay");
        if (display == null)
        {
            Canvas canvas = CreateWorldCanvas("AmmoDisplay", gun.transform, new Vector2(240, 90), 0.0005f, null, false);
            display = canvas.transform;
            display.localPosition = new Vector3(0f, 0.08f, 0.0f);
            display.localRotation = Quaternion.Euler(40f, 0f, 0f);

            Image background = CreateImage(display, "Background", new Color(0f, 0f, 0f, 0.6f));
            Stretch(background.rectTransform);

            Text text = CreateText(display, "AmmoText", "--", 60, TextAnchor.MiddleCenter);
            Stretch(text.rectTransform);
            gun.ammoText = text;
        }

        if (gun.laserLine != null)
        {
            gun.laserLine.shadowCastingMode = ShadowCastingMode.Off;
            gun.laserLine.receiveShadows = false;
        }

        EditorUtility.SetDirty(gun);
    }

    // ------------------------------------------------------------------
    // UI
    // ------------------------------------------------------------------

    private static readonly Color PanelColor = new Color(0.06f, 0.08f, 0.12f, 0.92f);
    private static readonly Color ButtonColor = new Color(0.18f, 0.32f, 0.55f, 1f);
    private static readonly Color AccentButtonColor = new Color(0.15f, 0.55f, 0.3f, 1f);
    private static readonly Color DangerButtonColor = new Color(0.6f, 0.2f, 0.2f, 1f);

    private static void SetupMenu(Camera headCamera)
    {
        GameObject existing = GameObject.Find("VR Menu");
        if (existing != null)
            Object.DestroyImmediate(existing);

        GameObject menuRoot = new GameObject("VR Menu");
        menuRoot.transform.position = new Vector3(0f, 1.4f, 4.5f);

        Canvas canvas = CreateWorldCanvas("MenuCanvas", menuRoot.transform, new Vector2(900, 640), 0.001f, headCamera, true);
        Transform panel = canvas.transform;

        Image background = CreateImage(panel, "Background", PanelColor);
        Stretch(background.rectTransform);

        Text title = CreateText(panel, "Title", "VR LASER TAG", 56, TextAnchor.MiddleCenter);
        title.fontStyle = FontStyle.Bold;
        Place(title.rectTransform, 0.03f, 0.88f, 0.97f, 0.98f);

        Text status = CreateText(panel, "Status", "Not connected", 26, TextAnchor.MiddleCenter);
        status.color = new Color(0.75f, 0.85f, 1f);
        Place(status.rectTransform, 0.03f, 0.81f, 0.97f, 0.88f);

        // --- Connect section ------------------------------------------
        RectTransform connect = CreateSection(panel, "ConnectSection");

        Text hostHeader = CreateText(connect, "HostHeader", "HOST A GAME", 30, TextAnchor.MiddleLeft);
        hostHeader.fontStyle = FontStyle.Bold;
        Place(hostHeader.rectTransform, 0.04f, 0.88f, 0.47f, 0.98f);

        Button hostButton = CreateButton(connect, "HostButton", "HOST GAME", AccentButtonColor, 34);
        Place(hostButton.GetComponent<RectTransform>(), 0.04f, 0.66f, 0.46f, 0.84f);

        Text help = CreateText(connect, "Help",
            "One player hosts. The others type the host's IP address and press JOIN.\n\n" +
            "All headsets must be on the same Wi-Fi network.\n\n" +
            "Trigger: fire / click\nA: reload\nLeft menu button: show/hide this panel",
            22, TextAnchor.UpperLeft);
        help.color = new Color(0.8f, 0.82f, 0.88f);
        Place(help.rectTransform, 0.04f, 0.04f, 0.47f, 0.62f);

        Text joinHeader = CreateText(connect, "JoinHeader", "JOIN A GAME", 30, TextAnchor.MiddleLeft);
        joinHeader.fontStyle = FontStyle.Bold;
        Place(joinHeader.rectTransform, 0.52f, 0.88f, 0.96f, 0.98f);

        Image addressBox = CreateImage(connect, "AddressBox", new Color(0f, 0f, 0f, 0.6f));
        Place(addressBox.rectTransform, 0.52f, 0.75f, 0.96f, 0.87f);
        Text address = CreateText(addressBox.transform, "AddressText", "192.168.1._", 40, TextAnchor.MiddleCenter);
        Stretch(address.rectTransform);

        string[] keys = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "dot", "0", "back" };
        string[] labels = { "1", "2", "3", "4", "5", "6", "7", "8", "9", ".", "0", "DEL" };

        for (int i = 0; i < keys.Length; i++)
        {
            int row = i / 3;
            int col = i % 3;
            float x0 = 0.52f + col * 0.148f;
            float y1 = 0.72f - row * 0.135f;

            Button key = CreateButton(connect, "Key_" + keys[i], labels[i], ButtonColor, 34);
            Place(key.GetComponent<RectTransform>(), x0, y1 - 0.12f, x0 + 0.14f, y1);
        }

        Button clearKey = CreateButton(connect, "Key_clear", "CLEAR", DangerButtonColor, 26);
        Place(clearKey.GetComponent<RectTransform>(), 0.52f, 0.04f, 0.66f, 0.16f);

        Button joinButton = CreateButton(connect, "JoinButton", "JOIN", AccentButtonColor, 34);
        Place(joinButton.GetComponent<RectTransform>(), 0.68f, 0.04f, 0.96f, 0.16f);

        // --- Session section ------------------------------------------
        RectTransform session = CreateSection(panel, "SessionSection");

        Text sessionText = CreateText(session, "SessionText", "", 28, TextAnchor.UpperLeft);
        Place(sessionText.rectTransform, 0.04f, 0.26f, 0.56f, 0.98f);

        Text playersText = CreateText(session, "PlayersText", "", 28, TextAnchor.UpperLeft);
        Place(playersText.rectTransform, 0.6f, 0.26f, 0.96f, 0.98f);

        Button startButton = CreateButton(session, "StartButton", "START MATCH", AccentButtonColor, 32);
        Place(startButton.GetComponent<RectTransform>(), 0.04f, 0.04f, 0.42f, 0.2f);

        Button closeButton = CreateButton(session, "HideButton", "HIDE", ButtonColor, 30);
        Place(closeButton.GetComponent<RectTransform>(), 0.45f, 0.04f, 0.61f, 0.2f);

        Button leaveButton = CreateButton(session, "LeaveButton", "LEAVE", DangerButtonColor, 30);
        Place(leaveButton.GetComponent<RectTransform>(), 0.64f, 0.04f, 0.96f, 0.2f);

        session.gameObject.SetActive(false);

        // --- Controller -----------------------------------------------
        VRMenuUI menu = menuRoot.AddComponent<VRMenuUI>();
        SerializedObject so = new SerializedObject(menu);
        so.FindProperty("panelRoot").objectReferenceValue = canvas.gameObject;
        so.FindProperty("connectSection").objectReferenceValue = connect.gameObject;
        so.FindProperty("sessionSection").objectReferenceValue = session.gameObject;
        so.FindProperty("head").objectReferenceValue = headCamera != null ? headCamera.transform : null;
        so.FindProperty("statusText").objectReferenceValue = status;
        so.FindProperty("addressText").objectReferenceValue = address;
        so.FindProperty("sessionText").objectReferenceValue = sessionText;
        so.FindProperty("playersText").objectReferenceValue = playersText;
        so.FindProperty("startButtonLabel").objectReferenceValue = startButton.GetComponentInChildren<Text>();
        so.FindProperty("hostButton").objectReferenceValue = hostButton;
        so.FindProperty("joinButton").objectReferenceValue = joinButton;
        so.FindProperty("startButton").objectReferenceValue = startButton;
        so.FindProperty("leaveButton").objectReferenceValue = leaveButton;
        so.FindProperty("closeButton").objectReferenceValue = closeButton;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetupHud(Camera headCamera)
    {
        if (headCamera == null)
            return;

        Transform existing = headCamera.transform.Find("PlayerHUD");
        if (existing != null)
            Object.DestroyImmediate(existing.gameObject);

        // 1000 x 600 px at 0.0005 m/px = 0.5 m x 0.3 m, 0.6 m in front of the eyes.
        Canvas canvas = CreateWorldCanvas("PlayerHUD", headCamera.transform, new Vector2(1000, 600), 0.0005f, headCamera, false);
        canvas.transform.localPosition = new Vector3(0f, 0f, 0.6f);
        canvas.transform.localRotation = Quaternion.identity;
        canvas.sortingOrder = 10;
        Transform hud = canvas.transform;

        // The flash extends far beyond the HUD so it tints the whole view.
        Image flash = CreateImage(hud, "DamageFlash", new Color(1f, 0f, 0f, 0f));
        Stretch(flash.rectTransform);
        flash.rectTransform.offsetMin = new Vector2(-1500f, -1500f);
        flash.rectTransform.offsetMax = new Vector2(1500f, 1500f);

        Text timer = CreateText(hud, "Timer", "", 44, TextAnchor.MiddleCenter);
        timer.fontStyle = FontStyle.Bold;
        Place(timer.rectTransform, 0.35f, 0.86f, 0.65f, 0.98f);
        AddShadow(timer);

        Text score = CreateText(hud, "Score", "", 26, TextAnchor.MiddleLeft);
        Place(score.rectTransform, 0.02f, 0.86f, 0.34f, 0.98f);
        AddShadow(score);

        Text feed = CreateText(hud, "Feed", "", 34, TextAnchor.MiddleCenter);
        Place(feed.rectTransform, 0.08f, 0.64f, 0.92f, 0.74f);
        AddShadow(feed);

        Text center = CreateText(hud, "CenterMessage", "", 64, TextAnchor.MiddleCenter);
        center.fontStyle = FontStyle.Bold;
        Place(center.rectTransform, 0.04f, 0.26f, 0.96f, 0.64f);
        AddShadow(center);

        Text hitMarker = CreateText(hud, "HitMarker", "X", 56, TextAnchor.MiddleCenter);
        hitMarker.color = new Color(1f, 0.95f, 0.4f);
        Place(hitMarker.rectTransform, 0.45f, 0.44f, 0.55f, 0.56f);

        // Bottom stats bar.
        RectTransform stats = new GameObject("Stats", typeof(RectTransform)).GetComponent<RectTransform>();
        stats.SetParent(hud, false);
        Place(stats, 0.12f, 0.02f, 0.88f, 0.2f);

        Image statsBackground = CreateImage(stats, "Background", new Color(0f, 0f, 0f, 0.45f));
        Stretch(statsBackground.rectTransform);

        Text health = CreateText(stats, "HealthText", "HP 100 / 100", 32, TextAnchor.MiddleLeft);
        Place(health.rectTransform, 0.03f, 0.5f, 0.48f, 0.98f);

        Image barBackground = CreateImage(stats, "HealthBarBackground", new Color(0.2f, 0.2f, 0.2f, 0.9f));
        Place(barBackground.rectTransform, 0.03f, 0.14f, 0.48f, 0.42f);

        Image bar = CreateImage(barBackground.transform, "HealthBar", new Color(0.3f, 0.95f, 0.4f));
        Stretch(bar.rectTransform);

        Text ammo = CreateText(stats, "AmmoText", "AMMO 10 / 10", 34, TextAnchor.MiddleRight);
        ammo.fontStyle = FontStyle.Bold;
        Place(ammo.rectTransform, 0.52f, 0.1f, 0.97f, 0.9f);

        PlayerHUD hudController = canvas.gameObject.AddComponent<PlayerHUD>();
        SerializedObject so = new SerializedObject(hudController);
        so.FindProperty("statsGroup").objectReferenceValue = stats.gameObject;
        so.FindProperty("healthText").objectReferenceValue = health;
        so.FindProperty("healthBar").objectReferenceValue = bar;
        so.FindProperty("ammoText").objectReferenceValue = ammo;
        so.FindProperty("timerText").objectReferenceValue = timer;
        so.FindProperty("scoreText").objectReferenceValue = score;
        so.FindProperty("centerText").objectReferenceValue = center;
        so.FindProperty("feedText").objectReferenceValue = feed;
        so.FindProperty("hitMarker").objectReferenceValue = hitMarker;
        so.FindProperty("damageFlash").objectReferenceValue = flash;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetupScoreboards(Camera headCamera)
    {
        foreach (string name in new[] { "Scoreboard North", "Scoreboard South" })
        {
            GameObject existing = GameObject.Find(name);
            if (existing != null)
                Object.DestroyImmediate(existing);
        }

        CreateScoreboard("Scoreboard North", new Vector3(0f, 2.0f, 7.2f), Quaternion.identity, headCamera);
        CreateScoreboard("Scoreboard South", new Vector3(0f, 2.0f, -7.2f), Quaternion.Euler(0f, 180f, 0f), headCamera);
    }

    private static void CreateScoreboard(string name, Vector3 position, Quaternion rotation, Camera headCamera)
    {
        // 1000 x 600 px at 0.0022 m/px = 2.2 m x 1.32 m, mounted on a wall.
        Canvas canvas = CreateWorldCanvas(name, null, new Vector2(1000, 600), 0.0022f, headCamera, false);
        canvas.transform.SetPositionAndRotation(position, rotation);
        Transform board = canvas.transform;

        Image background = CreateImage(board, "Background", new Color(0.03f, 0.04f, 0.08f, 0.9f));
        Stretch(background.rectTransform);

        Text header = CreateText(board, "Header", "VR LASER TAG", 56, TextAnchor.MiddleCenter);
        header.fontStyle = FontStyle.Bold;
        Place(header.rectTransform, 0.03f, 0.84f, 0.97f, 0.97f);

        Text ranks = CreateText(board, "Ranks", "", 44, TextAnchor.UpperCenter);
        Place(ranks.rectTransform, 0.03f, 0.16f, 0.1f, 0.82f);

        Text names = CreateText(board, "Names", "", 44, TextAnchor.UpperLeft);
        Place(names.rectTransform, 0.12f, 0.16f, 0.55f, 0.82f);

        Text scores = CreateText(board, "Scores", "", 44, TextAnchor.UpperCenter);
        Place(scores.rectTransform, 0.55f, 0.16f, 0.74f, 0.82f);

        Text deaths = CreateText(board, "Deaths", "", 44, TextAnchor.UpperCenter);
        Place(deaths.rectTransform, 0.74f, 0.16f, 0.98f, 0.82f);

        Text footer = CreateText(board, "Footer", "", 30, TextAnchor.MiddleCenter);
        footer.color = new Color(0.8f, 0.85f, 1f);
        Place(footer.rectTransform, 0.03f, 0.02f, 0.97f, 0.15f);

        ScoreboardUI scoreboard = canvas.gameObject.AddComponent<ScoreboardUI>();
        SerializedObject so = new SerializedObject(scoreboard);
        so.FindProperty("headerText").objectReferenceValue = header;
        so.FindProperty("rankText").objectReferenceValue = ranks;
        so.FindProperty("namesText").objectReferenceValue = names;
        so.FindProperty("scoresText").objectReferenceValue = scores;
        so.FindProperty("deathsText").objectReferenceValue = deaths;
        so.FindProperty("footerText").objectReferenceValue = footer;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static Canvas CreateWorldCanvas(string name, Transform parent, Vector2 sizePixels, float metresPerPixel, Camera eventCamera, bool interactive)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.layer = LayerMask.NameToLayer("UI");

        if (parent != null)
            go.transform.SetParent(parent, false);

        Canvas canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = eventCamera;

        CanvasScaler scaler = go.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 3f;
        scaler.referencePixelsPerUnit = 100f;

        if (interactive)
            go.AddComponent<TrackedDeviceGraphicRaycaster>();

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.sizeDelta = sizePixels;
        rect.localScale = Vector3.one * metresPerPixel;

        return canvas;
    }

    private static RectTransform CreateSection(Transform parent, string name)
    {
        RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        Place(rect, 0f, 0f, 1f, 0.8f);
        return rect;
    }

    private static Image CreateImage(Transform parent, string name, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);

        Image image = go.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static Text CreateText(Transform parent, string name, string content, int size, TextAnchor alignment)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);

        Text text = go.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = content;
        text.fontSize = size;
        text.alignment = alignment;
        text.color = Color.white;
        text.supportRichText = true;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        return text;
    }

    private static Button CreateButton(Transform parent, string name, string label, Color color, int fontSize)
    {
        Image image = CreateImage(parent, name, color);
        image.raycastTarget = true;

        Button button = image.gameObject.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f);
        colors.pressedColor = new Color(0.7f, 0.7f, 0.7f);
        colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.6f);
        colors.colorMultiplier = 1.4f;
        button.colors = colors;

        Text text = CreateText(image.transform, "Label", label, fontSize, TextAnchor.MiddleCenter);
        text.fontStyle = FontStyle.Bold;
        Stretch(text.rectTransform);

        return button;
    }

    private static void AddShadow(Text text)
    {
        Shadow shadow = text.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.8f);
        shadow.effectDistance = new Vector2(2f, -2f);
    }

    private static void Place(RectTransform rect, float xMin, float yMin, float xMax, float yMax)
    {
        rect.anchorMin = new Vector2(xMin, yMin);
        rect.anchorMax = new Vector2(xMax, yMax);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void Stretch(RectTransform rect)
    {
        Place(rect, 0f, 0f, 1f, 1f);
    }

    // ------------------------------------------------------------------
    // Meta Quest configuration
    // ------------------------------------------------------------------

    private static void ConfigureAndroidPlayerSettings()
    {
        NamedBuildTarget android = NamedBuildTarget.Android;

        PlayerSettings.companyName = "AUB CMPS297AG";
        PlayerSettings.productName = "VR Laser Tag";
        PlayerSettings.SetApplicationIdentifier(android, "com.aub.cmps297ag.vrlasertag");
        PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
        PlayerSettings.Android.forceInternetPermission = true;
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
        PlayerSettings.colorSpace = ColorSpace.Linear;
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
        EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;
    }

    private static void ConfigureXRManagement()
    {
        if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget perTarget) ||
            perTarget == null)
        {
            Directory.CreateDirectory("Assets/XR");
            perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
            AssetDatabase.CreateAsset(perTarget, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perTarget, true);
        }

        if (!perTarget.HasSettingsForBuildTarget(BuildTargetGroup.Android))
            perTarget.CreateDefaultSettingsForBuildTarget(BuildTargetGroup.Android);

        if (!perTarget.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android))
            perTarget.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);

        XRGeneralSettings androidSettings = perTarget.SettingsForBuildTarget(BuildTargetGroup.Android);
        androidSettings.InitManagerOnStart = true;

        // Only Android (Quest) gets OpenXR. Standalone stays without a loader so
        // the Editor and Windows test builds keep using the XR Device Simulator.
        XRPackageMetadataStore.AssignLoader(
            androidSettings.AssignedSettings,
            "UnityEngine.XR.OpenXR.OpenXRLoader",
            BuildTargetGroup.Android);

        EditorUtility.SetDirty(androidSettings);
        EditorUtility.SetDirty(perTarget);
    }

    private static void ConfigureOpenXRFeatures()
    {
        FeatureHelpers.RefreshFeatures(BuildTargetGroup.Android);

        OpenXRSettings settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
        if (settings == null)
        {
            Debug.LogError("[LaserTagSetup] OpenXR settings for Android were not found.");
            return;
        }

        settings.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;
        settings.depthSubmissionMode = OpenXRSettings.DepthSubmissionMode.Depth16Bit;

        EnableFeature(MetaQuestFeature.featureId);
        EnableFeature(OculusTouchControllerProfile.featureId);
        EnableFeature(MetaQuestTouchPlusControllerProfile.featureId);

        EditorUtility.SetDirty(settings);
    }

    private static void EnableFeature(string featureId)
    {
        var feature = FeatureHelpers.GetFeatureWithIdForBuildTarget(BuildTargetGroup.Android, featureId);

        if (feature == null)
        {
            Debug.LogWarning("[LaserTagSetup] OpenXR feature not found: " + featureId);
            return;
        }

        feature.enabled = true;
        EditorUtility.SetDirty(feature);
    }

    private static void ConfigureMobileRendering()
    {
        UniversalRenderPipelineAsset mobile =
            AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(MobileRPAssetPath);

        if (mobile == null)
            return;

        mobile.msaaSampleCount = 4;
        mobile.supportsHDR = false;
        mobile.renderScale = 1f;
        mobile.shadowDistance = 15f;
        EditorUtility.SetDirty(mobile);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static T GetOrAdd<T>(GameObject go) where T : Component
    {
        T component = go.GetComponent<T>();
        return component != null ? component : go.AddComponent<T>();
    }

    private static GameObject FindOrCreateRoot(string name)
    {
        GameObject go = GameObject.Find(name);
        return go != null ? go : new GameObject(name);
    }

    private static void RemoveCollider(Transform t)
    {
        foreach (Collider c in t.GetComponents<Collider>())
            Object.DestroyImmediate(c);
    }

    private static Transform CreatePrimitiveChild(Transform parent, string name, PrimitiveType type, Vector3 localPosition, Vector3 localScale)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = localScale;
        return go.transform;
    }

    private static Material GetOrCreateMaterial(string name, Color color, float smoothness)
    {
        string path = MaterialsFolder + "/" + name + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (material == null)
        {
            Directory.CreateDirectory(MaterialsFolder);
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, path);
        }

        material.SetColor("_BaseColor", color);
        material.SetFloat("_Smoothness", smoothness);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static AudioClip LoadClip(string name)
    {
        AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioFolder + "/" + name + ".wav");
        if (clip == null)
            Debug.LogWarning("[LaserTagSetup] Missing audio clip: " + name);
        return clip;
    }

    private static void SetObjectArray(SerializedProperty array, Object[] values)
    {
        array.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
            array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }

    private static void AddToObjectArray(SerializedProperty array, Object value)
    {
        if (value == null)
            return;

        for (int i = 0; i < array.arraySize; i++)
        {
            if (array.GetArrayElementAtIndex(i).objectReferenceValue == value)
                return;
        }

        array.arraySize++;
        array.GetArrayElementAtIndex(array.arraySize - 1).objectReferenceValue = value;
    }
}
