using UnityEditor;

/// <summary>
/// Ajustes automáticos al importar los assets de Quaternius, pensados para Meta Quest 3.
/// Unity llama a estos métodos solo cuando importa un archivo dentro de Assets/ThirdParty/Quaternius/.
///
/// Solo actúa la PRIMERA vez que se importa cada archivo (importSettingsMissing):
/// si luego cambias algo a mano en el Inspector, se respeta.
/// </summary>
public class ImportadorQuaternius : AssetPostprocessor
{
    const string Raiz = "Assets/ThirdParty/Quaternius/";

    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(Raiz)) return;
        var ti = (TextureImporter)assetImporter;
        if (!ti.importSettingsMissing) return;

        // Las texturas vienen hasta 4K: en gafas 1024 se ve bien y gasta 16 veces menos memoria
        ti.maxTextureSize = 1024;

        // Solo si el NOMBRE termina en "_Normal" (ojo: hay texturas llamadas "Bark_NormalTree"
        // que son de color, no mapas de normales)
        if (System.IO.Path.GetFileNameWithoutExtension(assetPath).EndsWith("_Normal"))
            ti.textureType = TextureImporterType.NormalMap;
        // Roughness / ORM guardan datos, no colores: van en espacio lineal
        if (assetPath.Contains("Roughness") || assetPath.Contains("ORM"))
            ti.sRGBTexture = false;

        // Formato comprimido que entiende la GPU de Quest
        var android = ti.GetPlatformTextureSettings("Android");
        android.overridden = true;
        android.maxTextureSize = 1024;
        android.format = TextureImporterFormat.ASTC_6x6;
        ti.SetPlatformTextureSettings(android);
    }

    void OnPreprocessModel()
    {
        if (!assetPath.StartsWith(Raiz)) return;
        var mi = (ModelImporter)assetImporter;
        if (!mi.importSettingsMissing) return;

        mi.importCameras = false;
        mi.importLights = false;

        if (assetPath.Contains("/MedievalVillage/") || assetPath.Contains("/StylizedNature/") || assetPath.Contains("/FantasyProps/"))
        {
            // Escenario: sin huesos ni animaciones, y con UVs para bakear la luz más adelante
            mi.animationType = ModelImporterAnimationType.None;
            mi.importAnimation = false;
            mi.generateSecondaryUV = true;
        }
        else if ((assetPath.Contains("/Characters/") && !assetPath.Contains("/Hair/")) || assetPath.Contains("/Animations/"))
        {
            // Rig Humanoid: permite usar las mismas animaciones (y las de Mixamo) en todos los personajes
            mi.animationType = ModelImporterAnimationType.Human;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        }
        else if (assetPath.Contains("/FarmAnimals/"))
        {
            mi.animationType = ModelImporterAnimationType.Generic;
        }
    }
}
