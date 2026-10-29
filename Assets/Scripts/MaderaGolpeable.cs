using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Madera que se marca y se astilla al golpearla con un arma (ArmaCuerpoACuerpo).
///
/// En cada golpe:
///  - Muesca: deja un corte con forma de ojo donde entró el filo, orientado como la hoja:
///    madera clara por fuera ("Madera fresca") y una raja oscura en el centro ("Corte oscuro").
///    Se pega a la superficie aunque sea curva (cada vértice se apoya en ella con un rayo).
///    Todas las muescas de una pieza van en UNA sola malla (una llamada de dibujo por pieza);
///    al llegar a "Max muescas" se recicla la más vieja.
///  - Astillas: saltan unas cuantas, caen con física y desaparecen encogiendo. Se reutilizan (pool),
///    van en la capa "Ignore Raycast" y no proyectan sombra, para no gastar en Quest.
///  - Temblor: el "Visual" vibra un instante, como si el golpe lo sacudiera.
///
/// Cómo usarlo: ponlo en la raíz de un objeto de madera con colliders en sus piezas
/// (cajas, cápsulas o mallas convexas). El menú "VR Quest 3 > Crear potro de prácticas" monta uno completo.
/// Si quieres que otra cosa reaccione (contar golpes, romperse...), usa el evento "Al recibir golpe".
/// </summary>
public class MaderaGolpeable : MonoBehaviour, IGolpeable
{
    [Header("Muescas (los cortes que se quedan)")]
    [Tooltip("Material de la madera recién cortada (claro). Vacío = uno beige por defecto.")]
    [SerializeField] private Material maderaFresca;

    [Tooltip("Material de la raja del centro del corte (oscuro). Vacío = uno marrón oscuro por defecto.")]
    [SerializeField] private Material corteOscuro;

    [Tooltip("Muescas máximas a la vez en todo el objeto; al pasarse se borra la más vieja.")]
    [SerializeField, Min(1)] private int maxMuescas = 40;

    [Tooltip("Largo de cada corte en metros (al azar entre los dos valores).")]
    [SerializeField] private Vector2 largoMuesca = new Vector2(0.07f, 0.13f);

    [Tooltip("Ancho de cada corte en metros (al azar entre los dos valores).")]
    [SerializeField] private Vector2 anchoMuesca = new Vector2(0.018f, 0.03f);

    [Tooltip("Qué parte del ancho es la raja oscura del centro.")]
    [SerializeField, Range(0.1f, 0.9f)] private float proporcionRaja = 0.35f;

    [Header("Astillas (saltan y caen)")]
    [Tooltip("Materiales de las astillas (se elige uno al azar). Vacío = el de madera fresca.")]
    [SerializeField] private Material[] materialesAstilla;

    [SerializeField] private Vector2Int astillasPorGolpe = new Vector2Int(4, 7);

    [Tooltip("Astillas que pueden existir a la vez (se reutilizan).")]
    [SerializeField, Min(0)] private int maxAstillas = 24;

    [Tooltip("Velocidad con la que salen disparadas.")]
    [SerializeField, Min(0f)] private float fuerzaAstillas = 2f;

    [Tooltip("Segundos que tardan en desaparecer.")]
    [SerializeField, Min(0.2f)] private float vidaAstillas = 2.5f;

    [Header("Temblor")]
    [Tooltip("Lo que tiembla al recibir el golpe. Vacío = busca un hijo \"Visual\".")]
    [SerializeField] private Transform visual;

    [SerializeField, Min(0f)] private float temblor = 0.012f;
    [SerializeField, Min(0.01f)] private float duracionTemblor = 0.15f;

    [Header("Sonido y eventos")]
    [Tooltip("Sonidos de golpe en madera (se elige uno al azar).")]
    [SerializeField] private AudioClip[] sonidos;

    public UnityEvent<Golpe> alRecibirGolpe;

    // Una "capa" de muescas por pieza golpeada: un objeto con una sola malla que las junta todas
    private class Capa
    {
        public Transform transform;
        public Mesh malla;
        public readonly List<Muesca> muescas = new List<Muesca>();
    }

    private class Muesca
    {
        public Capa capa;
        public Vector3[] vertices, normales;   // en el espacio local de la capa
    }

    private class Astilla
    {
        public Transform transform;
        public Rigidbody cuerpo;
        public Vector3 escala;
        public float muerte;
    }

    private const int Segmentos = 8;          // filas a lo largo del corte
    private const float Separacion = 0.0015f; // la muesca flota 1,5 mm sobre la madera (evita parpadeos)

