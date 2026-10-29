using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// FASE 2 del castillo: el exterior sobre la colina. Menú: VR Quest 3 > Castillo > 2. Exterior del castillo
///
/// Quaternius no tiene un "kit de castillo", así que la obra gruesa se levanta aquí con cajas y cilindros
/// low poly, pero con SUS texturas (piedra MI_Brick, remates MI_RockTrim, tejas MI_RoundTiles), para que
/// case con las casas del pueblo. Las piezas con detalle sí son modelos de Quaternius:
/// portón de madera de arco (Door_8_Round), su marco de piedra, estandartes y antorchas.
///
/// Qué construye (todo dentro de "Castillo (exterior)", que se rehace entero al volver a ejecutarlo):
///  - Muralla de 28 x 28 m y 7 m de alto con almenas.
///  - 4 torres redondas en las esquinas con tejado cónico (se ven desde el pueblo).
///  - Torre del homenaje en el centro (la que luego será el interior explorable).
///  - Cuerpo de la puerta con el portón de dos hojas (cerrado; cada hoja gira sobre su bisagra,
///    listo para el script de puerta), estandartes y dos antorchas con UNA sola luz en tiempo real
///    que parpadea (LuzParpadeante).
///  - Saeteras (ranuras oscuras) en torres y torre del homenaje.
/// Rendimiento: toda la piedra es UNA malla y todas las tejas otra (2 llamadas de dibujo),
/// con un solo MeshCollider para la piedra. Todo Static salvo el portón y la luz.
/// </summary>
public static class ConstructorCastilloExterior
{
    const string Salida = "Assets/Pueblo/Castillo/";
    const string RaizNombre = "Castillo (exterior)";
    const string Mat = "Assets/ThirdParty/Quaternius/MedievalVillage/Materials/";
    const string Props = "Assets/ThirdParty/Quaternius/FantasyProps/Models/";
    const string Vil = "Assets/ThirdParty/Quaternius/MedievalVillage/Models/";

    // ---------- Medidas (metros, coordenadas del mundo) ----------
    const float Suelo = 6f;                      // altura de la meseta
    const float X0 = -14f, X1 = 14f, Z0 = 50f, Z1 = 78f;   // muralla (eje del muro)
    const float Grosor = 1.6f, AltoMuro = 7f;
    const float RadioTorre = 3.2f, AltoTorre = 11f, AltoTejado = 5f;
    const float PuertaAncho = 4f, PuertaAlto = 4.3f;
    const float Escala = 0.5f;                   // UV: una repetición de textura cada 2 m

    // Geometría combinada: piedra, remates (almenas) y tejas
    static List<Vector3> pv, rv, tv; static List<int> pt, rt, tt; static List<Vector2> pu, ru, tu;

    [MenuItem("VR Quest 3/Castillo/2. Exterior del castillo")]
    public static void Construir()
    {
        var escena = EditorSceneManager.GetActiveScene();
        var piedra = AssetDatabase.LoadAssetAtPath<Material>(Mat + "MI_Brick.mat");
        var remate = AssetDatabase.LoadAssetAtPath<Material>(Mat + "MI_RockTrim.mat");
        var tejas = AssetDatabase.LoadAssetAtPath<Material>(Mat + "MI_RoundTiles.mat");
        if (piedra == null || remate == null || tejas == null)
        {
            Debug.LogError("[ConstructorCastilloExterior] Faltan materiales de Quaternius MedievalVillage (MI_Brick, MI_RockTrim, MI_RoundTiles).");
            return;
        }

        var vieja = escena.GetRootGameObjects().FirstOrDefault(g => g.name == RaizNombre);
        if (vieja != null) Undo.DestroyObjectImmediate(vieja);
        var raiz = new GameObject(RaizNombre);
        Undo.RegisterCreatedObjectUndo(raiz, "Castillo exterior");

        pv = new List<Vector3>(); pt = new List<int>(); pu = new List<Vector2>();
        rv = new List<Vector3>(); rt = new List<int>(); ru = new List<Vector2>();
        tv = new List<Vector3>(); tt = new List<int>(); tu = new List<Vector2>();

        Muralla();
        Torres();
        CuerpoPuerta();
        TorreHomenaje();

        // Mallas combinadas
        var mallaPiedra = Guardar("Castillo_Piedra", pv, pt, pu);
        var goPiedra = Objeto(raiz.transform, "Piedra", mallaPiedra, piedra);
        goPiedra.AddComponent<MeshCollider>().sharedMesh = mallaPiedra;
        var mallaRemate = Guardar("Castillo_Almenas", rv, rt, ru);
        Objeto(raiz.transform, "Almenas", mallaRemate, remate).AddComponent<MeshCollider>().sharedMesh = mallaRemate;
        Objeto(raiz.transform, "Tejados", Guardar("Castillo_Tejados", tv, tt, tu), tejas);
        Saeteras(raiz.transform);

        Porton(raiz.transform);
        Detalles(raiz.transform);

        EditorSceneManager.MarkSceneDirty(escena);
        AssetDatabase.SaveAssets();
        Debug.Log($"[ConstructorCastilloExterior] Castillo creado: {pt.Count / 3 + rt.Count / 3 + tt.Count / 3} triángulos de obra. Guarda la escena (Ctrl+S).");
    }

