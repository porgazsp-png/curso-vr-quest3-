using System.Collections;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Jump;

/// <summary>
/// Convierte un objeto agarrable en un arma de mano (espada, hacha...).
///
///  - Agarre: la mano coge el arma por el "Punto de agarre" (el puño), con la hoja hacia arriba
///    y el filo hacia delante. El ángulo se ajusta con "Inclinación hoja".
///  - Combo: cada pulsación (gatillo o botón primario A / X; teclas T o 1 en el simulador) lanza
///    el siguiente golpe de la lista "Combo". Por defecto: de arriba abajo → de izquierda a derecha →
///    de abajo arriba, y vuelta a empezar. Si pulsas durante un golpe, el siguiente se encadena;
///    si tardas más de "Ventana combo", se empieza otra vez por el primero.
///    Cada golpe: preparación (lleva la hoja al inicio del arco) → arco (el que corta) → vuelta a la guardia.
///    Lo que gira es el "Pivote" (modelo + filo), no el objeto: el XR Interaction Toolkit fija
///    la posición de agarre al coger.
///  - Golpe: el "Filo" es un BoxCollider que solo sirve de molde (siempre apagado, no gasta nada).
///    Durante el arco el arma "barre" con esa caja (Physics.OverlapBox cada pocos grados), así no se
///    salta ningún objeto aunque bajen los FPS. A lo que toque: lo empuja si tiene Rigidbody (no kinemático),
///    vibra el mando, llama a RecibirGolpe() si tiene un IGolpeable y lanza el evento "Al golpear".
///    Cada objeto se golpea una vez por golpe; el jugador, los triggers y el propio arma se ignoran.
///  - Si el botón de ataque es el mismo que el de saltar (A en la mano derecha), el salto se desactiva
///    mientras sujetas el arma.
///
/// Cómo usarlo: añádelo a un objeto con XRGrabInteractable y con estos hijos:
///   "Grip Attach" (en el puño) y "Pivote Ataque" (en el puño, con el modelo y un hijo "Filo" con BoxCollider).
/// El menú "VR Quest 3 > Preparar armas" lo monta todo solo en la Espada y el Hacha.
/// El modelo debe tener la hoja hacia +Z y el filo hacia +X (como los de Quaternius).
/// </summary>
[RequireComponent(typeof(XRGrabInteractable))]
public class ArmaCuerpoACuerpo : MonoBehaviour
{
    /// <summary>
    /// Un golpe del combo, visto desde la mano (X = derecha, Y = arriba, Z = delante).
    /// La hoja parte de la guardia, se inclina "Apertura" grados hacia delante, se gira "Giro filo" grados
    /// sobre sí misma (para que el filo vaya por delante) y recorre un arco de "Desde" a "Hasta" alrededor de "Eje".
    /// </summary>
    [System.Serializable]
    public class Movimiento
    {
        public string nombre = "Golpe";
        [Tooltip("Eje del arco, visto desde la mano. (1,0,0) = vertical; (0,1,0) = horizontal.")]
        public Vector3 eje = Vector3.right;
        [Tooltip("Ángulo donde empieza el arco (la preparación lleva la hoja aquí).")]
        public float desde = -25f;
        [Tooltip("Ángulo donde acaba el arco.")]
        public float hasta = 120f;
        [Tooltip("Grados que se inclina la hoja hacia delante antes del arco (para golpes horizontales).")]
        public float apertura;
        [Tooltip("Grados que gira la hoja sobre sí misma para que el filo vaya por delante.")]
        public float giroFilo;
        [Tooltip("Segundos del arco (el golpe en sí).")]
        [Min(0.03f)] public float duracion = 0.14f;

        public Quaternion Pose(float t)
        {
            float angulo = Mathf.LerpUnclamped(desde, hasta, t);
            return Quaternion.AngleAxis(angulo, eje) * Quaternion.AngleAxis(apertura, Vector3.right) * Quaternion.AngleAxis(giroFilo, Vector3.up);
        }
    }

    [Header("Agarre")]
    [Tooltip("Punto por donde la mano coge el arma (el puño). Vacío = busca un hijo \"Grip Attach\".")]
    [SerializeField] private Transform puntoDeAgarre;

