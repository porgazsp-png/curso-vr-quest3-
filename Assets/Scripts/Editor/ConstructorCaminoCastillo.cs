using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

/// <summary>
/// FASE 1 del castillo: el camino desde el pueblo, el río, el puente y la colina.
/// Menú: VR Quest 3 > Castillo > 1. Camino, río y colina
///
/// Qué construye (todo dentro de "Camino al castillo", que se rehace entero si lo vuelves a ejecutar):
///  - Colina: malla low poly facetada (verde como el suelo) con una meseta de 6 m para el castillo.
///    El camino se "talla" en la colina: donde pasa, el terreno se aplana a su altura.
///  - Camino de tierra: sale por el norte del pueblo, cruza el puente y sube rodeando la colina (≤ ~14°).
///  - Río de este a oeste: agua opaca con ondas que corren (AguaQueFluye), orillas de tierra,
///    rocas y cañas. Muros invisibles en las orillas: no se puede entrar en el agua andando,
///    y el agua tiene collider sin zona de teletransporte, así que tampoco se puede teletransportar dentro.
///  - Puente de piedra en arco con calzada de adoquín y pretiles (muretes) a los lados.
///  - Mojones, vallas de madera en la subida y 3 faroles medievales (poste + farol de Quaternius) con luz "Baked" (no cuesta nada en tiempo real;
///    se verá al hornear la iluminación, que se hace al final y avisando).
/// Además (fuera de "Camino al castillo"):
///  - Desactiva el muro invisible norte del pueblo y añade "Limites ampliados".
///  - Niebla hasta 120 m y cámara hasta 130 m, para ver el castillo desde lejos.
///
/// Mallas y materiales generados: Assets/Pueblo/Castillo/. Todo lo que no se mueve queda Static.
/// </summary>
public static class ConstructorCaminoCastillo
{
    const string Salida = "Assets/Pueblo/Castillo/";
    const string RaizNombre = "Camino al castillo";

    // ---------- Medidas del mundo (metros) ----------
    static readonly Vector2 CentroColina = new Vector2(0f, 64f);   // (x, z) del centro del castillo
    const float RadioMeseta = 20f, RadioBase = 34f, AlturaColina = 6f;

    const float RioZ = 26f;          // el río va de oeste a este, ondulando alrededor de esta z
    const float RioX0 = -48f, RioX1 = 48f;
    const float PuenteX = 0.5f, PuenteZ0 = 21.5f, PuenteZ1 = 30.5f, PuenteAncho = 3.2f, PuenteArco = 0.9f;

    const float AnchoCamino = 3.6f;

    // Puntos por los que pasa el camino (x, altura, z). Se unen con una curva suave (Catmull-Rom).
    static readonly Vector3[] PuntosCamino =
    {
        new Vector3(1f, 0f, 5f), new Vector3(-1.5f, 0f, 12f), new Vector3(0.5f, 0f, 18f), new Vector3(PuenteX, 0f, PuenteZ0),
        new Vector3(PuenteX, 0f, PuenteZ1), new Vector3(1.5f, 0f, 32.5f), new Vector3(7f, 0.3f, 34.5f), new Vector3(14f, 2f, 38f),
        new Vector3(15f, 3.2f, 43f), new Vector3(9f, 5f, 45f), new Vector3(3f, 5.9f, 46f), new Vector3(0f, 6f, 46.5f), new Vector3(0f, 6f, 48.5f),
    };

    static System.Random azar;
    static Collider colSuelo, colColina;
    static List<Vector3> camino;   // muestras de la curva del camino cada ~0,5 m

    [MenuItem("VR Quest 3/Castillo/1. Camino, río y colina")]
    public static void Construir()
    {
        var escena = EditorSceneManager.GetActiveScene();
        azar = new System.Random(1234);   // misma semilla = mismo resultado cada vez
        Directory.CreateDirectory(Salida);

        var suelo = GameObject.Find("Environment/Ground");
        if (suelo == null) { Debug.LogError("[ConstructorCaminoCastillo] No encuentro Environment/Ground."); return; }
        colSuelo = suelo.GetComponent<Collider>();
        var zonaSuelo = suelo.GetComponent<TeleportationArea>();

        var vieja = escena.GetRootGameObjects().FirstOrDefault(g => g.name == RaizNombre);
        if (vieja != null) Undo.DestroyObjectImmediate(vieja);
        var raiz = new GameObject(RaizNombre);
        Undo.RegisterCreatedObjectUndo(raiz, "Camino al castillo");

        var cesped = AssetDatabase.LoadAssetAtPath<Material>("Assets/NatureScene/Ground_Grass.mat");
        var tierra = AssetDatabase.LoadAssetAtPath<Material>("Assets/NatureScene/Ground_Dirt.mat");
        var adoquin = AssetDatabase.LoadAssetAtPath<Material>("Assets/ThirdParty/Quaternius/MedievalVillage/Materials/MI_UnevenBrick.mat");
        var piedra = AssetDatabase.LoadAssetAtPath<Material>("Assets/ThirdParty/Quaternius/MedievalVillage/Materials/MI_RockTrim.mat");
        if (cesped == null || tierra == null || adoquin == null || piedra == null)
        {
            Debug.LogError("[ConstructorCaminoCastillo] Falta algún material (Ground_Grass, Ground_Dirt, MI_UnevenBrick, MI_RockTrim).");
            return;
        }

        Limites();
        NieblaYCamara();

        camino = MuestrearCamino();
        Colina(raiz.transform, cesped, zonaSuelo);
        Physics.SyncTransforms();   // para poder lanzar rayos contra la colina recién creada
        Rio(raiz.transform, tierra);
        CaminoMalla(raiz.transform, tierra);
        Puente(raiz.transform, adoquin, piedra, zonaSuelo);
        Decoracion(raiz.transform);
        SueloLejano(raiz.transform, cesped);

        // Marcador para la Fase 2
        var puerta = new GameObject("Puerta del castillo (Fase 2)");
        puerta.transform.SetParent(raiz.transform, false);
        puerta.transform.position = new Vector3(0f, AlturaColina, 50f);

        EditorSceneManager.MarkSceneDirty(escena);
        AssetDatabase.SaveAssets();
        Debug.Log("[ConstructorCaminoCastillo] Camino, río, puente y colina creados. Guarda la escena (Ctrl+S).");
    }

