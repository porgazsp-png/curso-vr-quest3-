using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Puerta de una o varias hojas que se abre y se cierra con el gatillo.
///
/// Cómo funciona:
///  - Es un "interactable" del XR Interaction Toolkit: la mano (o el rayo) la detecta al
///    tocarla o apuntarla (hover). Si estando así aprietas el gatillo, la puerta se abre
///    (o se cierra si ya estaba abierta). El grip NO la agarra: una puerta no se coge.
///  - Cada hoja gira sobre su bisagra (el pivote de su Transform) con un movimiento suave
///    que arranca y frena despacio. Si la vuelves a pulsar a medio camino, da la vuelta desde ahí.
///  - Hacia qué lado se abre lo decide la flecha azul (eje Z) de este objeto: las hojas giran
///    hacia ese lado. Cada hoja calcula sola si tiene que girar a izquierdas o a derechas.
///  - Al pulsar, el mando vibra un poco y suena el clip (si le pones uno).
///
/// Cómo usarlo: ponlo en el objeto que agrupa las hojas (p. ej. "Porton") y arrastra a
/// "Hojas" los objetos bisagra (el pivote de cada uno debe estar en su bisagra y tener un
/// collider). Si dejas "Hojas" vacío, usa los hijos directos que tengan collider.
/// El menú "VR Quest 3 > Castillo > 3. Portón que se abre" lo monta en el castillo.
/// Desde otros scripts: Abrir(), Cerrar(), Alternar(), y los eventos "Al abrir" / "Al cerrar".
/// </summary>
public class Puerta : XRBaseInteractable
{
    [Header("Hojas")]
    [Tooltip("Objetos bisagra de cada hoja (giran sobre su pivote). Vacío = hijos directos con collider.")]
    [SerializeField] private List<Transform> hojas = new List<Transform>();

    [Tooltip("Grados que gira cada hoja al abrirse del todo.")]
    [SerializeField, Range(10f, 170f)] private float anguloApertura = 95f;

    [Tooltip("Abrir hacia donde mira la flecha azul (Z) de este objeto. Desmarcado = hacia el lado contrario.")]
    [SerializeField] private bool abrirHaciaDelante = true;

    [Header("Movimiento")]
    [Tooltip("Segundos que tarda en abrirse o cerrarse del todo.")]
    [SerializeField, Min(0.1f)] private float duracion = 2f;

