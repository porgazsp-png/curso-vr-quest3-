using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Monta aldeanos completos con los packs de Quaternius:
///   ropa (Modular Character Outfits - Fantasy) + cabeza, ojos, cejas y pelo (Universal Base Characters).
///
/// Los trajes NO traen cabeza y la versión gratuita de los personajes base solo trae el cuerpo entero.
/// Por eso ExtraerCabeza() recorta la cabeza del cuerpo entero: se queda con los triángulos
/// "pegados" (skinning) a los huesos del cuello y la cabeza.
///
/// Todas las piezas usan el mismo esqueleto (Universal Rig), así que basta con
/// enlazar cada pieza a los huesos del traje buscándolos por nombre.
/// </summary>
public static class ConstructorPersonajes
{
    public const string Raiz = "Assets/ThirdParty/Quaternius/Characters/";
    public const string Salida = "Assets/Pueblo/Personajes/";

    /// <summary>Crea (o actualiza) un asset de malla con solo la cabeza y el cuello del cuerpo completo.</summary>
    public static Mesh ExtraerCabeza(string cuerpoFbx, string nombreAsset)
    {
        var cuerpo = AssetDatabase.LoadAssetAtPath<GameObject>(Raiz + cuerpoFbx);
        var smr = cuerpo.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                        .OrderByDescending(r => r.sharedMesh.vertexCount).First();
        Mesh origen = smr.sharedMesh;

        // Huesos que cuentan como "cabeza": cuello, cabeza y todo lo que cuelga de ellos
        var huesosCabeza = new HashSet<int>();
        for (int i = 0; i < smr.bones.Length; i++)
            for (var t = smr.bones[i]; t != null; t = t.parent)
            {
                string n = t.name.ToLower();
                if (n.Contains("neck") || n.Contains("head")) { huesosCabeza.Add(i); break; }
            }

        // Un vértice es de la cabeza si su hueso con más peso es de la cabeza
        var pesos = origen.boneWeights;
        var esCabeza = new bool[origen.vertexCount];
        for (int v = 0; v < pesos.Length; v++)
            esCabeza[v] = huesosCabeza.Contains(pesos[v].boneIndex0);

        // Copiar solo los triángulos de la cabeza, compactando los vértices
        var mapa = new Dictionary<int, int>();
        var verts = new List<Vector3>(); var norms = new List<Vector3>(); var tans = new List<Vector4>();
        var uvs = new List<Vector2>(); var ws = new List<BoneWeight>();
        var oV = origen.vertices; var oN = origen.normals; var oT = origen.tangents; var oUV = origen.uv;
        var submallas = new List<List<int>>();
        for (int s = 0; s < origen.subMeshCount; s++)
        {
            var tris = origen.GetTriangles(s); var nuevos = new List<int>();
            for (int k = 0; k < tris.Length; k += 3)
            {
                if (!(esCabeza[tris[k]] && esCabeza[tris[k + 1]] && esCabeza[tris[k + 2]])) continue;
                for (int j = 0; j < 3; j++)
                {
                    int v = tris[k + j];
                    if (!mapa.TryGetValue(v, out int nv))
                    {
                        nv = verts.Count; mapa[v] = nv;
                        verts.Add(oV[v]); norms.Add(oN[v]); uvs.Add(oUV[v]); ws.Add(pesos[v]);
                        if (oT.Length > 0) tans.Add(oT[v]);
                    }
                    nuevos.Add(nv);
                }
            }
            submallas.Add(nuevos);
        }

        string ruta = Salida + nombreAsset + ".asset";
        var malla = AssetDatabase.LoadAssetAtPath<Mesh>(ruta);
        bool nueva = malla == null;
        if (nueva) malla = new Mesh();
        malla.Clear();
        malla.name = nombreAsset;
        malla.SetVertices(verts); malla.SetNormals(norms); malla.SetUVs(0, uvs);
        if (tans.Count == verts.Count) malla.SetTangents(tans);
        malla.boneWeights = ws.ToArray();
        malla.bindposes = origen.bindposes;
        malla.subMeshCount = submallas.Count;
        for (int s = 0; s < submallas.Count; s++) malla.SetTriangles(submallas[s], s);
        malla.RecalculateBounds();
        if (nueva) AssetDatabase.CreateAsset(malla, ruta);
        EditorUtility.SetDirty(malla);
        AssetDatabase.SaveAssets();
        return malla;
    }