    // ======================================================================
    // Límites, niebla y cámara
    // ======================================================================

    static void Limites()
    {
        var muroNorte = GameObject.Find("Environment/Boundary/Wall0");
        if (muroNorte != null && muroNorte.activeSelf)
        {
            Undo.RecordObject(muroNorte, "Limites");
            muroNorte.SetActive(false);   // desactivado, no borrado: se puede volver a activar
        }

        var entorno = GameObject.Find("Environment");
        var viejo = entorno != null ? entorno.transform.Find("Limites ampliados") : null;
        if (viejo != null) Undo.DestroyObjectImmediate(viejo.gameObject);

        var limites = new GameObject("Limites ampliados");
        Undo.RegisterCreatedObjectUndo(limites, "Limites");
        if (entorno != null) limites.transform.SetParent(entorno.transform, false);

        // Los muros este, oeste y sur del pueblo se quedan; esto encierra la zona nueva al norte
        Muro(limites.transform, "Oeste", new Vector3(-40f, 3f, 53.5f), new Vector3(2f, 6f, 91f));
        Muro(limites.transform, "Este", new Vector3(40f, 3f, 53.5f), new Vector3(2f, 6f, 91f));
        Muro(limites.transform, "Norte", new Vector3(0f, 3f, 99f), new Vector3(82f, 6f, 2f));
        Muro(limites.transform, "Union oeste", new Vector3(-31.5f, 3f, 8f), new Vector3(17f, 6f, 2f));
        Muro(limites.transform, "Union este", new Vector3(33.5f, 3f, 8f), new Vector3(13f, 6f, 2f));
    }

    static void Muro(Transform padre, string nombre, Vector3 centro, Vector3 tam)
    {
        var go = new GameObject("Muro " + nombre);
        go.transform.SetParent(padre, false);
        go.transform.position = centro;
        go.AddComponent<BoxCollider>().size = tam;
        go.isStatic = true;
    }

    static void NieblaYCamara()
    {
        RenderSettings.fogStartDistance = 30f;
        RenderSettings.fogEndDistance = 120f;
        var cam = Camera.main;
        if (cam != null)
        {
            Undo.RecordObject(cam, "Camara lejana");
            cam.farClipPlane = 130f;
        }
    }

    // ======================================================================
    // Camino (curva)
    // ======================================================================

    static List<Vector3> MuestrearCamino()
    {
        var lista = new List<Vector3>();
        for (int i = 0; i < PuntosCamino.Length - 1; i++)
        {
            Vector3 p0 = PuntosCamino[Mathf.Max(i - 1, 0)], p1 = PuntosCamino[i], p2 = PuntosCamino[i + 1];
            Vector3 p3 = PuntosCamino[Mathf.Min(i + 2, PuntosCamino.Length - 1)];
            int pasos = Mathf.Max(2, Mathf.CeilToInt(Vector3.Distance(p1, p2) / 0.5f));
            for (int k = 0; k < pasos; k++)
            {
                var p = CatmullRom(p0, p1, p2, p3, (float)k / pasos);
                p.y = Mathf.Max(0f, p.y);
                lista.Add(p);
            }
        }
        lista.Add(PuntosCamino[PuntosCamino.Length - 1]);
        return lista;
    }

    static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    /// <summary>Distancia horizontal al camino y altura del camino en el punto más cercano.</summary>
    static float DistanciaAlCamino(float x, float z, out float alturaCamino)
    {
        float mejor = float.MaxValue; alturaCamino = 0f;
        for (int i = 0; i < camino.Count - 1; i++)
        {
            Vector2 a = new Vector2(camino[i].x, camino[i].z), b = new Vector2(camino[i + 1].x, camino[i + 1].z), p = new Vector2(x, z);
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
            float d = (p - (a + ab * t)).sqrMagnitude;
            if (d < mejor) { mejor = d; alturaCamino = Mathf.Lerp(camino[i].y, camino[i + 1].y, t); }
        }
        return Mathf.Sqrt(mejor);
    }

    static bool SobreElPuente(Vector3 p) =>
        Mathf.Abs(p.x - PuenteX) < 3f && p.z > PuenteZ0 - 0.1f && p.z < PuenteZ1 + 0.1f;

    // ======================================================================
    // Colina
    // ======================================================================

