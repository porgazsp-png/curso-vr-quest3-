using System.Collections.Generic;
using System.Linq;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Da vida a los aldeanos de NatureScene. Menú: VR Quest 3 > Dar vida a los aldeanos
///
/// Pasos (en este orden, porque cada uno necesita el anterior):
///  1. Prepara las animaciones de Mixamo (Assets/ThirdParty/Mixamo) como Humanoid,
///     para poder usarlas en los personajes de Quaternius.
///  2. Monta la herrería: un yunque junto a la armería.
///  3. Genera el NavMesh: el "mapa de suelo pisable" por el que caminan (a partir de los colliders).
///  4. Crea un Animator Controller por oficio: "Moverse" (quieto ↔ andar) + un estado por tarea.
///  5. Pone cada aldeano en su zona con un NavMeshAgent y el script RutinaAldeano.
///
/// Lo que crea va en objetos nuevos ("Aldeanos", "Pueblo/Herreria", "Navegacion") y en
/// Assets/Pueblo/Rutinas/. Si lo vuelves a ejecutar, borra y rehace SOLO esas cosas suyas.
/// Los aldeanos de "Galeria de pruebas (Fase 1)" se desactivan (no se borran).
/// </summary>
public static class ConstructorRutinas
{
    const string Animaciones = "Assets/ThirdParty/Quaternius/Animations/";
    const string Mixamo = "Assets/ThirdParty/Mixamo/";
    const string Personajes = "Assets/Pueblo/Personajes/";
    const string Salida = "Assets/Pueblo/Rutinas/";

    /// <summary>Un oficio: qué animación usa para estar quieto, para andar, y qué tareas hace.</summary>
    class Oficio
    {
        public string nombre, quieto, andar;
        public float velocidad = 1.1f;
        public string[] tareas;
    }

    static readonly Oficio Herrero = new Oficio { nombre = "Herrero", quieto = "Idle_Loop", andar = "Walk_Loop",
        tareas = new[] { "Fixing_Kneeling", "PickUp_Table", "Interact", "Idle_FoldArms_Loop" } };
    static readonly Oficio Arquero = new Oficio { nombre = "Arquero", quieto = "Crouch_Idle_Loop", andar = "Arquero_Sigilo", velocidad = 0.8f,
        tareas = new[] { "Arquero_Disparo", "Crouch_Idle_Loop", "Idle_Loop" } };
    static readonly Oficio Lenador = new Oficio { nombre = "Lenador", quieto = "Idle_Loop", andar = "Walk_Loop",
        tareas = new[] { "TreeChopping_Loop" } };
    static readonly Oficio Aldeano = new Oficio { nombre = "Aldeano", quieto = "Idle_Loop", andar = "Walk_Loop", velocidad = 1.0f,
        tareas = new[] { "Farm_Watering", "Farm_PlantSeed", "Farm_Harvest", "Idle_Talking_Loop", "Consume" } };

    static Dictionary<string, AnimationClip> clips;

