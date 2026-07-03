# Informe de audio — 360inmersive (Concert360)

Fecha: 2026-06-17
Dispositivo de prueba: PICO (serial PA8E50MGH1040871D), paquete `com.SalsaSound.Concert360_1`
Unity: 2021.3.13f1

---

## 1. Síntoma
El vídeo 360 del concierto se veía, pero **no se oía nada** en las gafas.

## 2. CAUSA RAÍZ (lo que era EXACTAMENTE el fallo)

Dos hechos, confirmados leyendo el dispositivo por `adb`:

1. **El vídeo NO tiene pista de audio.** En el log del dispositivo:
   `[Audio] Video PREPARED. audioTrackCount=0`
   → El sonido del concierto **no está dentro del `.mp4`**. Tiene que venir de los *stems* (pistas de instrumentos en `.wav`).

2. **La escena apuntaba al contenedor de audio equivocado.**
   - `PlatformManager.audioSourceObject` (y `ControllerZoom.audioSourceObject`, y `VideoPlaybackToggle.audioSourceRoot`) apuntaban a **"360 Pisa Night Sources"** → 18 ficheros: `VOX ERIC.wav`, `OH L.wav`, `PNO HI.wav`, …
   - Pero en las gafas, en `/storage/emulated/0/Android/data/com.SalsaSound.Concert360_1/files/`, solo estaban los **6 stems "Day"**: `DR - stem.wav`, `GT - stem.wav`, `KEY - stem.wav`, `SAX - stem.wav`, `TBONE - stem.wav`, `TPT - stem.wav` (presentes desde el 10‑jun).
   - Resultado: el código intentaba cargar 18 ficheros que **no existían** en el dispositivo → todas las cargas fallaban → **silencio**.

**En una frase:** el audio estaba cableado a "Night Sources" (18 ficheros ausentes) cuando en el dispositivo solo estaban los 6 de "Day Sources". Más un vídeo sin pista de audio. **Era un problema de configuración, no de código.**

### Causa secundaria (sí dependía de código)
Aunque el cableado hubiera sido correcto, los `AudioSource` salían por:
- el **mixer "Meta XR Audio"** (plugin de Oculus, **no inicializado en Pico**), y
- el spatializer del proyecto = **Steam Audio**, **sin `SteamAudioManager` en escena**.
Ambos rotos → habrían silenciado igual. Por eso el bypass en código (abajo) **sí hacía falta**.

## 3. SOLUCIÓN APLICADA

### Configuración (lo principal)
Re-cableadas las 3 referencias de "Night Sources" (fileID 983008499) → **"Day Sources"** (fileID 1353352044):
- `PlatformManager.audioSourceObject`
- `ControllerZoom.audioSourceObject`
- `VideoPlaybackToggle.audioSourceRoot`

### Código (`Assets/Scripts/Platform Manager.cs`)
En la carga de cada clip:
- `outputAudioMixerGroup = null` → salta el mixer Meta (roto). **Necesario.**
- `spatialize = false` → salta el spatializer Steam Audio (roto). **Necesario.**
- `spatialBlend = 1` → audio 3D por **paneo** nativo de Unity (dirección izq/dcha + distancia). **Funciona.**
- Ruta de fichero con `new System.Uri(fsPath).AbsoluteUri` → escapa los espacios de los nombres (`DR - stem.wav`). **Recomendado.**

**Resultado: AUDIO FUNCIONANDO** (confirmado por el usuario: "lo oigo"), en 3D por paneo.

### Diagnóstico añadido (se puede quitar)
- `LogScreen(...)` vuelca el estado de audio al panel "Debug Log" en pantalla.
- `Assets/Scripts/AudioTestTone.cs` → tono de prueba autocontenido (confirmó que la salida del dispositivo funciona).

### Código innecesario (a revertir)
- `videoPlayer.audioOutputMode = Direct` + ajustes de pista del vídeo → **código muerto** (el vídeo no tiene audio). Pendiente de revertir.

## 4. Qué hicimos de más (lecciones)
- Probamos mucho en el **Editor** (`isTablet=True`), donde el audio **nunca** podía sonar porque los ficheros están en las gafas, no en el PC. Eso despistó.
- Asumimos causas (Steam Audio, Meta mixer, espacios) sin **leer primero el dispositivo**. En cuanto hicimos `adb shell ls` + `adb logcat`, la causa real (ficheros Day vs cableado Night, vídeo sin audio) apareció en minutos.
- **Lección:** ante "no suena en el dispositivo", lo primero es `adb logcat` + listar los ficheros reales en `persistentDataPath`.

## 5. Pendiente
- **Audio espacial real (HRTF) de Pico** → ver sección de setup (`PicoSpatialAudioSetup`). El proyecto ya trae el SDK `PXR_Audio_Spatializer_*`.
- (Opcional) Mezcla completa de 18 instrumentos "Night": copiar esos 18 `.wav` al dispositivo y re-apuntar las referencias a "Night Sources".
- Limpiar código de diagnóstico y el `audioOutputMode = Direct`.

## 6. Referencia útil (adb de Unity)
```
"C:\Program Files\Unity\Hub\Editor\2021.3.13f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe"
adb logcat -s Unity
adb shell ls -la /storage/emulated/0/Android/data/com.SalsaSound.Concert360_1/files/
```
