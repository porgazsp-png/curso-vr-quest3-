using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Crea un potro de madera para practicar con la espada. Menú: VR Quest 3 > Crear potro de prácticas
///
/// El potro es un caballete: una viga redonda sobre dos patas en "A" con travesaños.
///  - Madera: corteza con textura de Quaternius ("Madera Potro"); los cortes y las astillas usan
///    "Madera Fresca" (beige cálido) y "Corte Oscuro" (la raja), en Assets/Pueblo/Armeria/.
///  - Cada pieza tiene su CapsuleCollider (lo más barato que encaja con un cilindro).
///  - Rigidbody kinemático en la raíz: no se mueve con la física, pero puede temblar sin coste.
///  - NavMeshObstacle que recorta el NavMesh: los aldeanos (el herrero) lo rodean.
///  - MaderaGolpeable: muescas, astillas y temblor al golpearlo.
///
/// Se coloca junto a la armería (dentro de "Armory" si existe). Si ya existe, conserva su sitio
/// y solo rehace lo suyo (el hijo "Visual"). No toca nada más de la escena.
/// </summary>
public static class ConstructorPotro
{
    const string Nombre = "Potro de practicas";
    static readonly Vector3 Posicion = new Vector3(3.0f, 0f, -7.2f);   // hueco libre al sur de la mesa de armas
    const string Maderas = "Assets/ThirdParty/KenneyNatureKit/Materials/";
    const string Salida = "Assets/Pueblo/Armeria/";

    [MenuItem("VR Quest 3/Crear potro de prácticas")]
    public static void Crear()
    {
        var escena = EditorSceneManager.GetActiveScene();
        var todos = escena.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();

        // Materiales
        var corteza = AssetDatabase.LoadAssetAtPath<Material>(Maderas + "woodBark.mat");
        var interior = AssetDatabase.LoadAssetAtPath<Material>(Maderas + "woodInner.mat");
        if (corteza == null || interior == null)
        {
            Debug.LogError($"[ConstructorPotro] No encuentro woodBark.mat / woodInner.mat en {Maderas}.");
            return;
        }
        var oscuro = MaterialPlano("Corte Oscuro", new Color(0.28f, 0.17f, 0.10f));
        interior = MaterialPlano("Madera Fresca", new Color(0.86f, 0.70f, 0.48f));   // woodInner es casi blanco: brillaba
        corteza = MaterialCorteza(corteza);   // con textura de corteza de Quaternius; si falta, el plano de Kenney

        // Raíz (si ya existe se reutiliza y se respeta su posición)
        var raiz = todos.FirstOrDefault(t => t.name == Nombre);
        if (raiz == null)
        {
            var go = new GameObject(Nombre);
            Undo.RegisterCreatedObjectUndo(go, "Crear potro");
            raiz = go.transform;
            var armeria = todos.FirstOrDefault(t => t.name == "Armory");
            if (armeria != null) Undo.SetTransformParent(raiz, armeria, "Crear potro");
            raiz.SetPositionAndRotation(Posicion, Quaternion.identity);
        }
        else
        {
            var viejo = raiz.Find("Visual");
            if (viejo != null) Undo.DestroyObjectImmediate(viejo.gameObject);
        }

        // Piezas (la viga va a lo largo de X)
        var visual = new GameObject("Visual").transform;
        Undo.RegisterCreatedObjectUndo(visual.gameObject, "Crear potro");
        visual.SetParent(raiz, false);

        Pieza(visual, "Viga", new Vector3(0f, 0.95f, 0f), new Vector3(0f, 0f, 90f), new Vector3(0.22f, 0.75f, 0.22f), corteza);
        foreach (int lado in new[] { -1, 1 })
        {
            foreach (int fondo in new[] { -1, 1 })
                Pieza(visual, $"Pata {(lado < 0 ? "izq" : "der")} {(fondo < 0 ? "delante" : "detras")}",
                      new Vector3(lado * 0.64f, 0.46f, fondo * 0.18f), new Vector3(-fondo * 16.9f, 0f, lado * 5f),
                      new Vector3(0.085f, 0.49f, 0.085f), corteza);
            Pieza(visual, $"Travesano {(lado < 0 ? "izq" : "der")}", new Vector3(lado * 0.66f, 0.3f, 0f), new Vector3(90f, 0f, 0f),
                  new Vector3(0.06f, 0.25f, 0.06f), corteza);
        }
        Pieza(visual, "Travesano largo", new Vector3(0f, 0.3f, 0f), new Vector3(0f, 0f, 90f), new Vector3(0.065f, 0.66f, 0.065f), corteza);

        // Física: kinemático (no cae ni lo empujan, pero puede temblar barato)
        var cuerpo = raiz.GetComponent<Rigidbody>();
        if (cuerpo == null) cuerpo = Undo.AddComponent<Rigidbody>(raiz.gameObject);
        cuerpo.isKinematic = true;
        cuerpo.useGravity = false;

        // Navegación: que el herrero lo rodee
        var obstaculo = raiz.GetComponent<NavMeshObstacle>();
        if (obstaculo == null) obstaculo = Undo.AddComponent<NavMeshObstacle>(raiz.gameObject);
        obstaculo.shape = NavMeshObstacleShape.Box;
        obstaculo.center = new Vector3(0f, 0.55f, 0f);
        obstaculo.size = new Vector3(1.6f, 1.1f, 0.8f);
        obstaculo.carving = true;
        obstaculo.carveOnlyStationary = true;

        // Que se astille
        var madera = raiz.GetComponent<MaderaGolpeable>();
        if (madera == null) madera = Undo.AddComponent<MaderaGolpeable>(raiz.gameObject);
        var so = new SerializedObject(madera);
        so.FindProperty("maderaFresca").objectReferenceValue = interior;
        so.FindProperty("corteOscuro").objectReferenceValue = oscuro;
        so.FindProperty("visual").objectReferenceValue = visual;
        var astillas = so.FindProperty("materialesAstilla");
        astillas.arraySize = 2;
        astillas.GetArrayElementAtIndex(0).objectReferenceValue = interior;
        astillas.GetArrayElementAtIndex(1).objectReferenceValue = corteza;
        so.ApplyModifiedProperties();

        EditorSceneManager.MarkSceneDirty(escena);
        Selection.activeGameObject = raiz.gameObject;
        Debug.Log($"[ConstructorPotro] Potro creado en {raiz.position}. Guarda la escena (Ctrl+S) para conservarlo.");
    }

