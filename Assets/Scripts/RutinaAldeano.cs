using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Da vida a un aldeano: pasea por su zona y de vez en cuando hace una tarea (regar, talar, disparar...).
///
/// Ciclo: elegir una tarea → andar hasta su sitio → hacerla un rato → volver a empezar.
///  - Para andar usa un NavMeshAgent: busca caminos por el NavMesh (el "mapa de suelo pisable")
///    y así no atraviesa paredes, árboles ni muebles.
///  - Para las animaciones usa el Animator:
///      · estado "Moverse": Blend Tree que mezcla quieto ↔ andar según el parámetro "Velocidad".
///      · un estado por tarea (con el nombre de la tarea), al que se salta con CrossFade.
///
/// Cómo usarlo: añádelo a un aldeano con Animator, pon la Zona y las tareas.
/// La herramienta "VR Quest 3 > Dar vida a los aldeanos" lo monta todo sola.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class RutinaAldeano : MonoBehaviour
{
    [System.Serializable]
    public class Tarea
    {
        [Tooltip("Nombre del estado del Animator que se reproduce.")]
        public string estado;
        [Tooltip("Sitio fijo donde se hace (p. ej. delante del yunque). Vacío = un punto al azar de la zona.")]
        public Transform lugar;
        [Tooltip("Segundos que dura (se elige un valor al azar entre los dos).")]
        public float duracionMin = 4f, duracionMax = 8f;
    }

    [Tooltip("Centro de la zona por la que se mueve el aldeano.")]
    public Transform zona;
    [Tooltip("Radio de la zona en metros.")]
    public float radio = 4f;
    public Tarea[] tareas;

    [Tooltip("Probabilidad de, en vez de hacer una tarea, solo dar un paseo y quedarse quieto un momento.")]
    [Range(0, 1)] public float probabilidadPasear = 0.3f;

    private static readonly int ParamVelocidad = Animator.StringToHash("Velocidad");
    private const string EstadoMoverse = "Moverse";

    private NavMeshAgent agente;
    private Animator animator;

    private void Awake()
    {
        agente = GetComponent<NavMeshAgent>();
        animator = GetComponent<Animator>();
    }

    private IEnumerator Start()
    {
        // Colocarlo sobre el NavMesh (por si en la escena está unos centímetros fuera)
        if (NavMesh.SamplePosition(transform.position, out var punto, 3f, NavMesh.AllAreas))
            agente.Warp(punto.position);

        // Que no empiecen todos sincronizados
        yield return new WaitForSeconds(Random.Range(0f, 3f));

        while (true)
        {
            Tarea tarea = null;
            if (tareas != null && tareas.Length > 0 && Random.value > probabilidadPasear)
                tarea = tareas[Random.Range(0, tareas.Length)];

            Vector3 destino = tarea != null && tarea.lugar != null ? tarea.lugar.position : PuntoAlAzar();
            yield return Andar(destino);

            if (tarea != null)
            {
                if (tarea.lugar != null) yield return Girar(tarea.lugar.rotation);
                animator.CrossFadeInFixedTime(tarea.estado, 0.3f);
                yield return new WaitForSeconds(Random.Range(tarea.duracionMin, tarea.duracionMax));
                animator.CrossFadeInFixedTime(EstadoMoverse, 0.3f);
            }
            else
            {
                yield return new WaitForSeconds(Random.Range(2f, 5f));   // quieto mirando
            }
        }
    }

    private void Update()
    {
        // El Blend Tree pasa solo de "quieto" a "andar" según lo rápido que va el agente
        animator.SetFloat(ParamVelocidad, agente.velocity.magnitude, 0.1f, Time.deltaTime);
    }

    private IEnumerator Andar(Vector3 destino)
    {
        agente.isStopped = false;
        agente.SetDestination(destino);
        float limite = Time.time + 20f;   // si se atasca, se rinde y elige otra cosa
        while (agente.pathPending || agente.remainingDistance > agente.stoppingDistance + 0.05f)
        {
            if (Time.time > limite) break;
            yield return null;
        }
        agente.isStopped = true;
        agente.velocity = Vector3.zero;
    }

    private IEnumerator Girar(Quaternion objetivo)
    {
        while (Quaternion.Angle(transform.rotation, objetivo) > 2f)
        {
            transform.rotation = Quaternion.RotateTowards(transform.rotation, objetivo, 240f * Time.deltaTime);
            yield return null;
        }
    }

    private Vector3 PuntoAlAzar()
    {
        Vector3 centro = zona != null ? zona.position : transform.position;
        for (int i = 0; i < 10; i++)
        {
            var c = Random.insideUnitCircle * radio;
            if (NavMesh.SamplePosition(centro + new Vector3(c.x, 0, c.y), out var punto, 1.5f, NavMesh.AllAreas))
                return punto.position;
        }
        return transform.position;
    }

    private void OnDrawGizmosSelected()
    {
        // En la vista Scene, al seleccionar el aldeano se ve su zona
        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.6f);
        Gizmos.DrawWireSphere(zona != null ? zona.position : transform.position, radio);
    }
}