    [MenuItem("VR Quest 3/Dar vida a los aldeanos")]
    public static void Crear()
    {
        System.IO.Directory.CreateDirectory(Salida);

        // 1. Animaciones
        PrepararMixamo("Arquero_Disparo", bucle: false);
        PrepararMixamo("Arquero_Sigilo", bucle: true);
        clips = new Dictionary<string, AnimationClip>();
        foreach (var fbx in new[] { Animaciones + "UAL1_Standard.fbx", Animaciones + "UAL2_Standard.fbx",
                                    Mixamo + "Arquero_Disparo.fbx", Mixamo + "Arquero_Sigilo.fbx" })
            foreach (var c in AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>())
                if (!c.name.StartsWith("__preview")) clips[c.name] = c;

        // Borrar lo que creó una ejecución anterior (solo lo nuestro)
        foreach (var viejo in new[] { "Aldeanos", "Pueblo/Herreria", "Navegacion" })
        {
            var go = GameObject.Find(viejo);
            if (go != null) Object.DestroyImmediate(go);
        }

        // 2. Herrería: yunque al sur de la armería, junto a la Casa Norte
        var herreria = new GameObject("Herreria").transform;
        herreria.SetParent(GameObject.Find("Pueblo").transform, false);
        var yunque = Prop("Assets/ThirdParty/Quaternius/FantasyProps/Models/Anvil.fbx", herreria,
                          new Vector3(6.5f, 0, -8f), 90f);

        // 3. NavMesh
        ConstruirNavMesh();

        // 4. Animators
        var ctrlHerrero = CrearController(Herrero);
        var ctrlArquero = CrearController(Arquero);
        var ctrlLenador = CrearController(Lenador);
        var ctrlAldeano = CrearController(Aldeano);

        // 5. Aldeanos en sus zonas
        var raiz = new GameObject("Aldeanos").transform;

        // Herrero: entre la casa norte y la armería, con tres puestos de trabajo fijos
        var zHerreria = Zona(raiz, "Zona Herreria", new Vector3(4f, 0, -6.5f));
        var pYunque = Puesto(zHerreria, "Puesto Yunque", yunque.position + new Vector3(0, 0, 1.1f), yunque.position);
        var pMesa = Puesto(zHerreria, "Puesto Mesa", new Vector3(4.2f, 0, -4.2f), new Vector3(3.2f, 0, -4.2f));
        var pArmero = Puesto(zHerreria, "Puesto Armero", new Vector3(0.5f, 0, -4.0f), new Vector3(1.5f, 0, -4.0f));
        Aldeano_("Herrero", "Herrero", raiz, zHerreria, 2.5f, ctrlHerrero, Herrero,
                 new Dictionary<string, Transform> { { "Fixing_Kneeling", pYunque }, { "PickUp_Table", pMesa }, { "Interact", pArmero } });

        // Arqueros: bosque este
        var zBosqueEste = Zona(raiz, "Zona Bosque Este", new Vector3(24f, 0, -25f));
        Aldeano_("Cazador", "Arquero 1", raiz, zBosqueEste, 7f, ctrlArquero, Arquero, null);
        Aldeano_("Cazador", "Arquero 2", raiz, zBosqueEste, 7f, ctrlArquero, Arquero, null, new Vector3(2f, 0, 3f));

        // Leñador: bosque oeste, con un puesto delante de cada uno de los 3 árboles más cercanos
        var zBosqueOeste = Zona(raiz, "Zona Bosque Oeste", new Vector3(-24f, 0, -15f));
        var arboles = GameObject.Find("Pueblo/Vegetacion").transform.Cast<Transform>()
                                .Where(t => t.name.StartsWith("Pine") || t.name.StartsWith("CommonTree"))
                                .OrderBy(t => Vector3.Distance(t.position, zBosqueOeste.position)).Take(3).ToList();
        var puestosArbol = arboles.Select((a, i) =>
            Puesto(zBosqueOeste, "Puesto Arbol " + (i + 1), a.position + (zBosqueOeste.position - a.position).normalized * 1.0f, a.position)).ToArray();
        var lenador = Aldeano_("Lenador", "Lenador", raiz, zBosqueOeste, 6f, ctrlLenador, Lenador, null);
        lenador.tareas = puestosArbol.Select(p => new RutinaAldeano.Tarea { estado = "TreeChopping_Loop", lugar = p, duracionMin = 6, duracionMax = 12 }).ToArray();
        lenador.probabilidadPasear = 0.15f;

        // Aldeanos: delante de la puerta de su casa
        Aldeano_("Aldeana", "Aldeana", raiz, ZonaCasa(raiz, "Casa Oeste"), 4f, ctrlAldeano, Aldeano, null);
        Aldeano_("Paseante_Hombre", "Aldeano Casa Este", raiz, ZonaCasa(raiz, "Casa Este"), 4f, ctrlAldeano, Aldeano, null);
        Aldeano_("Paseante_Mujer", "Aldeana Casa Sur-Este", raiz, ZonaCasa(raiz, "Casa Sur-Este"), 4f, ctrlAldeano, Aldeano, null);

        // Los de la galería de pruebas ya no hacen falta (los animales se quedan)
        var galeria = GameObject.Find("Galeria de pruebas (Fase 1)");
        if (galeria != null)
            foreach (Transform t in galeria.transform)
            {
                var anim = t.GetComponent<Animator>();
                if (anim != null && anim.avatar != null && anim.avatar.isHuman) t.gameObject.SetActive(false);
            }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("[ConstructorRutinas] Aldeanos con vida. Guarda la escena (Ctrl+S) y dale a Play.");
    }

