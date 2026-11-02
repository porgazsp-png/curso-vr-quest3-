using System.Collections.Generic;
using System.Linq;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Comfort;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;

/// <summary>
/// Deja el XR Origin listo para jugar cómodo en la Quest. Menú: VR Quest 3 > Controles y confort VR
///
/// Solo toca el XR Origin (XR Rig) de la escena abierta:
///  1. Altura: Tracking Origin Mode = Floor (la altura de la cabeza la dan las gafas, no un número fijo).
///     CharacterController de adulto (1,8 m, radio 0,2) y el XR Origin apoyado justo sobre el suelo.
///  2. Controles: stick izquierdo = andar (8 direcciones, según hacia dónde mira la cabeza);
///     stick derecho = giro de 45º a los lados, media vuelta hacia atrás y teletransporte hacia delante.
///     Se apaga el giro continuo (sobraba: estaba activo junto al giro por pasos).
///  3. Confort: viñeta antimareo (TunnelingVignette del XRI) bajo la cámara, que se activa al andar,
///     girar y teletransportarse; y el panel ConfortJugador para ajustar velocidad, giro e intensidad.
///  4. Manos: las manos de Oculus (Left/Right Hand Model) se dibujan aunque Unity crea que no se ven
///     (Update When Offscreen) y se animan siempre (son solo dos: no cuesta nada).
///
/// Cómo usarlo: abre NatureScene y ejecuta el menú. Se puede repetir sin duplicar nada.
/// </summary>
public static class ConstructorControlesVR
{
    const string PrefabVineta = "Assets/Samples/XR Interaction Toolkit/3.2.1/Starter Assets/TunnelingVignette/TunnelingVignette.prefab";

