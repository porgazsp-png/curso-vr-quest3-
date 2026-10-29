# Proyecto VR Meta Quest 3 — Pueblo medieval

Proyecto de aprendizaje: un pueblo medieval en VR para Meta Quest 3, hecho en Unity con el XR Interaction Toolkit.
El autor está aprendiendo Unity y VR: explica lo que haces y por qué.

## Reglas de trabajo

- **No borrar ni sobrescribir nada sin OK explícito.** Preferir crear archivos nuevos, variantes de prefab y desactivar en vez de borrar.
- Trabajar en ramas (`main` es la estable). Antes de algo grande, rama nueva.
- Explicar cada paso (estoy aprendiendo).
- Comentarios y nombres en español, como el resto de scripts. Cada script lleva un `<summary>` con "Cómo usarlo".
- Rendimiento Quest (Android, móvil): pocas luces en tiempo real, Animator con culling, LODs, nada de colliders innecesarios.
- Assets de terceros solo CC0 (Kenney, Quaternius) en `Assets/ThirdParty/`.
- Herramientas de construcción como scripts de editor con menú (`Assets/Scripts/Editor/`), para poder regenerar.

### Al crear o cambiar un script
1. **Explicarlo**: qué hace, cómo funciona por dentro y cómo se usa.
2. **Depurarlo**: compilar, mirar la Console y probarlo en Play (por MCP) antes de darlo por bueno.
3. **Preguntar si se quiere extender**, con propuestas concretas.
4. **Pulir métodos y funciones pensando en la experiencia del jugador**: que sean amplios (configurables desde el Inspector, con valores por defecto buenos), robustos (no fallar si falta algo, avisar con un mensaje claro) y que se sientan naturales en VR.
5. **Revisar al final que no haya errores ni avisos** en la Console.

## Tecnología

- Unity 6 + URP 17.2, XR Interaction Toolkit 3.2.1, Input System 1.14.2, OpenXR.
- Unity MCP (`http://127.0.0.1:8080/mcp`) para controlar el editor desde Claude Code. Si falla al arrancar la sesión: arrancar el servidor en Unity y `/mcp` → reconectar.

## Estructura

- Escena principal: `Assets/Scenes/NatureScene.unity` (las otras dos son de ejemplo).
- `Assets/Scripts/` — scripts de juego · `Assets/Scripts/Editor/` — constructores (menús).
- `Assets/Pueblo/` — lo generado por los constructores (personajes, guanteletes).
- `Assets/Animated Hands/` — manos originales (Oculus), con Animator `Grip`/`Trigger`. **No modificar**: los guanteletes son variantes.

## Scripts

| Script | Qué hace |
|---|---|
| `SelectorDeModo` | Gafas o teclado+ratón (XR Interaction Simulator) |
| `ColisionSimulador` | Con el simulador, WASD mueve el rig con CharacterController (no atraviesa paredes) |
| `LuzParpadeante` | Parpadeo de fuego en luces |
| `AnimarManoConInput` | Pasa grip y gatillo del mando al Animator de la mano |
| `Editor/ConstructorPueblo` | Construye el pueblo |
| `Editor/ConstructorPersonajes` | Monta aldeanos Quaternius (traje + cabeza + pelo) |
| `Editor/ImportadorQuaternius` | Ajustes de importación de los FBX de Quaternius |
| `ArmaCuerpoACuerpo` | Arma de mano: agarre por el puño (hoja arriba), combo de 3 golpes con gatillo o A/X, golpe por barrido |
| `MaderaGolpeable` | Madera que al golpearla deja muescas, suelta astillas y tiembla (IGolpeable) |
| `Editor/ConstructorPotro` | Menú *VR Quest 3 > Crear potro de prácticas*: caballete de madera junto a la armería |
| `IGolpeable` | Interfaz para lo que reacciona a un golpe (`RecibirGolpe(Golpe)`) |
| `Editor/ConstructorArmas` | Menú *VR Quest 3 > Preparar armas*: puño, pivote, filo y agarre Toggle en los mandos |
| `RutinaAldeano` | El aldeano pasea por su zona (NavMeshAgent) y hace tareas animadas |
| `Editor/ConstructorRutinas` | Menú *VR Quest 3 > Dar vida a los aldeanos*: NavMesh, Animators por oficio, rutinas |