    /// <summary>Altura natural de la colina (sin el camino): meseta, ladera suave y algo de irregularidad.</summary>
    static float AlturaNatural(float x, float z)
    {
        float d = Vector2.Distance(new Vector2(x, z), CentroColina);
        float t = Mathf.Clamp01((d - RadioMeseta) / (RadioBase - RadioMeseta));
        float h = AlturaColina * (1f - Mathf.SmoothStep(0f, 1f, t));
        // Irregularidad solo en la ladera (la meseta queda plana para construir)
        h += Ruido(x, z) * 0.45f * Mathf.Sin(t * Mathf.PI);
        return h;
    }

    static float Ruido(float x, float z) => Mathf.PerlinNoise(x * 0.15f + 13.7f, z * 0.15f + 7.3f) * 2f - 1f;

    static float AlturaConCamino(float x, float z)
    {
        float h = AlturaNatural(x, z);
        float d = DistanciaAlCamino(x, z, out float hc);
        const float Plano = 2.6f, Transicion = 4.5f;   // ancho que se aplana y ancho de la transición
        if (d < Plano) return hc;
        if (d < Plano + Transicion) return Mathf.Lerp(hc, h, Mathf.SmoothStep(0f, 1f, (d - Plano) / Transicion));
        return h;
    }

    static void Colina(Transform raiz, Material cesped, TeleportationArea zonaSuelo)
    {
        const float X0 = -38f, X1 = 38f, Z0 = 28f, Z1 = 100f, Paso = 1.5f;
        int nx = Mathf.RoundToInt((X1 - X0) / Paso), nz = Mathf.RoundToInt((Z1 - Z0) / Paso);
        var alturas = new float[nx + 1, nz + 1];
        for (int i = 0; i <= nx; i++)
            for (int j = 0; j <= nz; j++)
                // 3 cm por debajo del suelo donde es llano: así no "parpadea" con el plano del suelo
                alturas[i, j] = AlturaConCamino(X0 + i * Paso, Z0 + j * Paso) - 0.03f;

        var v = new List<Vector3>(); var t = new List<int>();
        for (int i = 0; i < nx; i++)
            for (int j = 0; j < nz; j++)
            {
                Vector3 a = new Vector3(X0 + i * Paso, alturas[i, j], Z0 + j * Paso);
                Vector3 b = new Vector3(X0 + (i + 1) * Paso, alturas[i + 1, j], Z0 + j * Paso);
                Vector3 c = new Vector3(X0 + i * Paso, alturas[i, j + 1], Z0 + (j + 1) * Paso);
                Vector3 d = new Vector3(X0 + (i + 1) * Paso, alturas[i + 1, j + 1], Z0 + (j + 1) * Paso);
                if (((i + j) & 1) == 0) { Tri(v, t, a, c, b); Tri(v, t, b, c, d); }   // alternar la diagonal: más natural
                else { Tri(v, t, a, c, d); Tri(v, t, a, d, b); }
            }

        var malla = CrearMalla("Colina", v, t, null);
        var go = Objeto(raiz, "Colina", malla, cesped);
        go.AddComponent<MeshCollider>().sharedMesh = malla;
        colColina = go.GetComponent<MeshCollider>();
        ZonaTeletransporte(go, zonaSuelo);
    }

    // ======================================================================
    // Río
    // ======================================================================

    static float RioCentro(float x) => RioZ + 1.5f * Mathf.Sin(x * 0.09f);
    static float RioMedioAncho(float x) => 3f + 0.6f * Mathf.Sin(x * 0.05f + 1f);