    [Tooltip("Ángulo de la hoja respecto al mando: 90 = vertical hacia arriba, 0 = recta hacia delante.")]
    [SerializeField, Range(0f, 120f)] private float inclinacionHoja = 80f;

    [Header("Ataque: botones")]
    [Tooltip("Atacar con el gatillo (tecla T en el simulador).")]
    [SerializeField] private bool atacarConGatillo = true;

    [Tooltip("Atacar con el botón primario de la mano que sujeta (A / X; tecla 1 en el simulador).")]
    [SerializeField] private bool atacarConBotonPrimario = true;

    [SerializeField] private InputAction botonPrimarioIzquierdo =
        new InputAction("Ataque izquierdo", InputActionType.Button, "<XRController>{LeftHand}/{PrimaryButton}");

    [SerializeField] private InputAction botonPrimarioDerecho =
        new InputAction("Ataque derecho", InputActionType.Button, "<XRController>{RightHand}/{PrimaryButton}");

    [Tooltip("Si el botón de ataque también salta, desactivar el salto mientras se sujeta el arma.")]
    [SerializeField] private bool bloquearSaltoSiCoincide = true;

    [Header("Ataque: combo")]
    [Tooltip("Hijo que gira en los golpes (lleva el modelo y el filo). Vacío = busca \"Pivote Ataque\".")]
    [SerializeField] private Transform pivote;

    [Tooltip("Golpes que se encadenan, en orden. Al acabar la lista se vuelve al primero.")]
    [SerializeField] private Movimiento[] combo =
    {
        new Movimiento { nombre = "De arriba abajo",      eje = Vector3.right, desde = -25f, hasta = 120f, duracion = 0.14f },
        new Movimiento { nombre = "De izquierda a derecha", eje = Vector3.up,  desde = -80f, hasta =  80f, apertura = 105f, giroFilo = 90f,  duracion = 0.16f },
        new Movimiento { nombre = "De abajo arriba",      eje = Vector3.right, desde = 130f, hasta =  -5f, giroFilo = 180f, duracion = 0.14f },
    };

    [Tooltip("Segundos para llevar la hoja al inicio del arco.")]
    [SerializeField, Min(0.02f)] private float duracionPreparacion = 0.09f;

    [Tooltip("Segundos para volver a la guardia.")]
    [SerializeField, Min(0.02f)] private float duracionVuelta = 0.25f;

    [Tooltip("Segundos tras un golpe en los que la siguiente pulsación continúa el combo (si no, empieza de nuevo).")]
    [SerializeField, Min(0f)] private float ventanaCombo = 0.6f;

    [Header("Golpe")]
    [Tooltip("Caja que marca la zona que corta (siempre apagada, es solo el molde). Vacío = busca un hijo \"Filo\".")]
    [SerializeField] private BoxCollider filo;

    [Tooltip("Capas que el arma puede golpear. Por defecto todas menos \"Ignore Raycast\" (jugador, astillas...).")]
    [SerializeField] private LayerMask capasGolpeables = Physics.DefaultRaycastLayers;

    [Tooltip("Daño que se pasa a los IGolpeable.")]
    [SerializeField, Min(0f)] private float dano = 10f;

    [Tooltip("Impulso con el que empuja a los objetos con Rigidbody.")]
    [SerializeField, Min(0f)] private float empuje = 4f;

    [Tooltip("Fuerza de la vibración del mando al golpear (0 = nada).")]
    [SerializeField, Range(0f, 1f)] private float vibracion = 0.6f;

    [SerializeField] private AudioClip sonidoTajo;
    [SerializeField] private AudioClip sonidoGolpe;

    [Header("Eventos")]
    public UnityEvent alAtacar;
    public UnityEvent<Collider> alGolpear;

    private XRGrabInteractable agarre;
    private IXRSelectInteractor mano;        // la mano que lo sujeta (null = en el suelo)
    private InputAction botonActivo;         // botón primario de esa mano
    private Coroutine tajo;
    private bool pendiente;                  // se pulsó durante un golpe: encadenar el siguiente
    private int siguienteGolpe;
    private float finUltimoGolpe = -10f;
    private Quaternion poseActual = Quaternion.identity;   // pose de la hoja vista desde la mano
    private Quaternion reposoPivote;
    private Quaternion agarreEnPadre = Quaternion.identity; // giro del agarre en el espacio del padre del pivote
    private AudioSource fuenteAudio;
    private readonly HashSet<Collider> golpeados = new HashSet<Collider>();
    private readonly Collider[] solapes = new Collider[16];
    private const float PasoBarrido = 10f;   // grados máximos entre dos comprobaciones del barrido

