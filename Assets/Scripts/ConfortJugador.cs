using System.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Comfort;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;

/// <summary>
/// Panel de confort del jugador: todo lo que marea (o no) en un solo sitio del Inspector.
///
///  - Velocidad al andar con el stick izquierdo (Continuous Move Provider).
///  - Grados de cada giro con el stick derecho (Snap Turn) y si se puede dar media vuelta tirando hacia atrás.
///  - Viñeta antimareo (Tunneling Vignette del XRI): oscurece los bordes de la vista mientras te
///    mueves o giras con el stick. Tu cerebro ve menos movimiento "periférico" y se marea menos.
///  - Rescate de altura: si al arrancar la cabeza sale casi a ras de suelo (las gafas no han
///    detectado el suelo), sube la cámara a una altura por defecto y avisa en la Console.
///
/// Cómo funciona: no mueve nada por sí mismo; al empezar (y cada vez que cambias un valor en Play)
/// copia estos valores a los componentes del XRI que hay dentro del XR Origin.
///
/// Cómo usarlo: va en el XR Origin (XR Rig). El menú "VR Quest 3 > Controles y confort VR" lo pone.
/// Puedes tocar los valores en Play para probar y luego copiarlos (los cambios en Play no se guardan).
/// </summary>
[RequireComponent(typeof(XROrigin))]
public class ConfortJugador : MonoBehaviour
{
    [Header("Movimiento (stick izquierdo)")]
    [Tooltip("Metros por segundo al andar. 2 es un paso tranquilo; más de 3 marea a mucha gente.")]
    [SerializeField, Range(0.5f, 5f)] private float velocidadMovimiento = 2f;

    [Header("Giro (stick derecho)")]
    [Tooltip("Grados de cada giro a izquierda o derecha. 30–45 es lo habitual.")]
    [SerializeField, Range(15f, 90f)] private float gradosGiro = 45f;

    [Tooltip("Tirar del stick derecho hacia atrás da media vuelta (180º).")]
    [SerializeField] private bool mediaVuelta = true;

    [Header("Viñeta antimareo")]
    [Tooltip("Oscurecer los bordes de la vista al moverse y girar.")]
    [SerializeField] private bool usarVineta = true;

    [Tooltip("0 = casi no se nota, 1 = túnel muy cerrado. 0,5 va bien para empezar; bájala cuando te acostumbres.")]
    [SerializeField, Range(0f, 1f)] private float intensidadVineta = 0.5f;

    [Tooltip("Lo difuminado que es el borde de la viñeta (0 = borde duro).")]
    [SerializeField, Range(0f, 1f)] private float suavidadBorde = 0.25f;

    [Header("Rescate de altura")]
    [Tooltip("Si la cabeza sale por debajo de esta altura al arrancar, se considera que no se detectó el suelo.")]
    [SerializeField, Range(0f, 1f)] private float alturaMinimaCabeza = 0.5f;

    [Tooltip("Altura de los ojos que se pone en ese caso (metros).")]
    [SerializeField, Range(1f, 2f)] private float alturaRescate = 1.65f;

    private XROrigin origen;
    private ContinuousMoveProvider mover;
    private SnapTurnProvider girar;
    private TunnelingVignetteController vineta;

    private void Awake()
    {
        origen = GetComponent<XROrigin>();
        mover = GetComponentInChildren<ContinuousMoveProvider>(true);
        girar = GetComponentInChildren<SnapTurnProvider>(true);
        vineta = GetComponentInChildren<TunnelingVignetteController>(true);
    }

    private IEnumerator Start()
    {
        Aplicar();

        // Rescate de altura: damos un par de segundos a que el seguimiento arranque
        yield return new WaitForSeconds(2f);
        ComprobarAltura();
    }

    private void OnValidate()
    {
        if (Application.isPlaying && origen != null) Aplicar();
    }

    /// <summary>Copia los valores del Inspector a los componentes del XRI.</summary>
    public void Aplicar()
    {
        if (mover != null) mover.moveSpeed = velocidadMovimiento;
        else Debug.LogWarning("[ConfortJugador] No encuentro el Continuous/Dynamic Move Provider dentro del XR Origin.", this);

        if (girar != null)
        {
            girar.turnAmount = gradosGiro;
            girar.enableTurnAround = mediaVuelta;
        }
        else Debug.LogWarning("[ConfortJugador] No encuentro el Snap Turn Provider dentro del XR Origin.", this);

        if (vineta != null)
        {
            vineta.gameObject.SetActive(usarVineta);
            var p = vineta.defaultParameters;
            // apertureSize: 1 = vista abierta, más pequeño = túnel más cerrado
            p.apertureSize = Mathf.Lerp(0.95f, 0.4f, intensidadVineta);
            p.featheringEffect = suavidadBorde;
            vineta.defaultParameters = p;
        }
        else if (usarVineta)
            Debug.LogWarning("[ConfortJugador] No hay TunnelingVignette bajo la cámara. Ejecuta VR Quest 3 > Controles y confort VR.", this);
    }

    private void ComprobarAltura()
    {
        if (origen.Camera == null || origen.CameraFloorOffsetObject == null) return;

        float alturaCabeza = origen.CameraInOriginSpaceHeight;
        if (alturaCabeza >= alturaMinimaCabeza) return;

        // La cabeza está casi en el suelo: subimos el "Camera Offset" lo que falte hasta la altura de rescate
        var offset = origen.CameraFloorOffsetObject.transform;
        offset.localPosition += Vector3.up * (alturaRescate - alturaCabeza);
        Debug.LogWarning($"[ConfortJugador] La cabeza estaba a {alturaCabeza:F2} m del suelo (¿no se detectó el suelo?). " +
                         $"La subo a {alturaRescate:F2} m. Revisa el límite (Guardian) de las gafas.", this);
    }
}
