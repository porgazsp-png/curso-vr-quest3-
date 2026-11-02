using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Pone el script Puerta en el portón del castillo. Menú: VR Quest 3 > Castillo > 3. Portón que se abre
///
/// Busca "Castillo (exterior)/Porton" (lo crea el menú 2) y le añade Puerta con sus dos hojas
/// ("Hoja izquierda" y "Hoja derecha", que ya giran sobre la bisagra). Se abre hacia dentro del
/// castillo (+Z, la flecha azul del Porton).
///
/// Cómo usarlo: ejecuta el menú después del 2. Si vuelves a construir el exterior (menú 2),
/// el portón se rehace sin el script: vuelve a ejecutar este. Si Puerta ya está puesta,
/// solo actualiza la lista de hojas y respeta el resto de ajustes que hayas cambiado.
/// </summary>
public static class ConstructorPuertaCastillo
{
    const string RaizNombre = "Castillo (exterior)";

    [MenuItem("VR Quest 3/Castillo/3. Portón que se abre")]
    public static void Montar()
    {
        var escena = EditorSceneManager.GetActiveScene();
        var raiz = escena.GetRootGameObjects().FirstOrDefault(g => g.name == RaizNombre);
        var porton = raiz != null ? raiz.transform.Find("Porton") : null;
        if (porton == null)
        {
            Debug.LogError($"[ConstructorPuertaCastillo] No encuentro \"{RaizNombre}/Porton\". Ejecuta antes VR Quest 3 > Castillo > 2. Exterior del castillo.");
            return;
        }

        var hojas = new[] { porton.Find("Hoja izquierda"), porton.Find("Hoja derecha") }.Where(h => h != null).ToArray();
        if (hojas.Length == 0)
        {
            Debug.LogError("[ConstructorPuertaCastillo] El Porton no tiene \"Hoja izquierda\" ni \"Hoja derecha\".");
            return;
        }

        var puerta = porton.GetComponent<Puerta>();
        if (puerta == null) puerta = Undo.AddComponent<Puerta>(porton.gameObject);

        // Los campos son privados: se rellenan por SerializedObject (lo mismo que hace el Inspector)
        var so = new SerializedObject(puerta);
        var lista = so.FindProperty("hojas");
        lista.ClearArray();
        for (int i = 0; i < hojas.Length; i++)
        {
            lista.InsertArrayElementAtIndex(i);
            lista.GetArrayElementAtIndex(i).objectReferenceValue = hojas[i];
        }
        so.ApplyModifiedProperties();

        // Las hojas se mueven: que no queden marcadas como estáticas (el batching estático las congelaría)
        foreach (var h in hojas)
            foreach (var t in h.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);

        EditorSceneManager.MarkSceneDirty(escena);
        Selection.activeObject = porton.gameObject;
        Debug.Log($"[ConstructorPuertaCastillo] Puerta puesta en el portón con {hojas.Length} hojas. Guarda la escena (Ctrl+S).");
    }
}
