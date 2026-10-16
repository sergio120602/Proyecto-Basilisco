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
| Personalizar (nombre, portada, contraportada y filtros) | `E` o clic derecho sobre la caja |
| Cambiar de filtro | `Tab` / `Shift+Tab` o clic en la pestaña de arriba |
| Crear filtro | «+ Nuevo filtro» en la barra de arriba |
| Editar o eliminar filtro | Clic derecho sobre su pestaña |
| Quitar de la biblioteca | `Supr` (no borra nada del disco) |
| Pantalla completa | `F11` (`Esc` para salir) |

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
- [ ] **Paso 6** — Descarga de cajas y carátulas según los filtros (PS1, PS2, PS3…) de forma automática
- [ ] Tamaño de las cajas a elección del jugador
- [ ] Detectar la instalación de Steam, leer sus bibliotecas y lanzar sus juegos
- [x] **v0.0.6** — Importar directorios completos y escanearlos en busca de juegos

### 💡 Ideas para el futuro
- Información de cada juego (descripción, año, género…).
- Búsqueda, filtros y categorías (jugados recientemente, favoritos…).
- Soporte para mando.
- Soporte para otras plataformas (Epic Games, GOG, Xbox, etc.).
- Registro de tiempo de juego.

## 🌐 Carátulas desde internet

En Personalizar (`E`) puedes buscar y descargar la portada en **SteamGridDB** y la contraportada en **TheGamesDB**. Cada servicio necesita tu propia clave de API (gratuita), que Basilisco te pide la primera vez:

- **SteamGridDB**: entra en [steamgriddb.com](https://www.steamgriddb.com) con tu cuenta → Preferencias → API → genera la clave. Es inmediata.
- **TheGamesDB**: entra en [thegamesdb.net](https://thegamesdb.net) con tu cuenta y pulsa «API Access Request» al pie de la página. Tienen que aprobarla y tiene un límite de consultas al mes.

Las claves se guardan en `basilisco-data/settings.json`, junto al `.exe`. No compartas esa carpeta si no quieres compartir tus claves.

## 🛠️ Tecnologías

- **C# / .NET 10** con **WPF** para la interfaz.
- Carrusel 3D hecho con `Viewport3D` de WPF: cada juego es una caja 3D con sus seis caras y su reflejo.
- Publicado como `.exe` autocontenido de un solo archivo (`PublishSingleFile`).

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