    private Collider[] piezas;
    private readonly Dictionary<Collider, Capa> capas = new Dictionary<Collider, Capa>();
    private readonly Queue<Muesca> orden = new Queue<Muesca>();     // para reciclar la más vieja
    private readonly List<Astilla> astillas = new List<Astilla>();
    private readonly List<Vector3> bufVertices = new List<Vector3>();
    private readonly List<Vector3> bufNormales = new List<Vector3>();
    private readonly List<int> bufClaro = new List<int>();
    private readonly List<int> bufOscuro = new List<int>();
    private Transform contenedorAstillas;
    private Mesh mallaCubo;
    private Coroutine temblando;
    private Vector3 reposoVisual;
    private AudioSource fuenteAudio;

    private void Awake()
    {
        var lista = new List<Collider>();
        foreach (var c in GetComponentsInChildren<Collider>(true))
            if (!c.isTrigger) lista.Add(c);
        piezas = lista.ToArray();
        if (piezas.Length == 0) Debug.LogWarning($"[MaderaGolpeable] \"{name}\" no tiene colliders: nunca recibirá golpes.", this);

        if (visual == null) visual = transform.Find("Visual");
        if (visual != null) reposoVisual = visual.localPosition;

        if (maderaFresca == null) maderaFresca = MaterialPorDefecto("Madera fresca", new Color(0.93f, 0.82f, 0.62f));
        if (corteOscuro == null) corteOscuro = MaterialPorDefecto("Corte oscuro", new Color(0.30f, 0.19f, 0.11f));
        if (materialesAstilla == null || materialesAstilla.Length == 0) materialesAstilla = new[] { maderaFresca };
    }

    public void RecibirGolpe(Golpe golpe)
    {
        if (BuscarSuperficie(golpe, out var superficie))
        {
            CrearMuesca(golpe, superficie);
            SoltarAstillas(golpe, superficie.point, superficie.normal);
        }
        else SoltarAstillas(golpe, golpe.punto, -golpe.direccion);

        if (visual != null && temblor > 0f)
        {
            if (temblando != null) StopCoroutine(temblando);
            temblando = StartCoroutine(Temblar());
        }
        if (sonidos != null && sonidos.Length > 0) Sonar(sonidos[Random.Range(0, sonidos.Length)]);
        alRecibirGolpe?.Invoke(golpe);
    }

    // ---------- Muescas ----------

    /// <summary>Busca el punto de la superficie por donde entró el filo, lanzando rayos contra nuestras piezas.</summary>
    private bool BuscarSuperficie(Golpe golpe, out RaycastHit superficie)
    {
        superficie = default;
        Vector3 dir = golpe.direccion.sqrMagnitude > 1e-6f ? golpe.direccion.normalized : Vector3.down;
        // 1º: siguiendo el movimiento del filo. 2º: desde fuera hacia el centro de la pieza más cercana.
        if (Rayo(new Ray(golpe.punto - dir * 0.3f, dir), 0.6f, out superficie)) return true;

        foreach (var pieza in piezas)
        {
            Vector3 centro = pieza.bounds.center;
            Vector3 fuera = golpe.punto - centro;
            if (fuera.sqrMagnitude < 1e-6f) fuera = -dir;
            var rayo = new Ray(golpe.punto + fuera.normalized * 0.3f, -fuera.normalized);
            if (pieza.Raycast(rayo, out superficie, 0.6f)) return true;
        }
        return false;
    }

    private bool Rayo(Ray rayo, float distancia, out RaycastHit mejor)
    {
        mejor = default;
        float minimo = float.MaxValue;
        foreach (var pieza in piezas)
            if (pieza.enabled && pieza.Raycast(rayo, out var h, distancia) && h.distance < minimo)
            {
                minimo = h.distance;
                mejor = h;
            }
        return minimo < float.MaxValue;
    }