    [Tooltip("Forma del movimiento (por defecto arranca y frena suave, como un portón pesado).")]
    [SerializeField] private AnimationCurve curva = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Tooltip("Empezar abierta al dar a Play.")]
    [SerializeField] private bool empiezaAbierta = false;

    [Tooltip("Segundos que se queda abierta antes de cerrarse sola. 0 = no se cierra sola.")]
    [SerializeField, Min(0f)] private float cerrarSolaTras = 0f;

    [Header("Uso con el mando")]
    [Tooltip("Distancia máxima (m) entre la mano y la puerta para poder abrirla. Evita abrirla desde lejos con el rayo.")]
    [SerializeField, Min(0.1f)] private float alcance = 2.5f;

    [Tooltip("No reaccionar si esa mano ya sujeta algo (así no abres la puerta al atacar con la espada).")]
    [SerializeField] private bool ignorarManoOcupada = true;

    [Tooltip("Cerrada con llave: el gatillo no la abre (solo vibra). Se puede abrir con Abrir() desde otro script.")]
    [SerializeField] private bool bloqueada = false;

    [Tooltip("Fuerza de la vibración del mando al usarla (0 = sin vibración).")]
    [SerializeField, Range(0f, 1f)] private float vibracion = 0.3f;

    [Header("Sonido (opcional)")]
    [SerializeField] private AudioClip sonidoAbrir;
    [SerializeField] private AudioClip sonidoCerrar;
    [Tooltip("Sonido al intentar abrirla estando bloqueada.")]
    [SerializeField] private AudioClip sonidoBloqueada;
    [SerializeField, Range(0f, 1f)] private float volumen = 0.8f;

    [Header("Eventos")]
    [SerializeField] private UnityEvent alAbrir = new UnityEvent();
    [SerializeField] private UnityEvent alCerrar = new UnityEvent();

    /// <summary>True si está abierta o abriéndose.</summary>
    public bool Abierta => abriendo;

    /// <summary>0 = cerrada del todo, 1 = abierta del todo.</summary>
    public float Progreso => progreso;

    public bool Bloqueada { get => bloqueada; set => bloqueada = value; }

    // Lo que necesita cada hoja para moverse
    private struct DatosHoja
    {
        public Transform bisagra;
        public Quaternion rotacionCerrada;  // localRotation de partida
        public Vector3 ejeLocal;            // "arriba" del mundo visto desde el padre de la hoja
        public float signo;                 // +1 o -1: hacia qué lado gira
    }

    private readonly List<DatosHoja> datos = new List<DatosHoja>();
    private bool abriendo;
    private float progreso;
    private float tiempoAbierta;
    private AudioSource fuente;

    protected override void Awake()
    {
        if (hojas.Count == 0)
            foreach (Transform hijo in transform)
                if (hijo.GetComponent<Collider>() != null) hojas.Add(hijo);

        // El XRI solo "ve" la puerta por sus colliders: si no se han puesto a mano, usamos los de las hojas
        if (colliders.Count == 0)
            foreach (var hoja in hojas)
                if (hoja != null)
                    foreach (var c in hoja.GetComponentsInChildren<Collider>())
                        if (!c.isTrigger) colliders.Add(c);

        base.Awake();
        PrepararHojas();

        if (empiezaAbierta)
        {
            abriendo = true;
            progreso = 1f;
            AplicarGiro();
        }
    }

    private void PrepararHojas()
    {
        datos.Clear();
        Vector3 lado = transform.forward * (abrirHaciaDelante ? 1f : -1f);

        foreach (var hoja in hojas)
        {
            if (hoja == null) continue;

            // Hacia dónde se extiende la hoja desde su bisagra (al centro de su collider o de su malla)
            Vector3 centro;
            var col = hoja.GetComponentInChildren<Collider>();
            var ren = hoja.GetComponentInChildren<Renderer>();
            if (col != null) centro = col.bounds.center;
            else if (ren != null) centro = ren.bounds.center;
            else
            {
                Debug.LogWarning($"Puerta '{name}': la hoja '{hoja.name}' no tiene collider ni malla; no sé hacia dónde se abre.", this);
                continue;
            }

            Vector3 extension = Vector3.ProjectOnPlane(centro - hoja.position, Vector3.up);
            // Producto vectorial: su signo dice si hay que girar a izquierdas o a derechas para ir hacia "lado"
            float signo = Mathf.Sign(Vector3.Cross(extension, lado).y);
            if (signo == 0f) signo = 1f;

            Quaternion rotPadre = hoja.parent != null ? hoja.parent.rotation : Quaternion.identity;
            datos.Add(new DatosHoja
            {
                bisagra = hoja,
                rotacionCerrada = hoja.localRotation,
                ejeLocal = Quaternion.Inverse(rotPadre) * Vector3.up,
                signo = signo,
            });
        }

        if (datos.Count == 0)
            Debug.LogWarning($"Puerta '{name}': no tiene hojas. Arrastra los objetos bisagra a la lista \"Hojas\".", this);
        if (colliders.Count == 0)
            Debug.LogWarning($"Puerta '{name}': no tiene colliders; la mano no podrá detectarla.", this);
    }

    // Una puerta no se agarra con el grip
    public override bool IsSelectableBy(IXRSelectInteractor interactor) => false;

    public override void ProcessInteractable(XRInteractionUpdateOrder.UpdatePhase updatePhase)
    {
        base.ProcessInteractable(updatePhase);
        if (updatePhase != XRInteractionUpdateOrder.UpdatePhase.Dynamic) return;

        MirarGatillos();
        Mover();
    }

    private void MirarGatillos()
    {
        foreach (var interactor in interactorsHovering)
        {
            if (interactor is not XRBaseInputInteractor mano) continue;
            if (!mano.activateInput.ReadWasPerformedThisFrame()) continue;
            if (ignorarManoOcupada && mano.hasSelection) continue;
            if (!AlAlcance(mano.transform.position)) continue;

            if (bloqueada && !abriendo)
            {
                Vibrar(mano, vibracion * 0.5f, 0.05f);
                Sonar(sonidoBloqueada);
            }
            else
            {
                Vibrar(mano, vibracion, 0.1f);
                Alternar();
            }
            return;   // una sola pulsación por fotograma aunque apunten las dos manos
        }
    }

    private bool AlAlcance(Vector3 posicionMano)
    {
        foreach (var c in colliders)
            if (c != null && c.enabled && (c.ClosestPoint(posicionMano) - posicionMano).sqrMagnitude <= alcance * alcance)
                return true;
        return false;
    }

    private void Mover()
    {
        // Cierre automático
        if (abriendo && cerrarSolaTras > 0f && progreso >= 1f)
        {
            tiempoAbierta += Time.deltaTime;
            if (tiempoAbierta >= cerrarSolaTras) Cerrar();
        }

        float objetivo = abriendo ? 1f : 0f;
        if (Mathf.Approximately(progreso, objetivo)) return;

        progreso = Mathf.MoveTowards(progreso, objetivo, Time.deltaTime / duracion);
        AplicarGiro();
    }

    private void AplicarGiro()
    {
        float angulo = anguloApertura * curva.Evaluate(progreso);
        foreach (var d in datos)
            if (d.bisagra != null)
                d.bisagra.localRotation = Quaternion.AngleAxis(d.signo * angulo, d.ejeLocal) * d.rotacionCerrada;
    }

    /// <summary>Abre la puerta (aunque esté bloqueada: para llaves, palancas, guardias...).</summary>
    public void Abrir()
    {
        if (abriendo) return;
        abriendo = true;
        tiempoAbierta = 0f;
        Sonar(sonidoAbrir);
        alAbrir.Invoke();
    }

    public void Cerrar()
    {
        if (!abriendo) return;
        abriendo = false;
        Sonar(sonidoCerrar);
        alCerrar.Invoke();
    }

    public void Alternar()
    {
        if (abriendo) Cerrar();
        else Abrir();
    }

    private void Vibrar(XRBaseInputInteractor mano, float fuerza, float segundos)
    {
        // Los mandos del simulador (teclado y ratón) no vibran: pedírselo solo llena la Console de avisos
        var simulador = UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation.XRInteractionSimulator.instance;
        if (simulador != null && simulador.isActiveAndEnabled) return;
        if (fuerza > 0f) mano.SendHapticImpulse(fuerza, segundos);
    }

    private void Sonar(AudioClip clip)
    {
        if (clip == null) return;
        if (fuente == null)
        {
            fuente = gameObject.AddComponent<AudioSource>();
            fuente.playOnAwake = false;
            fuente.spatialBlend = 1f;       // sonido 3D: se oye desde la puerta
            fuente.minDistance = 2f;
            fuente.maxDistance = 25f;
        }
        fuente.PlayOneShot(clip, volumen);
    }

    private void OnDrawGizmosSelected()
    {
        // Flecha hacia donde se abre
        Gizmos.color = Color.cyan;
        Vector3 lado = transform.forward * (abrirHaciaDelante ? 1f : -1f);
        Gizmos.DrawLine(transform.position + Vector3.up, transform.position + Vector3.up + lado * 2f);
        Gizmos.DrawWireSphere(transform.position + Vector3.up + lado * 2f, 0.1f);
    }
}
