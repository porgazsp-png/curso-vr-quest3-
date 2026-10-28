using UnityEngine;

/// <summary>
/// Hace que una luz parpadee como el fuego (fogatas, forjas, antorchas).
///
/// Cómo usarlo: añádelo a un GameObject que tenga un componente Light.
/// Usa ruido Perlin (valores aleatorios pero suaves) para que el parpadeo no sea brusco.
/// </summary>
[RequireComponent(typeof(Light))]
public class LuzParpadeante : MonoBehaviour
{
    [Tooltip("Intensidad media de la luz.")]
    public float intensidadBase = 2f;

    [Tooltip("Cuánto sube y baja la intensidad alrededor de la media.")]
    public float variacion = 0.6f;

    [Tooltip("Rapidez del parpadeo.")]
    public float velocidad = 3f;

    private Light luz;
    private float semilla;   // para que dos fuegos no parpadeen igual

    private void Awake()
    {
        luz = GetComponent<Light>();
        semilla = Random.Range(0f, 100f);
    }

    private void Update()
    {
        // PerlinNoise devuelve 0..1; lo pasamos a -1..1
        float ruido = Mathf.PerlinNoise(semilla, Time.time * velocidad) * 2f - 1f;
        luz.intensity = intensidadBase + ruido * variacion;
    }
}