    /// <summary>Un cilindro de madera con su CapsuleCollider (el que trae la primitiva).</summary>
    static void Pieza(Transform padre, string nombre, Vector3 posicion, Vector3 giro, Vector3 escala, Material material)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Undo.RegisterCreatedObjectUndo(go, "Crear potro");
        go.name = nombre;
        go.transform.SetParent(padre, false);
        go.transform.localPosition = posicion;
        go.transform.localEulerAngles = giro;
        go.transform.localScale = escala;
        go.GetComponent<MeshRenderer>().sharedMaterial = material;
    }

    /// <summary>
    /// Madera del potro: corteza con veta y relieve (texturas CC0 de Quaternius StylizedNature).
    /// Un cilindro liso con color plano parece una tubería; con la textura parece un tronco.
    /// </summary>
    static Material MaterialCorteza(Material reserva)
    {
        const string Texturas = "Assets/ThirdParty/Quaternius/StylizedNature/Textures/";
        var color = AssetDatabase.LoadAssetAtPath<Texture2D>(Texturas + "Bark_NormalTree.png");
        var relieve = AssetDatabase.LoadAssetAtPath<Texture2D>(Texturas + "Bark_NormalTree_Normal.png");
        if (color == null)
        {
            Debug.LogWarning("[ConstructorPotro] No encuentro Bark_NormalTree.png: uso el material plano de Kenney.");
            return reserva;
        }

        System.IO.Directory.CreateDirectory(Salida);
        string ruta = Salida + "Madera Potro.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(ruta);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, ruta);
        }
        mat.SetTexture("_BaseMap", color);
        mat.SetTextureScale("_BaseMap", new Vector2(1f, 2f));
        mat.SetColor("_BaseColor", new Color(0.9f, 0.82f, 0.75f));   // un pelín más claro y cálido que el tronco de un árbol
        if (relieve != null)
        {
            mat.SetTexture("_BumpMap", relieve);
            mat.EnableKeyword("_NORMALMAP");
        }
        mat.SetFloat("_Smoothness", 0.1f);
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
        return mat;
    }

    /// <summary>Material mate de un color (para los cortes). Se crea una vez en Assets/Pueblo/Armeria/ y se reutiliza.</summary>
    static Material MaterialPlano(string nombre, Color color)
    {
        System.IO.Directory.CreateDirectory(Salida);
        string ruta = Salida + nombre + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(ruta);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, ruta);
        }
        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Smoothness", 0.05f);
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
        return mat;
    }
}
