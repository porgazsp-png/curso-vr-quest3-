using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Mueve los dedos de la mano según los botones del mando.
///
/// El Animator de las manos tiene un Blend Tree con dos parámetros:
///   Grip    (0 = mano abierta, 1 = puño)     → botón lateral del mando (grip)
///   Trigger (0 = índice estirado, 1 = pinza) → gatillo
/// Este script lee cada frame cuánto está apretado cada botón (de 0 a 1) y se lo pasa al Animator.
///
/// Funciona igual con los mandos de la Quest y con el XR Interaction Simulator (teclado y ratón).
///
/// Como el agarre es "de un toque" (grip para coger, grip otra vez para soltar), mientras la mano
/// sujeta algo el puño se queda cerrado aunque ya no aprietes el grip.
///
/// Cómo usarlo: añádelo al objeto de la mano que tiene el Animator y asigna las dos acciones
/// (XRI Left/Right Interaction → "Select Value" para el grip y "Activate Value" para el gatillo).
/// En NatureScene ya está puesto en "Left Hand Model" y "Right Hand Model" (dentro de cada Controller).
/// </summary>
[RequireComponent(typeof(Animator))]
public class AnimarManoConInput : MonoBehaviour
{
    [Tooltip("Acción con el valor del grip (XRI ... Interaction/Select Value).")]
    [SerializeField] private InputActionProperty accionGrip;

    [Tooltip("Acción con el valor del gatillo (XRI ... Interaction/Activate Value).")]
    [SerializeField] private InputActionProperty accionGatillo;

    [Tooltip("Rapidez con la que los dedos siguen al botón. Más alto = más brusco.")]
    [SerializeField] private float suavizado = 20f;

    [Tooltip("Mantener el puño cerrado mientras la mano sujeta un objeto.")]
    [SerializeField] private bool cerrarPunoAlSujetar = true;

    [Tooltip("Interactor que coge los objetos con esta mano. Vacío = busca el Near-Far Interactor del mismo mando.")]
    [SerializeField] private XRBaseInteractor interactor;

    private static readonly int ParamGrip = Animator.StringToHash("Grip");
    private static readonly int ParamTrigger = Animator.StringToHash("Trigger");

    private Animator animator;
    private float grip, gatillo;   // valores suavizados que se mandan al Animator

    private void Awake()
    {
        animator = GetComponent<Animator>();
        if (interactor == null && transform.parent != null)
            interactor = transform.parent.GetComponentInChildren<NearFarInteractor>(true);
    }

    private void OnEnable()
    {
        // Normalmente el Input Action Manager del XR Origin ya las activa; activarlas otra vez no hace daño
        accionGrip.action?.Enable();
        accionGatillo.action?.Enable();
    }

    private void Update()
    {
        float gripObjetivo = Leer(accionGrip);
        float gatilloObjetivo = Leer(accionGatillo);
        if (cerrarPunoAlSujetar && interactor != null && interactor.hasSelection) gripObjetivo = 1f;

        // Suavizado: los dedos no saltan de golpe aunque el botón sea digital (como en el simulador)
        float t = 1f - Mathf.Exp(-suavizado * Time.deltaTime);
        grip = Mathf.Lerp(grip, gripObjetivo, t);
        gatillo = Mathf.Lerp(gatillo, gatilloObjetivo, t);

        animator.SetFloat(ParamGrip, grip);
        animator.SetFloat(ParamTrigger, gatillo);
    }

    private static float Leer(InputActionProperty propiedad)
    {
        var accion = propiedad.action;
        return accion != null ? Mathf.Clamp01(accion.ReadValue<float>()) : 0f;
    }
}