    // ======================================================================
    // Obra gruesa
    // ======================================================================

    static void Muralla()
    {
        float b = Suelo - 0.6f, a = Suelo + AltoMuro;   // base hundida para tapar irregularidades del suelo
        // Lado frontal (sur) partido por el cuerpo de la puerta; los otros tres enteros
        Caja(pv, pt, pu, new Vector3(-9.25f, (a + b) / 2, Z0), new Vector3(9.5f, a - b, Grosor));
        Caja(pv, pt, pu, new Vector3(9.25f, (a + b) / 2, Z0), new Vector3(9.5f, a - b, Grosor));
        Caja(pv, pt, pu, new Vector3(0f, (a + b) / 2, Z1), new Vector3(X1 - X0, a - b, Grosor));
        Caja(pv, pt, pu, new Vector3(X0, (a + b) / 2, (Z0 + Z1) / 2), new Vector3(Grosor, a - b, Z1 - Z0));
        Caja(pv, pt, pu, new Vector3(X1, (a + b) / 2, (Z0 + Z1) / 2), new Vector3(Grosor, a - b, Z1 - Z0));

        // Almenas en el borde exterior de los cuatro lados (sin pisar torres ni la puerta)
        float margen = RadioTorre + 0.6f;
        Almenas(new Vector3(X0 + margen, a, Z0 - Grosor / 2 + 0.25f), new Vector3(-4.5f - 0.4f, a, Z0 - Grosor / 2 + 0.25f));
        Almenas(new Vector3(4.5f + 0.4f, a, Z0 - Grosor / 2 + 0.25f), new Vector3(X1 - margen, a, Z0 - Grosor / 2 + 0.25f));
        Almenas(new Vector3(X0 + margen, a, Z1 + Grosor / 2 - 0.25f), new Vector3(X1 - margen, a, Z1 + Grosor / 2 - 0.25f));
        Almenas(new Vector3(X0 - Grosor / 2 + 0.25f, a, Z0 + margen), new Vector3(X0 - Grosor / 2 + 0.25f, a, Z1 - margen));
        Almenas(new Vector3(X1 + Grosor / 2 - 0.25f, a, Z0 + margen), new Vector3(X1 + Grosor / 2 - 0.25f, a, Z1 - margen));
    }

    static void Torres()
    {
        foreach (var x in new[] { X0, X1 })
            foreach (var z in new[] { Z0, Z1 })
            {
                var centro = new Vector3(x, Suelo - 1.5f, z);
                Cilindro(pv, pt, pu, centro, RadioTorre, AltoTorre + 1.5f, 12);
                Cono(new Vector3(x, Suelo + AltoTorre, z), RadioTorre + 0.5f, AltoTejado, 12);
            }
    }