    static void Rio(Transform raiz, Material tierra)
    {
        var padre = new GameObject("Rio").transform;
        padre.SetParent(raiz, false);

        // Agua: tira ondulante a 5 cm del suelo
        var v = new List<Vector3>(); var t = new List<int>(); var uv = new List<Vector2>();
        const float Paso = 2f, AlturaAgua = 0.05f;
        for (float x = RioX0; x < RioX1 - 0.01f; x += Paso)
        {
            float x2 = x + Paso;
            Vector3 a = new Vector3(x, AlturaAgua, RioCentro(x) - RioMedioAncho(x));
            Vector3 b = new Vector3(x2, AlturaAgua, RioCentro(x2) - RioMedioAncho(x2));
            Vector3 c = new Vector3(x, AlturaAgua, RioCentro(x) + RioMedioAncho(x));
            Vector3 d = new Vector3(x2, AlturaAgua, RioCentro(x2) + RioMedioAncho(x2));
            int baseIndice = v.Count;
            v.AddRange(new[] { a, b, c, d });
            foreach (var p in new[] { a, b, c, d }) uv.Add(new Vector2(p.x / 4f, (p.z - RioZ) / 4f));
            t.AddRange(new[] { baseIndice, baseIndice + 2, baseIndice + 1, baseIndice + 1, baseIndice + 2, baseIndice + 3 });
        }
        var mallaAgua = CrearMalla("Agua", v, t, uv, suave: true);
        var agua = Objeto(padre, "Agua", mallaAgua, MaterialAgua(), batching: false);
        agua.AddComponent<MeshCollider>().sharedMesh = mallaAgua;   // el teletransporte choca aquí y no es zona válida
        agua.AddComponent<AguaQueFluye>();

        // Orillas de tierra a cada lado (borde exterior irregular)
        foreach (int lado in new[] { -1, 1 })
        {
            var vo = new List<Vector3>(); var to = new List<int>();
            for (float x = RioX0; x < RioX1 - 0.01f; x += Paso)
            {
                float x2 = x + Paso;
                float o1 = 0.9f + Rand(0f, 0.8f), o2 = 0.9f + Rand(0f, 0.8f);
                Vector3 dentro1 = new Vector3(x, 0.045f, RioCentro(x) + lado * RioMedioAncho(x));
                Vector3 dentro2 = new Vector3(x2, 0.045f, RioCentro(x2) + lado * RioMedioAncho(x2));
                Vector3 fuera1 = new Vector3(x, 0.015f, dentro1.z + lado * o1);
                Vector3 fuera2 = new Vector3(x2, 0.015f, dentro2.z + lado * o2);
                if (lado < 0) { Tri(vo, to, fuera1, dentro1, fuera2); Tri(vo, to, fuera2, dentro1, dentro2); }
                else { Tri(vo, to, dentro1, fuera1, dentro2); Tri(vo, to, dentro2, fuera1, fuera2); }
            }
            Objeto(padre, lado < 0 ? "Orilla sur" : "Orilla norte", CrearMalla(lado < 0 ? "Orilla_Sur" : "Orilla_Norte", vo, to, null), tierra);

            // Muros invisibles en el borde del agua (menos bajo el puente). Capa "Ignore Raycast":
            // paran al jugador pero no estorban a los rayos de las manos ni al teletransporte.
            var muros = new GameObject(lado < 0 ? "Muros orilla sur" : "Muros orilla norte");
            muros.transform.SetParent(padre, false);
            muros.layer = 2;
            for (float x = RioX0; x < RioX1 - 0.01f; x += Paso)
            {
                float x2 = x + Paso;
                if (x2 > PuenteX - PuenteAncho * 0.5f - 0.5f && x < PuenteX + PuenteAncho * 0.5f + 0.5f) continue;
                Vector3 p1 = new Vector3(x, 0.6f, RioCentro(x) + lado * (RioMedioAncho(x) - 0.2f));
                Vector3 p2 = new Vector3(x2, 0.6f, RioCentro(x2) + lado * (RioMedioAncho(x2) - 0.2f));
                var m = new GameObject("Muro");
                m.layer = 2;
                m.transform.SetParent(muros.transform, false);
                m.transform.SetPositionAndRotation((p1 + p2) * 0.5f, Quaternion.LookRotation(p2 - p1));
                m.AddComponent<BoxCollider>().size = new Vector3(0.3f, 1.2f, Vector3.Distance(p1, p2) + 0.3f);
                m.isStatic = true;
            }
        }
    }