    // El salto es uno para todo el juego: si dos armas lo bloquean, se reactiva al soltar la última
    private static JumpProvider salto;
    private static int bloqueosDeSalto;
    private bool bloqueoSaltoPropio;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ReiniciarEstatico()
    {
        salto = null;
        bloqueosDeSalto = 0;
    }

    private void Awake()
    {
        agarre = GetComponent<XRGrabInteractable>();
        if (puntoDeAgarre == null) puntoDeAgarre = BuscarHijo("Grip Attach");
        if (pivote == null) pivote = BuscarHijo("Pivote Ataque");
        if (filo == null) { var f = BuscarHijo("Filo"); if (f != null) filo = f.GetComponent<BoxCollider>(); }

        if (puntoDeAgarre != null)
        {
            agarre.attachTransform = puntoDeAgarre;
            OrientarAgarre();
        }
        else Debug.LogWarning($"[ArmaCuerpoACuerpo] \"{name}\" no tiene \"Grip Attach\": se cogerá por el centro.", this);

        if (pivote != null)
        {
            reposoPivote = pivote.localRotation;
            if (puntoDeAgarre != null)
                agarreEnPadre = Quaternion.Inverse(pivote.parent.rotation) * puntoDeAgarre.rotation;
        }
        else Debug.LogWarning($"[ArmaCuerpoACuerpo] \"{name}\" no tiene \"Pivote Ataque\": golpeará sin animación.", this);

        if (filo != null)
        {
            filo.isTrigger = true;
            filo.enabled = false;   // solo es el molde del barrido; apagado no participa en la física
        }
        else Debug.LogWarning($"[ArmaCuerpoACuerpo] \"{name}\" no tiene \"Filo\": los golpes no tocarán nada.", this);

        if (combo == null || combo.Length == 0)
            Debug.LogWarning($"[ArmaCuerpoACuerpo] \"{name}\" tiene el combo vacío: no atacará.", this);
    }

    private void OnEnable()
    {
        agarre.selectEntered.AddListener(AlCoger);
        agarre.selectExited.AddListener(AlSoltar);
        agarre.activated.AddListener(AlApretarGatillo);
    }

    private void OnDisable()
    {
        agarre.selectEntered.RemoveListener(AlCoger);
        agarre.selectExited.RemoveListener(AlSoltar);
        agarre.activated.RemoveListener(AlApretarGatillo);
        Liberar();
    }

    /// <summary>Lanza el siguiente golpe del combo (o lo deja encadenado si ya hay uno). También vale desde un evento.</summary>
    public void Atacar()
    {
        if (combo == null || combo.Length == 0 || !isActiveAndEnabled) return;
        if (tajo != null) { pendiente = true; return; }
        if (Time.time - finUltimoGolpe > ventanaCombo) siguienteGolpe = 0;
        tajo = StartCoroutine(Combo());
    }

    // ---------- Agarre ----------

    /// <summary>
    /// Gira el punto de agarre para que, vista desde la mano, la hoja (+Z del arma) apunte hacia arriba
    /// con la inclinación elegida y el filo (+X del arma) mire hacia delante.
    /// </summary>
    private void OrientarAgarre()
    {
        float a = inclinacionHoja * Mathf.Deg2Rad;
        var hoja = new Vector3(0f, Mathf.Sin(a), Mathf.Cos(a));
        var delante = new Vector3(0f, -Mathf.Cos(a), Mathf.Sin(a));
        var armaVistaDesdeLaMano = Quaternion.LookRotation(hoja, Vector3.Cross(hoja, delante));
        puntoDeAgarre.localRotation = Quaternion.Inverse(armaVistaDesdeLaMano);
    }

    private void AlCoger(SelectEnterEventArgs args)
    {
        Liberar();   // por si pasa de una mano a otra
        mano = args.interactorObject;
        siguienteGolpe = 0;
        if (!atacarConBotonPrimario) return;

        botonActivo = mano.handedness == InteractorHandedness.Left ? botonPrimarioIzquierdo
                    : mano.handedness == InteractorHandedness.Right ? botonPrimarioDerecho
                    : null;
        if (botonActivo == null) return;

        botonActivo.performed += AlApretarBoton;
        botonActivo.Enable();
        if (bloquearSaltoSiCoincide) BloquearSalto();
    }