    static void CuerpoPuerta()
    {
        // Bloque que sobresale de la muralla, con el paso de la puerta en medio
        const float x0 = -4.5f, x1 = 4.5f, z0 = Z0 - 1.4f - Grosor / 2, z1 = Z0 + Grosor / 2, alto = 10f;
        float b = Suelo - 0.6f, a = Suelo + alto, hueco = PuertaAncho / 2;
        float zc = (z0 + z1) / 2, zs = z1 - z0;
        Caja(pv, pt, pu, new Vector3((x0 - hueco) / 2, (a + b) / 2, zc), new Vector3(-hueco - x0, a - b, zs));   // pilar izquierdo
        Caja(pv, pt, pu, new Vector3((x1 + hueco) / 2, (a + b) / 2, zc), new Vector3(x1 - hueco, a - b, zs));    // pilar derecho
        float dintel = Suelo + PuertaAlto;
        Caja(pv, pt, pu, new Vector3(0f, (a + dintel) / 2, zc), new Vector3(PuertaAncho, a - dintel, zs));      // encima del hueco

        Almenas(new Vector3(x0 + 0.3f, a, z0 + 0.25f), new Vector3(x1 - 0.3f, a, z0 + 0.25f));
        Almenas(new Vector3(x0 + 0.25f, a, z0 + 0.6f), new Vector3(x0 + 0.25f, a, z1 - 0.3f));
        Almenas(new Vector3(x1 - 0.25f, a, z0 + 0.6f), new Vector3(x1 - 0.25f, a, z1 - 0.3f));
    }

    static void TorreHomenaje()
    {
        const float x0 = -5.5f, x1 = 5.5f, z0 = 62f, z1 = 73f, alto = 16f;
        float b = Suelo - 0.6f, a = Suelo + alto;
        Caja(pv, pt, pu, new Vector3(0f, (a + b) / 2, (z0 + z1) / 2), new Vector3(x1 - x0, a - b, z1 - z0));
        Almenas(new Vector3(x0 + 0.25f, a, z0 + 0.25f), new Vector3(x1 - 0.25f, a, z0 + 0.25f));
        Almenas(new Vector3(x0 + 0.25f, a, z1 - 0.25f), new Vector3(x1 - 0.25f, a, z1 - 0.25f));
        Almenas(new Vector3(x0 + 0.25f, a, z0 + 1.2f), new Vector3(x0 + 0.25f, a, z1 - 1.2f));
        Almenas(new Vector3(x1 - 0.25f, a, z0 + 1.2f), new Vector3(x1 - 0.25f, a, z1 - 1.2f));
        // Torreta con tejado en una esquina: rompe la silueta y se reconoce de lejos
        Cilindro(pv, pt, pu, new Vector3(x1 - 1.2f, a - 2f, z1 - 1.2f), 1.6f, 6f, 10);
        Cono(new Vector3(x1 - 1.2f, a + 4f, z1 - 1.2f), 2f, 3.5f, 10);
    }

    /// <summary>Fila de almenas (bloques de 0,9 m cada 1,8 m) entre dos puntos sobre el muro.</summary>
    static void Almenas(Vector3 desde, Vector3 hasta)
    {
        Vector3 dir = hasta - desde; float largo = dir.magnitude;
        if (largo < 0.9f) return;
        dir /= largo;
        int n = Mathf.Max(1, Mathf.FloorToInt((largo + 0.9f) / 1.8f));
        float sobrante = largo - ((n - 1) * 1.8f);
        var giro = Quaternion.LookRotation(Vector3.Cross(dir, Vector3.up), Vector3.up);   // X local = a lo largo del muro
        for (int i = 0; i < n; i++)
        {
            Vector3 c = desde + dir * (sobrante / 2 + i * 1.8f) + Vector3.up * 0.55f;
            Caja(rv, rt, ru, c, new Vector3(0.9f, 1.1f, 0.5f), giro);
        }
    }