    [MenuItem("VR Quest 3/Controles y confort VR")]
    public static void Preparar()
    {
        var origen = Object.FindFirstObjectByType<XROrigin>(FindObjectsInactive.Include);
        if (origen == null)
        {
            Debug.LogError("[ConstructorControlesVR] No hay XR Origin en la escena abierta.");
            return;
        }
        var informe = new List<string>();

        // ---------- 1. Altura ----------
        Undo.RecordObject(origen, "Controles VR");
        origen.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
        informe.Add("Tracking Origin Mode = Floor");

        var cuerpo = origen.GetComponent<CharacterController>();
        if (cuerpo != null)
        {
            Undo.RecordObject(cuerpo, "Controles VR");
            cuerpo.height = 1.8f;
            cuerpo.center = new Vector3(0f, 0.9f, 0f);
            cuerpo.radius = 0.2f;
            cuerpo.skinWidth = 0.02f;     // Unity recomienda ~10 % del radio
            cuerpo.stepOffset = 0.3f;     // escalones normales sí; muretes no
            informe.Add("CharacterController 1,8 m / centro 0,9 / radio 0,2 / piel 0,02 / escalón 0,3");
        }

        ApoyarEnElSuelo(origen, informe);

        // ---------- 2. Controles ----------
        var giroContinuo = origen.GetComponentInChildren<ContinuousTurnProvider>(true);
        if (giroContinuo != null && giroContinuo.enabled)
        {
            Undo.RecordObject(giroContinuo, "Controles VR");
            giroContinuo.enabled = false;
            informe.Add("ContinuousTurnProvider desactivado (se usa Snap Turn)");
        }

        var mover = origen.GetComponentInChildren<ContinuousMoveProvider>(true);
        if (mover != null)
        {
            Undo.RecordObject(mover, "Controles VR");
            mover.moveSpeed = 2f;
            mover.enableStrafe = true;    // sin esto el stick solo va adelante/atrás (y gira con los lados)
            mover.forwardSource = origen.Camera.transform;
        }

        foreach (var gestor in origen.GetComponentsInChildren<MonoBehaviour>(true).Where(m => m.GetType().Name == "ControllerInputActionManager"))
        {
            // Izquierdo: andar. Derecho: girar por pasos + teletransporte
            bool izquierdo = gestor.name.Contains("Left");
            var so = new SerializedObject(gestor);
            so.FindProperty("m_SmoothMotionEnabled").boolValue = izquierdo;
            so.FindProperty("m_SmoothTurnEnabled").boolValue = false;
            so.ApplyModifiedProperties();
        }
        informe.Add("Mando izquierdo: Move · Mando derecho: Snap Turn + Teleport");

        // ---------- 3. Confort ----------
        var vineta = origen.Camera.GetComponentInChildren<TunnelingVignetteController>(true);
        if (vineta == null)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabVineta);
            if (prefab == null)
                Debug.LogWarning($"[ConstructorControlesVR] No encuentro {PrefabVineta}: importa los Starter Assets del XRI.");
            else
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, origen.Camera.transform);
                Undo.RegisterCreatedObjectUndo(go, "Controles VR");
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                vineta = go.GetComponent<TunnelingVignetteController>();
                informe.Add("TunnelingVignette añadida bajo la Main Camera");
            }
        }
        if (vineta != null)
        {
            Undo.RecordObject(vineta, "Controles VR");
            var lista = new List<LocomotionVignetteProvider>();
            void Anadir(LocomotionProvider p) { if (p != null) lista.Add(new LocomotionVignetteProvider { locomotionProvider = p, enabled = true }); }
            Anadir(mover);
            Anadir(origen.GetComponentInChildren<SnapTurnProvider>(true));
            Anadir(origen.GetComponentInChildren<TeleportationProvider>(true));
            vineta.locomotionVignetteProviders = lista;
            PrefabUtility.RecordPrefabInstancePropertyModifications(vineta);
            informe.Add($"Viñeta conectada a {lista.Count} formas de moverse (andar, girar, teletransporte)");
        }

        if (origen.GetComponent<ConfortJugador>() == null)
        {
            Undo.AddComponent<ConfortJugador>(origen.gameObject);
            informe.Add("ConfortJugador añadido al XR Origin");
        }

        // ---------- 4. Manos ----------
        foreach (var animator in origen.GetComponentsInChildren<Animator>(true).Where(a => a.name.Contains("Hand Model")))
        {
            Undo.RecordObject(animator, "Controles VR");
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            foreach (var piel in animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Undo.RecordObject(piel, "Controles VR");
                piel.updateWhenOffscreen = true;
            }
            informe.Add($"{animator.name}: siempre animada y visible");
        }

        foreach (var c in origen.GetComponentsInChildren<Component>(true))
            if (c != null && PrefabUtility.IsPartOfPrefabInstance(c)) PrefabUtility.RecordPrefabInstancePropertyModifications(c);

        EditorSceneManager.MarkSceneDirty(origen.gameObject.scene);
        Debug.Log("[ConstructorControlesVR] Hecho:\n - " + string.Join("\n - ", informe) + "\nGuarda la escena (Ctrl+S).");
    }

    static void ApoyarEnElSuelo(XROrigin origen, List<string> informe)
    {
        // Rayo hacia abajo desde un poco por encima, ignorando el propio rig
        var desde = origen.transform.position + Vector3.up * 2f;
        var golpes = Physics.RaycastAll(desde, Vector3.down, 10f, ~0, QueryTriggerInteraction.Ignore)
                            .Where(h => !h.collider.transform.IsChildOf(origen.transform))
                            .OrderBy(h => h.distance).ToArray();
        if (golpes.Length == 0)
        {
            Debug.LogWarning("[ConstructorControlesVR] No hay suelo debajo del XR Origin: colócalo a mano sobre el Ground.");
            return;
        }
        float suelo = golpes[0].point.y;
        if (Mathf.Abs(origen.transform.position.y - suelo) > 0.005f)
        {
            Undo.RecordObject(origen.transform, "Controles VR");
            var p = origen.transform.position;
            origen.transform.position = new Vector3(p.x, suelo, p.z);
        }
        informe.Add($"XR Origin en {origen.transform.position:F2}, sobre '{golpes[0].collider.name}' (suelo y = {suelo:F2})");
    }
}