    private void AlSoltar(SelectExitEventArgs args) => Liberar();

    /// <summary>Deja el arma en reposo: sin botón escuchando, sin bloquear el salto y sin golpe a medias.</summary>
    private void Liberar()
    {
        if (botonActivo != null)
        {
            botonActivo.performed -= AlApretarBoton;
            botonActivo.Disable();
            botonActivo = null;
        }
        DesbloquearSalto();
        mano = null;

        if (tajo != null) { StopCoroutine(tajo); tajo = null; }
        pendiente = false;
        PonerPose(Quaternion.identity);
    }

    private void AlApretarGatillo(ActivateEventArgs args)
    {
        if (atacarConGatillo) Atacar();
    }

    private void AlApretarBoton(InputAction.CallbackContext contexto) => Atacar();

    // ---------- Combo ----------

    private IEnumerator Combo()
    {
        while (true)
        {
            pendiente = false;
            var golpe = combo[siguienteGolpe];
            siguienteGolpe = (siguienteGolpe + 1) % combo.Length;

            golpeados.Clear();
            alAtacar?.Invoke();
            Reproducir(sonidoTajo);

            yield return Llevar(golpe.Pose(0f), duracionPreparacion);   // preparación
            yield return Arco(golpe);                                   // el golpe
            finUltimoGolpe = Time.time;

            if (!pendiente) yield return Llevar(Quaternion.identity, duracionVuelta, true);   // vuelta (interrumpible)
            if (!pendiente) break;
        }
        tajo = null;
    }

    /// <summary>Lleva la hoja de la pose actual a otra, sin golpear. Si "interrumpible", para al pulsar otra vez.</summary>
    private IEnumerator Llevar(Quaternion destino, float duracion, bool interrumpible = false)
    {
        Quaternion origen = poseActual;
        for (float t = 0f; t < 1f; t += Time.deltaTime / duracion)
        {
            if (interrumpible && pendiente) yield break;
            PonerPose(Quaternion.Slerp(origen, destino, Mathf.SmoothStep(0f, 1f, t)));
            yield return null;
        }
        PonerPose(destino);
    }

    /// <summary>Recorre el arco del golpe barriendo con el filo.</summary>
    private IEnumerator Arco(Movimiento golpe)
    {
        float anterior = 0f;
        for (float t = 0f; ; t += Time.deltaTime / golpe.duracion)
        {
            float actual = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            Barrer(golpe, anterior, actual);
            anterior = actual;
            if (t >= 1f) yield break;
            yield return null;
        }
    }

    /// <summary>Gira el pivote para que la hoja tenga esta pose vista desde la mano (identidad = guardia).</summary>
    private void PonerPose(Quaternion pose)
    {
        poseActual = pose;
        if (pivote != null)
            pivote.localRotation = agarreEnPadre * pose * Quaternion.Inverse(agarreEnPadre) * reposoPivote;
    }

    // ---------- Golpe ----------

    /// <summary>Mueve la hoja de t0 a t1 del arco comprobando el filo cada pocos grados.</summary>
    private void Barrer(Movimiento golpe, float t0, float t1)
    {
        float grados = Mathf.Abs(golpe.hasta - golpe.desde) * Mathf.Abs(t1 - t0);
        int pasos = Mathf.Max(1, Mathf.CeilToInt(grados / PasoBarrido));
        Vector3 antes = filo != null ? filo.transform.TransformPoint(filo.center) : Vector3.zero;

        for (int i = 1; i <= pasos; i++)
        {
            PonerPose(golpe.Pose(Mathf.Lerp(t0, t1, (float)i / pasos)));
            if (filo == null) continue;

            var t = filo.transform;
            Vector3 centro = t.TransformPoint(filo.center);
            Vector3 direccion = centro - antes;
            antes = centro;

            int n = Physics.OverlapBoxNonAlloc(centro, Vector3.Scale(filo.size, t.lossyScale) * 0.5f,
                                              solapes, t.rotation, capasGolpeables, QueryTriggerInteraction.Ignore);
            for (int k = 0; k < n; k++) Golpear(solapes[k], direccion);
        }
    }

