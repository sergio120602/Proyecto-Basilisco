# 🐍 Proyecto Basilisco

**Proyecto Basilisco** es un launcher de videojuegos para PC que reúne en un solo lugar **todos tus juegos**, estén donde estén: en tu biblioteca de Steam o sueltos en cualquier carpeta de tu disco.

Es **100 % portable**: un único `.exe`, sin instalación de ningún tipo. Lo copias donde quieras (un USB, otro disco…) y funciona.

> ⚠️ Proyecto en fase inicial. Este README y la hoja de ruta irán cambiando con el tiempo.

---

## 📥 Descargar

Descarga el último `Basilisco-vX.Y.Z-win-x64.exe` desde **[Releases](https://github.com/sergio120602/Proyecto-Basilisco/releases)** y ábrelo. No necesita instalar nada (ni siquiera .NET).

La biblioteca, las portadas y los filtros se guardan en una carpeta `basilisco-data` junto al `.exe`, así que si mueves el `.exe`, mueve también esa carpeta.

## 🎯 Objetivo

Abrir Basilisco y ver de un vistazo toda tu colección de videojuegos en un carrusel de portadas, sin tener que saltar entre launchers ni buscar ejecutables por el explorador de archivos.

## 🎮 Controles

| Acción | Teclado / ratón |
| --- | --- |
| Navegar por el carrusel | `←` `→`, rueda del ratón o clic en una caja lateral |
| Jugar | `Enter` o clic en la caja central |
| Girar la caja (ver grosor y contraportada) | Arrastrar la caja central con el ratón |
| Dar la vuelta a la caja | `R` |
| Importar juego | `I`, botón «Importar juego» o arrastrar archivos a la ventana |
| Importar una carpeta entera | `C`, botón «Importar carpeta» o arrastrar la carpeta a la ventana |
| Personalizar (nombre, portada, contraportada, lomo, consola y filtros) | `E` o clic derecho sobre la caja |
| Cambiar el formato (cuadrada / rectangular) de uno, varios o todos | `F` o botón «Formato» |
| Encuadrar portada, contraportada o lomo | «Ajustar…» en Personalizar: rueda para ampliar, arrastrar para mover |
| Cambiar de filtro o de consola | `Tab` / `Shift+Tab` o clic en la pestaña de arriba |
| Crear filtro | «+ Nuevo filtro» en la barra de arriba |
| Editar o eliminar filtro | Clic derecho sobre su pestaña |
| Quitar de la biblioteca | `Supr` (no borra nada del disco) |
| Pantalla completa | `F11` (`Esc` para salir) |

### 🕹️ Con mando

Funcionan los mandos de **Xbox One / Series / 360**, **PS5 (DualSense)** y **Nintendo Switch Pro** (también DualShock 4 y Joy-Con), por cable o Bluetooth.

| Acción | Xbox | PlayStation | Switch Pro |
| --- | --- | --- | --- |
| Navegar | Cruceta / stick izquierdo | Cruceta / stick izquierdo | Cruceta / stick izquierdo |
| Jugar / aceptar | A | ✕ | A |
| Volver / cancelar | B | ○ | B |
| Dar la vuelta a la caja | X | □ | Y |
| Personalizar | Y | △ | X |
| Girar la caja | Stick derecho | Stick derecho | Stick derecho |
| Cambiar de filtro | LB / RB | L1 / R1 | L / R |
| Saltar de 5 en 5 | LT / RT | L2 / R2 | ZL / ZR |
| Formato | View | Create | − |
| Pantalla completa | Menu | Options | + |

Importar juegos y escribir nombres se sigue haciendo con teclado y ratón.

## ✨ Funcionalidades previstas

### 📚 Biblioteca de Steam
- Detección automática de la instalación de Steam.
- Lectura de las bibliotecas de Steam (incluidas las que estén en otros discos).
- Listado de todos los juegos instalados.
- Lanzamiento de los juegos directamente desde Basilisco.

### 📁 Importación de carpetas
- Importar juegos individuales desde cualquier carpeta. ✅ *(v0.0.1)*
- **Importar directorios completos**: seleccionar una carpeta y que Basilisco escanee todo su contenido para detectar los juegos que contiene. ✅ *(v0.0.6)*
- Detección de ejecutables de juego (descartando instaladores, desinstaladores, herramientas, etc.). ✅ *(v0.0.6)*
- Volver a escanear una carpeta para detectar juegos nuevos (los ya importados se marcan y no se duplican). ✅ *(v0.0.6)*

### 🎮 Biblioteca unificada
- Vista única con todos los juegos, vengan de donde vengan.
- Indicador del origen de cada juego (Steam, carpeta local…).
- Lanzar cualquier juego con un clic. ✅ *(v0.0.1)*

## 🗺️ Hoja de ruta

> La hoja de ruta se irá actualizando a medida que avance el proyecto.

- [x] Crear el repositorio y el README inicial
- [x] Elegir tecnología → C# + WPF (.NET 10), compilado como `.exe` portable
- [x] **Paso 1 · v0.0.1** — El `.exe` abre, carrusel 3D con cajas negras, importar y lanzar cualquier juego o aplicación
- [x] **Paso 2 · v0.0.2** — Menú de personalización: cambiar el nombre y la portada de cada juego
- [x] **Paso 3 · v0.0.3** — Filtros personalizados (con nombre e imagen) para agrupar los juegos
- [x] **Paso 4 · v0.0.5** — Personalización con imagen delantera y trasera de la caja
- [x] **Paso 5 · v0.0.5** — Girar las cajas como modelos 3D completos, con su grosor
- [x] **v0.0.11** — Descargar portadas de SteamGridDB y contraportadas de TheGamesDB desde Personalizar
- [x] **v0.1.0** — Consolas (PS1, PS2, PS3, PSP, Xbox, Xbox 360, GBA, Wii, Wii U, DS y PC): menú al importar y caja con el borde de cada consola
- [x] **v0.1.2** — Editor de encuadre para la contraportada y lomo personalizado (imagen propia o banner de TheGamesDB)
- [x] **v0.1.3** — Formatos de caja cuadrada (1024 × 1024) y rectangular (600 × 800), cada uno con sus imágenes
- [x] **v0.1.4** — Compatibilidad con mandos de Xbox One, PS5 y Switch Pro
- [ ] **Paso 6** — Descarga de cajas y carátulas según los filtros (PS1, PS2, PS3…) de forma automática
- [ ] Tamaño de las cajas a elección del jugador
- [ ] Detectar la instalación de Steam, leer sus bibliotecas y lanzar sus juegos
- [x] **v0.0.6** — Importar directorios completos y escanearlos en busca de juegos

### 💡 Ideas para el futuro
- Información de cada juego (descripción, año, género…).
- Búsqueda, filtros y categorías (jugados recientemente, favoritos…).
- Soporte para otras plataformas (Epic Games, GOG, Xbox, etc.).
- Registro de tiempo de juego.

## 🎮 Consolas

Cada vez que importas juegos, Basilisco te pregunta de qué consola son: **PS1, PS2, PS3, PSP, Xbox, Xbox 360, Game Boy, Wii, Wii U, Nintendo DS o PC**. La caja de cada juego lleva la franja y el color de plástico de su consola, y las consolas con juegos aparecen como pestañas arriba. Puedes mover un juego de consola en Personalizar (`E`).

Para usar las franjas oficiales, pon una imagen de cada franja en `basilisco-data/borders/` junto al `.exe`, con el nombre de su consola: `ps1.png`, `ps2.png`, `ps3.png`, `psp.png`, `gba.png` (Game Boy), `nds.png`, `wii.png`, `wiiu.png`, `xbox.png`, `x360.png` y `pc.png`. Si falta alguna, Basilisco dibuja la suya. Las franjas oficiales no se incluyen en este repositorio porque llevan logotipos de sus marcas.

## 📦 Formatos de caja

Cada juego puede usar una caja **cuadrada** (carátula de 1024 × 1024, franja de la consola a la izquierda) o **rectangular** (carátula de 600 × 800, franja arriba). Cada formato guarda sus propias imágenes, así que puedes cambiar de uno a otro sin perder nada. Las franjas de arriba se pueden sustituir con `basilisco-data/borders/<consola>-top.png`.

## 🌐 Carátulas desde internet

En Personalizar (`E`) puedes buscar y descargar la portada en **SteamGridDB**, y la contraportada y el lomo en **TheGamesDB** (para el lomo se usan sus *banners*, girados). Cada servicio necesita tu propia clave de API (gratuita), que Basilisco te pide la primera vez:

- **SteamGridDB**: entra en [steamgriddb.com](https://www.steamgriddb.com) con tu cuenta → Preferencias → API → genera la clave. Es inmediata.
- **TheGamesDB**: entra en [thegamesdb.net](https://thegamesdb.net) con tu cuenta y pulsa «API Access Request» al pie de la página. Tienen que aprobarla y tiene un límite de consultas al mes.

Las claves se guardan en `basilisco-data/settings.json`, junto al `.exe`. No compartas esa carpeta si no quieres compartir tus claves.

## 🛠️ Tecnologías

- **C# / .NET 10** con **WPF** para la interfaz.
- Carrusel 3D hecho con `Viewport3D` de WPF: cada juego es una caja 3D con sus seis caras y su reflejo.
- Publicado como `.exe` autocontenido de un solo archivo (`PublishSingleFile`).
- Mandos con [SDL3](https://www.libsdl.org/) a través de [SDL3-CS](https://github.com/ppy/SDL3-CS).

### Compilar desde el código

Necesitas el [SDK de .NET 10](https://dotnet.microsoft.com/download).

```powershell
dotnet publish src/Basilisco/Basilisco.csproj -c Release -o dist
```

El `.exe` portable queda en `dist/Basilisco.exe`.

### Publicar una nueva versión

1. Sube `<Version>` en [`src/Basilisco/Basilisco.csproj`](src/Basilisco/Basilisco.csproj).
2. Añade la sección de la versión en [`CHANGELOG.md`](CHANGELOG.md) y haz commit.
3. Ejecuta `./scripts/release.ps1`: compila, sube el código, crea la etiqueta y publica la Release con el `.exe`.

## 📄 Licencia

Por decidir.
