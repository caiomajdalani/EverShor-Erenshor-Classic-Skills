using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace ErenshorSkills.Stations
{
    /// <summary>Owns runtime-created Unity assets that a destroyed station
    /// would otherwise leave allocated until process exit.</summary>
    public sealed class RuntimeGeneratedAssets : MonoBehaviour
    {
        private readonly List<UnityEngine.Object> _assets =
            new List<UnityEngine.Object>();

        public void Track(UnityEngine.Object asset)
        {
            if (asset != null && !_assets.Contains(asset)) _assets.Add(asset);
        }

        private void OnDestroy()
        {
            foreach (var asset in _assets)
                if (asset != null) UnityEngine.Object.Destroy(asset);
            _assets.Clear();
        }
    }

    /// <summary>
    /// Runtime OBJ file loader. Parses .obj files and creates Unity Mesh objects.
    /// Supports vertices, normals, UVs, and textured materials via .png files.
    /// </summary>
    public static class ObjLoader
    {
        /// <summary>
        /// Load an OBJ file and create a GameObject with mesh and texture.
        /// </summary>
        public static GameObject Load(string objPath, string texturePath = null)
        {
            if (!File.Exists(objPath))
            {
                SkillsPlugin.Log.LogWarning($"ObjLoader: File not found: {objPath}");
                return null;
            }

            try
            {
                var lines = File.ReadAllLines(objPath);
                var positions = new List<Vector3>();
                var normals = new List<Vector3>();
                var uvs = new List<Vector2>();
                var triVerts = new List<Vector3>();
                var triNorms = new List<Vector3>();
                var triUVs = new List<Vector2>();
                var triIndices = new List<int>();
                int invalidRecords = 0;

                foreach (var rawLine in lines)
                {
                    var line = rawLine.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;

                    var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2) continue;

                    if (parts[0] == "v" && parts.Length >= 4)
                    {
                        if (TryParseFloat(parts[1], out float x) &&
                            TryParseFloat(parts[2], out float y) &&
                            TryParseFloat(parts[3], out float z))
                            positions.Add(new Vector3(x, y, z));
                        else invalidRecords++;
                    }
                    else if (parts[0] == "vn" && parts.Length >= 4)
                    {
                        if (TryParseFloat(parts[1], out float x) &&
                            TryParseFloat(parts[2], out float y) &&
                            TryParseFloat(parts[3], out float z))
                            normals.Add(new Vector3(x, y, z));
                        else invalidRecords++;
                    }
                    else if (parts[0] == "vt" && parts.Length >= 3)
                    {
                        if (TryParseFloat(parts[1], out float x) &&
                            TryParseFloat(parts[2], out float y))
                            uvs.Add(new Vector2(x, y));
                        else invalidRecords++;
                    }
                    else if (parts[0] == "f")
                    {
                        // Triangulate face (supports quads and n-gons)
                        var faceVerts = new List<int[]>();
                        bool validFace = true;
                        for (int i = 1; i < parts.Length; i++)
                        {
                            if (!TryParseFaceVertex(parts[i], positions.Count,
                                uvs.Count, normals.Count, out int[] faceVertex))
                            {
                                validFace = false;
                                break;
                            }
                            faceVerts.Add(faceVertex);
                        }
                        if (!validFace || faceVerts.Count < 3)
                        { invalidRecords++; continue; }
                        // Fan triangulation
                        for (int i = 1; i < faceVerts.Count - 1; i++)
                        {
                            AddVertex(faceVerts[0], positions, normals, uvs,
                                triVerts, triNorms, triUVs, triIndices);
                            AddVertex(faceVerts[i], positions, normals, uvs,
                                triVerts, triNorms, triUVs, triIndices);
                            AddVertex(faceVerts[i + 1], positions, normals, uvs,
                                triVerts, triNorms, triUVs, triIndices);
                        }
                    }
                }

                if (triVerts.Count == 0 || triIndices.Count == 0)
                {
                    SkillsPlugin.Log.LogWarning(
                        $"ObjLoader: '{objPath}' contains no valid triangles.");
                    return null;
                }

                // Build Unity mesh
                var mesh = new Mesh();
                mesh.name = Path.GetFileNameWithoutExtension(objPath);
                if (triVerts.Count > 65535)
                    mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.SetVertices(triVerts);
                if (triNorms.Count == triVerts.Count) mesh.SetNormals(triNorms);
                if (triUVs.Count == triVerts.Count) mesh.SetUVs(0, triUVs);

                // Reverse triangle winding for Unity's left-handed coords
                // OBJ files use right-handed winding — without this, faces are inside-out
                for (int i = 0; i < triIndices.Count; i += 3)
                {
                    int tmp = triIndices[i];
                    triIndices[i] = triIndices[i + 2];
                    triIndices[i + 2] = tmp;
                }

                mesh.SetTriangles(triIndices, 0);
                if (triNorms.Count != triVerts.Count) mesh.RecalculateNormals();
                mesh.RecalculateBounds();

                // Create GameObject
                var go = new GameObject(mesh.name);
                var assets = go.AddComponent<RuntimeGeneratedAssets>();
                assets.Track(mesh);
                var mf = go.AddComponent<MeshFilter>();
                mf.mesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();

                // Load texture if provided — use double-sided rendering
                Shader shader = Shader.Find("Standard") ??
                    Shader.Find("Legacy Shaders/Diffuse") ?? Shader.Find("Unlit/Texture");
                if (shader == null)
                {
                    UnityEngine.Object.Destroy(go);
                    SkillsPlugin.Log.LogError("ObjLoader: No compatible material shader is available.");
                    return null;
                }
                var mat = new Material(shader);
                assets.Track(mat);
                mat.SetFloat("_Cull", 0); // Cull Off = render both sides
                if (!string.IsNullOrEmpty(texturePath) && File.Exists(texturePath))
                {
                    var texData = File.ReadAllBytes(texturePath);
                    var tex = new Texture2D(2, 2);
                    if (ImageConversion.LoadImage(tex, texData))
                    {
                        tex.filterMode = FilterMode.Bilinear;
                        mat.mainTexture = tex;
                        assets.Track(tex);
                    }
                    else
                    {
                        UnityEngine.Object.Destroy(tex);
                        SkillsPlugin.Log.LogWarning(
                            $"ObjLoader: Ignored invalid texture '{texturePath}'.");
                    }
                }
                mr.sharedMaterial = mat;

                // Add collider for interaction
                var bc = go.AddComponent<BoxCollider>();
                bc.center = mesh.bounds.center;
                bc.size = mesh.bounds.size;

                SkillsPlugin.Log.LogInfo(
                    $"ObjLoader: Loaded {mesh.name} ({triVerts.Count} verts, " +
                    $"{triIndices.Count / 3} tris, {invalidRecords} invalid records skipped)");
                return go;
            }
            catch (Exception ex)
            {
                SkillsPlugin.Log.LogError($"ObjLoader: Failed to load {objPath}: {ex.Message}");
                return null;
            }
        }

        static bool TryParseFloat(string value, out float result)
        {
            return float.TryParse(value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out result) &&
                !float.IsNaN(result) && !float.IsInfinity(result);
        }

        static bool TryParseFaceVertex(string value, int positionCount,
            int uvCount, int normalCount, out int[] result)
        {
            // Formats: v, v/vt, v/vt/vn, v//vn
            result = null;
            var parts = value.Split('/');
            if (parts.Length < 1 || parts.Length > 3 ||
                !int.TryParse(parts[0], NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int rawPosition)) return false;
            int position = ResolveObjIndex(rawPosition, positionCount);
            if (position < 0) return false;

            int uv = -1;
            if (parts.Length > 1 && parts[1].Length > 0)
            {
                if (!int.TryParse(parts[1], NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int rawUv)) return false;
                uv = ResolveObjIndex(rawUv, uvCount);
                if (uv < 0) return false;
            }

            int normal = -1;
            if (parts.Length > 2 && parts[2].Length > 0)
            {
                if (!int.TryParse(parts[2], NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int rawNormal)) return false;
                normal = ResolveObjIndex(rawNormal, normalCount);
                if (normal < 0) return false;
            }
            result = new[] { position, uv, normal };
            return true;
        }

        static int ResolveObjIndex(int index, int count)
        {
            if (index > 0) index--;
            else if (index < 0) index = count + index;
            else return -1;
            return index >= 0 && index < count ? index : -1;
        }

        static void AddVertex(int[] face, List<Vector3> positions,
            List<Vector3> normals, List<Vector2> uvs,
            List<Vector3> triVerts, List<Vector3> triNorms,
            List<Vector2> triUVs, List<int> triIndices)
        {
            int idx = triVerts.Count;
            triIndices.Add(idx);

            // Position (flip X for Unity's left-handed coords)
            var p = positions[face[0]];
            triVerts.Add(new Vector3(-p.x, p.y, p.z));

            // UV
            if (face[1] >= 0 && face[1] < uvs.Count)
                triUVs.Add(uvs[face[1]]);
            else
                triUVs.Add(Vector2.zero);

            // Normal
            if (face[2] >= 0 && face[2] < normals.Count)
            {
                var n = normals[face[2]];
                triNorms.Add(new Vector3(-n.x, n.y, n.z));
            }
        }
    }
}