    private void Golpear(Collider otro, Vector3 movimiento)
    {
        if (golpeados.Contains(otro)) return;                             // una vez por golpe
        if (otro.transform.IsChildOf(transform)) return;                  // el propio arma
        if (otro.GetComponentInParent<XROrigin>() != null) return;        // el jugador
        golpeados.Add(otro);

        Vector3 centroFilo = filo.transform.TransformPoint(filo.center);
        // ClosestPoint solo vale para cajas, esferas, cápsulas y mallas convexas (no para casas ni terreno)
        bool exacto = otro is BoxCollider || otro is SphereCollider || otro is CapsuleCollider
                   || (otro is MeshCollider malla && malla.convex);
        Vector3 punto = exacto ? otro.ClosestPoint(centroFilo) : otro.ClosestPointOnBounds(centroFilo);
        Vector3 direccion = movimiento.sqrMagnitude > 1e-8f ? movimiento.normalized : filo.transform.right;

        var cuerpo = otro.attachedRigidbody;
        if (cuerpo != null && !cuerpo.isKinematic && cuerpo.gameObject != gameObject)
            cuerpo.AddForceAtPosition(direccion * empuje, punto, ForceMode.Impulse);

        var golpeable = otro.GetComponentInParent<IGolpeable>();
        golpeable?.RecibirGolpe(new Golpe
        {
            arma = this, punto = punto, direccion = direccion, ejeHoja = filo.transform.forward, dano = dano
        });

        if (vibracion > 0f && mano is XRBaseInputInteractor interactor)
            interactor.SendHapticImpulse(vibracion, 0.1f);
        Reproducir(sonidoGolpe);
        alGolpear?.Invoke(otro);
    }

    // ---------- Salto ----------

    private void BloquearSalto()
    {
        if (bloqueoSaltoPropio) return;
        if (salto == null) salto = FindAnyObjectByType<JumpProvider>();
        if (salto == null || !Coinciden(AccionDeSalto(), botonActivo)) return;

        bloqueoSaltoPropio = true;
        if (bloqueosDeSalto++ == 0) salto.enabled = false;
    }

    private void DesbloquearSalto()
    {
        if (!bloqueoSaltoPropio) return;
        bloqueoSaltoPropio = false;
        if (--bloqueosDeSalto == 0 && salto != null) salto.enabled = true;
    }

    private static InputAction AccionDeSalto()
    {
        var lector = salto.jumpInput;
        return lector.inputActionReferencePerformed != null ? lector.inputActionReferencePerformed.action
                                                            : lector.inputActionPerformed;
    }

    /// <summary>¿Las dos acciones usan el mismo botón? (por ruta de binding o por control real).</summary>
    private static bool Coinciden(InputAction a, InputAction b)
    {
        if (a == null || b == null) return false;
        foreach (var ba in a.bindings)
            foreach (var bb in b.bindings)
                if (!string.IsNullOrEmpty(ba.effectivePath) && ba.effectivePath == bb.effectivePath) return true;
        foreach (var control in a.controls)
            if (b.controls.IndexOf(c => c == control) >= 0) return true;
        return false;
    }

    // ---------- Utilidades ----------

    private void Reproducir(AudioClip clip)
    {
        if (clip == null) return;
        if (fuenteAudio == null)
        {
            fuenteAudio = gameObject.AddComponent<AudioSource>();
            fuenteAudio.playOnAwake = false;
            fuenteAudio.spatialBlend = 1f;   // sonido 3D: se oye desde el arma
        }
        fuenteAudio.pitch = Random.Range(0.9f, 1.1f);
        fuenteAudio.PlayOneShot(clip);
    }

    private Transform BuscarHijo(string nombre)
    {
        foreach (var t in GetComponentsInChildren<Transform>(true))
            if (t != transform && t.name == nombre) return t;
        return null;
    }

    private void OnDrawGizmosSelected()
    {
        // En la vista Scene: punto de agarre en amarillo y filo en rojo
        if (puntoDeAgarre != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(puntoDeAgarre.position, 0.03f);
        }
        if (filo != null)
        {
            Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.8f);
            Gizmos.matrix = filo.transform.localToWorldMatrix;
            Gizmos.DrawWireCube(filo.center, filo.size);
        }
    }
}
