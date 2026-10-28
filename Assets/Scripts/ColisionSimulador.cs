using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;

/// <summary>
/// Con el XR Interaction Simulator, las teclas W A S D mueven la cámara (la "cabeza")
/// directamente, sin pasar por el CharacterController, y por eso se atraviesan paredes.
///
/// Este script, mientras el simulador está en modo "cuerpo" (cabeza y manos a la vez, el modo normal):
///  1. Pone a 0 el desplazamiento propio del simulador (solo mientras se juega, no cambia ningún asset).
///  2. Lee las mismas teclas (W A S D) y mueve el XR Origin con CharacterController.Move(),
///     que SÍ choca con paredes, árboles y muebles. Q y E (subir/bajar) se ignoran.
/// Si cambias a mover solo una mano o solo la cabeza (Tab), el simulador vuelve a su comportamiento normal.
///
/// Con las gafas (simulador desactivado) no hace nada.
///
/// Cómo usarlo: añádelo al XR Origin (XR Rig) y arrastra el objeto "XR Interaction Simulator" al campo "Simulador".
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class ColisionSimulador : MonoBehaviour
{
    [Tooltip("El objeto XR Interaction Simulator de la escena.")]
    [SerializeField] private GameObject simulador;

    [Tooltip("Velocidad al andar con W A S D (metros por segundo).")]
    [SerializeField] private float velocidad = 2f;

    private XRInteractionSimulator sim;
    private CharacterController cuerpo;
    private Transform cabeza;
    private float multiplicadorOriginal;
    private bool controlando;

    private void Start()
    {
        cuerpo = GetComponent<CharacterController>();
        cabeza = Camera.main != null ? Camera.main.transform : null;
        if (simulador != null)
            sim = simulador.GetComponent<XRInteractionSimulator>();
        if (sim != null)
            multiplicadorOriginal = sim.bodyTranslateMultiplier;
    }

    private void Update()
    {
        if (sim == null || cabeza == null)
            return;

        bool activo = simulador.activeInHierarchy && sim.isActiveAndEnabled && sim.manipulatingFPS;

        // Encender/apagar el desplazamiento propio del simulador solo cuando cambia el modo
        if (activo != controlando)
        {
            sim.bodyTranslateMultiplier = activo ? 0f : multiplicadorOriginal;
            controlando = activo;
        }
        if (!activo)
            return;

        // Las mismas teclas que usa el simulador (A/D = X, W/S = Z)
        float x = sim.translateXInput.ReadValue();
        float z = sim.translateZInput.ReadValue();
        if (Mathf.Approximately(x, 0f) && Mathf.Approximately(z, 0f))
            return;

        // Dirección según hacia dónde miras, pero siempre en horizontal
        Vector3 adelante = Vector3.ProjectOnPlane(cabeza.forward, Vector3.up).normalized;
        Vector3 derecha = Vector3.ProjectOnPlane(cabeza.right, Vector3.up).normalized;
        Vector3 direccion = Vector3.ClampMagnitude(adelante * z + derecha * x, 1f);

        cuerpo.Move(direccion * velocidad * Time.deltaTime);
    }

    private void OnDisable()
    {
        // Dejar el simulador como estaba
        if (sim != null && controlando)
            sim.bodyTranslateMultiplier = multiplicadorOriginal;
        controlando = false;
    }
}
