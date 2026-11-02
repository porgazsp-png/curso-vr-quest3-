using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Música de fondo que cambia según la zona donde estés (pueblo, camino, castillo...).
///
/// Cómo funciona:
///  - Tiene dos "altavoces" (AudioSource) en 2D: la música suena igual mires donde mires.
///    Al cambiar de tema, uno baja mientras el otro sube (fundido cruzado), sin cortes.
///  - Cada medio segundo mira dónde está la cabeza del jugador y busca la ZonaMusical
///    que la contiene (la de más prioridad). Fuera de todas, suena la "Música por defecto".
///  - Recuerda por dónde iba cada tema: si sales del pueblo y vuelves, sigue donde lo dejaste.
///  - Pasa de una escena a otra sin cortarse (con "Mantener entre escenas"): si la nueva
///    escena trae su propio MusicaAmbiente, solo se toma de él la música por defecto.
///
/// Cómo usarlo: pon UN objeto con este script en la escena y arrastra el tema principal a
/// "Música por defecto". Luego crea zonas con ZonaMusical. El menú "VR Quest 3 > Música por zonas"
/// lo monta todo. Desde otros scripts: MusicaAmbiente.Instancia.Forzar(clip) (p. ej. música de
/// combate) y Forzar(null) para volver a la de la zona; o cambiar Volumen.
/// </summary>
public class MusicaAmbiente : MonoBehaviour
{
    [Header("Música")]
    [Tooltip("Tema que suena fuera de todas las zonas (el del pueblo).")]
    [SerializeField] private AudioClip musicaPorDefecto;

    [Tooltip("Volumen general de la música. En VR conviene bajito para que se oigan el mundo y los golpes.")]
    [SerializeField, Range(0f, 1f)] private float volumen = 0.35f;

    [Tooltip("Segundos que tarda un tema en dejar paso al otro.")]
    [SerializeField, Min(0.1f)] private float duracionFundido = 3f;

    [Tooltip("Segundos de subida suave al empezar la partida.")]
    [SerializeField, Min(0f)] private float fundidoInicial = 4f;

    [Tooltip("Al volver a una zona, seguir el tema por donde iba (desmarcado = empezar desde el principio).")]
    [SerializeField] private bool recordarPosicion = true;

    [Header("Jugador")]
    [Tooltip("Lo que se usa como posición del jugador. Vacío = la cámara principal (la cabeza en VR).")]
    [SerializeField] private Transform oyente;

    [Tooltip("Cada cuántos segundos se mira en qué zona está el jugador.")]
    [SerializeField, Min(0.05f)] private float intervaloComprobacion = 0.5f;

    [Tooltip("No destruirse al cambiar de escena (la música sigue sonando sin cortes).")]
    [SerializeField] private bool mantenerEntreEscenas = true;

    /// <summary>El MusicaAmbiente que está sonando (null si no hay ninguno).</summary>
    public static MusicaAmbiente Instancia { get; private set; }

    public float Volumen
    {
        get => volumen;
        set { volumen = Mathf.Clamp01(value); }
    }

    /// <summary>Tema que suena ahora (o hacia el que se está fundiendo).</summary>
    public AudioClip TemaActual => altavoces[actual].clip;

    private readonly AudioSource[] altavoces = new AudioSource[2];
    private readonly float[] factorVolumen = { 1f, 1f };   // el "Volumen" de la zona de cada altavoz
    private int actual;                  // índice del altavoz que sube; el otro baja
    private float mezcla = 1f;           // 0 → 1 durante el fundido
    private float volumenInicial;        // 0 → 1 al empezar
    private float siguienteComprobacion;
    private AudioClip forzado;
    private readonly Dictionary<AudioClip, float> posiciones = new Dictionary<AudioClip, float>();

    private void Awake()
    {
        if (Instancia != null && Instancia != this)
        {
            // Ya suena uno que viene de otra escena: le pasamos nuestro tema y nos quitamos de en medio
            if (musicaPorDefecto != null) Instancia.musicaPorDefecto = musicaPorDefecto;
            Destroy(gameObject);
            return;
        }
        Instancia = this;
        if (mantenerEntreEscenas)
        {
            if (transform.parent != null) transform.SetParent(null);   // DontDestroyOnLoad solo vale en la raíz
            DontDestroyOnLoad(gameObject);
        }

        for (int i = 0; i < 2; i++)
        {
            var a = gameObject.AddComponent<AudioSource>();
            a.playOnAwake = false;
            a.loop = true;
            a.spatialBlend = 0f;        // 2D: la música no sale de ningún sitio
            a.priority = 0;             // la más importante: que nunca la corte otro sonido
            a.bypassReverbZones = true;
            a.volume = 0f;
            altavoces[i] = a;
        }
        volumenInicial = fundidoInicial > 0f ? 0f : 1f;

        if (musicaPorDefecto == null && ZonaMusical.Activas.Count == 0)
            Debug.LogWarning("[MusicaAmbiente] No hay \"Música por defecto\" ni zonas: no sonará nada.", this);
    }