    static void Saeteras(Transform raiz)
    {
        // Ranuras oscuras: pequeños planos negros un pelín por delante del muro (casi gratis y dan mucha vida)
        var oscuro = AssetDatabase.LoadAssetAtPath<Material>("Assets/Pueblo/Armeria/Corte Oscuro.mat");
        var v = new List<Vector3>(); var t = new List<int>(); var uv = new List<Vector2>();
        void Ranura(Vector3 c, Vector3 normal, float alto)
        {
            Vector3 lado = Vector3.Cross(Vector3.up, normal).normalized * 0.12f, arriba = Vector3.up * alto / 2;
            Vector3 p = c + normal * 0.02f;
            Quad(v, t, uv, p - lado - arriba, p + lado - arriba, p + lado + arriba, p - lado + arriba, normal);
        }
        foreach (var x in new[] { X0, X1 })
            foreach (var z in new[] { Z0, Z1 })
                for (int k = 0; k < 4; k++)
                {
                    float ang = (k * 90f + 45f + (x < 0 ? 0f : 90f)) * Mathf.Deg2Rad;
                    var n = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                    // Solo las que miran hacia fuera del castillo
                    if (Vector3.Dot(n, new Vector3(x, 0f, z - (Z0 + Z1) / 2)) <= 0f) continue;
                    Ranura(new Vector3(x, Suelo + 6.5f, z) + n * (RadioTorre * Mathf.Cos(Mathf.PI / 12)), n, 1.2f);
                }
        for (int fila = 0; fila < 3; fila++)
            for (int col = -1; col <= 1; col++)
            {
                float y = Suelo + 8f + fila * 3f;
                Ranura(new Vector3(col * 3f, y, 62f), Vector3.back, 1.4f);
                Ranura(new Vector3(-5.5f, y, 67.5f + col * 3f), Vector3.left, 1.4f);
                Ranura(new Vector3(5.5f, y, 67.5f + col * 3f), Vector3.right, 1.4f);
            }
        Objeto(raiz, "Saeteras", Guardar("Castillo_Saeteras", v, t, uv), oscuro);
    }

    // ======================================================================
    // Portón, estandartes, antorchas
    // ======================================================================

    static void Porton(Transform raiz)
    {
        var porton = new GameObject("Porton").transform;
        porton.SetParent(raiz, false);
        float zPuerta = Z0 - 0.2f;   // a media profundidad del paso

        // Door_8_Round: la hoja va de x = 0 (bisagra) a x = -1,085 m y mide 2,36 m de alto.
        // Un pelín más ancha que medio hueco para que las dos hojas se toquen en el centro (sin rendija)
        float escala = (PuertaAncho / 2f + 0.03f) / 1.085f;
        foreach (int lado in new[] { -1, 1 })
        {
            var bisagra = new GameObject(lado < 0 ? "Hoja izquierda" : "Hoja derecha").transform;
            bisagra.SetParent(porton, false);
            bisagra.position = new Vector3(lado * PuertaAncho / 2f, Suelo, zPuerta);
            var hoja = Instanciar(bisagra, Vil + "Door_8_Round.fbx", estatico: false);
            if (hoja == null) continue;
            hoja.transform.localPosition = Vector3.zero;
            // Componer con el giro que trae el FBX (Quaternius importa con -90º en X): sustituirlo la tumbaba
            if (lado < 0) hoja.transform.localRotation = Quaternion.Euler(0f, 180f, 0f) * hoja.transform.localRotation;
            hoja.transform.localScale *= escala;
            var caja = bisagra.gameObject.AddComponent<BoxCollider>();   // para no atravesar el portón cerrado
            caja.center = new Vector3(-lado * PuertaAncho / 4f, PuertaAlto / 2f, 0f);
            caja.size = new Vector3(PuertaAncho / 2f, PuertaAlto, 0.35f);
        }

        // Marco de piedra con arco (el de Quaternius, agrandado al tamaño del portón)
        var marco = Instanciar(porton, Vil + "DoorFrame_Round_Brick.fbx", estatico: true);
        if (marco != null)
        {
            marco.transform.position = new Vector3(0f, Suelo, Z0 - Grosor / 2 - 1.4f - 0.05f);
            marco.transform.localScale = Vector3.Scale(marco.transform.localScale, new Vector3(PuertaAncho / 1.15f, PuertaAlto / 2.36f, 1.5f));
        }
    }

