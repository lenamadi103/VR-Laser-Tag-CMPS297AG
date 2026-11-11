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
        foreach (string part in new[] { "PlayerBody", "NetworkHead", "NetworkLeftHand", "NetworkRightHand" })
            Tint(FindDeep(avatar.transform, part), color);

        Transform aRight = FindDeep(avatar.transform, "NetworkRightHand");
        Transform aLeft = FindDeep(avatar.transform, "NetworkLeftHand");
        aRight.localPosition = new Vector3(0.22f, 1.32f, 0.38f);
        aRight.localRotation = Quaternion.Euler(-3f, -8f, 0f);
        aLeft.localPosition = new Vector3(-0.24f, 1.25f, 0.3f);
        aLeft.localRotation = Quaternion.Euler(10f, 10f, 0f);

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

        Debug.Log("[LaserTagPreview] Rendered previews to " + folder);
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

    private static void Shot(Camera camera, string path)
    {
        const int width = 1280, height = 720;
        RenderTexture rt = new RenderTexture(width, height, 24) { antiAliasing = 4 };
        RenderTexture previousTarget = camera.targetTexture;

        camera.aspect = (float)width / height;
        camera.fieldOfView = 80f;
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
