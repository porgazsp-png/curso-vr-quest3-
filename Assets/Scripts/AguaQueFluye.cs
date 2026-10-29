using UnityEngine;

/// <summary>
/// Hace que el agua parezca correr: desplaza poco a poco la textura (y sus ondas) del material.
///
/// Es el truco más barato para un río en Quest: el agua es un plano opaco y lo único que se mueve
/// es el "offset" de la textura (no hay transparencias, reflejos en tiempo real ni malla que se deforme).
/// En URP Lit el desplazamiento de la textura base se aplica también al mapa de normales (las ondas).
///
/// Cómo usarlo: añádelo al objeto del agua (con un MeshRenderer y un material URP Lit con normal map)
/// y ajusta "Velocidad" (en repeticiones de textura por segundo; X = a lo largo del río).
/// El menú "VR Quest 3 > Castillo > 1. Camino, río y colina" ya lo pone en el río.
/// </summary>
[RequireComponent(typeof(Renderer))]
public class AguaQueFluye : MonoBehaviour
{
    [Tooltip("Velocidad de la corriente (repeticiones de textura por segundo). X = a lo largo del río.")]
    [SerializeField] private Vector2 velocidad = new Vector2(0.05f, 0.008f);

    private Material material;
    private Vector2 offsetInicial;

    private void Awake()
    {
        // .material crea una copia solo para este río: así no se modifica el archivo del material
        material = GetComponent<Renderer>().material;
        if (!material.HasProperty("_BaseMap"))
        {
            Debug.LogWarning($"[AguaQueFluye] El material de \"{name}\" no es URP Lit (no tiene _BaseMap): el agua no se moverá.", this);
            enabled = false;
            return;
        }
        offsetInicial = material.GetTextureOffset("_BaseMap");
    }

    private void Update()
    {
        // Repeat(…, 1) mantiene el número pequeño aunque pasen horas (sin pérdida de precisión)
        var desplazamiento = new Vector2(Mathf.Repeat(velocidad.x * Time.time, 1f), Mathf.Repeat(velocidad.y * Time.time, 1f));
        material.SetTextureOffset("_BaseMap", offsetInicial + desplazamiento);
    }

    private void OnDestroy()
    {
        if (material != null) Destroy(material);
    }
}
