using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Una zona del mapa con su propia música (el camino, el castillo...).
///
/// Es una caja invisible SIN collider: no gasta física. MusicaAmbiente mira cada poco
/// si la cabeza del jugador está dentro de alguna zona y pone su tema con un fundido.
/// Si está dentro de varias, gana la de mayor "Prioridad". Fuera de todas suena la
/// "Música por defecto" de MusicaAmbiente.
///
/// Cómo usarlo: crea un objeto vacío, colócalo en el centro de la zona, añádele este script,
/// ajusta "Tamaño" (la caja verde que ves al seleccionarlo) y arrastra un AudioClip a "Tema".
/// Se puede girar el objeto: la caja gira con él.
/// El menú "VR Quest 3 > Música por zonas" crea las zonas del camino y del castillo.
/// </summary>
public class ZonaMusical : MonoBehaviour
{
    [Tooltip("Música que suena dentro de esta zona.")]
    [SerializeField] private AudioClip tema;

    [Tooltip("Tamaño de la caja en metros (ancho X, alto Y, largo Z).")]
    [SerializeField] private Vector3 tamano = new Vector3(20f, 20f, 20f);

    [Tooltip("Si el jugador está en varias zonas a la vez, suena la de prioridad más alta.")]
    [SerializeField] private int prioridad = 0;

    [Tooltip("Volumen de este tema respecto al general (para igualar temas que suenan más fuerte que otros).")]
    [SerializeField, Range(0f, 2f)] private float volumen = 1f;

    public AudioClip Tema => tema;
    public int Prioridad => prioridad;
    public float Volumen => volumen;

    /// <summary>Todas las zonas activas (de todas las escenas cargadas).</summary>
    public static readonly List<ZonaMusical> Activas = new List<ZonaMusical>();

    private void OnEnable() => Activas.Add(this);
    private void OnDisable() => Activas.Remove(this);

    /// <summary>¿Está este punto (en coordenadas del mundo) dentro de la caja?</summary>
    public bool Contiene(Vector3 punto)
    {
        // Pasamos el punto al espacio de la zona (sin escala) y comparamos con media caja
        Vector3 local = Quaternion.Inverse(transform.rotation) * (punto - transform.position);
        Vector3 mitad = tamano * 0.5f;
        return Mathf.Abs(local.x) <= mitad.x && Mathf.Abs(local.y) <= mitad.y && Mathf.Abs(local.z) <= mitad.z;
    }

    private void OnDrawGizmos()
    {
        Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
        Gizmos.color = new Color(0.3f, 1f, 0.4f, 0.06f);
        Gizmos.DrawCube(Vector3.zero, tamano);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
        Gizmos.color = new Color(0.3f, 1f, 0.4f, 0.9f);
        Gizmos.DrawWireCube(Vector3.zero, tamano);
    }
}
