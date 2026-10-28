using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

/// <summary>
/// Ayudas para montar el pueblo con las piezas del Medieval Village MegaKit (Quaternius).
/// Las piezas siguen una cuadrícula de 2 m: paredes de 2 m de ancho y 3,12 m de alto, suelos de 2x2 m.
///
/// Convención de una casa (en sus coordenadas locales):
///  - El centro de la casa es (0,0,0) y la PUERTA está en el lado +Z (delante).
///  - "ancho" son módulos en X y "fondo" módulos en Z (cada módulo = 2 m).
/// Después se gira la casa entera para que la puerta mire a donde queramos.
/// </summary>
public static class ConstructorPueblo
{
    const string Medieval = "Assets/ThirdParty/Quaternius/MedievalVillage/Models/";
    public const float Modulo = 2f;
    public const float AltoPared = 3.12f;

    /// <summary>Instancia un modelo del kit como hijo de "padre" en posición/giro locales.</summary>
    public static GameObject Pieza(string modelo, Transform padre, Vector3 posLocal, float giroY, bool colision = false)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(Medieval + modelo + ".fbx");
        if (asset == null) { Debug.LogError("No encuentro el modelo " + modelo); return null; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, padre);
        go.transform.localPosition = posLocal;
        // El FBX trae su propia rotación interna (-90 en X, viene de Blender): la conservamos y giramos en Y
        go.transform.localRotation = Quaternion.Euler(0, giroY, 0) * asset.transform.localRotation;
        if (colision) go.AddComponent<MeshCollider>();
        return go;
    }

    /// <summary>
    /// Construye una casa de ancho x fondo módulos. "estilo" elige el material de las paredes
    /// ("Plaster" = encalado con vigas, "UnevenBrick" = piedra).
    /// Devuelve la raíz de la casa (con hijos Suelo, Paredes, Tejado, Interior...).
    /// </summary>
    public static Transform Casa(string nombre, Transform padre, Vector3 posicion, float giroY,
                                 int ancho, int fondo, string estilo = "Plaster", bool puertaAbierta = true)
    {
        var casa = new GameObject(nombre).transform;
        casa.SetParent(padre, false);
        casa.position = posicion;
        casa.rotation = Quaternion.Euler(0, giroY, 0);
        Undo.RegisterCreatedObjectUndo(casa.gameObject, "Construir " + nombre);

        var suelo = Grupo("Suelo", casa);
        var paredes = Grupo("Paredes", casa);
        var tejado = Grupo("Tejado", casa);
        Grupo("Interior", casa);

        float mx = ancho * Modulo / 2f, mz = fondo * Modulo / 2f;

        // Suelo: una baldosa por celda
        for (int i = 0; i < ancho; i++)
            for (int j = 0; j < fondo; j++)
                Pieza("Floor_WoodDark", suelo, new Vector3(-mx + 1 + i * 2, 0.02f, -mz + 1 + j * 2), 0);

        // Paredes: delante (+Z, con puerta en el centro), detrás (-Z), izquierda (-X) y derecha (+X)
        int puerta = ancho / 2;
        for (int i = 0; i < ancho; i++)
        {
            float x = -mx + 1 + i * 2;
            string frente = i == puerta ? "Door_Round" : "Window_Wide_Round";
            Pared(estilo, frente, paredes, new Vector3(x, 0, mz), 0);
            Pared(estilo, i == puerta ? "Window_Wide_Round" : "Straight", paredes, new Vector3(x, 0, -mz), 180);
            if (i == puerta)
            {
                var marco = Pieza("DoorFrame_Round_WoodDark", paredes, new Vector3(x, 0, mz), 0);
                marco.name = "Marco Puerta";
                // La hoja tiene la bisagra en su borde derecho: la colocamos en el lado derecho del hueco
                var hoja = Pieza("Door_1_Round", paredes, new Vector3(x + 0.55f, 0, mz - 0.05f), puertaAbierta ? -95 : 0);
                hoja.name = "Puerta";
                hoja.AddComponent<BoxCollider>();
            }
        }
        for (int j = 0; j < fondo; j++)
        {
            float z = -mz + 1 + j * 2;
            string tipo = j == fondo / 2 ? "Window_Wide_Round" : "Straight";
            Pared(estilo, tipo, paredes, new Vector3(-mx, 0, z), -90);
            Pared(estilo, tipo, paredes, new Vector3(mx, 0, z), 90);
        }
        // Postes de esquina
        foreach (var e in new[] { new Vector3(-mx, 0, -mz), new Vector3(mx, 0, -mz), new Vector3(-mx, 0, mz), new Vector3(mx, 0, mz) })
            Pieza("Corner_Exterior_Wood", paredes, e, 0);

        // Tejado del tamaño de la casa y frontones de ladrillo en los dos extremos
        string roof = "Roof_RoundTiles_" + (ancho * 2) + "x" + (fondo * 2);
        if (AssetDatabase.LoadAssetAtPath<GameObject>(Medieval + roof + ".fbx") == null)
            Debug.LogWarning("No hay tejado " + roof + " en el pack; usa otra medida de casa");
        else
        {
            Pieza(roof, tejado, new Vector3(0, AltoPared, 0), 0, true);
            // Frontones: cierran el triángulo bajo el tejado delante y detrás
            string fronton = "Roof_Front_Brick" + (ancho * 2);
            Pieza(fronton, tejado, new Vector3(0, AltoPared, mz), 0);
            Pieza(fronton, tejado, new Vector3(0, AltoPared, -mz), 180);
        }

        // Suelo invisible: colisión + zona de teletransporte (capa Teleport)
        var tp = new GameObject("Suelo Interior (Teleport)");
        tp.transform.SetParent(casa, false);
        tp.transform.localPosition = new Vector3(0, 0.015f, 0);
        var bc = tp.AddComponent<BoxCollider>();
        bc.size = new Vector3(ancho * Modulo - 0.5f, 0.03f, fondo * Modulo - 0.5f);
        tp.AddComponent<TeleportationArea>().interactionLayers = InteractionLayerMask.GetMask("Teleport");

        return casa;
    }

    const string Props = "Assets/ThirdParty/Quaternius/FantasyProps/Models/";

    /// <summary>Instancia un mueble/objeto del Fantasy Props MegaKit (con colisión de caja por defecto).</summary>
    public static GameObject Mueble(string modelo, Transform padre, Vector3 posLocal, float giroY, bool colision = true)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(Props + modelo + ".fbx");
        if (asset == null) { Debug.LogError("No encuentro el mueble " + modelo); return null; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, padre);
        go.transform.localPosition = posLocal;
        go.transform.localRotation = Quaternion.Euler(0, giroY, 0) * asset.transform.localRotation;
        if (colision) go.AddComponent<BoxCollider>();
        return go;
    }

    /// <summary>
    /// Amuebla una casa construida con Casa(). Coordenadas locales: puerta en +Z, centro en (0,0,0).
    /// Deja libre la zona de la puerta (centro del lado +Z) para poder entrar.
    /// </summary>
    public static void Amueblar(Transform casa, int ancho, int fondo)
    {
        var interior = casa.Find("Interior");
        float mz = fondo * Modulo / 2f;   // 3 (casa de 6 m) o 4 (casa de 8 m)
        float y = 0.03f;                  // justo encima del suelo de madera
        float fondoInt = -mz + 0.35f;     // cara interior de la pared del fondo

        // Cama en la esquina del fondo a la izquierda
        Mueble("Bed_Twin1", interior, new Vector3(-1.6f, y, fondoInt + 1.25f), 0);
        // Mesa grande a la derecha con dos sillas y un candelabro
        Mueble("Table_Large", interior, new Vector3(1.45f, y, 0.1f), 90);
        Mueble("Chair_1", interior, new Vector3(0.55f, y, 0.1f), 90);
        Mueble("Chair_1", interior, new Vector3(2.3f, y, 0.1f), -90);
        Mueble("CandleStick_Triple", interior, new Vector3(1.45f, y + 0.8f, 0.1f), 30, false);
        // Estantería contra la pared del fondo
        Mueble("Bookcase_2", interior, new Vector3(1.5f, y, fondoInt + 0.25f), 0);
        // Barril y caja en la esquina delantera izquierda
        Mueble("Barrel", interior, new Vector3(-2.15f, y, mz - 0.75f), 0);
        Mueble("Crate_Wooden", interior, new Vector3(-1.25f, y, mz - 0.8f), 15);
        // Farol de pared en la pared izquierda
        Mueble("Lantern_Wall", interior, new Vector3(-ancho + 0.4f, 2.1f, 0.2f), 90, false);
        if (fondo >= 4)
        {
            Mueble("Cabinet", interior, new Vector3(-2.3f, y, 1.2f), 90);
            Mueble("Chest_Wood", interior, new Vector3(-1.6f, y, fondoInt + 2.85f), 0);
        }

        // Luz cálida (sin sombras: en Quest las sombras de luces puntuales son caras)
        var luz = new GameObject("Luz Interior");
        luz.transform.SetParent(interior, false);
        luz.transform.localPosition = new Vector3(0.3f, 2.6f, -0.3f);
        var l = luz.AddComponent<Light>();
        l.type = LightType.Point;
        l.range = 7f;
        l.intensity = 1.5f;
        l.color = new Color(1f, 0.76f, 0.48f);
        l.shadows = LightShadows.None;
    }

    /// <summary>
    /// Camino empedrado: coloca baldosas de 2x2 m (4 triángulos cada una) cada ~1,8 m
    /// a lo largo de los puntos dados, giradas en la dirección de avance.
    /// </summary>
    public static Transform Camino(string nombre, Transform padre, Vector3[] puntos)
    {
        var camino = Grupo(nombre, padre);
        for (int i = 0; i < puntos.Length - 1; i++)
        {
            Vector3 a = puntos[i], b = puntos[i + 1];
            float giro = Mathf.Atan2(b.x - a.x, b.z - a.z) * Mathf.Rad2Deg;
            int pasos = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / 1.8f));
            for (int k = 0; k <= pasos; k++)
            {
                var p = Vector3.Lerp(a, b, (float)k / pasos);
                // Alturas alternas mínimas para que las baldosas solapadas no parpadeen (z-fighting)
                p.y = 0.02f + (k % 2) * 0.004f;
                Pieza("Floor_UnevenBrick", camino, camino.InverseTransformPoint(p), giro);
            }
        }
        return camino;
    }

    /// <summary>Plaza empedrada de lado x lado baldosas, centrada en "centro".</summary>
    public static Transform Plaza(string nombre, Transform padre, Vector3 centro, int lado)
    {
        var plaza = Grupo(nombre, padre);
        float o = -(lado - 1) * Modulo / 2f;
        for (int i = 0; i < lado; i++)
            for (int j = 0; j < lado; j++)
                Pieza("Floor_UnevenBrick", plaza, centro + new Vector3(o + i * 2, 0.025f, o + j * 2), 0);
        return plaza;
    }

    static void Pared(string estilo, string tipo, Transform padre, Vector3 pos, float giro)
    {
        var p = Pieza("Wall_" + estilo + "_" + tipo, padre, pos, giro, true);
        if (p == null) return;
        // Las paredes con hueco de ventana llevan su ventana (marco + cristal)
        if (tipo == "Window_Wide_Round") Pieza("Window_Wide_Round1", p.transform.parent, pos, giro);
    }

    static Transform Grupo(string nombre, Transform padre)
    {
        var g = new GameObject(nombre).transform;
        g.SetParent(padre, false);
        return g;
    }
}
