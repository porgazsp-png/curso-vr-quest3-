using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Casters;

/// <summary>
/// Prepara las armas de la escena para usarlas con las manos. Menú: VR Quest 3 > Preparar armas
///
/// Para cada arma (Espada, Hacha):
///  1. Coloca "Grip Attach" en el centro del puño (su giro lo pone ArmaCuerpoACuerpo al empezar).
///  2. Crea "Pivote Ataque" en el puño y mete dentro el "Modelo": es lo que gira en el tajo.
///  3. Crea "Filo": BoxCollider trigger que cubre solo la hoja (o la cabeza del hacha), apagado.
///     Su tamaño se calcula con los vértices del modelo, así encaja sin medir a mano.
///  4. Crea "Zona Agarre": BoxCollider trigger algo más grande que el arma. Solo sirve para que la mano
///     la detecte sin tener que clavarla encima (no cambia la física: el arma no flota ni empuja).
///  5. Añade ArmaCuerpoACuerpo y le asigna esas piezas.
/// Y en los dos mandos, el Near-Far Interactor:
///  - modo "Toggle": grip para coger, grip otra vez para soltar.
///  - esfera de agarre cercano más grande y que detecte triggers (para ver las "Zona Agarre").
///
/// No borra nada: si lo vuelves a ejecutar, reutiliza lo que ya creó y solo recoloca.
/// Para otra arma, añade una línea en "Armas" con su nombre, dónde está el puño y desde dónde corta.
/// </summary>
public static class ConstructorArmas
{
    /// <summary>Medidas en el espacio local del arma, a lo largo de su eje Z (la hoja apunta a +Z).</summary>
    class Arma
    {
        public string nombre;
        public float puno;        // Z del centro del puño
        public float filoDesde;   // a partir de esta Z, lo que corta
    }

    static readonly Arma[] Armas =
    {
        new Arma { nombre = "Espada", puno = -0.37f, filoDesde = -0.20f },   // pomo -0.51, guarda -0.23
        new Arma { nombre = "Hacha",  puno = -0.28f, filoDesde =  0.18f },   // cabeza de 0.21 a 0.45
    };

    const float Margen = 0.03f;          // el filo un poco más grueso que la malla: golpes más fiables
    const float MargenAgarre = 0.08f;    // la zona de agarre sobresale 8 cm del arma por cada lado
    const float RadioAgarreMano = 0.15f; // esfera de agarre cercano de la mano (el Starter Asset trae 0.1)

    [MenuItem("VR Quest 3/Preparar armas")]
    public static void Preparar()
    {
        var escena = EditorSceneManager.GetActiveScene();
        var todos = escena.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToList();

        foreach (var arma in Armas)
        {
            var t = todos.FirstOrDefault(x => x.name == arma.nombre && x.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>() != null);
            if (t == null) { Debug.LogWarning($"[ConstructorArmas] No encuentro \"{arma.nombre}\" con XRGrabInteractable en la escena."); continue; }
            PrepararArma(t, arma);
        }

        foreach (var nf in Object.FindObjectsByType<NearFarInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            Undo.RecordObject(nf, "Preparar armas");
            nf.selectActionTrigger = XRBaseInputInteractor.InputTriggerType.Toggle;

            var esfera = nf.GetComponent<SphereInteractionCaster>();
            if (esfera == null) continue;
            Undo.RecordObject(esfera, "Preparar armas");
            esfera.castRadius = RadioAgarreMano;
            esfera.physicsTriggerInteraction = QueryTriggerInteraction.Collide;
        }

        EditorSceneManager.MarkSceneDirty(escena);
        Debug.Log("[ConstructorArmas] Armas preparadas. Guarda la escena (Ctrl+S) para conservar el cambio.");
    }

