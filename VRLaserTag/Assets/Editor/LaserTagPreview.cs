using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Development tool: renders still images of the hands, gun and a remote
// player avatar from the Editor, without running a match. The scene is not
// saved. Needs a graphics device (do not use -nographics).
//
//   Unity.exe -batchmode -projectPath <project> -executeMethod
//       LaserTagPreview.BatchRender -previewOut <folder>
public static class LaserTagPreview
{
    private const string ScenePath = "Assets/ArenaScene.unity";
    private const string PlayerPrefabPath = "Assets/Prefabs/NetworkPlayer.prefab";

    public static void BatchRender()
    {
        int code = 0;
        try
        {
            string[] args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-previewOut");
            string folder = i >= 0 && i + 1 < args.Length ? args[i + 1] : "Builds/Previews";
            Render(folder);
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            code = 1;
        }

        if (Application.isBatchMode)
            EditorApplication.Exit(code);
    }

    // Logs a rough cost summary of ArenaScene (plus one player avatar):
    // renderers, triangles, materials, textures and an estimate of draw
    // calls after static batching (static objects that share a material
    // and lightmap are merged; UI canvases are counted separately).
    //   Unity.exe -batchmode -projectPath <project> -executeMethod LaserTagPreview.BatchStats
    public static void BatchStats()
    {
        int code = 0;
        try
        {
            LogStats();
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            code = 1;
        }

        if (Application.isBatchMode)
            EditorApplication.Exit(code);
    }

    private static void LogStats()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        int renderers = 0, staticRenderers = 0, triangles = 0, transparent = 0;
        var materials = new System.Collections.Generic.HashSet<Material>();
        var textures = new System.Collections.Generic.HashSet<Texture>();
        var staticBatches = new System.Collections.Generic.HashSet<(Material, int)>();
        int dynamicDraws = 0;