    static void Detalles(Transform raiz)
    {
        var detalles = new GameObject("Detalles").transform;
        detalles.SetParent(raiz, false);
        float frente = Z0 - Grosor / 2 - 1.4f;   // cara delantera del cuerpo de la puerta

        // Estandartes: dos en la puerta y dos en la torre del homenaje
        Estandarte(detalles, new Vector3(-3.3f, Suelo + 9.6f, frente - 0.05f), 1.9f, "Banner_1_Cloth.fbx");
        Estandarte(detalles, new Vector3(3.3f, Suelo + 9.6f, frente - 0.05f), 1.9f, "Banner_1_Cloth.fbx");
        Estandarte(detalles, new Vector3(-3f, Suelo + 15f, 61.95f), 2.4f, "Banner_2_Cloth.fbx");
        Estandarte(detalles, new Vector3(3f, Suelo + 15f, 61.95f), 2.4f, "Banner_2_Cloth.fbx");

        // Antorchas a los lados de la puerta: llama emisiva (sin luz propia) + UNA luz que parpadea
        var fuego = MaterialFuego();
        foreach (int lado in new[] { -1, 1 })
        {
            var antorcha = Instanciar(detalles, Props + "Torch_Metal.fbx", estatico: true);
            if (antorcha == null) continue;
            antorcha.transform.SetPositionAndRotation(new Vector3(lado * 2.9f, Suelo + 2.5f, frente),
                                                      Quaternion.Euler(0f, 180f, 0f) * antorcha.transform.localRotation);
            antorcha.transform.localScale *= 1.6f;

            // La llama, en la punta real de la antorcha (medida con su caja envolvente), no a ojo
            var limites = antorcha.GetComponentInChildren<Renderer>().bounds;
            foreach (var r in antorcha.GetComponentsInChildren<Renderer>()) limites.Encapsulate(r.bounds);
            var punta = antorcha.GetComponentsInChildren<MeshFilter>()
                                .SelectMany(mf => mf.sharedMesh.vertices.Select(p => mf.transform.TransformPoint(p)))
                                .OrderByDescending(p => p.y).First();
            var llama = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(llama.GetComponent<Collider>());
            llama.name = "Llama";
            llama.transform.SetParent(detalles, false);
            llama.transform.position = new Vector3(punta.x, limites.max.y + 0.08f, punta.z);
            llama.transform.localScale = new Vector3(0.16f, 0.3f, 0.16f);
            var render = llama.GetComponent<MeshRenderer>();
            render.sharedMaterial = fuego;
            render.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        var luz = new GameObject("Luz antorchas").AddComponent<Light>();
        luz.transform.SetParent(detalles, false);
        luz.transform.position = new Vector3(0f, Suelo + 3.3f, frente - 1.2f);
        luz.type = LightType.Point;
        luz.color = new Color(1f, 0.6f, 0.3f);
        luz.range = 9f;
        luz.shadows = LightShadows.None;
        luz.lightmapBakeType = LightmapBakeType.Mixed;   // ilumina ya; al hornear, la parte indirecta se precalcula
        var parpadeo = luz.gameObject.AddComponent<LuzParpadeante>();
        parpadeo.intensidadBase = 1.6f;
        parpadeo.variacion = 0.4f;
    }

    static void Estandarte(Transform padre, Vector3 pos, float escala, string modelo)
    {
        var e = Instanciar(padre, Props + modelo, estatico: true);
        if (e == null) return;
        e.transform.position = pos;   // conserva el giro de importación del FBX
        e.transform.localScale *= escala;

        // Tela roja: copia propia del material (el MI_Banner de Quaternius no se toca)
        var rojo = MaterialEstandarte();
        if (rojo == null) return;
        foreach (var r in e.GetComponentsInChildren<Renderer>())
            r.sharedMaterials = r.sharedMaterials.Select(m => m != null && m.name == "MI_Banner" ? rojo : m).ToArray();
    }

    static Material MaterialEstandarte()
    {
        string ruta = Salida + "Estandarte Rojo.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(ruta);
        if (mat != null) return mat;
        var original = AssetDatabase.FindAssets("MI_Banner t:Material").Select(AssetDatabase.GUIDToAssetPath)
                                    .Select(AssetDatabase.LoadAssetAtPath<Material>).FirstOrDefault(m => m != null && m.name == "MI_Banner");
        if (original == null) return null;
        mat = new Material(original);
        mat.SetColor("_BaseColor", new Color(0.62f, 0.10f, 0.08f));
        AssetDatabase.CreateAsset(mat, ruta);
        return mat;
    }

    static Material MaterialFuego()
    {
        string ruta = Salida + "Fuego.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(ruta);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));   // sin luz: brilla por sí mismo
            AssetDatabase.CreateAsset(mat, ruta);
        }
        mat.SetColor("_BaseColor", new Color(1f, 0.62f, 0.18f));
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // ======================================================================
    // Geometría
    // ======================================================================

    static void Caja(List<Vector3> v, List<int> t, List<Vector2> uv, Vector3 c, Vector3 tam, Quaternion? giro = null)
    {
        var g = giro ?? Quaternion.identity; Vector3 h = tam / 2;
        Vector3 P(float x, float y, float z) => c + g * new Vector3(x * h.x, y * h.y, z * h.z);
        Vector3 R = g * Vector3.right, U = g * Vector3.up, F = g * Vector3.forward;
        Quad(v, t, uv, P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), F);
        Quad(v, t, uv, P(1, -1, -1), P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1), -F);
        Quad(v, t, uv, P(1, -1, 1), P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), R);
        Quad(v, t, uv, P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1), -R);
        Quad(v, t, uv, P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), P(-1, 1, -1), U);
    }

    static void Cilindro(List<Vector3> v, List<int> t, List<Vector2> uv, Vector3 baseC, float r, float alto, int lados)
    {
        float arco = 2f * Mathf.PI * r / lados;
        for (int k = 0; k < lados; k++)
        {
            float a0 = k * Mathf.PI * 2f / lados, a1 = (k + 1) * Mathf.PI * 2f / lados;
            Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * r, d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * r;
            Vector3 n = (d0 + d1).normalized;
            int i = v.Count;
            v.AddRange(new[] { baseC + d0, baseC + d1, baseC + d1 + Vector3.up * alto, baseC + d0 + Vector3.up * alto });
            float u0 = k * arco * Escala, u1 = (k + 1) * arco * Escala;
            uv.AddRange(new[] { new Vector2(u0, baseC.y * Escala), new Vector2(u1, baseC.y * Escala), new Vector2(u1, (baseC.y + alto) * Escala), new Vector2(u0, (baseC.y + alto) * Escala) });
            if (Vector3.Dot(Vector3.Cross(v[i + 1] - v[i], v[i + 2] - v[i]), n) > 0f) t.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            else t.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
        }
        // tapa de arriba (por si algún día se quita el tejado)
        var arriba = baseC + Vector3.up * alto;
        for (int k = 0; k < lados; k++)
        {
            float a0 = k * Mathf.PI * 2f / lados, a1 = (k + 1) * Mathf.PI * 2f / lados;
            Tri(v, t, uv, arriba, arriba + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * r, arriba + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * r, Vector3.up);
        }
    }

    static void Cono(Vector3 baseC, float r, float alto, int lados)
    {
        var punta = baseC + Vector3.up * alto;
        float generatriz = Mathf.Sqrt(r * r + alto * alto);
        for (int k = 0; k < lados; k++)
        {
            float a0 = k * Mathf.PI * 2f / lados, a1 = (k + 1) * Mathf.PI * 2f / lados;
            Vector3 p0 = baseC + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * r, p1 = baseC + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * r;
            Vector3 fuera = ((p0 + p1) / 2 - baseC).normalized + Vector3.up * 0.5f;
            int i = tv.Count;
            tv.AddRange(new[] { p0, p1, punta });
            float ancho = Vector3.Distance(p0, p1) * Escala * 1.5f;
            tu.AddRange(new[] { new Vector2(0f, 0f), new Vector2(ancho, 0f), new Vector2(ancho / 2, generatriz * Escala * 1.5f) });
            if (Vector3.Dot(Vector3.Cross(p1 - p0, punta - p0), fuera) > 0f) tt.AddRange(new[] { i, i + 1, i + 2 }); else tt.AddRange(new[] { i, i + 2, i + 1 });
            // panza del tejado (vista desde abajo)
            Tri(tv, tt, tu, baseC, p1, p0, Vector3.down);
        }
    }

    static void Quad(List<Vector3> v, List<int> t, List<Vector2> uv, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 fuera)
    {
        int i = v.Count;
        v.AddRange(new[] { a, b, c, d });
        foreach (var p in new[] { a, b, c, d }) uv.Add(UVPlana(p, fuera));
        if (Vector3.Dot(Vector3.Cross(b - a, c - a), fuera) > 0f) t.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
        else t.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
    }

    static void Tri(List<Vector3> v, List<int> t, List<Vector2> uv, Vector3 a, Vector3 b, Vector3 c, Vector3 fuera)
    {
        int i = v.Count;
        v.AddRange(new[] { a, b, c });
        foreach (var p in new[] { a, b, c }) uv.Add(UVPlana(p, fuera));
        if (Vector3.Dot(Vector3.Cross(b - a, c - a), fuera) > 0f) t.AddRange(new[] { i, i + 1, i + 2 }); else t.AddRange(new[] { i, i + 2, i + 1 });
    }

    /// <summary>UV en metros según hacia dónde mira la cara (texturas a escala real, sin estirar).</summary>
    static Vector2 UVPlana(Vector3 p, Vector3 n)
    {
        n = new Vector3(Mathf.Abs(n.x), Mathf.Abs(n.y), Mathf.Abs(n.z));
        if (n.y >= n.x && n.y >= n.z) return new Vector2(p.x, p.z) * Escala;
        return n.x >= n.z ? new Vector2(p.z, p.y) * Escala : new Vector2(p.x, p.y) * Escala;
    }

    static Mesh Guardar(string nombre, List<Vector3> v, List<int> t, List<Vector2> uv)
    {
        var malla = new Mesh { name = nombre };
        if (v.Count > 65000) malla.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        malla.SetVertices(v); malla.SetTriangles(t, 0); malla.SetUVs(0, uv);
        malla.RecalculateNormals(); malla.RecalculateBounds(); malla.RecalculateTangents();

        Directory.CreateDirectory(Salida + "Mallas/");
        string ruta = Salida + "Mallas/" + nombre + ".asset";
        var existente = AssetDatabase.LoadAssetAtPath<Mesh>(ruta);
        if (existente != null) { EditorUtility.CopySerialized(malla, existente); Object.DestroyImmediate(malla); return existente; }
        AssetDatabase.CreateAsset(malla, ruta);
        return malla;
    }

    static GameObject Objeto(Transform padre, string nombre, Mesh malla, Material material)
    {
        var go = new GameObject(nombre);
        go.transform.SetParent(padre, false);
        go.AddComponent<MeshFilter>().sharedMesh = malla;
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
        Estatico(go);
        return go;
    }

    static GameObject Instanciar(Transform padre, string ruta, bool estatico)
    {
        var modelo = AssetDatabase.LoadAssetAtPath<GameObject>(ruta);
        if (modelo == null) { Debug.LogWarning($"[ConstructorCastilloExterior] No encuentro {ruta}"); return null; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(modelo, padre);
        if (estatico) Estatico(go);
        return go;
    }

    static void Estatico(GameObject go)
    {
        var flags = StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic
                  | StaticEditorFlags.BatchingStatic | StaticEditorFlags.ReflectionProbeStatic;
        foreach (var t in go.GetComponentsInChildren<Transform>(true))
            GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags);
    }
}