    static Material MaterialAgua()
    {
        string ruta = Salida + "Agua.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(ruta);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, ruta);
        }
        mat.SetColor("_BaseColor", new Color(0.20f, 0.45f, 0.52f));
        mat.SetFloat("_Smoothness", 0.85f);
        mat.SetFloat("_Metallic", 0f);
        mat.SetTexture("_BumpMap", TexturaOndas());
        mat.SetFloat("_BumpScale", 0.5f);
        mat.EnableKeyword("_NORMALMAP");
        EditorUtility.SetDirty(mat);
        return mat;
    }

    /// <summary>Mapa de normales de ondas, que se repite sin cortes (frecuencias enteras), generado una vez.</summary>
    static Texture2D TexturaOndas()
    {
        string ruta = Salida + "Agua_Ondas.png";
        if (!File.Exists(ruta))
        {
            const int N = 128;
            var alt = new float[N, N];
            for (int x = 0; x < N; x++)
                for (int y = 0; y < N; y++)
                {
                    float u = x * Mathf.PI * 2f / N, w = y * Mathf.PI * 2f / N;
                    alt[x, y] = Mathf.Sin(3 * u + w) * 0.5f + Mathf.Sin(u - 4 * w) * 0.3f
                              + Mathf.Sin(5 * u + 2 * w + 1.3f) * 0.2f + Mathf.Sin(-2 * u + 6 * w + 0.7f) * 0.15f;
                }
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
            for (int x = 0; x < N; x++)
                for (int y = 0; y < N; y++)
                {
                    float dx = alt[(x + 1) % N, y] - alt[(x + N - 1) % N, y];
                    float dy = alt[x, (y + 1) % N] - alt[x, (y + N - 1) % N];
                    var n = new Vector3(-dx * 2f, -dy * 2f, 1f).normalized;
                    tex.SetPixel(x, y, new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f));
                }
            File.WriteAllBytes(ruta, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(ruta);
            var imp = (TextureImporter)AssetImporter.GetAtPath(ruta);
            imp.textureType = TextureImporterType.NormalMap;
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(ruta);
    }

    // ======================================================================
    // Camino (malla)
    // ======================================================================

    static void CaminoMalla(Transform raiz, Material tierra)
    {
        var v = new List<Vector3>(); var t = new List<int>();
        for (int i = 0; i < camino.Count - 1; i++)
        {
            if (SobreElPuente(camino[i]) && SobreElPuente(camino[i + 1])) continue;   // ahí va la calzada del puente
            Bordes(i, out var l0, out var r0);
            Bordes(i + 1, out var l1, out var r1);
            Tri(v, t, l0, l1, r0);
            Tri(v, t, r0, l1, r1);
        }
        Objeto(raiz, "Camino", CrearMalla("Camino", v, t, null), tierra);
    }

    /// <summary>Bordes izquierdo y derecho del camino en la muestra i, apoyados sobre el terreno.</summary>
    static void Bordes(int i, out Vector3 izquierda, out Vector3 derecha)
    {
        Vector3 tangente = camino[Mathf.Min(i + 1, camino.Count - 1)] - camino[Mathf.Max(i - 1, 0)];
        tangente.y = 0f; tangente.Normalize();
        var lado = new Vector3(tangente.z, 0f, -tangente.x);
        float medio = AnchoCamino * 0.5f + Mathf.PerlinNoise(i * 0.3f, 3.1f) * 0.3f - 0.15f;   // bordes algo irregulares
        izquierda = Apoyar(camino[i] - lado * medio, 0.05f);
        derecha = Apoyar(camino[i] + lado * medio, 0.05f);
    }

    /// <summary>Pone el punto sobre la colina o el suelo (lo que esté más alto) más un pequeño margen.</summary>
    static Vector3 Apoyar(Vector3 p, float margen)
    {
        float y = 0f;
        var rayo = new Ray(new Vector3(p.x, 60f, p.z), Vector3.down);
        if (colColina != null && colColina.Raycast(rayo, out var h1, 120f)) y = Mathf.Max(y, h1.point.y);
        if (colSuelo != null && colSuelo.Raycast(rayo, out var h2, 120f)) y = Mathf.Max(y, h2.point.y);
        return new Vector3(p.x, y + margen, p.z);
    }

    // ======================================================================
    // Puente
    // ======================================================================

    static void Puente(Transform raiz, Material adoquin, Material piedra, TeleportationArea zonaSuelo)
    {
        var puente = new GameObject("Puente").transform;
        puente.SetParent(raiz, false);

        const int N = 18; const float Grosor = 0.45f, AltoPretil = 0.85f, AnchoPretil = 0.3f;
        float medio = PuenteAncho * 0.5f, L = PuenteZ1 - PuenteZ0;
        float Y(int k) => PuenteArco * Mathf.Sin(Mathf.PI * k / N);
        float Z(int k) => PuenteZ0 + L * k / N;

        // Calzada (por donde se anda): adoquín como las calles del pueblo
        var cv = new List<Vector3>(); var ct = new List<int>();
        for (int k = 0; k < N; k++)
            Quad(cv, ct, new Vector3(PuenteX - medio, Y(k), Z(k)), new Vector3(PuenteX + medio, Y(k), Z(k)),
                 new Vector3(PuenteX + medio, Y(k + 1), Z(k + 1)), new Vector3(PuenteX - medio, Y(k + 1), Z(k + 1)), Vector3.up);
        var mallaCalzada = CrearMalla("Puente_Calzada", cv, ct, PlanarUV(cv, ct));
        var calzada = Objeto(puente, "Calzada", mallaCalzada, adoquin);
        calzada.AddComponent<MeshCollider>().sharedMesh = mallaCalzada;
        ZonaTeletransporte(calzada, zonaSuelo);

        // Arco: costados y panza del puente en piedra
        var av = new List<Vector3>(); var at = new List<int>();
        for (int k = 0; k < N; k++)
        {
            foreach (int s in new[] { -1, 1 })
            {
                float x = PuenteX + s * medio;
                Quad(av, at, new Vector3(x, Y(k) - Grosor, Z(k)), new Vector3(x, Y(k), Z(k)),
                     new Vector3(x, Y(k + 1), Z(k + 1)), new Vector3(x, Y(k + 1) - Grosor, Z(k + 1)), new Vector3(s, 0f, 0f));
            }
            Quad(av, at, new Vector3(PuenteX - medio, Y(k) - Grosor, Z(k)), new Vector3(PuenteX + medio, Y(k) - Grosor, Z(k)),
                 new Vector3(PuenteX + medio, Y(k + 1) - Grosor, Z(k + 1)), new Vector3(PuenteX - medio, Y(k + 1) - Grosor, Z(k + 1)), Vector3.down);
        }
        Objeto(puente, "Arco", CrearMalla("Puente_Arco", av, at, PlanarUV(av, at)), piedra);

        // Pretiles: muretes de piedra a cada lado, con collider para no caerse
        foreach (int s in new[] { -1, 1 })
        {
            var pv = new List<Vector3>(); var pt = new List<int>();
            float xi = PuenteX + s * (medio - AnchoPretil), xo = PuenteX + s * (medio + 0.05f);
            for (int k = 0; k < N; k++)
            {
                float y0 = Y(k), y1 = Y(k + 1), z0 = Z(k), z1 = Z(k + 1);
                Quad(pv, pt, new Vector3(xi, y0, z0), new Vector3(xi, y0 + AltoPretil, z0), new Vector3(xi, y1 + AltoPretil, z1), new Vector3(xi, y1, z1), new Vector3(-s, 0f, 0f));
                Quad(pv, pt, new Vector3(xo, y0 - Grosor, z0), new Vector3(xo, y0 + AltoPretil, z0), new Vector3(xo, y1 + AltoPretil, z1), new Vector3(xo, y1 - Grosor, z1), new Vector3(s, 0f, 0f));
                Quad(pv, pt, new Vector3(xi, y0 + AltoPretil, z0), new Vector3(xo, y0 + AltoPretil, z0), new Vector3(xo, y1 + AltoPretil, z1), new Vector3(xi, y1 + AltoPretil, z1), Vector3.up);
            }
            // tapas de los extremos
            Quad(pv, pt, new Vector3(xi, 0f, Z(0)), new Vector3(xo, 0f, Z(0)), new Vector3(xo, AltoPretil, Z(0)), new Vector3(xi, AltoPretil, Z(0)), Vector3.back);
            Quad(pv, pt, new Vector3(xi, 0f, Z(N)), new Vector3(xo, 0f, Z(N)), new Vector3(xo, AltoPretil, Z(N)), new Vector3(xi, AltoPretil, Z(N)), Vector3.forward);
            var nombre = s < 0 ? "Pretil oeste" : "Pretil este";
            var malla = CrearMalla("Puente_" + nombre.Replace(' ', '_'), pv, pt, PlanarUV(pv, pt));
            Objeto(puente, nombre, malla, piedra).AddComponent<MeshCollider>().sharedMesh = malla;

            // Pilares en las 4 esquinas
            foreach (float z in new[] { PuenteZ0, PuenteZ1 })
            {
                var pilar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pilar.name = "Pilar";
                pilar.transform.SetParent(puente, false);
                pilar.transform.position = new Vector3(PuenteX + s * (medio - AnchoPretil * 0.5f + 0.02f), 0.55f, z);
                pilar.transform.localScale = new Vector3(0.48f, 1.1f, 0.48f);
                pilar.GetComponent<MeshRenderer>().sharedMaterial = piedra;
                Estatico(pilar, true);
            }
        }
    }

    // ======================================================================
    // Decoración
    // ======================================================================

    const string Nat = "Assets/ThirdParty/Quaternius/StylizedNature/Models/";
    const string Vil = "Assets/ThirdParty/Quaternius/MedievalVillage/Models/";

    static void Decoracion(Transform raiz)
    {
        var deco = new GameObject("Decoracion").transform;
        deco.SetParent(raiz, false);

        // Mojones: piedras alargadas cada ~12 m en los tramos llanos, alternando lado
        var mojones = Grupo(deco, "Mojones");
        int lado = 1;
        for (int i = 6; i < camino.Count; i += 24)
        {
            if (SobreElPuente(camino[i]) || camino[i].y > 0.5f) continue;
            var p = PuntoAlLado(i, lado * 2.5f);
            // Escala uniforme: con el giro de importación, la Y local del modelo no es "arriba"
            Colocar(mojones, Nat + "Rock_Medium_" + (1 + azar.Next(3)) + ".fbx", Apoyar(p, -0.05f), Rand(0f, 360f), 0.22f);
            lado = -lado;
        }

        // Vallas de madera en el lado de fuera de la subida (donde la ladera cae)
        var vallas = Grupo(deco, "Vallas");
        float recorrido = 0f;
        for (int i = 1; i < camino.Count - 4; i++)
        {
            recorrido += Vector3.Distance(camino[i], camino[i - 1]);
            if (camino[i].y < 0.6f || camino[i].y > 5.6f || recorrido < 2.1f) continue;
            recorrido = 0f;
            int j = Mathf.Min(i + 4, camino.Count - 1);   // la valla mide 2 m: de la muestra i a la i+4
            float izq = AlturaNatural(PuntoAlLado(i, -3f).x, PuntoAlLado(i, -3f).z);
            float der = AlturaNatural(PuntoAlLado(i, 3f).x, PuntoAlLado(i, 3f).z);
            float s = izq < der ? -1f : 1f;
            Vector3 a = Apoyar(PuntoAlLado(i, s * (AnchoCamino * 0.5f + 0.25f)), 0f);
            Vector3 b = Apoyar(PuntoAlLado(j, s * (AnchoCamino * 0.5f + 0.25f)), 0f);
            Vector3 eje = (b - a).normalized;
            Vector3 frente = Vector3.Cross(eje, Vector3.up).normalized;
            var giro = Quaternion.LookRotation(frente, Vector3.Cross(frente, eje));   // X de la valla = dirección del camino (con su pendiente)
            var valla = Instanciar(vallas, Vil + "Prop_WoodenFence_Single.fbx");
            if (valla == null) break;
            valla.transform.SetPositionAndRotation((a + b) * 0.5f, giro * valla.transform.localRotation);
        }

        // Faroles (luz horneada: no cuesta nada en tiempo real)
        var faroles = Grupo(deco, "Faroles");
        Farol(faroles, new Vector3(3.2f, 0f, 8.5f));
        Farol(faroles, new Vector3(PuenteX + 2.6f, 0f, PuenteZ0 - 1.2f));
        Farol(faroles, new Vector3(PuenteX - 2.6f, 0f, PuenteZ1 + 1.2f));

        // Orillas: rocas y cañas (lejos del puente). Rocas de Quaternius: las de Kenney usan un material
        // turquesa pensado para otro sombreado y aquí salían rosas y azules.
        var orillas = Grupo(deco, "Orillas");
        for (int n = 0; n < 32; n++)
        {
            float x = Rand(RioX0 + 4f, RioX1 - 4f); if (Mathf.Abs(x - PuenteX) < 4f) continue;
            int s = azar.Next(2) == 0 ? -1 : 1;
            var p = new Vector3(x, -0.1f, RioCentro(x) + s * (RioMedioAncho(x) + Rand(-0.2f, 0.6f)));
            Colocar(orillas, Nat + "Rock_Medium_" + (1 + azar.Next(3)) + ".fbx", p, Rand(0f, 360f), Rand(0.15f, 0.35f));
        }
        for (int n = 0; n < 40; n++)
        {
            float x = Rand(RioX0 + 4f, RioX1 - 4f); if (Mathf.Abs(x - PuenteX) < 3.5f) continue;
            int s = azar.Next(2) == 0 ? -1 : 1;
            var p = new Vector3(x, 0f, RioCentro(x) + s * (RioMedioAncho(x) + Rand(-0.3f, 0.5f)));
            string cana = azar.Next(3) == 0 ? "Grass_Wispy_Tall.fbx" : "Grass_Common_Tall.fbx";
            Colocar(orillas, Nat + cana, p, Rand(0f, 360f), Rand(0.55f, 0.9f));
        }
        // Rocas grandes en la ladera: dan aspecto de "alto"
        var rocas = Grupo(deco, "Rocas colina");
        int puestas = 0;
        for (int intento = 0; intento < 200 && puestas < 14; intento++)
        {
            float ang = Rand(0f, Mathf.PI * 2f), r = Rand(RadioMeseta + 3f, RadioBase - 3f);
            float x = CentroColina.x + Mathf.Cos(ang) * r, z = CentroColina.y + Mathf.Sin(ang) * r;
            if (DistanciaAlCamino(x, z, out _) < 6f) continue;
            Colocar(rocas, Nat + "Rock_Medium_" + (1 + azar.Next(3)) + ".fbx", Apoyar(new Vector3(x, 0f, z), -0.4f), Rand(0f, 360f), Rand(0.9f, 1.8f));
            puestas++;
        }
    }

    /// <summary>
    /// Suelo verde enorme por debajo del suelo del pueblo (sin collider): con la niebla más lejana
    /// se veía dónde acaba el plano de 200 m. No toca el suelo original.
    /// </summary>
    static void SueloLejano(Transform raiz, Material cesped)
    {
        var plano = GameObject.CreatePrimitive(PrimitiveType.Plane);
        plano.name = "Suelo lejano";
        Object.DestroyImmediate(plano.GetComponent<Collider>());
        plano.transform.SetParent(raiz, false);
        plano.transform.position = new Vector3(0f, -0.06f, 0f);
        plano.transform.localScale = new Vector3(80f, 1f, 80f);   // el plano de Unity mide 10 m: 800 m
        var render = plano.GetComponent<MeshRenderer>();
        render.sharedMaterial = cesped;
        render.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        Estatico(plano, false);
    }

    /// <summary>Farol medieval: poste de madera con el farol de pared de Quaternius colgando hacia el camino.</summary>
    static void Farol(Transform padre, Vector3 pos)
    {
        // Corteza (la del potro): el WoodTrim de Quaternius es una "hoja de molduras" y en un cilindro sale a franjas
        var madera = AssetDatabase.LoadAssetAtPath<Material>("Assets/Pueblo/Armeria/Madera Potro.mat");
        pos = Apoyar(pos, 0f);

        // Que el brazo del farol mire al punto más cercano del camino
        Vector3 cercano = camino.OrderBy(p => (new Vector2(p.x, p.z) - new Vector2(pos.x, pos.z)).sqrMagnitude).First();
        Vector3 haciaCamino = cercano - pos; haciaCamino.y = 0f;

        var farol = new GameObject("Farol").transform;
        farol.SetParent(padre, false);
        farol.SetPositionAndRotation(pos, Quaternion.LookRotation(haciaCamino.sqrMagnitude > 0.01f ? haciaCamino : Vector3.forward));

        var poste = GameObject.CreatePrimitive(PrimitiveType.Cylinder);   // su CapsuleCollider impide atravesarlo
        poste.name = "Poste";
        poste.transform.SetParent(farol, false);
        poste.transform.localPosition = new Vector3(0f, 1.15f, 0f);
        poste.transform.localScale = new Vector3(0.14f, 1.15f, 0.14f);
        if (madera != null) poste.GetComponent<MeshRenderer>().sharedMaterial = madera;
        Estatico(poste, true);

        var lampara = Instanciar(farol, "Assets/ThirdParty/Quaternius/FantasyProps/Models/Lantern_Wall.fbx");
        if (lampara != null)
        {
            lampara.transform.localPosition = new Vector3(0f, 1.45f, 0.07f);   // el giro de importación se conserva
            lampara.transform.localScale *= 0.6f;
        }

        var luz = new GameObject("Luz").AddComponent<Light>();
        luz.transform.SetParent(farol, false);
        luz.transform.localPosition = new Vector3(0f, 1.85f, 0.42f);
        luz.type = LightType.Point;
        luz.color = new Color(1f, 0.68f, 0.38f);
        luz.range = 7f;
        luz.intensity = 1.5f;
        luz.shadows = LightShadows.None;
        luz.lightmapBakeType = LightmapBakeType.Baked;   // solo cuenta al hornear
    }

    static Vector3 PuntoAlLado(int i, float desplazamiento)
    {
        Vector3 tangente = camino[Mathf.Min(i + 1, camino.Count - 1)] - camino[Mathf.Max(i - 1, 0)];
        tangente.y = 0f; tangente.Normalize();
        return camino[i] + new Vector3(tangente.z, 0f, -tangente.x) * desplazamiento;
    }

    static Transform Grupo(Transform padre, string nombre)
    {
        var g = new GameObject(nombre).transform;
        g.SetParent(padre, false);
        return g;
    }

    static GameObject Instanciar(Transform padre, string ruta)
    {
        var modelo = AssetDatabase.LoadAssetAtPath<GameObject>(ruta);
        if (modelo == null) { Debug.LogWarning($"[ConstructorCaminoCastillo] No encuentro {ruta}"); return null; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(modelo, padre);
        Estatico(go, true);
        return go;
    }

    static GameObject Colocar(Transform padre, string ruta, Vector3 pos, float giroY, float escala)
    {
        var go = Instanciar(padre, ruta);
        if (go == null) return null;
        // Componer con el giro de importación (los FBX de Quaternius vienen con -90º en X): sustituirlo los tumba
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, giroY, 0f) * go.transform.localRotation);
        // Multiplicar, no sustituir: los FBX de Quaternius traen escala 100 en la raíz (vienen en centímetros)
        go.transform.localScale *= escala;
        return go;
    }

    // ======================================================================
    // Utilidades de mallas
    // ======================================================================

    static void Tri(List<Vector3> v, List<int> t, Vector3 a, Vector3 b, Vector3 c)
    {
        // Vértices propios por triángulo: sombreado facetado (estilo low poly)
        int i = v.Count;
        v.Add(a); v.Add(b); v.Add(c);
        t.Add(i); t.Add(i + 1); t.Add(i + 2);
    }

    /// <summary>Cuadrilátero que mira hacia "fuera" (si los vértices vienen al revés, se da la vuelta).</summary>
    static void Quad(List<Vector3> v, List<int> t, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 fuera)
    {
        if (Vector3.Dot(Vector3.Cross(b - a, c - a), fuera) < 0f) { var x = b; b = d; d = x; }
        int i = v.Count;
        v.Add(a); v.Add(b); v.Add(c); v.Add(d);
        t.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
    }

    /// <summary>UV planas en metros (1 repetición por metro) según hacia dónde mire cada triángulo.</summary>
    static List<Vector2> PlanarUV(List<Vector3> v, List<int> t)
    {
        var uv = new Vector2[v.Count];
        for (int k = 0; k < t.Count; k += 3)
        {
            Vector3 n = Vector3.Cross(v[t[k + 1]] - v[t[k]], v[t[k + 2]] - v[t[k]]);
            n = new Vector3(Mathf.Abs(n.x), Mathf.Abs(n.y), Mathf.Abs(n.z));
            for (int m = 0; m < 3; m++)
            {
                var p = v[t[k + m]];
                uv[t[k + m]] = n.y >= n.x && n.y >= n.z ? new Vector2(p.x, p.z) : n.x >= n.z ? new Vector2(p.z, p.y) : new Vector2(p.x, p.y);
            }
        }
        return uv.ToList();
    }

    static Mesh CrearMalla(string nombre, List<Vector3> v, List<int> t, List<Vector2> uv, bool suave = false)
    {
        var malla = new Mesh { name = nombre };
        if (v.Count > 65000) malla.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        malla.SetVertices(v);
        malla.SetTriangles(t, 0);
        if (uv != null) malla.SetUVs(0, uv);
        malla.RecalculateNormals();
        malla.RecalculateBounds();
        if (uv != null) malla.RecalculateTangents();   // necesarias para el mapa de normales

        // Guardar como asset: si ya existe se sobrescribe su contenido (las referencias siguen valiendo)
        string ruta = Salida + "Mallas/" + nombre + ".asset";
        Directory.CreateDirectory(Salida + "Mallas/");
        var existente = AssetDatabase.LoadAssetAtPath<Mesh>(ruta);
        if (existente != null)
        {
            EditorUtility.CopySerialized(malla, existente);
            Object.DestroyImmediate(malla);
            return existente;
        }
        AssetDatabase.CreateAsset(malla, ruta);
        return malla;
    }

    static GameObject Objeto(Transform padre, string nombre, Mesh malla, Material material, bool batching = true)
    {
        var go = new GameObject(nombre);
        go.transform.SetParent(padre, false);
        go.AddComponent<MeshFilter>().sharedMesh = malla;
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
        Estatico(go, batching);
        return go;
    }

    static void Estatico(GameObject go, bool batching)
    {
        var flags = StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic
                  | StaticEditorFlags.ReflectionProbeStatic;
        if (batching) flags |= StaticEditorFlags.BatchingStatic;
        foreach (var t in go.GetComponentsInChildren<Transform>(true))
            GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags);
    }

    static void ZonaTeletransporte(GameObject go, TeleportationArea modelo)
    {
        var zona = go.AddComponent<TeleportationArea>();
        if (modelo != null)
        {
            zona.interactionLayers = modelo.interactionLayers;
            zona.matchOrientation = modelo.matchOrientation;
        }
    }

    static float Rand(float a, float b) => a + (float)azar.NextDouble() * (b - a);
}
