using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Management;

/// <summary>
/// Decide si se juega con las gafas (mandos reales) o con teclado y ratón
/// (XR Interaction Simulator). Así puedes probar la escena con o sin la Quest.
///
/// Cómo usarlo:
/// 1. Crea un GameObject vacío en la escena y llámalo "SelectorDeModo".
/// 2. Añádele este script.
/// 3. Arrastra el objeto "XR Interaction Simulator" de la Hierarchy al campo "Simulador".
/// 4. Elige el modo en el Inspector.
/// </summary>
public class SelectorDeModo : MonoBehaviour
{
    public enum ModoDeJuego
    {
        Automatico,     // Si hay gafas conectadas usa las gafas; si no, teclado y ratón
        Gafas,          // Fuerza usar las gafas y los mandos reales
        TecladoYRaton   // Fuerza el simulador aunque haya gafas
    }

    [Tooltip("Automático detecta solo si hay gafas conectadas.")]
    public ModoDeJuego modo = ModoDeJuego.Automatico;

    [Tooltip("Arrastra aquí el objeto XR Interaction Simulator de la escena.")]
    [SerializeField] private GameObject simulador;

    // Solo lectura: para ver en el Inspector qué modo se ha elegido al final
    [SerializeField] private bool usandoSimulador;

    private IEnumerator Start()
    {
        // Esperamos un frame: los dispositivos XR tardan un momento en aparecer
        yield return null;

        usandoSimulador = DebeUsarSimulador();

        if (simulador != null)
        {
            simulador.SetActive(usandoSimulador);
        }
        else
        {
            Debug.LogWarning("[SelectorDeModo] No has asignado el XR Interaction Simulator en el Inspector.");
        }

        Debug.Log(usandoSimulador
            ? "[SelectorDeModo] Modo TECLADO Y RATÓN (simulador activo)"
            : "[SelectorDeModo] Modo GAFAS (mandos reales)");
    }

    private bool DebeUsarSimulador()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        // Dentro de la Quest nunca queremos el simulador
        return false;
#else
        switch (modo)
        {
            case ModoDeJuego.Gafas:
                return false;
            case ModoDeJuego.TecladoYRaton:
                return true;
            default:
                return !HayGafasConectadas();
        }
#endif
    }

    private bool HayGafasConectadas()
    {
        // 1. ¿Está arrancado el sistema XR (OpenXR)?
        var settings = XRGeneralSettings.Instance;
        if (settings == null || settings.Manager == null || settings.Manager.activeLoader == null)
            return false;

        // 2. ¿Hay algún dispositivo "en la cabeza" (unas gafas)?
        var dispositivos = new List<InputDevice>();
        InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.HeadMounted, dispositivos);
        return dispositivos.Count > 0;
    }
}
