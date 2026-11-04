# Configuración VR Meta Quest 3 que funciona (sin mareo)

Probada en gafas el 2026-11-04 con este proyecto: la cabeza gira bien, se ven las manos y no marea.
Úsala como plantilla para otros proyectos de Unity + XR Interaction Toolkit.

## Versiones

- Unity 6 (6000.2.6f2) + URP 17.2
- XR Interaction Toolkit 3.2.1 (con los *Starter Assets* importados desde Package Manager > Samples)
- Input System 1.14.2 (Active Input Handling = Input System Package)
- OpenXR (XR Plug-in Management)

## 1. Proyecto (Android)

| Ajuste | Valor |
|---|---|
| Plataforma | Android |
| Scripting Backend | IL2CPP, solo ARM64 |
| Minimum API Level | 30 (Android 11) o más |
| Color Space | Linear |
| Graphics API | Vulkan |
| XR Plug-in Management (Android) | OpenXR ✔, *Initialize XR on Startup* ✔ |
| OpenXR (Android) | Feature *Meta Quest Support* ✔ · perfil *Oculus Touch Controller* |
| Render Mode | Single Pass Instanced |
| Foveated Rendering (OpenXR Android) | ✔ (el nivel lo pone el script, ver abajo) |
| Build Settings | Solo la escena principal marcada |

## 2. ⚠️ Lo más importante: el simulador de teclado y ratón

El objeto **XR Interaction Simulator** tiene que estar **DESACTIVADO en la escena**.

- Su componente `SimulatedDeviceLifecycleManager` (opción *Remove Other HMD Devices*) **borra las gafas
  reales** del sistema de input en cuanto se enciende, y no las devuelve.
- Se ejecuta antes que cualquier script tuyo, así que apagarlo en `Awake`/`Start` llega tarde.
- Síntoma en la Quest: **el mundo "va contigo"** al girar la cabeza, la cabeza está a 0 m (no hay seguimiento),
  no se ven las manos y marea muchísimo.
- Arreglo:
  1. Simulador desactivado en la escena; un script (`SelectorDeModo`) lo activa **solo en el PC sin gafas**.
  2. *Project Settings > XR Plug-in Management > XR Interaction Toolkit*: **desmarcar** "Use XR Interaction
     Simulator in scenes" (`XRDeviceSimulatorSettings > Automatically Instantiate Simulator Prefab = false`).
     Si no, el XRI crea otro simulador y destruye el tuyo por duplicado.

## 3. XR Origin (XR Rig) — prefab de Starter Assets

| Ajuste | Valor | Por qué |
|---|---|---|
| Tracking Origin Mode | **Floor** | Altura real del jugador (de pie) |
| Camera Y Offset | 1,36 (solo se usa en el editor) | |
| CharacterController | alto 1,8 · centro 0,9 · radio 0,2 · piel 0,02 · escalón 0,3 | No atravesar paredes |
| Main Camera > Tracked Pose Driver | Rotation And Position | |

## 4. Locomoción y confort

| Ajuste | Valor |
|---|---|
| Stick izquierdo | **Continuous Move**, 2 m/s, strafe ✔ |
| Dirección al andar (*Forward Source*) | **Mando izquierdo** (no la cabeza): puedes mirar a los lados mientras andas recto |
| Stick derecho | **Snap Turn 45º** + media vuelta al tirar atrás + teleport delante |
| Continuous Turn Provider | **Desactivado** (el giro suave marea) |
| Tunneling Vignette (oscurecer bordes al moverse) | **Quitada** (`ConfortJugador > Usar Viñeta = false`): a este jugador le molestaba más que ayudaba |
| Foveated Rendering | 0,66 (`ConfortJugador > Nivel Foveated`); FPS estables a 72 = menos mareo |
| Manos (`Left/Right Hand Model`) | Animator *Always Animate*, Skinned Mesh *Update When Offscreen* |
| Controller Visual | Apagados (se usan las manos) |

## 5. Scripts reutilizables de este proyecto

Copiar de `Assets/Scripts/`:

- `SelectorDeModo.cs`: gafas o teclado+ratón; enciende el simulador solo cuando hace falta y avisa si está activo en la escena.
- `ColisionSimulador.cs`: con el simulador, WASD mueve con CharacterController (no atraviesa paredes).
- `ConfortJugador.cs`: velocidad, giro, dirección al andar, viñeta, foveated y rescate de altura, todo en un Inspector.
- `Editor/ConstructorControlesVR.cs`: menú *VR Quest 3 > Controles y confort VR* que aplica las secciones 3 y 4 de golpe.
- `AnimarManoConInput.cs`: grip y gatillo del mando → Animator de la mano.

## 6. Cómo depurar en las gafas

- adb viene con Unity: `.../Unity/Hub/Editor/<versión>/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe`
- `adb devices` (Quest por USB, depuración activada) → `adb logcat -c` → abrir el juego → `adb logcat | grep Unity`
- Los `Debug.Log` de tus scripts salen ahí. La línea `VrApi FPS=72/72` dice si va fluido.
- Si `ConfortJugador` avisa de "La cabeza estaba a 0,00 m": **no hay seguimiento de cabeza** → revisar la sección 2.
