# Historial de cambios

Todas las versiones de Basilisco. Cada versión se publica en
[Releases](https://github.com/sergio120602/Proyecto-Basilisco/releases) con su `.exe` portable.

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