    static void PrepararArma(Transform raiz, Arma arma)
    {
        var modelo = raiz.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(m => m.name == "Modelo");
        if (modelo == null || modelo.sharedMesh == null) { Debug.LogWarning($"[ConstructorArmas] \"{arma.nombre}\" no tiene hijo \"Modelo\" con malla."); return; }

        // 1. Punto de agarre en el puño
        var agarre = Hijo(raiz, "Grip Attach", raiz);
        Undo.RecordObject(agarre, "Preparar armas");
        agarre.localPosition = new Vector3(0f, 0f, arma.puno);

        // 2. Pivote en el puño, con el modelo dentro (si ya existía, se mueve sin arrastrar a sus hijos)
        var pivote = Hijo(raiz, "Pivote Ataque", raiz);
        var hijos = pivote.Cast<Transform>().Select(h => (h, h.position, h.rotation)).ToList();
        Undo.RecordObject(pivote, "Preparar armas");
        pivote.localPosition = new Vector3(0f, 0f, arma.puno);
        pivote.localRotation = Quaternion.identity;
        foreach (var (h, p, r) in hijos) { Undo.RecordObject(h, "Preparar armas"); h.SetPositionAndRotation(p, r); }
        if (modelo.transform.parent != pivote) Undo.SetTransformParent(modelo.transform, pivote, "Preparar armas");

        // 3. Filo: caja que envuelve los vértices de la hoja (Z >= filoDesde), en el espacio del pivote
        var aPivote = pivote.worldToLocalMatrix * modelo.transform.localToWorldMatrix;
        var aRaiz = raiz.worldToLocalMatrix * modelo.transform.localToWorldMatrix;
        var verts = modelo.sharedMesh.vertices;
        Bounds? caja = null;
        for (int i = 0; i < verts.Length; i++)
        {
            if (aRaiz.MultiplyPoint3x4(verts[i]).z < arma.filoDesde) continue;
            var p = aPivote.MultiplyPoint3x4(verts[i]);
            if (caja == null) caja = new Bounds(p, Vector3.zero);
            else { var c = caja.Value; c.Encapsulate(p); caja = c; }
        }
        if (caja == null) { Debug.LogWarning($"[ConstructorArmas] \"{arma.nombre}\": no hay malla a partir de Z={arma.filoDesde}."); return; }

        var filoT = Hijo(pivote, "Filo", raiz);
        var filo = filoT.GetComponent<BoxCollider>();
        if (filo == null) filo = Undo.AddComponent<BoxCollider>(filoT.gameObject);
        Undo.RecordObject(filo, "Preparar armas");
        filo.isTrigger = true;
        filo.center = caja.Value.center;
        filo.size = caja.Value.size + new Vector3(Margen, Margen, Margen * 0.5f);
        filo.enabled = false;   // solo se enciende durante el tajo

        // 4. Zona de agarre: copia de la caja sólida del arma, más grande y trigger
        var solida = raiz.GetComponents<BoxCollider>().FirstOrDefault(c => !c.isTrigger);
        if (solida != null)
        {
            var zonaT = Hijo(raiz, "Zona Agarre", raiz);
            Undo.RecordObject(zonaT, "Preparar armas");
            zonaT.localPosition = Vector3.zero;
            zonaT.localRotation = Quaternion.identity;
            zonaT.localScale = Vector3.one;
            var zona = zonaT.GetComponent<BoxCollider>();
            if (zona == null) zona = Undo.AddComponent<BoxCollider>(zonaT.gameObject);
            Undo.RecordObject(zona, "Preparar armas");
            zona.isTrigger = true;
            zona.center = solida.center;
            zona.size = solida.size + Vector3.one * (MargenAgarre * 2f);

            // El XRGrabInteractable descarta los triggers al rellenar solo su lista "Colliders":
            // hay que escribirla a mano con los colliders sólidos + la zona (el filo no, que no es para agarrar)
            var grab = raiz.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
            Undo.RecordObject(grab, "Preparar armas");
            grab.colliders.Clear();
            grab.colliders.AddRange(raiz.GetComponentsInChildren<Collider>(true).Where(c => !c.isTrigger));
            grab.colliders.Add(zona);
            EditorUtility.SetDirty(grab);
        }
        else Debug.LogWarning($"[ConstructorArmas] \"{arma.nombre}\" no tiene BoxCollider sólido: sin zona de agarre.");

        // 5. El script del arma
        var script = raiz.GetComponent<ArmaCuerpoACuerpo>();
        if (script == null) script = Undo.AddComponent<ArmaCuerpoACuerpo>(raiz.gameObject);
        var so = new SerializedObject(script);
        so.FindProperty("puntoDeAgarre").objectReferenceValue = agarre;
        so.FindProperty("pivote").objectReferenceValue = pivote;
        so.FindProperty("filo").objectReferenceValue = filo;
        so.FindProperty("capasGolpeables").intValue = Physics.DefaultRaycastLayers;   // sin "Ignore Raycast" (jugador, astillas)
        so.ApplyModifiedProperties();

        // Los MeshCollider cuestan mucho más que una caja: avisar si alguno se ha colado
        foreach (var mc in raiz.GetComponentsInChildren<MeshCollider>(true))
            Debug.LogWarning($"[ConstructorArmas] \"{mc.name}\" tiene MeshCollider; mejor una caja o cápsula para Quest.", mc);

        Debug.Log($"[ConstructorArmas] {arma.nombre}: puño en Z={arma.puno}, filo {filo.size.ToString("F2")}.");
    }

    /// <summary>Devuelve el hijo con ese nombre (lo busca en todo el arma) o lo crea bajo "padre".</summary>
    static Transform Hijo(Transform padre, string nombre, Transform raiz)
    {
        var t = raiz.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x != raiz && x.name == nombre);
        if (t != null) return t;
        var go = new GameObject(nombre);
        Undo.RegisterCreatedObjectUndo(go, "Preparar armas");
        go.transform.SetParent(padre, false);
        return go.transform;
    }
}