    private void CrearMuesca(Golpe golpe, RaycastHit superficie)
    {
        Vector3 n = superficie.normal;
        // La línea del corte sigue a la hoja, aplastada sobre la superficie
        Vector3 u = Vector3.ProjectOnPlane(golpe.ejeHoja, n);
        if (u.sqrMagnitude < 1e-4f) u = Vector3.Cross(n, golpe.direccion);
        if (u.sqrMagnitude < 1e-4f) u = Vector3.Cross(n, Vector3.up);
        if (u.sqrMagnitude < 1e-4f) u = Vector3.Cross(n, Vector3.right);
        u.Normalize();
        Vector3 v = Vector3.Cross(n, u);

        float largo = Random.Range(largoMuesca.x, largoMuesca.y);
        float ancho = Random.Range(anchoMuesca.x, anchoMuesca.y);

        var capa = CapaDe(superficie.collider);
        var muesca = new Muesca { capa = capa, vertices = new Vector3[(Segmentos + 1) * 4], normales = new Vector3[(Segmentos + 1) * 4] };
        Matrix4x4 aMundo = capa.transform.localToWorldMatrix;
        Matrix4x4 aLocal = capa.transform.worldToLocalMatrix;
        Matrix4x4 normalALocal = aMundo.transpose;   // las normales se pasan con la traspuesta (vale con escala no uniforme)

        for (int i = 0; i <= Segmentos; i++)
        {
            float s = ((float)i / Segmentos - 0.5f) * largo;
            float forma = Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Pow(2f * s / largo, 2f)));   // ojo: fino en las puntas
            float irregular = (i == 0 || i == Segmentos) ? 1f : Random.Range(0.75f, 1.15f);
            float medio = ancho * 0.5f * forma * irregular;
            float raja = medio * proporcionRaja * Random.Range(0.8f, 1.2f);
            float[] columnas = { medio, raja, -raja, -medio };

