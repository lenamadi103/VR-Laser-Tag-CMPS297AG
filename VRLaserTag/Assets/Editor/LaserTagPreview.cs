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
