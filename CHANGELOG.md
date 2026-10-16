# Historial de cambios

Todas las versiones de Basilisco. Cada versión se publica en
[Releases](https://github.com/sergio120602/Proyecto-Basilisco/releases) con su `.exe` portable.

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
