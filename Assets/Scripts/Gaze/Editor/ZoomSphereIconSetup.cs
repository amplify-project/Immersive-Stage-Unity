using UnityEditor;
using UnityEngine;

/// <summary>
/// One-click add-on for the zoom spheres: Tools > Gaze > Add Zoom Sphere Icons.
/// A flat +/- icon wrapped around a full 3D sphere via UV mapping repeats and
/// distorts across the back/poles, so instead a small transparent-background
/// Quad is parented in front of each sphere, showing only the icon with the
/// sphere's own tint color visible around it (SetSphereActive still drives
/// that tint - this script only adds the icon overlay, it doesn't touch
/// PicoInteractionController).
/// </summary>
public static class ZoomSphereIconSetup
{
    const string ZoomInIconPath = "Assets/Textures/ZoomInIcon.png";
    const string ZoomOutIconPath = "Assets/Textures/ZoomOutIcon.png";

    [MenuItem("Tools/Gaze/Add Zoom Sphere Icons")]
    public static void AddIcons()
    {
        GameObject zoomIn = GameObject.Find("ZoomSphere_In");
        GameObject zoomOut = GameObject.Find("ZoomSphere_Out");

        if (zoomIn == null && zoomOut == null)
        {
            Debug.LogError("[ZoomSphereIconSetup] Neither 'ZoomSphere_In' nor 'ZoomSphere_Out' found in the open scene. Create the zoom spheres first.");
            return;
        }

        if (zoomIn != null)
            AddIconQuad(zoomIn, ZoomInIconPath, "Mat_ZoomIn");
        else
            Debug.LogWarning("[ZoomSphereIconSetup] 'ZoomSphere_In' not found - skipped.");

        if (zoomOut != null)
            AddIconQuad(zoomOut, ZoomOutIconPath, "Mat_ZoomOut");
        else
            Debug.LogWarning("[ZoomSphereIconSetup] 'ZoomSphere_Out' not found - skipped.");

        Debug.Log("[ZoomSphereIconSetup] Done. If an icon appears invisible from your viewpoint, select its 'Icon' child and set Local Rotation Y to 180.");
    }

    static void AddIconQuad(GameObject sphere, string texturePath, string materialName)
    {
        Transform existing = sphere.transform.Find("Icon");
        GameObject iconGO = existing != null ? existing.gameObject : GameObject.CreatePrimitive(PrimitiveType.Quad);
        if (existing == null)
        {
            iconGO.name = "Icon";
            Undo.RegisterCreatedObjectUndo(iconGO, "Add Zoom Sphere Icon");
            iconGO.transform.SetParent(sphere.transform, false);
        }

        // Collider not needed for a purely visual overlay; gaze detection is
        // angular against the sphere's own transform, not a physics hit.
        Collider col = iconGO.GetComponent<Collider>();
        if (col != null)
            Object.DestroyImmediate(col);

        // -0.55 (not a fixed world distance like -0.03) because localPosition is
        // expressed in the SPHERE's own local space, which is then multiplied by the
        // sphere's lossyScale to get world position - the same multiplication the
        // sphere's own surface (mesh radius 0.5 in that same local space) goes
        // through. -0.03 previously placed the icon only ~1.5mm in front of the
        // center at the sphere's actual 0.05 scale (surface is 25mm out), so it sat
        // inside the sphere and z-fought with it. -0.55 sits just outside the
        // surface regardless of whatever scale the sphere ends up at.
        iconGO.transform.localPosition = new Vector3(0f, 0f, -0.55f);
        iconGO.transform.localRotation = Quaternion.identity;
        iconGO.transform.localScale = Vector3.one * 1.2f;

        Renderer renderer = iconGO.GetComponent<Renderer>();
        renderer.sharedMaterial = CreateOrLoadIconMaterial(materialName, texturePath);
    }

    static Material CreateOrLoadIconMaterial(string materialName, string texturePath)
    {
        string materialPath = $"Assets/Textures/{materialName}.mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (existing != null)
            return existing;

        Shader shader = Shader.Find("Unlit/Transparent") ?? Shader.Find("Legacy Shaders/Transparent/Diffuse");
        var mat = new Material(shader);

        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        if (texture == null)
            Debug.LogWarning($"[ZoomSphereIconSetup] Texture not found at '{texturePath}' - material created without it.");
        else
            mat.mainTexture = texture;

        AssetDatabase.CreateAsset(mat, materialPath);
        return mat;
    }
}
