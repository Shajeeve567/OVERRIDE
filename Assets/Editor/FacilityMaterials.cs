// FacilityMaterials.cs
// Turns downloaded PBR texture sets (ambientCG / Poly Haven) into the greybox's materials, and makes
// the textures tile per metre instead of stretching across whole walls.
// Run it from the menu bar with the greybox scene open: OVERRIDE > Apply Facility Materials.
//
// HOW IT FINDS THINGS
//   Each folder in Assets/OVERRIDE/Textures/ is one surface. The folder's name picks the greybox
//   material it feeds: "Wall" or "Walls" -> Wall.mat, "Floor_Labs" -> Floor_Labs.mat, and so on.
//   Inside a folder, maps are found by the END of their file name: _Color, _NormalGL, _Roughness,
//   _Metalness, _AmbientOcclusion. So any ambientCG set works without renaming.
//
// WHY IT EDITS THE EXISTING GREYBOX MATERIALS (instead of making new ones)
//   GreyboxBuilder reuses Wall.mat, Floor_Labs.mat... on every rebuild. Putting the textures INTO
//   those files means a rebuilt level keeps its look automatically.

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Override.EditorTools
{
    public static class FacilityMaterials
    {
        const string TextureRoot    = "Assets/OVERRIDE/Textures";
        const string MaterialFolder = "Assets/OVERRIDE/Greybox/Materials";
        const string PackedSuffix   = "_MetallicSmoothness";

        // How many metres one copy of the texture covers when a material is FIRST textured.
        // After that, tune it live: select the material and change Tiling in the Inspector
        // (Tiling = 1 / metres, so 0.125 = one texture every 8 m; smaller Tiling = bigger plates).
        // Re-running this tool keeps whatever Tiling you set.
        const float DefaultTileMetres = 4f;
        // Texture folders already prepared (imported and packed) during the current run.
        static readonly HashSet<string> preparedFolders = new HashSet<string>();

        static readonly Dictionary<string, float> TileMetres = new Dictionary<string, float>
        {
            { "Wall", 8f },     // MetalPlates packs several plates per image; 8 m gives ~2 m plates
            { "Ceiling", 8f },  // far away and in shadow: keep it calm
        };

        [MenuItem("OVERRIDE/Apply Facility Materials")]
        static void Apply()
        {
            if (!AssetDatabase.IsValidFolder(TextureRoot))
            {
                EditorUtility.DisplayDialog("No textures yet",
                    "Put each texture set in its own folder under " + TextureRoot + " (e.g. Textures/Wall).", "OK");
                return;
            }

            // 1. Decide which folder feeds which material.
            //    First pass: exact names ("Wall"/"Walls" -> Wall.mat, "Floor_Labs" -> Floor_Labs.mat).
            //    Second pass: group folders. "Floor" feeds every "Floor_..." material that doesn't
            //    already have its own folder, so one download can cover all zones, and a zone can
            //    still get a different look later just by adding its own folder.
            string[] folders = AssetDatabase.GetSubFolders(TextureRoot);
            var source = new Dictionary<Material, string>();
            foreach (string folder in folders)
            {
                Material exact = FindGreyboxMaterial(Path.GetFileName(folder));
                if (exact != null) source[exact] = folder;
            }
            foreach (string folder in folders)
            {
                string surface = Path.GetFileName(folder);
                if (FindGreyboxMaterial(surface) != null) continue; // already used as an exact match
                var group = FindGreyboxMaterialsStartingWith(surface + "_");
                if (group.Count == 0)
                {
                    Debug.LogWarning("[Level] No greybox material matches the folder '" + surface +
                                     "'. Name the folder after one in " + MaterialFolder + ".");
                    continue;
                }
                foreach (var mat in group)
                    if (!source.ContainsKey(mat)) source[mat] = folder; // specific folders win
            }

            // 2. Build each material from its folder.
            var textured = new List<Material>();
            preparedFolders.Clear();
            foreach (var pair in source)
            {
                string folder = pair.Value;
                if (BuildMaterial(folder, Path.GetFileName(folder), pair.Key)) textured.Add(pair.Key);
            }
            AssetDatabase.SaveAssets();

            // 3. Stop the stretching on every greybox piece that uses one of those materials.
            int pieces = FixUVsInOpenScene(textured);
            Debug.Log("[Level] Textured " + textured.Count + " material(s); re-tiled " + pieces +
                      " greybox pieces. Save the scene to keep the tiling.");
        }

        // ==================================================================
        // MATERIALS
        // ==================================================================

        // "Walls" should find Wall.mat, so we also try the name without a trailing "s".
        static Material FindGreyboxMaterial(string folderName)
        {
            string[] candidates = { folderName, folderName.TrimEnd('s', 'S') };
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { MaterialFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string name = Path.GetFileNameWithoutExtension(path);
                foreach (string c in candidates)
                    if (string.Equals(name, c, System.StringComparison.OrdinalIgnoreCase))
                        return AssetDatabase.LoadAssetAtPath<Material>(path);
            }
            return null;
        }

        // Every greybox material whose name starts with the prefix, e.g. "Floor_" -> all six floors.
        static List<Material> FindGreyboxMaterialsStartingWith(string prefix)
        {
            var found = new List<Material>();
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { MaterialFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path).StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
                    found.Add(AssetDatabase.LoadAssetAtPath<Material>(path));
            }
            return found;
        }

        static bool BuildMaterial(string folder, string surface, Material mat)
        {
            string color  = FindMap(folder, "_Color");
            string normal = FindMap(folder, "_NormalGL");
            string rough  = FindMap(folder, "_Roughness");
            string metal  = FindMap(folder, "_Metalness");
            string ao     = FindMap(folder, "_AmbientOcclusion");

            if (color == null)
            {
                Debug.LogWarning("[Level] " + folder + " has no *_Color texture, skipped.");
                return false;
            }

            // Import settings. Colour stays sRGB (it's a picture for eyes). Everything else is DATA:
            // numbers that must reach the shader unchanged, so sRGB (a brightness curve) is turned off.
            // A normal map must also be marked as one, or Unity reads its colours as plain colours.
            // A group folder like "Floor" feeds several materials. Its textures only need preparing
            // once per run, so later materials skip straight to using them.
            bool firstUseThisRun = preparedFolders.Add(folder);
            if (firstUseThisRun)
            {
                if (normal != null) Reimport(normal, t => t.textureType = TextureImporterType.NormalMap);
                if (ao != null)     Reimport(ao, t => t.sRGBTexture = false);
                if (rough != null)  Reimport(rough, t => t.sRGBTexture = false);
                if (metal != null)  Reimport(metal, t => t.sRGBTexture = false);
            }

            // URP's Lit shader reads metallic from RED and smoothness from ALPHA of one texture.
            // ambientCG ships metalness and roughness separately, so they are packed into one file.
            string packed = null;
            if (rough != null || metal != null)
            {
                packed = folder + "/" + surface + PackedSuffix + ".png";
                if (firstUseThisRun) PackMetallicSmoothness(metal, rough, packed);
            }

            // Remember whether this is the first time the material gets a texture (still a plain
            // greybox colour). Only then do we set its Tiling, so your Inspector tweaks survive re-runs.
            // A Tiling of exactly (1, 1) also counts as "not tuned yet": that's Unity's default, and
            // the value an earlier version of this tool left behind.
            bool firstTime = mat.GetTexture("_BaseMap") == null || mat.GetTextureScale("_BaseMap") == Vector2.one;

            mat.SetTexture("_BaseMap", Load(color));
            mat.SetColor("_BaseColor", Color.white); // the greybox tint would darken the texture

            // Setting a texture from code doesn't switch on the shader feature that USES it (the
            // Inspector does that for you when you drag a map in). These keywords do it here.
            if (normal != null)
            {
                mat.SetTexture("_BumpMap", Load(normal));
                mat.SetFloat("_BumpScale", 1f);
                mat.EnableKeyword("_NORMALMAP");
            }
            if (packed != null)
            {
                mat.SetTexture("_MetallicGlossMap", Load(packed));
                mat.SetFloat("_Smoothness", 1f); // with a map, this becomes a multiplier on the map's alpha
                mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            }
            if (ao != null)
            {
                mat.SetTexture("_OcclusionMap", Load(ao));
                mat.EnableKeyword("_OCCLUSIONMAP");
            }

            // Each piece's UVs are in METRES (see FixUVsInOpenScene), so the material's Tiling
            // decides how many metres one texture covers: Tiling = 1 / metres. URP's Lit shader uses
            // the Base Map's tiling for the normal and metallic maps too, so they stay lined up.
            if (firstTime)
            {
                float metres = TileMetres.TryGetValue(mat.name, out float m) ? m : DefaultTileMetres;
                mat.SetTextureScale("_BaseMap", Vector2.one / metres);
            }
            EditorUtility.SetDirty(mat);
            return true;
        }

        // Finds "<anything><suffix>.png/.jpg" directly inside the folder (not in subfolders like the
        // "Materials" folder Unity creates when a texture is dragged onto an object).
        static string FindMap(string folder, string suffix)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetDirectoryName(path).Replace('\\', '/') != folder) continue;
                string name = Path.GetFileNameWithoutExtension(path);
                if (name.EndsWith(PackedSuffix)) continue; // our own output, not a source map
                if (name.EndsWith(suffix, System.StringComparison.OrdinalIgnoreCase)) return path;
            }
            return null;
        }

        static Texture2D Load(string path) => AssetDatabase.LoadAssetAtPath<Texture2D>(path);

        static void Reimport(string path, System.Action<TextureImporter> change)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            change(importer);
            importer.SaveAndReimport();
        }

        // Builds one RGBA texture: R = metalness, A = smoothness (1 - roughness).
        // The source PNGs are read straight from disk, so their import settings (compression,
        // "Read/Write" off) don't matter.
        static void PackMetallicSmoothness(string metalPath, string roughPath, string outPath)
        {
            Texture2D metal = metalPath != null ? ReadPng(metalPath) : null;
            Texture2D rough = roughPath != null ? ReadPng(roughPath) : null;
            Texture2D size  = metal != null ? metal : rough;

            var packed = new Texture2D(size.width, size.height, TextureFormat.RGBA32, false, true);
            var pixels = new Color[size.width * size.height];
            for (int y = 0; y < size.height; y++)
            {
                for (int x = 0; x < size.width; x++)
                {
                    // Sample by position (0-1), so the two maps may even differ in resolution.
                    float u = (x + 0.5f) / size.width, v = (y + 0.5f) / size.height;
                    float m = metal != null ? metal.GetPixelBilinear(u, v).r : 0f;   // no map: not metal
                    float r = rough != null ? rough.GetPixelBilinear(u, v).r : 0.5f; // no map: half rough
                    pixels[y * size.width + x] = new Color(m, m, m, 1f - r);
                }
            }
            packed.SetPixels(pixels);
            File.WriteAllBytes(outPath, packed.EncodeToPNG());

            Object.DestroyImmediate(packed);
            if (metal != null) Object.DestroyImmediate(metal);
            if (rough != null) Object.DestroyImmediate(rough);

            AssetDatabase.ImportAsset(outPath);
            Reimport(outPath, t =>
            {
                t.sRGBTexture = false;                                   // data, not colour
                t.alphaSource = TextureImporterAlphaSource.FromInput;    // keep our smoothness channel
                t.alphaIsTransparency = false;                           // alpha is NOT see-through here
            });
        }

        // "linear: true" loads the numbers exactly as stored, without any colour conversion.
        static Texture2D ReadPng(string assetPath)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            tex.LoadImage(File.ReadAllBytes(assetPath)); // resizes the texture to the image's real size
            return tex;
        }

        // ==================================================================
        // WORLD-SPACE UVs
        // A greybox wall is a 1 x 1 x 1 cube stretched to e.g. 150 x 10 m. Its UVs still say
        // "one texture per face", so the texture gets stretched 150 times. Here each piece gets a
        // copy of its mesh whose UVs come from WORLD position instead: one texture every N metres,
        // and neighbouring pieces line up because they share the same world grid.
        // ==================================================================
        static int FixUVsInOpenScene(List<Material> textured)
        {
            var root = GameObject.Find("OVERRIDE_Greybox");
            if (root == null)
            {
                Debug.LogWarning("[Level] Materials updated, but no greybox is open, so tiling wasn't fixed. " +
                                 "Open the greybox scene and run this again.");
                return 0;
            }

            int count = 0;
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true)) // true = include hidden ceilings
            {
                Material mat = renderer.sharedMaterial;
                if (mat == null || !textured.Contains(mat)) continue;
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;

                Undo.RecordObject(filter, "Apply Facility Materials");
                filter.sharedMesh = WorldUVMesh(filter.sharedMesh, renderer.transform);
                count++;
            }
            EditorSceneManager.MarkSceneDirty(root.scene);
            return count;
        }

        // UVs come out in plain metres (1 UV unit = 1 m). The material's Tiling then scales them,
        // which is what makes the size adjustable live in the Inspector.
        static Mesh WorldUVMesh(Mesh source, Transform t)
        {
            var mesh = Object.Instantiate(source); // a copy: never edit Unity's shared built-in cube
            mesh.name = "WorldUV_" + t.name;

            Vector3[] vertices = mesh.vertices;
            Vector3[] normals  = mesh.normals;
            var uvs = new Vector2[vertices.Length];

            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 p = t.TransformPoint(vertices[i]);   // where this corner is in the world
                Vector3 n = t.TransformDirection(normals[i]); // which way this face points in the world
                float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);

                // Project the texture from the axis the face looks along most:
                // walls facing east/west use (z, y), floors and ceilings use (x, z), walls facing
                // north/south use (x, y). This is "triplanar" mapping, baked into the UVs once.
                Vector2 uv = ax >= ay && ax >= az ? new Vector2(p.z, p.y)
                           : ay >= az            ? new Vector2(p.x, p.z)
                                                 : new Vector2(p.x, p.y);
                uvs[i] = uv;
            }
            mesh.uv = uvs;

            // Normal maps need tangents that follow the UVs; the copied ones followed the OLD UVs.
            mesh.RecalculateTangents();
            // The lightmapper needs a second, non-overlapping UV set; generate it for the new mesh.
            Unwrapping.GenerateSecondaryUVSet(mesh);
            return mesh;
        }
    }
}