    /// <summary>
    /// Monta un aldeano y lo guarda como prefab.
    /// cuerpoFbx: de dónde salen ojos y cejas (Base/Superhero_Male_FullBody.fbx o el Female).
    /// pelo: FBX de Hair/ (o null para calvo). Devuelve la ruta del prefab.
    /// </summary>
    public static string CrearAldeano(string nombre, string trajeFbx, Mesh cabeza, Material piel,
                                      string cuerpoFbx, string pelo, string cejas, Material ropa = null)
    {
        var traje = AssetDatabase.LoadAssetAtPath<GameObject>(Raiz + trajeFbx);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(traje);
        PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        go.name = nombre;

        // Diccionario de huesos del traje por nombre
        var huesos = go.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
        var refTraje = go.GetComponentsInChildren<SkinnedMeshRenderer>(true).First();

        if (ropa != null)
            foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] != null && mats[i].name.Contains("Peasant") || mats[i] != null && mats[i].name.Contains("Ranger")) mats[i] = ropa;
                r.sharedMaterials = mats;
            }

        // Cabeza (recortada) con los huesos del cuerpo base, reenlazados al traje
        var cuerpo = AssetDatabase.LoadAssetAtPath<GameObject>(Raiz + cuerpoFbx);
        var smrCuerpo = cuerpo.GetComponentsInChildren<SkinnedMeshRenderer>(true).OrderByDescending(r => r.sharedMesh.vertexCount).First();
        Pieza(go.transform, "Cabeza", cabeza, new[] { piel }, smrCuerpo, huesos, refTraje);

        // Ojos del cuerpo base (y cejas/pelo de los FBX de pelo)
        foreach (var r in cuerpo.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (r.name.StartsWith("Eyes"))
                Pieza(go.transform, "Ojos", r.sharedMesh, r.sharedMaterials, r, huesos, refTraje);
        foreach (var extra in new[] { cejas, pelo })
        {
            if (string.IsNullOrEmpty(extra)) continue;
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(Raiz + "Hair/" + extra + ".fbx");
            foreach (var r in fbx.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                Pieza(go.transform, extra, r.sharedMesh, r.sharedMaterials, r, huesos, refTraje);
        }

        // Animator: sin animar cuando no se ve (requisito de rendimiento en Quest)
        // (los trajes que copian el avatar de otro modelo no traen Animator: se lo ponemos)
        var anim = go.GetComponent<Animator>();
        if (anim == null)
        {
            anim = go.AddComponent<Animator>();
            anim.avatar = ((ModelImporter)AssetImporter.GetAtPath(Raiz + trajeFbx)).sourceAvatar;
        }
        anim.cullingMode = AnimatorCullingMode.CullCompletely;
        anim.applyRootMotion = false;

        // LOD: si ocupa menos del 4 % de la pantalla (lejos), no se dibuja y por tanto tampoco se anima
        var renderers = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        foreach (var r in renderers) r.updateWhenOffscreen = false;
        var lod = go.AddComponent<LODGroup>();
        lod.SetLODs(new[] { new LOD(0.04f, renderers) });
        lod.RecalculateBounds();

        System.IO.Directory.CreateDirectory(Salida);
        string ruta = Salida + nombre + ".prefab";
        PrefabUtility.SaveAsPrefabAsset(go, ruta);
        Object.DestroyImmediate(go);
        return ruta;
    }

    static void Pieza(Transform raiz, string nombre, Mesh malla, Material[] mats, SkinnedMeshRenderer origen,
                      Dictionary<string, Transform> huesos, SkinnedMeshRenderer refTraje)
    {
        var go = new GameObject(nombre);
        go.transform.SetParent(raiz, false);
        var smr = go.AddComponent<SkinnedMeshRenderer>();
        smr.sharedMesh = malla;
        smr.sharedMaterials = mats;
        smr.bones = origen.bones.Select(b => b != null && huesos.TryGetValue(b.name, out var t) ? t : null).ToArray();
        if (origen.rootBone != null && huesos.TryGetValue(origen.rootBone.name, out var rb))
        {
            smr.rootBone = rb;
            smr.localBounds = origen.localBounds;   // mismo esqueleto: los límites originales sirven
        }
        else
        {
            smr.rootBone = refTraje.rootBone;
            smr.localBounds = refTraje.localBounds;
        }
    }
}
