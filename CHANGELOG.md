# Historial de cambios

Todas las versiones de Basilisco. Cada versión se publica en
[Releases](https://github.com/sergio120602/Proyecto-Basilisco/releases) con su `.exe` portable.

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