            for (int j = 0; j < 4; j++)
            {
                Vector3 p = superficie.point + u * s + v * columnas[j];
                Vector3 normal = n;
                // Apoyar el vértice en la superficie real (por si es curva, como un tronco)
                if (superficie.collider.Raycast(new Ray(p + n * 0.05f, -n), out var apoyo, 0.12f))
                {
                    p = apoyo.point;
                    normal = apoyo.normal;
                }
                p += normal * Separacion;
                muesca.vertices[i * 4 + j] = aLocal.MultiplyPoint3x4(p);
                muesca.normales[i * 4 + j] = normalALocal.MultiplyVector(normal).normalized;
            }
        }

        capa.muescas.Add(muesca);
        orden.Enqueue(muesca);
        while (orden.Count > maxMuescas)
        {
            var vieja = orden.Dequeue();
            vieja.capa.muescas.Remove(vieja);
            if (vieja.capa != capa) Reconstruir(vieja.capa);
        }
        Reconstruir(capa);
    }

    private Capa CapaDe(Collider pieza)
    {
        if (capas.TryGetValue(pieza, out var capa)) return capa;

        var go = new GameObject("Muescas");
        go.transform.SetParent(pieza.transform, false);
        var malla = new Mesh { name = "Muescas" };
        malla.MarkDynamic();
        go.AddComponent<MeshFilter>().sharedMesh = malla;
        var render = go.AddComponent<MeshRenderer>();
        render.sharedMaterials = new[] { maderaFresca, corteOscuro };
        render.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        capa = new Capa { transform = go.transform, malla = malla };
        capas[pieza] = capa;
        return capa;
    }

    /// <summary>Junta todas las muescas de una pieza en su malla: submalla 0 = madera clara, 1 = raja oscura.</summary>
    private void Reconstruir(Capa capa)
    {
        bufVertices.Clear(); bufNormales.Clear(); bufClaro.Clear(); bufOscuro.Clear();
        foreach (var m in capa.muescas)
        {
            int baseIndice = bufVertices.Count;
            bufVertices.AddRange(m.vertices);
            bufNormales.AddRange(m.normales);
            for (int i = 0; i < Segmentos; i++)
                for (int j = 0; j < 3; j++)
                {
                    int a = baseIndice + i * 4 + j, b = a + 1, c = a + 4, d = a + 5;
                    var lista = j == 1 ? bufOscuro : bufClaro;   // la columna del medio es la raja
                    // Orden de los vértices: con este, la cara visible mira hacia fuera de la madera
                    lista.Add(a); lista.Add(b); lista.Add(c);
                    lista.Add(b); lista.Add(d); lista.Add(c);
                }
        }
        capa.malla.Clear();
        capa.malla.SetVertices(bufVertices);
        capa.malla.SetNormals(bufNormales);
        capa.malla.subMeshCount = 2;
        capa.malla.SetTriangles(bufClaro, 0);
        capa.malla.SetTriangles(bufOscuro, 1);
        capa.malla.RecalculateBounds();
    }

    // ---------- Astillas ----------

    private void SoltarAstillas(Golpe golpe, Vector3 punto, Vector3 normal)
    {
        if (maxAstillas <= 0) return;
        int cuantas = Random.Range(astillasPorGolpe.x, astillasPorGolpe.y + 1);
        for (int i = 0; i < cuantas; i++)
        {
            var a = AstillaLibre();
            if (a == null) return;

            a.escala = new Vector3(Random.Range(0.008f, 0.016f), Random.Range(0.005f, 0.01f), Random.Range(0.03f, 0.08f));
            a.transform.localScale = a.escala;
            a.transform.SetPositionAndRotation(punto + normal * 0.02f + Random.insideUnitSphere * 0.03f, Random.rotation);
            a.transform.GetComponent<MeshRenderer>().sharedMaterial = materialesAstilla[Random.Range(0, materialesAstilla.Length)];
            a.transform.gameObject.SetActive(true);

            // Salen hacia fuera de la madera y un poco en la dirección del golpe
            Vector3 velocidad = (normal * Random.Range(0.6f, 1.2f) + golpe.direccion * Random.Range(0.3f, 0.8f) + Random.insideUnitSphere * 0.5f)
                                * fuerzaAstillas;
            a.cuerpo.linearVelocity = velocidad;
            a.cuerpo.angularVelocity = Random.insideUnitSphere * 25f;
            a.muerte = Time.time + vidaAstillas * Random.Range(0.8f, 1.2f);
        }
        enabled = true;   // el Update solo trabaja mientras hay astillas vivas
    }

    private Astilla AstillaLibre()
    {
        foreach (var a in astillas)
            if (!a.transform.gameObject.activeSelf) return a;

        if (astillas.Count < maxAstillas)
        {
            if (contenedorAstillas == null)
                contenedorAstillas = new GameObject($"Astillas ({name})").transform;   // fuera del objeto: no tiemblan con él
            if (mallaCubo == null)
            {
                var cubo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                mallaCubo = cubo.GetComponent<MeshFilter>().sharedMesh;
                Destroy(cubo);
            }
            var go = new GameObject("Astilla");
            go.layer = 2;   // Ignore Raycast: las armas y los rayos de las manos no las tienen en cuenta
            go.transform.SetParent(contenedorAstillas, false);
            go.AddComponent<MeshFilter>().sharedMesh = mallaCubo;
            var render = go.AddComponent<MeshRenderer>();
            render.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.AddComponent<BoxCollider>();
            var cuerpo = go.AddComponent<Rigidbody>();
            cuerpo.mass = 0.02f;
            cuerpo.linearDamping = 0.3f;
            cuerpo.angularDamping = 0.5f;
            go.SetActive(false);
            var nueva = new Astilla { transform = go.transform, cuerpo = cuerpo };
            astillas.Add(nueva);
            return nueva;
        }

        // Todas en uso: reciclar la que antes vaya a morir
        Astilla vieja = null;
        foreach (var a in astillas)
            if (vieja == null || a.muerte < vieja.muerte) vieja = a;
        return vieja;
    }

    private void Update()
    {
        bool quedan = false;
        foreach (var a in astillas)
        {
            if (!a.transform.gameObject.activeSelf) continue;
            float resto = a.muerte - Time.time;
            if (resto <= 0f) { a.transform.gameObject.SetActive(false); continue; }
            if (resto < 0.3f) a.transform.localScale = a.escala * (resto / 0.3f);   // encoger antes de desaparecer
            quedan = true;
        }
        if (!quedan) enabled = false;
    }

    // ---------- Temblor y sonido ----------

    private IEnumerator Temblar()
    {
        for (float t = 0f; t < duracionTemblor; t += Time.deltaTime)
        {
            float fuerza = temblor * (1f - t / duracionTemblor);
            visual.localPosition = reposoVisual + Random.insideUnitSphere * fuerza;
            yield return null;
        }
        visual.localPosition = reposoVisual;
        temblando = null;
    }

    private void Sonar(AudioClip clip)
    {
        if (fuenteAudio == null)
        {
            fuenteAudio = gameObject.AddComponent<AudioSource>();
            fuenteAudio.playOnAwake = false;
            fuenteAudio.spatialBlend = 1f;
        }
        fuenteAudio.pitch = Random.Range(0.9f, 1.1f);
        fuenteAudio.PlayOneShot(clip);
    }

    private static Material MaterialPorDefecto(string nombre, Color color)
    {
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = nombre };
        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Smoothness", 0.1f);
        return mat;
    }

    private void OnDestroy()
    {
        if (contenedorAstillas != null) Destroy(contenedorAstillas.gameObject);
        foreach (var capa in capas.Values) if (capa.malla != null) Destroy(capa.malla);
    }
}