        foreach (MeshRenderer r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude))
        {
            MeshFilter f = r.GetComponent<MeshFilter>();
            if (!r.enabled || f == null || f.sharedMesh == null)
                continue;

            renderers++;
            Mesh mesh = f.sharedMesh;
            for (int s = 0; s < mesh.subMeshCount; s++)
                triangles += (int)mesh.GetIndexCount(s) / 3;

            bool isStatic = GameObjectUtility.AreStaticEditorFlagsSet(r.gameObject, StaticEditorFlags.BatchingStatic);
            if (isStatic)
                staticRenderers++;

            foreach (Material m in r.sharedMaterials)
            {
                if (m == null)
                    continue;

                materials.Add(m);
                if (m.renderQueue >= 3000)
                    transparent++;

                foreach (string name in m.GetTexturePropertyNames())
                {
                    Texture t = m.GetTexture(name);
                    if (t != null)
                        textures.Add(t);
                }

                if (isStatic)
                    staticBatches.Add((m, r.lightmapIndex));
                else
                    dynamicDraws++;
            }
        }

        int canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude).Length;

        long textureBytes = 0;
        foreach (Texture t in textures)
            textureBytes += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t);

        long lightmapBytes = 0;
        foreach (LightmapData map in LightmapSettings.lightmaps)
        {
            if (map.lightmapColor != null)
                lightmapBytes += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(map.lightmapColor);
        }

        int realtimeLights = 0;
        foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude))
        {
            if (l.lightmapBakeType != LightmapBakeType.Baked)
                realtimeLights++;
        }

        Debug.Log("[LaserTagStats] renderers=" + renderers + " (static " + staticRenderers + ")" +
                  " triangles=" + triangles +
                  " materials=" + materials.Count + " transparentMaterialSlots=" + transparent +
                  " estimatedMeshDraws=" + (staticBatches.Count + dynamicDraws) +
                  " (static batches " + staticBatches.Count + ", dynamic " + dynamicDraws + ")" +
                  " canvases=" + canvases +
                  " textures=" + textures.Count + " (~" + textureBytes / 1024 + " KB in Editor)" +
                  " lightmaps=" + LightmapSettings.lightmaps.Length + " (~" + lightmapBytes / 1024 + " KB in Editor)" +
                  " realtimeLights=" + realtimeLights);

        LogAvatarStats();
    }

    // Cost of one remote player avatar as other players see it: enabled
    // mesh renderers (each sub-mesh is one draw), triangles and materials.
    private static void LogAvatarStats()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        try
        {
            int draws = 0, triangles = 0;
            var materials = new System.Collections.Generic.HashSet<Material>();
            var parts = new System.Text.StringBuilder();

            foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(false))
            {
                MeshFilter f = r.GetComponent<MeshFilter>();
                if (!r.enabled || f == null || f.sharedMesh == null)
                    continue;

                int tris = 0;
                for (int s = 0; s < f.sharedMesh.subMeshCount; s++)
                    tris += (int)f.sharedMesh.GetIndexCount(s) / 3;

                draws += f.sharedMesh.subMeshCount;
                triangles += tris;
                foreach (Material m in r.sharedMaterials)
                    if (m != null) materials.Add(m);

                parts.Append(" ").Append(r.name).Append("(").Append(tris).Append(")");
            }

            Debug.Log("[LaserTagStats] avatar draws=" + draws + " triangles=" + triangles +
                      " materials=" + materials.Count + " parts:" + parts);

            // Reference for the old placeholder avatar (capsule body, sphere
            // head, cube visor).
            int Tris(string builtin)
            {
                Mesh m = Resources.GetBuiltinResource<Mesh>(builtin);
                return m != null ? (int)m.GetIndexCount(0) / 3 : -1;
            }
            Debug.Log("[LaserTagStats] built-in capsule=" + Tris("Capsule.fbx") + " sphere=" + Tris("Sphere.fbx") +
                      " cube=" + Tris("Cube.fbx") + " triangles");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void Render(string folder)
    {
        Directory.CreateDirectory(folder);
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        foreach (Canvas c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude))
        {
            // Hide head-locked HUD and menu panels so the view is clear.
            if (c.name.Contains("HUD") || c.name.Contains("Menu"))
                c.gameObject.SetActive(false);
        }

        Camera camera = Camera.main;
        Transform right = GameObject.Find("Right Hand").transform;
        Transform left = GameObject.Find("Left Hand").transform;

        // 1. First-person view: both hands in front of the player.
        Place(camera.transform, new Vector3(0f, 1.6f, -5.6f), Quaternion.Euler(8f, 0f, 0f));
        Place(right, new Vector3(0.2f, 1.36f, -5.27f), Quaternion.Euler(4f, -4f, 0f));
        Place(left, new Vector3(-0.22f, 1.32f, -5.3f), Quaternion.Euler(10f, 8f, 0f));
        Shot(camera, folder + "/c1_first_person.png");

        // 2. Gun close-up from the side.
        Place(right, new Vector3(0f, 1.3f, -5f), Quaternion.identity);
        Place(left, new Vector3(-5f, -5f, -5f), Quaternion.identity);
        camera.transform.position = new Vector3(0.42f, 1.42f, -4.82f);
        camera.transform.LookAt(new Vector3(0f, 1.27f, -4.9f));
        Shot(camera, folder + "/c2_gun_closeup.png");

        // 3. Another player (player 2 colour) shooting towards the camera.
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        GameObject avatar = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        avatar.transform.SetPositionAndRotation(new Vector3(0.6f, 0f, -2.2f), Quaternion.Euler(0f, 200f, 0f));

        Color color = PlayerInfo.PlayerColors[1];
        ColorAvatar(avatar, color);

        Transform aRight = FindDeep(avatar.transform, "NetworkRightHand");
        Transform aLeft = FindDeep(avatar.transform, "NetworkLeftHand");
        aRight.localPosition = new Vector3(0.22f, 1.32f, 0.38f);
        aRight.localRotation = Quaternion.Euler(-3f, -8f, 0f);
        aLeft.localPosition = new Vector3(-0.24f, 1.25f, 0.3f);
        aLeft.localRotation = Quaternion.Euler(10f, 10f, 0f);
        RefreshBody(avatar);

        // Name tag as PlayerNameLabel shows it in play.
        Transform nameLabel = FindDeep(avatar.transform, "NameLabel");
        if (nameLabel != null)
        {
            nameLabel.gameObject.SetActive(true);
            UnityEngine.UI.Text labelText = nameLabel.GetComponentInChildren<UnityEngine.UI.Text>();
            labelText.text = "PLAYER 2";
            labelText.color = color;
            nameLabel.position = FindDeep(avatar.transform, "NetworkHead").position + Vector3.up * 0.32f;
        }

        Place(camera.transform, new Vector3(-0.3f, 1.55f, -4.3f), Quaternion.identity);
        if (nameLabel != null)
        {
            Vector3 away = nameLabel.position - camera.transform.position;
            away.y = 0f;
            nameLabel.rotation = Quaternion.LookRotation(away, Vector3.up);
        }

        Transform tip = FindDeep(avatar.transform, "VisualMuzzle");
        Transform flash = FindDeep(avatar.transform, "MuzzleFlash");
        if (flash != null)
            flash.gameObject.SetActive(true);

        LineRenderer beam = FindDeep(avatar.transform, "RemoteBeam").GetComponent<LineRenderer>();
        beam.useWorldSpace = true;
        beam.positionCount = 2;
        beam.SetPosition(0, tip.position);
        beam.SetPosition(1, tip.position + tip.forward * 12f);
        beam.startColor = Color.Lerp(color, Color.white, 0.55f);
        beam.endColor = color;
        beam.enabled = true;

        Place(camera.transform, new Vector3(-0.3f, 1.55f, -4.3f), Quaternion.identity);
        camera.transform.LookAt(new Vector3(0.6f, 1.1f, -2.2f));
        Shot(camera, folder + "/c3_remote_player.png");

        beam.enabled = false;
        if (flash != null)
            flash.gameObject.SetActive(false);
        if (nameLabel != null)
            nameLabel.gameObject.SetActive(false);
        Object.DestroyImmediate(avatar);

        // 4. All five player colours side by side (different heights, one
        //    crouching, one turned), to check proportions and readability.
        float[] heights = { 1.62f, 1.75f, 1.5f, 1.05f, 1.68f };
        float[] yaws = { 160f, 180f, 200f, 180f, 230f };
        for (int p = 0; p < 5; p++)
        {
            GameObject a = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            a.transform.SetPositionAndRotation(new Vector3(-2.4f + p * 1.2f, 0f, -1.2f), Quaternion.identity);
            ColorAvatar(a, PlayerInfo.PlayerColors[p]);

            Transform h = FindDeep(a.transform, "NetworkHead");
            h.localPosition = new Vector3(0f, heights[p], 0f);
            h.localRotation = Quaternion.Euler(p == 3 ? 15f : 0f, yaws[p], 0f);

            Quaternion facing = Quaternion.Euler(0f, yaws[p], 0f);
            Transform r = FindDeep(a.transform, "NetworkRightHand");
            Transform l = FindDeep(a.transform, "NetworkLeftHand");
            r.localPosition = new Vector3(0f, heights[p], 0f) + facing * new Vector3(0.2f, -0.32f, 0.3f);
            r.localRotation = facing * Quaternion.Euler(5f, 0f, 0f);
            l.localPosition = new Vector3(0f, heights[p], 0f) + facing * new Vector3(-0.22f, -0.5f, 0.12f);
            l.localRotation = facing * Quaternion.Euler(40f, 0f, 0f);
            RefreshBody(a);
        }

        Place(camera.transform, new Vector3(0f, 1.45f, -5.2f), Quaternion.Euler(4f, 0f, 0f));
        Shot(camera, folder + "/c4_avatar_lineup.png");

        // 5. Close-up of one avatar from the front-left.
        camera.transform.position = new Vector3(-1.1f, 1.5f, -2.6f);
        camera.transform.LookAt(new Vector3(-0.1f, 1.15f, -1.2f));
        Shot(camera, folder + "/c5_avatar_closeup.png");

        Debug.Log("[LaserTagPreview] Rendered previews to " + folder);
    }

    // HUD as the player sees it: ComfortHud placement (1.3 m, lazy follow),
    // shown in front of a wall to check the distance clamp too.
    //   Unity.exe -batchmode -projectPath <project> -executeMethod
    //       LaserTagPreview.BatchRenderHud -previewOut <folder>
    public static void BatchRenderHud()
    {
        int code = 0;
        try
        {
            string[] args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-previewOut");
            string folder = i >= 0 && i + 1 < args.Length ? args[i + 1] : "Builds/Previews";
            Directory.CreateDirectory(folder);
            RenderHud(folder);
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            code = 1;
        }

        if (Application.isBatchMode)
            EditorApplication.Exit(code);
    }

    private static void RenderHud(string folder)
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        foreach (Canvas c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude))
        {
            if (c.name.Contains("Menu"))
                c.gameObject.SetActive(false);
        }

        Camera camera = Camera.main;
        Unity.XR.CoreUtils.XROrigin origin = Object.FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>();
        ComfortHud hud = Object.FindAnyObjectByType<ComfortHud>();

        // Sample values (PlayerHUD only fills them in a running match).
        SetText("HealthText", "HP  75");
        SetText("AmmoText", "7 / 10");
        SetText("Timer", "2:41");
        SetText("Score", "<b><color=#4D8CFF>Player 2</color></b>\n<color=#9FB3C8>RANK</color> #1 of 5   <color=#9FB3C8>SCORE</color> 3");
        SetText("Feed", "You hit Player 4");

        // Open arena, looking at the centre. The HUD shots use a 34 degree
        // vertical field of view (about 57 degrees wide): roughly the pixels
        // per degree of a Quest 3, so text size is judged realistically.
        const float hudFov = 34f;
        origin.transform.SetPositionAndRotation(new Vector3(0f, 0f, -5.5f), Quaternion.identity);
        camera.transform.localPosition = new Vector3(0f, 0.5f, 0f); // eyes at 1.6 m
        camera.transform.localRotation = Quaternion.identity;
        hud.Snap();
        Shot(camera, folder + "/c6_hud_open.png", hudFov);

        // Head turned 12 degrees: inside the follow threshold, the HUD stays.
        camera.transform.localRotation = Quaternion.Euler(0f, 12f, 0f);
        Shot(camera, folder + "/c7_hud_small_turn.png", hudFov);

        // Standing 0.85 m in front of cover: the HUD comes closer (and
        // smaller, same angular size) instead of disappearing inside the block.
        origin.transform.SetPositionAndRotation(new Vector3(-3.5f, 0f, 1.9f), Quaternion.identity); // Cover2 face at z = 2.75
        camera.transform.localRotation = Quaternion.identity;
        Physics.SyncTransforms();
        hud.Snap();
        Shot(camera, folder + "/c8_hud_near_cover.png", hudFov);

        Debug.Log("[LaserTagPreview] HUD distance in front of cover: " +
                  Vector3.Distance(camera.transform.position, hud.transform.position).ToString("0.00") + " m");
        Debug.Log("[LaserTagPreview] Rendered HUD previews to " + folder);
    }

    private static void SetText(string objectName, string value)
    {
        GameObject go = GameObject.Find(objectName);
        UnityEngine.UI.Text text = go != null ? go.GetComponent<UnityEngine.UI.Text>() : null;
        if (text != null)
            text.text = value;
    }

    // Player colour as PlayerInfo applies it: old colour renderers (gloves)
    // get the base colour, the suit gets glowing accents.
    private static void ColorAvatar(GameObject avatar, Color color)
    {
        foreach (string part in new[] { "NetworkLeftHand", "NetworkRightHand" })
            Tint(FindDeep(avatar.transform, part), color);

        foreach (string part in new[] { "Helmet", "AvatarTorso", "AvatarLegs" })
        {
            Transform t = FindDeep(avatar.transform, part);
            Renderer r = t != null ? t.GetComponent<Renderer>() : null;
            if (r == null)
                continue;

            MaterialPropertyBlock block = new MaterialPropertyBlock();
            block.SetColor("_EmissionColor", color * 1.6f);
            r.SetPropertyBlock(block);
        }
    }

    private static void RefreshBody(GameObject avatar)
    {
        AvatarBodyVisual body = avatar.GetComponent<AvatarBodyVisual>();
        if (body != null)
            body.Refresh();
    }

    private static void Place(Transform t, Vector3 position, Quaternion rotation)
    {
        t.SetPositionAndRotation(position, rotation);
    }

    private static void Tint(Transform t, Color color)
    {
        Renderer r = t != null ? t.GetComponent<Renderer>() : null;
        if (r == null)
            return;

        MaterialPropertyBlock block = new MaterialPropertyBlock();
        block.SetColor("_BaseColor", color);
        r.SetPropertyBlock(block);
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

    private static void Shot(Camera camera, string path, float fieldOfView = 80f)
    {
        const int width = 1280, height = 720;
        RenderTexture rt = new RenderTexture(width, height, 24) { antiAliasing = 4 };
        RenderTexture previousTarget = camera.targetTexture;

        camera.aspect = (float)width / height;
        camera.fieldOfView = fieldOfView;
        camera.targetTexture = rt;
        camera.Render();

        RenderTexture.active = rt;
        Texture2D image = new Texture2D(width, height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        image.Apply();
        File.WriteAllBytes(path, image.EncodeToPNG());

        RenderTexture.active = null;
        camera.targetTexture = previousTarget;
        Object.DestroyImmediate(image);
        rt.Release();
        Object.DestroyImmediate(rt);
    }
}


// HUD-only review tools. All camera/visibility changes below are temporary;
// neither the scene nor any gameplay state is saved by the capture command.
public static class LaserTagHudReview
{
    private const System.Reflection.BindingFlags Fields = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
    private static string Output => Path.GetFullPath(Path.Combine(Application.dataPath,
        "../Builds/HudReview"));

    [MenuItem("VR Laser Tag/HUD/Apply Approved HUD Profiles")]
    public static void ApplyProfiles()
    {
        PlayerHUD hud = Object.FindAnyObjectByType<PlayerHUD>(FindObjectsInactive.Include);
        if (hud == null) throw new System.InvalidOperationException("PlayerHUD was not found.");
        Undo.RecordObject(hud, "Apply approved HUD profiles");
        var so = new SerializedObject(hud);
        so.FindProperty("profile").enumValueIndex = (int)HudProfile.Auto;
        SetProfile(so.FindProperty("windows"), .78f, .76f, .83f, 704f, 1.7f, 2f);
        SetProfile(so.FindProperty("quest2"), 40f, 30f, 38f, 704f, 1.9f, 2.2f);
        SetProfile(so.FindProperty("quest3"), 43f, 32f, 44f, 704f, 2.3f, 2.7f);
        so.ApplyModifiedProperties();
        Selection.activeGameObject = hud.gameObject;
        Debug.Log("[HUD Review] Applied independent HUD profiles only; Auto detection retained.");
    }

    private static void SetProfile(SerializedProperty p, float top, float side, float bottom, float width, float topScale, float bottomScale)
    {
        p.FindPropertyRelative("topOffset").floatValue = top;
        p.FindPropertyRelative("sideOffset").floatValue = side;
        p.FindPropertyRelative("bottomOffset").floatValue = bottom;
        p.FindPropertyRelative("topBarWidth").floatValue = width;
        p.FindPropertyRelative("topScale").floatValue = topScale;
        p.FindPropertyRelative("bottomScale").floatValue = bottomScale;
        p.FindPropertyRelative("distance").floatValue = 1.3f;
    }

    [MenuItem("VR Laser Tag/HUD/Capture Layout Previews (Play Mode)")]
    public static void Capture()
    {
        if (!Application.isPlaying) throw new System.InvalidOperationException("Enter Play Mode first.");
        PlayerHUD hud = Object.FindAnyObjectByType<PlayerHUD>();
        Camera camera = Camera.main;
        ComfortHud comfort = hud.GetComponent<ComfortHud>();
        var stats = (GameObject)Field(hud, "statsGroup");
        var texts = hud.GetComponentsInChildren<UnityEngine.UI.Text>(true);
        var textValues = new string[texts.Length];
        for (int i = 0; i < texts.Length; i++) textValues[i] = texts[i].text;
        var originalProfile = (HudProfile)Field(hud, "profile");
        Vector3 position = camera.transform.position;
        Quaternion rotation = camera.transform.rotation;
        Matrix4x4 projection = camera.projectionMatrix;
        float fov = camera.fieldOfView, aspect = camera.aspect;
        RenderTexture target = camera.targetTexture;
        bool active = stats.activeSelf;
        var hidden = new System.Collections.Generic.List<Renderer>();
        Transform rig = camera.GetComponentInParent<Unity.XR.CoreUtils.XROrigin>().transform;
        foreach (Renderer renderer in rig.GetComponentsInChildren<Renderer>())
            if (renderer.enabled) { hidden.Add(renderer); renderer.enabled = false; }
        var canvases = new System.Collections.Generic.List<Canvas>();
        foreach (Canvas canvas in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            if (canvas.enabled && canvas.transform != hud.transform) { canvases.Add(canvas); canvas.enabled = false; }

        bool live = PlayerCombat.Local != null && PlayerCombat.Local.IsSpawned;
        bool previewAmmo = false;
        var ammoFills = (System.Collections.Generic.List<UnityEngine.UI.Image>)Field(hud, "ammoFill");
        var healthFills = (System.Collections.Generic.List<UnityEngine.UI.Image>)Field(hud, "healthFill");
        try
        {
            Directory.CreateDirectory(Output);
            File.WriteAllText(Path.Combine(Output, "layout-audit.txt"), "HUD projection bounds checks\n");
            stats.SetActive(true);
            if (live)
                typeof(PlayerHUD).GetMethod("UpdateStats", Fields).Invoke(hud, new object[] { PlayerCombat.Local, MatchManager.Instance });
            else
            {
                Text(hud, "rankValue").text = "#2";
                Text(hud, "timerText").text = "3:29";
                Text(hud, "scoreText").text = "0";
                Text(hud, "healthText").text = "100<size=22> HP</size>";
                Text(hud, "ammoText").text = "10<size=26> / 10</size>";
                if (ammoFills.Count == 0)
                {
                    typeof(PlayerHUD).GetMethod("BuildSegments", Fields).Invoke(hud, new object[] { Field(hud, "ammoBar"), ammoFills, 10 });
                    previewAmmo = true;
                }
                foreach (var fill in healthFills) { fill.enabled = true; fill.color = new Color(.3f, .95f, .45f); }
                foreach (var fill in ammoFills) { fill.enabled = true; fill.color = new Color(1f, .6f, .18f); }
            }
            Text(hud, "centerText").text = "";
            Text(hud, "feedText").text = "";
            camera.transform.SetPositionAndRotation(new Vector3(0f, 1.65f, -5.5f), Quaternion.identity);
            CaptureOne(hud, comfort, camera, HudProfile.Windows, 1280, 720, 60f, "windows-preview.png");
            CaptureOne(hud, comfort, camera, HudProfile.Quest2, 1100, 1100, 90f, "quest2-preview.png");
            CaptureOne(hud, comfort, camera, HudProfile.Quest3, 1100, 1100, 96f, "quest3-preview.png");
            // The same layout must remain visible in asymmetric left/right
            // eye projections and retain its size when brought in front of cover.
            foreach (bool rightEye in new[] { false, true })
            {
                camera.projectionMatrix = rightEye
                    ? Matrix4x4.Frustum(-.0085f, .011f, -.01f, .01f, .01f, 100f)
                    : Matrix4x4.Frustum(-.011f, .0085f, -.01f, .01f, .01f, 100f);
                foreach (HudProfile q in new[] { HudProfile.Quest2, HudProfile.Quest3 })
                {
                    typeof(PlayerHUD).GetField("profile", Fields).SetValue(hud, q);
                    typeof(PlayerHUD).GetMethod("ApplyPlacement", Fields).Invoke(hud, null);
                    comfort.Snap(); Canvas.ForceUpdateCanvases();
                    Audit(hud, camera, q + (rightEye ? " right-eye simulation" : " left-eye simulation"));
                }
            }
            camera.fieldOfView = 60f; camera.aspect = 1280f / 720f; camera.ResetProjectionMatrix();
            typeof(PlayerHUD).GetField("profile", Fields).SetValue(hud, HudProfile.Windows);
            typeof(PlayerHUD).GetMethod("ApplyPlacement", Fields).Invoke(hud, null);
            camera.transform.SetPositionAndRotation(new Vector3(0f, 1.65f, -5.5f), Quaternion.identity);
            Physics.SyncTransforms(); comfort.Snap();
            Rect before = ViewRect((RectTransform)Field(hud, "healthPanelRect"), camera);
            camera.transform.SetPositionAndRotation(new Vector3(-3.5f, 1.65f, 1.9f), Quaternion.identity);
            Physics.SyncTransforms(); comfort.Snap();
            Rect after = ViewRect((RectTransform)Field(hud, "healthPanelRect"), camera);
            if (Mathf.Abs(before.width - after.width) > .001f || Mathf.Abs(before.height - after.height) > .001f)
                throw new System.InvalidOperationException("HUD angular size changed near cover.");
            Audit(hud, camera, "Windows near cover; constant angular size PASS");
            File.WriteAllText(Path.Combine(Output, "preview-context.txt"),
                "Revision: " + "HUD layout review\n" +
                "Data: " + (live ? "live synchronized gameplay values" : "labelled design samples; no gameplay state changed") + "\n" +
                "Windows: 1280x720, 60 degree vertical projection.\n" +
                "Quest 2: 1100x1100, representative 90 degree mono eye preview.\n" +
                "Quest 3: 1100x1100, representative 96 degree mono eye preview.\n" +
                "Quest images are layout simulations, not headset captures. No headset performance claim.\n");
            Debug.Log("[HUD Review] Captured all three layouts: " + Output + "; live=" + live);
        }
        finally
        {
            camera.targetTexture = target;
            camera.fieldOfView = fov; camera.aspect = aspect; camera.projectionMatrix = projection;
            camera.transform.SetPositionAndRotation(position, rotation);
            typeof(PlayerHUD).GetField("profile", Fields).SetValue(hud, originalProfile);
            typeof(PlayerHUD).GetMethod("ApplyPlacement", Fields).Invoke(hud, null);
            comfort.Snap();
            for (int i = 0; i < texts.Length; i++) if (texts[i] != null) texts[i].text = textValues[i];
            stats.SetActive(active);
            foreach (Renderer renderer in hidden) if (renderer != null) renderer.enabled = true;
            foreach (Canvas canvas in canvases) if (canvas != null) canvas.enabled = true;
            if (previewAmmo)
            {
                Transform row = (Transform)Field(hud, "ammoBar");
                for (int i = row.childCount - 1; i >= 0; i--) Object.DestroyImmediate(row.GetChild(i).gameObject);
                ammoFills.Clear();
            }
        }
    }

    private static void CaptureOne(PlayerHUD hud, ComfortHud comfort, Camera camera, HudProfile profile, int width, int height, float fov, string file)
    {
        camera.fieldOfView = fov; camera.aspect = (float)width / height; camera.ResetProjectionMatrix();
        typeof(PlayerHUD).GetField("profile", Fields).SetValue(hud, profile);
        typeof(PlayerHUD).GetMethod("ApplyPlacement", Fields).Invoke(hud, null);
        Physics.SyncTransforms();
        comfort.Snap(); Canvas.ForceUpdateCanvases();
        Audit(hud, camera, profile.ToString());
        RenderTexture rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
        RenderTexture previous = RenderTexture.active;
        Texture2D image = null;
        try
        {
            camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
            image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(Output, file), image.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = null; RenderTexture.active = previous;
            if (image != null) Object.DestroyImmediate(image);
            RenderTexture.ReleaseTemporary(rt);
        }
    }

    private static Rect ViewRect(RectTransform panel, Camera camera)
    {
        var corners = new Vector3[4]; panel.GetWorldCorners(corners);
        Vector2 min = Vector2.one * float.PositiveInfinity, max = Vector2.one * float.NegativeInfinity;
        foreach (Vector3 corner in corners)
        {
            Vector3 point = camera.WorldToViewportPoint(corner);
            if (point.z <= 0f) throw new System.InvalidOperationException("HUD behind the preview camera.");
            min = Vector2.Min(min, point); max = Vector2.Max(max, point);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    private static void Audit(PlayerHUD hud, Camera camera, string label)
    {
        Transform top = (Transform)Field(hud, "topBarRect");
        foreach (RectTransform panel in new[] { (RectTransform)Field(hud, "barFrameRect"),
            (RectTransform)top.Find("Time"), (RectTransform)top.Find("ModeTag"),
            (RectTransform)Field(hud, "healthPanelRect"), (RectTransform)Field(hud, "ammoPanelRect") })
        {
            Rect bounds = ViewRect(panel, camera);
            if (bounds.xMin < -.001f || bounds.yMin < -.001f || bounds.xMax > 1.001f || bounds.yMax > 1.001f)
                throw new System.InvalidOperationException(label + " clips " + panel.name + ": " + bounds);
        }
        File.AppendAllText(Path.Combine(Output, "layout-audit.txt"), label + ": all five panel bounds PASS\n");
    }

    [MenuItem("VR Laser Tag/HUD/Validate Live HUD Values")]
    public static void ValidateLive()
    {
        PlayerHUD hud = Object.FindAnyObjectByType<PlayerHUD>();
        PlayerCombat combat = PlayerCombat.Local;
        MatchManager match = MatchManager.Instance;
        if (!Application.isPlaying || combat == null || !combat.IsSpawned || match == null || !match.IsSpawned)
            throw new System.InvalidOperationException("Connect to a live match first.");
        typeof(PlayerHUD).GetMethod("UpdateStats", Fields).Invoke(hud, new object[] { combat, match });
        PlayerInfo info = PlayerInfo.FindByClientId(combat.OwnerClientId);
        string expectedRank = "#" + (MatchManager.GetStandings().IndexOf(info) + 1);
        bool health = Text(hud, "healthText").text.StartsWith(combat.Health.Value.ToString() + "<");
        bool ammo = combat.IsReloading.Value ? Text(hud, "ammoText").text.Contains("RELOADING") : Text(hud, "ammoText").text.StartsWith(combat.Ammo.Value.ToString() + "<");
        bool score = Text(hud, "scoreText").text == info.Score.Value.ToString();
        bool rank = Text(hud, "rankValue").text == expectedRank;
        string expectedTime = (string)typeof(PlayerHUD).GetMethod("DescribeTimer", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).Invoke(null, new object[] { match });
        bool timer = Text(hud, "timerText").text == expectedTime;
        string result = "Health=" + health + " (" + combat.Health.Value + "), Ammo=" + ammo + " (" + combat.Ammo.Value + "), Rank=" + rank + ", Score=" + score + " (" + info.Score.Value + "), Time=" + timer + " (" + expectedTime + ")";
        Directory.CreateDirectory(Output); File.AppendAllText(Path.Combine(Output, "live-values.txt"), result + "\n");
        if (!(health && ammo && rank && score && timer)) throw new System.InvalidOperationException(result);
        Debug.Log("[HUD Review] LIVE VALUES PASS: " + result);
    }

    private static object Field(PlayerHUD hud, string name) => typeof(PlayerHUD).GetField(name, Fields).GetValue(hud);
    private static UnityEngine.UI.Text Text(PlayerHUD hud, string name) => (UnityEngine.UI.Text)Field(hud, name);
}
