# Historial de cambios

Todas las versiones de Basilisco. Cada versión se publica en
[Releases](https://github.com/sergio120602/Proyecto-Basilisco/releases) con su `.exe` portable.

## [0.3.1] - 2026-10-17

- **Botón «Apariencia»** en la barra de arriba (o tecla `A`): fondo estático (imagen) o dinámico (vídeo) y color de las letras **para todos los juegos**, con vista previa.
- Si un juego tiene fondo o color propios (Personalizar), se usan los suyos; si no, los generales. «Quitar las de cada juego» deja que todos usen la apariencia general.

## [0.3.0] - 2026-10-17

Apariencia de cada juego y tiempo de juego.

- **Fondo de cada juego** (Personalizar → Fondo): **estático** con una imagen o **dinámico** con un vídeo (MP4, WMV, AVI, MOV, M4V o MKV) en bucle y sin sonido. Se ve detrás del carrusel cuando el juego está seleccionado y cambia con un fundido al pasar de un juego a otro. El vídeo se pausa cuando Basilisco no está en primer plano.
- **Color de las letras** (Personalizar → Color de las letras): muestras rápidas (blanco, negro, gris, amarillo, celeste, rosa, verde) o cualquier color en `#RRGGBB`. Cambia el título, la barra de arriba, la de abajo y el tiempo de juego; con letras claras, el halo del título pasa a ser oscuro para que se lean.
- **Tiempo de juego** debajo de la caja seleccionada. Se cuenta solo desde que lanzas un juego hasta que lo cierras o vuelves a Basilisco (los primeros 20 segundos no cuentan), se puede editar en Personalizar (horas y minutos, o «A cero») y al importar desde Steam se traen tus horas.
- La vista previa de Personalizar enseña el fondo elegido detrás de la caja.
- Los fondos se guardan en `basilisco-data/backgrounds/`, junto al .exe.

## [0.2.3] - 2026-10-17

- **Cajas más grandes**: el carrusel se ve con un 20 % más de zoom por defecto.
- **Barra de zoom** abajo a la derecha, del 70 % al 180 %. También con `Ctrl + rueda` o con las teclas `+` y `−`. El título y el suelo se recolocan solos, y el zoom elegido se recuerda al cerrar.

## [0.2.2] - 2026-10-17

El orden personalizado pasa a ser una estantería.

- **Estantería**: al elegir «Orden → Personalizado», las cajas giran y se colocan una junto a otra enseñando el lomo, como en una estantería de verdad (cada una con su grosor real).
- **↑** coge el juego del centro (sube), **← →** lo lleva entre los demás (que se apartan deslizándose) y **↓** lo suelta en ese sitio. **Esc** termina y vuelve al carrusel con el nuevo orden guardado.
- Sin coger nada, **← →**, la rueda o un clic en un lomo recorren la estantería.
- Con mando: cruceta o stick arriba y abajo para coger y soltar, izquierda y derecha para mover, A para coger o soltar y B para terminar.
- La barra de abajo enseña los controles de la estantería mientras está abierta.
- Se quita el «Ctrl + ← →» de la versión anterior.

## [0.2.1] - 2026-10-17

Barra de arriba agrupada y orden de los juegos.

- **Grupos en la barra de arriba**: «Todos los juegos», **Consolas ▾** (las consolas con juegos) y **Filtros ▾** (tus filtros, «+ Nuevo filtro…» y «Editar filtro»). Cada grupo dice lo que tienes elegido, por ejemplo «PS2 (3)», o enseña el logo del filtro. Clic derecho en Filtros edita el filtro activo.
- **Menú «Orden»** para todos los juegos:
  - **Personalizado**: colócalos a tu gusto moviendo el juego del centro con `Ctrl + ← →`.
  - **Alfabético** de la A a la Z y de la Z a la A.
  - **Fecha de lanzamiento**: más nuevos o más antiguos primero.
  - **Fecha de adición**: más recientes o más antiguos primero.
- **Fechas de lanzamiento**: al ordenar por ellas, Basilisco busca en LaunchBox (si está descargada) la fecha de los juegos que no la tienen, solo cuando el nombre coincide de verdad. También se puede escribir a mano en Personalizar (`E`), con día o solo el año. Los juegos sin fecha van al final.
- El índice de LaunchBox guarda ahora la fecha completa de lanzamiento (antes solo el año); «Actualizar base de datos» lo pone al día.
- El orden elegido se recuerda al cerrar Basilisco.

## [0.2.0] - 2026-10-17

Conexión con Steam.

- **Importar desde Steam** (botón «Steam» arriba o tecla `S`), con tres opciones que se pueden combinar:
  - **Juegos instalados en este PC**: se leen de tu instalación de Steam (todas sus bibliotecas, en cualquier disco). Sin clave ni internet. Los instalados desde la biblioteca de un familiar se reconocen solos.
  - **Toda tu biblioteca**, también los juegos no instalados, con la clave de la Web API de Steam (gratis y permanente; el perfil debe tener los «Detalles de juegos» públicos) o con el token de sesión, que funciona aunque el perfil sea privado.
  - **Steam Familiar**: los juegos que comparte contigo tu familia de Steam, con quién los comparte. Necesita el token de sesión, que se consigue con un botón que abre su página de Steam (con la sesión iniciada en el navegador) y dura unas 24 horas.
- Lista para revisar antes de importar: tuyo o de la familia (y de quién), instalado o no, y horas jugadas. Los que ya tenías (importados desde Steam o como ejecutable de su carpeta de `steamapps`) salen marcados y no se duplican, así que puedes volver a sincronizar cuando quieras.
- Los juegos de la familia pueden ir a un filtro «Steam Familiar».
- Los juegos de Steam se lanzan a través de Steam (`steam://rungameid/…`); si no están instalados, Steam ofrece instalarlos. Son de PC sin tener que elegir la consola.
- Las etiquetas del juego indican «Steam · instalado / no instalado» y «Steam Familiar · de …».
- La clave de la Web API se guarda en `basilisco-data/settings.json`; **el token de sesión no se guarda nunca** en disco (da acceso a la cuenta mientras dura).

## [0.1.8] - 2026-10-17

- **Cajas rectangulares sin franja de consola**: la carátula ocupa toda la portada. Están pensadas para las portadas reales de LaunchBox, que ya traen impresa la franja oficial de su consola.
- Imágenes por defecto en el formato rectangular: la portada se busca primero en LaunchBox (si su base de datos ya está descargada) y después en SteamGridDB.
- Las cajas cuadradas no cambian: siguen con su franja a la izquierda.
- Ya no se usan los archivos `borders/<consola>-top.png`.

## [0.1.7] - 2026-10-17

Nuevas consolas: Nintendo 64, GameCube y Game Boy Advance.

- **Nintendo 64**, **GameCube** y **Game Boy Advance** en el menú de consolas, en Personalizar y en las pestañas de arriba, con su franja dibujada (vertical y horizontal), su color de plástico y su plataforma en LaunchBox.
- Medidas de sus cajas rectangulares (aproximadas): Nintendo 64, caja de cartón apaisada de 12,7 × 18,0 × 3,4 cm · GameCube, estuche de mini-DVD de 18,7 × 13,5 × 1,4 cm · Game Boy Advance, caja de cartón de 12,5 × 12,5 × 2,0 cm.
- La **Game Boy** y la **Game Boy Advance** pasan a ser consolas distintas. Los juegos que tenías como Game Boy siguen siéndolo (se migran solos, igual que su franja orders/gba.png, que pasa a llamarse gb.png).
- Las franjas de arriba reducen el nombre de la consola si no cabe en cajas estrechas.

## [0.1.6] - 2026-10-17

Las cajas rectangulares tienen las medidas reales de cada consola.

- **Medidas reales (alto × ancho × grosor)** de la caja de cada consola en el formato rectangular:
  PS1 jewel case 12,5 × 14,2 × 1,0 cm · PS2, Xbox, Xbox 360, Wii y PC caja de DVD 19,0 × 13,5 × 1,4 cm · PS3 Blu-ray 17,0 × 13,5 × 1,4 cm ·
  PSP UMD 18,0 × 10,5 × 1,5 cm · Game Boy cartón 12,8 × 12,8 × 2,5 cm · Wii U Blu-ray 17,0 × 13,5 × 1,5 cm · Nintendo DS (Europa) 13,6 × 12,4 × 1,9 cm.
- Están a escala entre sí, como en una estantería: una caja de PS1 o de DS se ve más baja que una de PS2, y la de Game Boy, más gruesa.
- El lomo, la contraportada, el editor de encuadre y el zoom de «Llenar» usan las medidas de cada caja.
- El título del juego se coloca justo encima de la caja seleccionada, sea cual sea su altura.
- Las cajas cuadradas no cambian.

## [0.1.5] - 2026-10-17

LaunchBox como segunda opción para las imágenes.

- **Buscar en LaunchBox** en Personalizar, para la **portada**, la **contraportada** y el **lomo**: fotos reales de las cajas de casi 200 000 juegos de la base de datos de LaunchBox, de varias regiones (España, Europa, Norteamérica, Japón…).
- No necesita clave. La primera vez descarga su base de datos (unos 100 MB) y la guarda resumida en `basilisco-data/launchbox/`, junto al .exe; después las búsquedas son locales e instantáneas, también sin internet (solo hace falta para bajar la imagen elegida).
- Los resultados ponen primero los juegos de la consola del tuyo, y las imágenes de España y Europa antes que las de otras regiones. Cada miniatura indica su región, y las rehechas por la comunidad salen al final.
- Busca también por los nombres alternativos de cada juego y sin tener en cuenta tildes ni signos.
- Botón «Actualizar base de datos» en el buscador de LaunchBox para descargar la versión más reciente.
- Imágenes por defecto: si SteamGridDB o TheGamesDB no encuentran nada y la base de datos de LaunchBox ya está descargada, se usa como respaldo.

## [0.1.4] - 2026-10-17

Compatibilidad con mandos.

- **Mandos de Xbox One / Series / 360, PS5 (DualSense) y Nintendo Switch Pro**, por cable o Bluetooth, gracias a SDL3 (incluida en el .exe: sigue sin instalarse nada). También funcionan el DualShock 4 y los Joy-Con.
- **Controles**: cruceta o stick izquierdo para navegar (mantenido, avanza solo), aceptar para jugar, X/□/Y para dar la vuelta a la caja, Y/△/X para personalizar, stick derecho para girar la caja y ver su grosor, LB/RB (L1/R1, L/R) para cambiar de filtro, LT/RT (L2/R2, ZL/ZR) para saltar de 5 en 5, View/Create/− para el formato y Menu/Options/+ para pantalla completa.
- En los mandos de Nintendo se acepta con A (derecha) y se vuelve con B (abajo), como en la consola.
- En los menús, el botón de aceptar guarda (como `Enter`) y el de volver cancela (como `Esc`).
- La barra de abajo enseña los botones del mando que estés usando, con sus nombres o símbolos (✕ ○ □ △ en PlayStation), y vuelve a las teclas al usar el teclado.
- El mando solo actúa con Basilisco en primer plano: mientras juegas no mueve nada por detrás.
- Aviso al conectar o desconectar un mando.

## [0.1.3] - 2026-10-17

Dos formatos de caja: cuadrada y rectangular.

- **Formato por juego**: cada caja puede ser **cuadrada** (carátula de 1024 × 1024, franja de la consola a la izquierda) o **rectangular** (carátula de 600 × 800, franja de la consola arriba, como las cajas de PS3, Xbox 360 o Wii).
- **Cada formato guarda sus imágenes**: portada, contraportada, lomo y encuadres van por separado; al volver al otro formato, el juego recupera las que tenía.
- **Cambiar el formato de uno, varios o todos**: botón «Formato» arriba (o tecla `F`), con la lista de juegos para marcar, «Marcar todos» y la opción de poner imágenes por defecto. También desde Personalizar (`E`), sección «Formato de la caja».
- **Imágenes por defecto**: al pasar a un formato que aún no tiene imágenes, Basilisco intenta poner la portada de SteamGridDB (con las medidas del formato) y la contraportada de TheGamesDB, si tienes sus claves y las páginas responden. Si no, la caja se queda vacía para que la elijas tú.
- **Franjas de arriba**: la de PS3 es la oficial de tu ejemplo; las demás las dibuja Basilisco. Se pueden sustituir con `basilisco-data/borders/<consola>-top.png`.
- «Ajustar…» también en la **portada**, con el mismo editor de encuadre que la contraportada y el lomo.
- El carrusel junta más las cajas cuando la del centro es rectangular.
- Las bibliotecas de versiones anteriores se migran solas: sus imágenes pasan a la caja cuadrada.

## [0.1.2] - 2026-10-17

- **Editor de encuadre** («Ajustar…» en Personalizar → Contraportada): amplía la contraportada con la rueda del ratón o la barra de zoom (del 50 % al 1200 %) y muévela arrastrando para que cuadre en la caja. «Entera» la deja completa sin recortar y «Llenar» hace que ocupe toda la cara. Lo que queda fuera se recorta.
- **Lomo personalizado**: nueva sección «Lomo» en Personalizar para poner una imagen en el lomo de la caja (el lado con el nombre que se ve en las cajas apiladas del carrusel), elegida a mano o arrastrada a la ventana. Las imágenes horizontales se giran para leerse de arriba abajo, y se encuadran con el mismo editor.
- **Lomos desde TheGamesDB**: «Buscar en TheGamesDB» en la sección Lomo busca los *banners* del juego (TheGamesDB no tiene lomos como tal) y los usa girados.
- El lomo con imagen se pinta al doble de resolución para que se lea bien.
- La vista previa 3D tiene un botón «Lomo» para girar la caja y verlo de frente.
- El lado derecho de Personalizar se adapta al alto de la ventana y se desplaza si no cabe.

## [0.1.1] - 2026-10-17

- **Franjas oficiales de cada consola**: si en `basilisco-data/borders/` hay una imagen de la franja (`ps1.png`, `ps2.png`, `ps3.png`, `psp.png`, `gba.png`, `nds.png`, `wii.png`, `wiiu.png`, `xbox.png`, `x360.png`, `pc.png`), Basilisco la usa en lugar de la dibujada. Su ancho sale de su propia proporción para que no se deforme.
- Si falta alguna, se sigue usando la franja dibujada por Basilisco.
- «Game Boy Advance» pasa a llamarse «Game Boy»; los juegos que ya la tenían asignada no cambian.

## [0.1.0] - 2026-10-17

Consolas: cada juego lleva la caja de su consola.

- **Consolas disponibles**: PS1, PS2, PS3, PSP, Xbox, Xbox 360, Game Boy Advance, Wii, Wii U, Nintendo DS y PC.
- **Menú «¿De qué consola son?»** cada vez que importas uno o varios juegos (archivos sueltos o una carpeta entera): botones para asignar la misma consola a todos de golpe y un desplegable para cada juego. «Ahora no» los deja sin consola.
- **Borde de cada consola**: la franja de la izquierda de la portada cambia según la consola (PS1 negra con «PlayStation», PS2 con «PlayStation 2», PS3 con «PLAYSTATION 3», Xbox 360 con el degradado verde y la esfera, Wii blanca, Wii U azul…). Son recreaciones dibujadas por Basilisco, no los logotipos oficiales.
- **El plástico también cambia**: los cantos, el lomo y la contraportada sin imagen toman el color de la caja de la consola (verde en Xbox 360, blanco en Wii, azul en Wii U…).
- **Pestañas de consola** arriba, junto a tus filtros: aparecen las consolas que tienen algún juego. `Tab` también pasa por ellas.
- **Mover un juego de consola** en Personalizar (`E`), sección «Consola»; la vista previa 3D cambia de borde al momento.
- Lo que importas con una consola seleccionada arriba entra directamente en ella.
- La franja de la izquierda es más ancha, como en las cajas reales; la zona de la carátula sigue siendo cuadrada para las imágenes de 1024 × 1024.

## [0.0.11] - 2026-10-17

Descarga de carátulas desde internet.

- **Buscar en SteamGridDB** (en Personalizar → Portada): busca el juego en steamgriddb.com, enseña sus carátulas (primero las cuadradas de 1024 × 1024 y 512 × 512, que llenan la caja) y descarga la que elijas.
- **Buscar en TheGamesDB** (en Personalizar → Contraportada): busca el juego en thegamesdb.net y enseña las fotos de la parte de atrás de sus cajas, con la plataforma y el año de cada resultado.
- La primera vez se pide la clave de la API de cada servicio, con el botón «Conseguir clave» que abre su página. Se puede cambiar cuando quieras con «Cambiar clave».
- Las claves se guardan en `basilisco-data/settings.json`, junto al .exe, para seguir siendo portable.
- La imagen descargada se comporta igual que una elegida a mano: se ve al momento en la vista previa 3D y solo se guarda al pulsar «Guardar».
- Mensajes claros si la clave no es válida, se agota el límite de consultas o no hay conexión.

## [0.0.10] - 2026-10-17

- **Vuelve la franja de la izquierda** típica de las cajas de PlayStation, también en las cajas con carátula.
- La caja es ahora un poco más ancha, justo lo que mide la franja: a su derecha queda una zona cuadrada en la que una carátula de 1024 × 1024 sigue llenándose entera, sin recortarla ni deformarla.
- Ajustada la separación del carrusel al nuevo ancho, con margen para que no reaparezca la raya negra al deslizar.

## [0.0.9] - 2026-10-17

- **Carátulas cuadradas de 1024 × 1024**: las cajas pasan a ser cuadradas y sus texturas de 1024 × 1024, así que una carátula de ese tamaño llena la cara entera y se ve a resolución completa, sin franjas negras.
- Con carátula, la imagen ocupa toda la cara: la franja de lomo pintada ya no hace falta porque la caja tiene su lomo de verdad en 3D (las cajas sin carátula la conservan).
- Las imágenes con otras proporciones se siguen mostrando enteras, sin deformar ni recortar.
- Ajustada la separación del carrusel a las cajas cuadradas, manteniendo el margen para que no reaparezca la raya negra al deslizar.

## [0.0.8] - 2026-10-17

- Arreglado: al pasar de un juego a otro se veía durante unos instantes una raya negra cruzando la portada de la caja de al lado. Era el lomo de la caja de detrás, que atravesaba la portada de la de delante mientras cada una giraba con un ángulo distinto. Ahora las cajas apiladas tienen más separación en profundidad y nunca se tocan durante la animación.

## [0.0.7] - 2026-10-17

Los juegos que cambias de carpeta ya no se pierden ni se duplican.

- **Reparación automática al abrir Basilisco**: si un juego ya no está donde estaba porque has movido su carpeta (por ejemplo, a una subcarpeta `ports`), lo busca en las carpetas cercanas y actualiza su ubicación sin perder portada, contraportada, filtros ni partidas.
- **Duplicados**: si después de moverlo lo habías vuelto a importar, las dos entradas se juntan en una (se suman las partidas, se conservan las portadas y los filtros de ambas, y se queda el nombre que pusiste a mano).
- **Importar reconoce los juegos movidos**: al importar un archivo o una carpeta, los juegos que ya tenías en otra carpeta se marcan como «Lo tenías en otra carpeta» y se actualiza su ubicación en lugar de duplicarlos.
- **Al jugar un juego que no se encuentra**, Basilisco intenta localizarlo; si no puede, te ofrece buscarlo tú.
- **Cambiar ubicación** en Personalizar (`E`), que además avisa en rojo si el archivo no se encuentra.
- Nombres más limpios al importar carpetas: se quitan la versión y la plataforma del final (`ico-pc-v0.7.2` → «Ico-Pc», `Dusk-v1.0.0-win32-x86_64` → «Dusk»).

## [0.0.6] - 2026-10-17

Importación de carpetas enteras.

- **Importar carpeta**: con el botón «+ Importar carpeta», la tecla `C` o arrastrando una carpeta a la ventana. Basilisco la recorre entera y encuentra todos los juegos que tenga, aunque sean decenas.
- **Detección de juegos**: cada subcarpeta con un juego cuenta como un juego, con el nombre de la carpeta. Se elige su ejecutable principal (el que se llama como la carpeta, el de 64 bits, el menos escondido…), aunque esté en subcarpetas como `bin\x64`. Las colecciones dentro de colecciones (por ejemplo `Juegos\Emuladores\…`) también se detectan.
- **Se descartan** instaladores, desinstaladores, redistribuibles (`_CommonRedist`, DirectX, vcredist…), *crash reporters*, actualizadores, antitrampas y herramientas.
- Los accesos directos (`.lnk`, `.url`) sueltos en la carpeta cuentan cada uno como un juego.
- **Revisión antes de importar**: lista con todo lo encontrado para marcar o desmarcar, indicando qué ejecutable se ha elegido y cuáles ya estaban en tu biblioteca.
- **Agrupar en un filtro**: opción para meter todos los juegos importados en un filtro nuevo con el nombre de la carpeta.
- El escaneo va en segundo plano, enseña su progreso y se puede cancelar.

## [0.0.5] - 2026-10-17

Las cajas pasan a ser modelos 3D completos.

- **Cajas 3D de verdad**: cada juego es una caja con portada, contraportada, lomo y cantos, con el grosor de una caja de CD de PS1. Las cajas laterales del carrusel enseñan su lomo con el nombre del juego en vertical, y el reflejo del suelo es la caja entera en espejo.
- **Girar las cajas**: arrastra con el ratón la caja seleccionada para girarla en cualquier dirección y ver su grosor, o pulsa `R` para darle la vuelta y ver la contraportada. Al pasar a otro juego, la caja vuelve a ponerse de frente sola.
- **Contraportada personalizada**: en Personalizar (`E`) hay una nueva sección «Contraportada» para elegir o quitar la imagen de atrás. Se ve entera, sin deformarla ni recortarla.
- **Vista previa en 3D** en Personalizar: la misma caja del carrusel, que puedes girar arrastrando o con «Ver portada» / «Ver contraportada». Si sueltas una imagen en la ventana, va a la cara que estés viendo.
- Luz con volumen: cada cara de la caja tiene su tono, para que se note la forma.
- Un clic sobre la caja seleccionada sigue lanzando el juego; solo gira si arrastras.

## [0.0.4] - 2026-10-16

- **Pestañas de filtro con logo**: si un filtro tiene imagen, en la barra de arriba se ve solo el logo, más grande (hasta 40 px de alto), entero y sin deformar. El nombre y el número de juegos aparecen al pasar el ratón por encima.
- La vista previa del editor de filtros muestra la pestaña igual que se verá arriba.

## [0.0.3] - 2026-10-16

Filtros para agrupar los juegos como quieras.

- **Barra de filtros** arriba: «Todos los juegos» y tus filtros, cada uno con el número de juegos que tiene. Cambia de filtro con un clic o con `Tab` / `Shift+Tab`.
- **Crear filtros** con «+ Nuevo filtro»: ponles un nombre y la imagen que quieras (se ve entera, sin deformarla ni recortarla) con vista previa de cómo quedará la pestaña.
- **Editar o eliminar** un filtro con clic derecho sobre su pestaña. Eliminar un filtro no borra sus juegos.
- **Asignar juegos** a uno o varios filtros desde Personalizar (`E`), en la nueva sección «Filtros».
- Lo que importes con un filtro seleccionado entra directamente en ese filtro.
- En «Todos los juegos», las etiquetas de cada juego muestran a qué filtros pertenece.
- Arreglado: al abrir un menú con el ratón, el cursor no se colocaba en el campo del nombre.

## [0.0.2] - 2026-10-16

Menú de personalización para cada juego.

- **Personalizar** un juego con `E` o con clic derecho sobre su caja.
- **Cambiar el nombre** del juego.
- **Portada propia**: elige cualquier imagen (PNG, JPG, BMP, GIF, WebP, TIFF) o arrástrala a la ventana con el menú abierto. Se ve entera, sin deformarla ni recortarla; si no tiene la forma de la caja, el hueco que sobra queda en negro.
- **Quitar la portada** para volver a la caja negra.
- Vista previa en el menú igual que como se verá en el carrusel. Los cambios solo se aplican al pulsar «Guardar» (`Enter`); `Esc` o clic fuera cancela.
- Las portadas se copian a `basilisco-data/covers`, así la biblioteca sigue siendo portable aunque muevas o borres la imagen original.
- Portadas con más resolución para que las imágenes se vean nítidas.

## [0.0.1] - 2026-10-16

Primera versión: el launcher abre y ya permite añadir y lanzar juegos.

- **Portable**: un único `Basilisco.exe`, sin instalación. La biblioteca se guarda en la carpeta `basilisco-data` junto al `.exe`.
- **Carrusel 3D** de portadas: la caja seleccionada de frente, el resto giradas y apiladas a los lados, con reflejo en el suelo y transiciones suaves. De momento las cajas son rectángulos negros genéricos.
- **Importar juego**: botón, tecla `I`, tarjeta «Importar juego» al final del carrusel o arrastrando archivos a la ventana. Admite ejecutables (`.exe`), accesos directos (`.lnk`, `.url`), scripts (`.bat`, `.cmd`) y cualquier otro archivo.
- **Lanzar** el juego seleccionado con `Enter` o haciendo clic en su caja.
- **Quitar** un juego de la biblioteca con `Supr` (no lo borra del disco).
- Etiquetas con el tipo de archivo, veces jugado, última partida y carpeta.
- Navegación con flechas, rueda del ratón o clic en las cajas laterales. `F11` para pantalla completa.