## Estado actual (revisado 2026-10-29)

**Hecho**
- Escena de naturaleza (Kenney) + pueblo medieval (Quaternius) con interiores y luces de fuego.
- XR Origin (Starter Assets) + XR Interaction Simulator, selector de modo y colisión en simulador.
- Aldeanos colocados con Animator, LOD y culling: Herrero, Cazador, Leñador, Aldeana, 2 Paseantes.
- Objetos agarrables (`XRGrabInteractable`): Hacha, Espada, Key, Lid, Potion.

**En curso (rama `guanteletes`, sin commitear)**
- Manos: los guanteletes se descartaron (feos) y se borraron (prefabs y `ConstructorGuanteletes`).
  Se usan las manos originales `Left/Right Hand Model` con `AnimarManoConInput` (Select Value / Activate Value)
  y material `Assets/Pueblo/Manos/Cuero Manos.mat` como override en la escena (el prefab original no se toca).
  Arranca en Play sin errores; **falta probar grip/gatillo con input real** (gafas o simulador).
- Rutinas de aldeanos (`ConstructorRutinas` + `RutinaAldeano`): ya ejecutado. NavMesh generado, 7 aldeanos
  en `Aldeanos` (Herrero, 2 Arqueros, Leñador, 3 aldeanos) que caminan en Play sin errores.
  Los de "Galeria de pruebas (Fase 1)" siguen en escena.
- **Ojo licencia**: usa animaciones de Mixamo (`Assets/ThirdParty/Mixamo/`), que NO son CC0 (regla del proyecto).

**Agarrar y armas**
- Agarre de un toque: Near-Far Interactor en modo `Toggle` (grip coge, grip otra vez suelta).
  La mano mantiene el puño cerrado mientras sujeta algo (`AnimarManoConInput`).
- Espada y Hacha con `ArmaCuerpoACuerpo` (probado en Play por código: agarre, orientación, golpe, salto):
  se cogen por el puño con la hoja a 80° hacia arriba; gatillo (T) o A/X (tecla 1) = siguiente golpe del combo:
  arriba→abajo, izquierda→derecha, abajo→arriba (se encadena si pulsas durante el golpe; >0,6 s reinicia).
  La Espada está girada con el puño sobresaliendo 10 cm del borde de la mesa.
- Potro de prácticas en (3, 0, -7.2) (dentro de Armory): viga + patas en A con CapsuleColliders, Rigidbody
  kinemático, NavMeshObstacle con carving (el herrero lo rodea) y `MaderaGolpeable`. Probado en Play:
  los 3 golpes del combo lo tocan, deja muescas y suelta astillas. Materiales en `Assets/Pueblo/Armeria/`.
- Las 3 figuras en pose de T apiladas sobre la Fogata (2 `Aldeana` + `Paseante_Mujer` en la raíz) están desactivadas.
  El "Filo" es un BoxCollider apagado que sirve de molde al barrido (Physics.OverlapBox), no gasta física.
  Mientras A ataca con la derecha, el JumpProvider se desactiva (A también salta).
- Alcance de agarre: cada arma tiene "Zona Agarre" (BoxCollider trigger, +8 cm por lado) metida a mano en la
  lista Colliders del XRGrabInteractable (el XRI descarta los triggers si la rellena solo). Esfera de agarre
  de la mano: 0,15 m y detecta triggers. Probado: coge a 20 cm, no a 40 cm.
- Visual de la esfera del dedo (Poke Point/Pinch_Pointer_LOD0) desactivado: no hay UI que tocar.
- Botones del simulador: G grip, T gatillo, 1 primario (A/X), 3 clic joystick (= Scale Toggle, no usar).
- Key, Lid, Potion: dentro de `Phase2 Interactables` (cofre), **desactivados** → no se pueden coger ahora.

**Falta**
- Probar armas en gafas y ajustar "Inclinación hoja" si en la Quest no se siente natural.
- Sonidos de tajo y de golpe en madera (los scripts ya tienen los huecos).
- Acciones con el gatillo sobre objetos (p. ej. beber poción, abrir con llave).
- Decidir qué hacer con Mixamo (sustituir por CC0 o aceptar la excepción).
