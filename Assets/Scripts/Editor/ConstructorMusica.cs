using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Monta la música por zonas en la escena. Menú: VR Quest 3 > Música por zonas
///
///  - Ajusta la importación de las pistas de Assets/ThirdParty/Musica/ para Quest:
///    "Streaming" (se lee del disco poco a poco: casi no gasta memoria) y Vorbis al 50 %.
///  - Crea el objeto "Musica" con MusicaAmbiente (pueblo = música por defecto) y dos zonas:
///    "Zona Camino" (río y subida a la colina) y "Zona Castillo" (colina y murallas, gana al camino).
///
/// Cómo usarlo: ejecuta el menú en NatureScene. Si "Musica" ya existe, no la rehace: solo crea
/// lo que falte, así que tus cambios en volúmenes o tamaños se respetan.
/// </summary>
public static class ConstructorMusica
{
    const string Carpeta = "Assets/ThirdParty/Musica/";
    const string Pueblo = Carpeta + "Pueblo_The_Old_Tower_Inn_Loop.wav";
    const string Camino = Carpeta + "Camino_Exploration.mp3";
    const string Castillo = Carpeta + "Castillo_A_Legend_Will_Rise.mp3";

    [MenuItem("VR Quest 3/Música por zonas")]
    public static void Montar()
    {
        foreach (var ruta in new[] { Pueblo, Camino, Castillo }) AjustarImportacion(ruta);

        var pueblo = AssetDatabase.LoadAssetAtPath<AudioClip>(Pueblo);
        var camino = AssetDatabase.LoadAssetAtPath<AudioClip>(Camino);
        var castillo = AssetDatabase.LoadAssetAtPath<AudioClip>(Castillo);
        if (pueblo == null || camino == null || castillo == null)
        {
            Debug.LogError($"[ConstructorMusica] Faltan pistas en {Carpeta} (Pueblo, Camino o Castillo).");
            return;
        }

        var escena = EditorSceneManager.GetActiveScene();
        var raiz = escena.GetRootGameObjects().FirstOrDefault(g => g.name == "Musica");
        if (raiz == null)
        {
            raiz = new GameObject("Musica");
            Undo.RegisterCreatedObjectUndo(raiz, "Música por zonas");
        }

        var musica = raiz.GetComponent<MusicaAmbiente>();
        if (musica == null)
        {
            musica = Undo.AddComponent<MusicaAmbiente>(raiz);
            var so = new SerializedObject(musica);
            so.FindProperty("musicaPorDefecto").objectReferenceValue = pueblo;
            so.ApplyModifiedProperties();
        }

        // Camino: del río (z≈26) a la puerta del castillo; el castillo (prioridad mayor) gana donde se pisan
        Zona(raiz.transform, "Zona Camino", camino, new Vector3(0f, 5f, 34f), new Vector3(80f, 40f, 24f), 1, 1f);
        // Castillo: la colina entera (centro z 64, radio 20) y algo de margen
        Zona(raiz.transform, "Zona Castillo", castillo, new Vector3(0f, 10f, 66f), new Vector3(60f, 50f, 46f), 2, 1f);

        EditorSceneManager.MarkSceneDirty(escena);
        Selection.activeObject = raiz;
        Debug.Log("[ConstructorMusica] Música por zonas lista (pueblo por defecto, camino y castillo). Guarda la escena (Ctrl+S).");
    }

    static void Zona(Transform padre, string nombre, AudioClip tema, Vector3 centro, Vector3 tamano, int prioridad, float volumen)
    {
        if (padre.Find(nombre) != null) return;   // ya existe: respetar lo que haya

        var go = new GameObject(nombre);
        Undo.RegisterCreatedObjectUndo(go, "Música por zonas");
        go.transform.SetParent(padre, false);
        go.transform.position = centro;
        var zona = go.AddComponent<ZonaMusical>();
        var so = new SerializedObject(zona);
        so.FindProperty("tema").objectReferenceValue = tema;
        so.FindProperty("tamano").vector3Value = tamano;
        so.FindProperty("prioridad").intValue = prioridad;
        so.FindProperty("volumen").floatValue = volumen;
        so.ApplyModifiedProperties();
    }

    static void AjustarImportacion(string ruta)
    {
        var imp = AssetImporter.GetAtPath(ruta) as AudioImporter;
        if (imp == null) return;

        var ajustes = imp.defaultSampleSettings;
        if (ajustes.loadType == AudioClipLoadType.Streaming && ajustes.compressionFormat == AudioCompressionFormat.Vorbis) return;

        ajustes.loadType = AudioClipLoadType.Streaming;          // música larga: leer del disco, no a memoria
        ajustes.compressionFormat = AudioCompressionFormat.Vorbis;
        ajustes.quality = 0.5f;                                   // de sobra para música de fondo
        ajustes.preloadAudioData = false;
        imp.defaultSampleSettings = ajustes;
        imp.loadInBackground = true;
        imp.SaveAndReimport();
    }
}
