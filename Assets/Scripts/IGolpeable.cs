using UnityEngine;

/// <summary>
/// Datos de un golpe de arma: quién golpea, dónde, hacia dónde y cuánto daño hace.
/// </summary>
public struct Golpe
{
    public ArmaCuerpoACuerpo arma;
    public Vector3 punto;       // punto de contacto (en el mundo)
    public Vector3 direccion;   // hacia dónde iba el tajo (normalizada)
    public Vector3 ejeHoja;     // hacia dónde apuntaba la hoja (normalizada): la línea del corte
    public float dano;
}

/// <summary>
/// Cualquier cosa que reacciona cuando un arma la golpea (un muñeco de prácticas, un barril, un enemigo...).
///
/// Cómo usarlo: en tu script escribe "public class MiCosa : MonoBehaviour, IGolpeable" y crea el método
/// "public void RecibirGolpe(Golpe golpe) { ... }". El arma lo encuentra sola en el objeto golpeado
/// (o en sus padres) y lo llama una vez por tajo.
/// </summary>
public interface IGolpeable
{
    void RecibirGolpe(Golpe golpe);
}