    private void OnDestroy()
    {
        if (Instancia == this) Instancia = null;
    }

    private void Start()
    {
        Comprobar();
    }

    private void Update()
    {
        if (Time.unscaledTime >= siguienteComprobacion) Comprobar();

        // Fundidos (con tiempo real: funcionan aunque el juego esté en pausa)
        float dt = Time.unscaledDeltaTime;
        if (fundidoInicial > 0f) volumenInicial = Mathf.MoveTowards(volumenInicial, 1f, dt / fundidoInicial);
        mezcla = Mathf.MoveTowards(mezcla, 1f, dt / duracionFundido);

        // Fundido de igual potencia: a medio camino no se nota un "bache" de volumen
        float sube = Mathf.Sin(mezcla * Mathf.PI * 0.5f);
        float baja = Mathf.Cos(mezcla * Mathf.PI * 0.5f);
        int otro = 1 - actual;
        altavoces[actual].volume = volumen * volumenInicial * factorVolumen[actual] * sube;
        altavoces[otro].volume = volumen * volumenInicial * factorVolumen[otro] * baja;

        if (mezcla >= 1f && altavoces[otro].isPlaying)
        {
            Guardar(altavoces[otro]);
            altavoces[otro].Stop();
        }
    }

    private void Comprobar()
    {
        siguienteComprobacion = Time.unscaledTime + intervaloComprobacion;

        AudioClip tema = musicaPorDefecto;
        float factor = 1f;
        if (forzado != null) tema = forzado;
        else
        {
            var zona = ZonaDe(PosicionOyente());
            if (zona != null && zona.Tema != null)
            {
                tema = zona.Tema;
                factor = zona.Volumen;
            }
        }
        factorVolumen[actual] = altavoces[actual].clip == tema ? factor : factorVolumen[actual];
        Poner(tema, factor);
    }

    private Vector3 PosicionOyente()
    {
        if (oyente == null && Camera.main != null) oyente = Camera.main.transform;
        return oyente != null ? oyente.position : transform.position;
    }

    private static ZonaMusical ZonaDe(Vector3 punto)
    {
        ZonaMusical mejor = null;
        foreach (var z in ZonaMusical.Activas)
            if (z.Contiene(punto) && (mejor == null || z.Prioridad > mejor.Prioridad))
                mejor = z;
        return mejor;
    }

    private void Poner(AudioClip tema, float factor)
    {
        var a = altavoces[actual];
        if (a.clip == tema && (tema == null || a.isPlaying)) return;

        // Si el tema nuevo es el que se estaba apagando, simplemente se le da la vuelta al fundido
        int otro = 1 - actual;
        if (tema != null && altavoces[otro].clip == tema && altavoces[otro].isPlaying)
        {
            actual = otro;
            mezcla = 1f - mezcla;
            factorVolumen[actual] = factor;
            return;
        }

        // El que sonaba pasa a bajar; el otro altavoz arranca el tema nuevo.
        // Si pillamos un fundido a medias, el que baja empieza desde el volumen que ya tenía (sin saltos).
        float pesoQueTenia = Mathf.Sin(mezcla * Mathf.PI * 0.5f);
        if (a.isPlaying) Guardar(a);
        actual = otro;
        mezcla = a.isPlaying ? Mathf.Acos(Mathf.Clamp01(pesoQueTenia)) * 2f / Mathf.PI : 1f;
        var nuevo = altavoces[actual];
        if (nuevo.isPlaying) Guardar(nuevo);
        nuevo.Stop();
        nuevo.clip = tema;
        factorVolumen[actual] = factor;
        if (tema == null) return;

        if (recordarPosicion && posiciones.TryGetValue(tema, out float t) && t < tema.length) nuevo.time = t;
        nuevo.Play();
    }

    private void Guardar(AudioSource a)
    {
        if (a.clip != null) posiciones[a.clip] = a.time;
    }

    /// <summary>Pone un tema por encima de las zonas (combate, jefe...). null = volver a la música de la zona.</summary>
    public void Forzar(AudioClip tema)
    {
        forzado = tema;
        Comprobar();
    }
}