    static void PrepararMixamo(string nombre, bool bucle)
    {
        string ruta = Mixamo + nombre + ".fbx";
        var mi = (ModelImporter)AssetImporter.GetAtPath(ruta);
        if (mi == null) { Debug.LogWarning("[ConstructorRutinas] Falta " + ruta); return; }
        mi.animationType = ModelImporterAnimationType.Human;            // para usarla en cualquier humanoide
        mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        var tomas = mi.defaultClipAnimations;
        foreach (var t in tomas)
        {
            t.name = nombre;
            t.loopTime = bucle;
            // "Bake Into Pose": el personaje anima en el sitio y es el NavMeshAgent quien lo mueve
            t.lockRootRotation = true;
            t.lockRootHeightY = true;
            t.lockRootPositionXZ = true;
            t.keepOriginalOrientation = true;
            t.keepOriginalPositionY = true;   // mantiene la altura (agachado en el sigilo)
        }
        mi.clipAnimations = tomas;
        mi.SaveAndReimport();
    }

    static Transform Prop(string fbx, Transform padre, Vector3 pos, float giroY)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(fbx), padre);
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, giroY, 0));
        // Collider con el tamaño del modelo: el NavMesh lo esquiva y el jugador no lo atraviesa
        var rs = go.GetComponentsInChildren<Renderer>();
        var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
        var col = go.AddComponent<BoxCollider>();
        col.center = go.transform.InverseTransformPoint(b.center);
        col.size = go.transform.InverseTransformVector(b.size);
        col.size = new Vector3(Mathf.Abs(col.size.x), Mathf.Abs(col.size.y), Mathf.Abs(col.size.z));
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
        return go.transform;
    }

    static void ConstruirNavMesh()
    {
        // Radio del agente "Humanoid" a 0.3 m (por defecto 0.5): si no, no caben entre la mesa y el armero
        var ajustes = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/NavMeshAreas.asset")[0]);
        var humanoide = ajustes.FindProperty("m_Settings").GetArrayElementAtIndex(0);
        humanoide.FindPropertyRelative("agentRadius").floatValue = 0.3f;
        ajustes.ApplyModifiedProperties();

        // El jugador (CharacterController) y lo que se puede coger no deben hacer "agujeros" en el NavMesh
        foreach (var ignorar in new[] { "XR Origin (XR Rig)" }.Select(GameObject.Find)
                                .Concat(Object.FindObjectsByType<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>(FindObjectsSortMode.None).Select(g => g.gameObject)))
        {
            if (ignorar == null) continue;
            var mod = ignorar.GetComponent<NavMeshModifier>();
            if (mod == null) mod = Undo.AddComponent<NavMeshModifier>(ignorar);
            mod.ignoreFromBuild = true;
        }

        var nav = new GameObject("Navegacion");
        var superficie = nav.AddComponent<NavMeshSurface>();
        superficie.collectObjects = CollectObjects.All;
        superficie.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        superficie.BuildNavMesh();

        string ruta = Salida + "NavMesh.asset";
        AssetDatabase.DeleteAsset(ruta);   // el de la ejecución anterior
        AssetDatabase.CreateAsset(superficie.navMeshData, ruta);
    }

    static AnimatorController CrearController(Oficio o)
    {
        string ruta = Salida + "Rutina_" + o.nombre + ".controller";
        AssetDatabase.DeleteAsset(ruta);   // el de la ejecución anterior
        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ruta);
        ctrl.AddParameter("Velocidad", AnimatorControllerParameterType.Float);

        var moverse = ctrl.CreateBlendTreeInController("Moverse", out var arbol, 0);
        arbol.blendParameter = "Velocidad";
        arbol.useAutomaticThresholds = false;
        arbol.AddChild(Clip(o.quieto), 0f);
        arbol.AddChild(Clip(o.andar), o.velocidad);

        var sm = ctrl.layers[0].stateMachine;
        foreach (var t in o.tareas.Distinct())
            sm.AddState(t).motion = Clip(t);
        sm.defaultState = moverse;
        return ctrl;
    }

    static AnimationClip Clip(string nombre)
    {
        if (clips.TryGetValue(nombre, out var c)) return c;
        Debug.LogWarning("[ConstructorRutinas] No encuentro la animación " + nombre);
        return null;
    }

    static Transform Zona(Transform padre, string nombre, Vector3 pos)
    {
        var t = new GameObject(nombre).transform;
        t.SetParent(padre, false);
        t.position = Suelo(pos);
        return t;
    }

    /// <summary>Zona delante de la puerta de una casa (la puerta da a la calle).</summary>
    static Transform ZonaCasa(Transform padre, string casa)
    {
        var c = GameObject.Find("Pueblo/Casas/" + casa).transform;
        var puerta = c.GetComponentsInChildren<Transform>().First(t => t.name == "Marco Puerta");   // la pared de la puerta cambia (yeso o ladrillo); el marco lo tienen todas
        var fuera = puerta.position - c.position; fuera.y = 0;
        return Zona(padre, "Zona " + casa, puerta.position + fuera.normalized * 3.5f);
    }

    static Transform Puesto(Transform zona, string nombre, Vector3 pos, Vector3 mirarA)
    {
        var t = new GameObject(nombre).transform;
        t.SetParent(zona, false);
        t.position = Suelo(pos);
        var dir = mirarA - t.position; dir.y = 0;
        t.rotation = Quaternion.LookRotation(dir);
        return t;
    }

    /// <summary>El punto del NavMesh más cercano (para que los puestos queden siempre en suelo pisable).</summary>
    static Vector3 Suelo(Vector3 p) => NavMesh.SamplePosition(p, out var hit, 2f, NavMesh.AllAreas) ? hit.position : p;

    static RutinaAldeano Aldeano_(string prefab, string nombre, Transform padre, Transform zona, float radio,
                                  AnimatorController ctrl, Oficio oficio, Dictionary<string, Transform> lugares,
                                  Vector3 desplazamiento = default)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Personajes + prefab + ".prefab"), padre);
        go.name = nombre;
        go.transform.position = Suelo(zona.position + desplazamiento);

        go.GetComponent<Animator>().runtimeAnimatorController = ctrl;

        var agente = go.AddComponent<NavMeshAgent>();
        agente.speed = oficio.velocidad;
        agente.angularSpeed = 300f;
        agente.acceleration = 4f;
        agente.radius = 0.3f;
        agente.height = 1.8f;
        agente.stoppingDistance = 0.15f;
        agente.autoBraking = true;

        var rutina = go.AddComponent<RutinaAldeano>();
        rutina.zona = zona;
        rutina.radio = radio;
        rutina.tareas = oficio.tareas.Select(t =>
        {
            var clip = Clip(t);
            bool bucle = clip != null && clip.isLooping;
            return new RutinaAldeano.Tarea
            {
                estado = t,
                lugar = lugares != null && lugares.TryGetValue(t, out var l) ? l : null,
                // las animaciones de bucle duran lo que queramos; las de una sola vez, lo que dura el clip
                duracionMin = bucle ? 5f : clip != null ? clip.length : 2f,
                duracionMax = bucle ? 10f : clip != null ? clip.length : 2f,
            };
        }).ToArray();
        return rutina;
    }
}
