# Historial de cambios

Todas las versiones de Basilisco. Cada versión se publica en
[Releases](https://github.com/sergio120602/Proyecto-Basilisco/releases) con su `.exe` portable.

## [0.6.23] - 2026-10-24

- **Buscar la banda sonora desde la propia banda sonora**: en el librito de canciones hay un botón **«Buscar banda sonora»** (o la tecla **S**) que la busca en MusicBrainz con el nombre del juego y deja elegir el disco de una lista.
  - Si ya tenía una, el botón pasa a ser «Buscar otra banda sonora».
  - Al cambiarla, la música importada y los vídeos de YouTube que ya tenías se quedan en las canciones que se llaman igual (ahora también en Personalizar).

## [0.6.22] - 2026-10-24

- El lector de manuales tiene dos opciones nuevas arriba:
  - **«Una sola página»**: alterna entre leer de página en página y el libro abierto de siempre (**modo libreta**). Se recuerda para cada juego (tecla **P**).
  - **«Hacer con Pinterest»**: abre Pinterest para hacer el manual desde ahí. Las imágenes que ya tiene salen elegidas, para cambiarlas, quitar o reordenar; al guardar, el manual se vuelve a abrir con los cambios.
- **Imágenes dobles**: al elegir las imágenes en Pinterest, cada una lleva un botón **«↔»** para que ocupe **las dos páginas** del libro abierto (pensado para las horizontales). En la tira de elegidas, las dobles se ven el doble de anchas.
  - En el modo libreta, una imagen doble empieza siempre en la página izquierda; si le tocaba la derecha, esa queda en blanco.
  - En una sola página, la imagen horizontal se ve entera, con su forma.
- **Girar** en el modo de una sola página: el botón **«⟳ Girar»** (o la tecla **R**) gira la página un cuarto de vuelta, y se recuerda.
- Las páginas ya no se deforman: cada imagen se ve entera sobre el papel, con su proporción.
- Al rehacer el manual en Pinterest, las imágenes que ya tenía no se vuelven a descargar.

## [0.6.21] - 2026-10-24

- La **banda sonora** ahora es **parte de la caja**: al pasar la hoja del disco aparece otra doble página encima de la caja abierta, en su sitio y con su tamaño.
  - Tiene el **mismo fondo que la carátula** (la imagen interior o la portada), con el plástico de la consola por encima y el lomo en medio.
  - **A la izquierda, el disco** en su bandeja, con la canción, las barras y el reproductor de YouTube debajo.
  - **A la derecha, el librito** «Banda Sonora»: una hoja de papel con la lista de canciones, como un manual de verdad.
- **Manual desde SteamGridDB**: nuevo botón «Buscar en SteamGridDB» en la sección del manual del menú de edición, que busca en los **grids** del juego (primero los verticales, luego todos), igual que las portadas.

## [0.6.20] - 2026-10-24

- Las canciones de la banda sonora que no tienes en archivo suenan desde **YouTube**, con su **reproductor oficial a la vista** debajo del disco (como piden las condiciones de YouTube).
  - Es streaming: no se descarga nada.
  - El disco, las flechas, la barra de la canción y la del volumen lo manejan igual que tu música.
  - Al salir de la banda sonora se para, porque el reproductor dejaría de verse. Tu música sigue sonando.
- **Elegir el vídeo de cada canción**: la primera vez se abre la búsqueda de YouTube de esa canción, y el vídeo que abras se queda para ella (se recuerda; en la lista lleva «YouTube»).
  - Con **clic derecho** en una canción se elige otro vídeo o se quita.
  - Con tu **clave de la API de YouTube** (gratuita; se pone en esa misma ventana), el vídeo se busca solo.
- Pinterest y el reproductor de YouTube comparten el mismo navegador de Windows y la misma sesión.

## [0.6.19] - 2026-10-24

- **Banda sonora** en la caja abierta.
  - Con la flecha derecha (o «Banda sonora ▶»), la **hoja de plástico donde está el disco pasa como una página**.
  - A la izquierda aparece un papel con «**Banda Sonora**» arriba y la lista de canciones. A la derecha, el **disco de la banda sonora**.
  - Un **clic en el disco** hace sonar la canción elegida y lo pone a girar; otro clic la para.
  - Las **flechas izquierda y derecha** retroceden y avanzan 10 segundos en la canción, y arriba y abajo eligen canción. Debajo del disco están la **barra de la canción** (se puede arrastrar) y la del **volumen**, que se recuerda.
  - Al acabar una canción, suena la siguiente que tenga música. Esc, B o «Volver al juego» devuelven la hoja a su sitio.
  - Funciona también con el mando.
- La **lista de canciones** y la carátula salen de **MusicBrainz** (base de datos de música abierta con API oficial). Si la edición encontrada no tiene carátula, se usa la de otra edición del mismo disco. VGMdb no deja conectarse a los programas.
- En el **menú de edición**, sección nueva «**Banda sonora**»:
  - «**Buscar banda sonora**» elige entre las que encuentre.
  - «**Importar música…**»: eliges tus archivos (MP3, FLAC, M4A, WAV…) y los que **se llaman como una canción de la lista** se registran en ella. Da igual el número de pista, las mayúsculas o lo que lleven delante. Los demás no se importan. Si el juego no tiene lista, los archivos forman la lista. Se copian a `basilisco-data/music`.
  - «**Imagen del disco**», «**Ajustar disco**» y «Quitar banda sonora».
- El **disco del juego** también se puede **ajustar** en el menú de edición («Ajustar…»: ampliar y mover la imagen dentro del disco).
- La **Descarga Metadatos Actualización** trae también la lista de canciones y el disco de la banda sonora de cada juego.

## [0.6.18] - 2026-10-23

- Al jugar desde la **caja abierta**, el disco **sigue girando mientras el juego esté abierto** (antes la caja se cerraba a los pocos segundos). Al cerrar el juego, **Basilisco vuelve al frente**, el disco **frena poco a poco** hasta pararse (el cartucho vuelve a bajar a la caja) y la **carátula se cierra**. Si el juego no llega a arrancar, también frena y se cierra.
- Mientras juegas, la animación del disco se pausa (no se ve), para no gastar gráfica, y sigue al volver.
- Arreglado: Basilisco hacía caso al **mando aunque no estuviera delante**. Mientras jugabas, un B en el juego cerraba la caja, y las flechas movían el carrusel por detrás. Ahora el mando solo cuenta con Basilisco en primer plano.

## [0.6.17] - 2026-10-23

- **Zoom en los manuales**, para las pantallas pequeñas: del 100 % al 400 %.
  - Se usa con **Ctrl + rueda** (acerca hacia donde está el ratón), con **+** y **−**, con los botones nuevos de abajo («−», el porcentaje y «+»), o con **doble clic** (alterna entre el libro entero y el doble). «Ajustar» o **0** vuelven al libro entero.
  - Con zoom, el libro se mueve **arrastrándolo** con el ratón. Un clic sin arrastrar sigue pasando página.
  - Al acercar, las páginas se vuelven a dibujar con más resolución para que el texto se lea nítido. El libro se queda en su zona y no tapa los botones.

## [0.6.16] - 2026-10-23

- Arreglado el **inicio de sesión en Pinterest**, que se quedaba en blanco al entrar con Google, Facebook o Apple. Esas ventanas de inicio de sesión se abren ahora como **ventanas de verdad**, con la misma sesión. Al terminar avisan a Pinterest y se cierran solas, y la sesión queda iniciada. Lo demás que se abra en otra pestaña (un pin) se sigue abriendo dentro.

## [0.6.15] - 2026-10-23

- **Manuales con Pinterest**: en el menú de edición de cada juego, en la sección del manual, hay un botón nuevo, **«Ver Pinterest»**.
  - Abre Pinterest dentro de Basilisco, con tu cuenta (el navegador Edge que trae Windows; la sesión se guarda en `basilisco-data/webview`). La búsqueda viene puesta como «#juego #artbook» y se puede cambiar.
  - Un clic en una imagen la elige o la quita. Encima lleva un **número con su orden de selección** y un borde dorado, y abajo se ven las elegidas en orden (un clic en una la quita).
  - Con «Seleccionar imágenes» apagado, Pinterest funciona como siempre (abrir pines, iniciar sesión…).
  - **«Guardar como manual»** descarga las elegidas y pasan a ser el manual del juego, en ese orden. Va antes que el PDF, y la primera página es el librito de la caja abierta si no tenía imagen.
- **El lector de manuales es un libro en 3D**: abierto sobre su tapa y algo tumbado. Al avanzar o volver, la **hoja pasa curvándose** sobre el lomo, con la página siguiente por detrás, como un cuaderno impreso. Vale para los PDF y para los manuales de imágenes.
- **Para que los manuales ocupen poco**:
  - Cada página se guarda como JPEG al alto al que se lee (1600 px, calidad 82), unos 250-400 KB por página. Las imágenes repetidas se guardan una sola vez.
  - Hay un **tope de espacio** (2 GB, que se cambia en `settings.json` con `ManualSpaceLimitMb`). Si se pasa, se sueltan las páginas de los manuales que llevas más tiempo sin abrir, y se vuelven a descargar solas al abrirlos.
  - El libro solo tiene en memoria las páginas que se ven y las de al lado.

## [0.6.14] - 2026-10-23

- Arreglado **«Ver sus logros»**, que decía «perfil privado» a miembros con el perfil público. Pasaban dos cosas:
  - Con la clave de la Web API, Steam pide los logros de los demás como si fueras un desconocido. Si un miembro tiene los «Detalles de juegos» en **«solo amigos»**, no los da, aunque su perfil sea público. Ahora, si has pegado tu **token de sesión** en la ventana de Steam (solo se guarda en memoria), se vuelven a pedir **como tú**, que eres de su familia. Si sigue sin dejar, se explica que tiene que poner sus «Detalles de juegos» como públicos.
  - Al pedir los logros de muchos juegos muy seguidos, el cortafuegos de Steam corta las consultas («Access Denied»), y eso se tomaba por «perfil privado». Ahora se pregunta de dos en dos y con pausas. Si Steam frena o el perfil no los enseña, se deja de preguntar en ese momento y se dice el motivo.
- Si Steam no da tus logros en la página de un juego, ahora lo dice, en lugar de enseñarlos todos como «sin conseguir».

## [0.6.13] - 2026-10-23

- **Logros de cada miembro de Steam familiar**: en su menú de personalizar (clic derecho en el miembro) hay un botón nuevo, **«Ver sus logros»** («Ver mis logros» en tu caso).
  - Abre, con la misma animación, la lista de **todos sus juegos**, cada uno con su portada, su **plataforma** y su **barra de logros** (azul con degradado, y dorada al 100 %).
  - Tú ves todos tus juegos, los que tienen logros y los que no, y los que no son de Steam con su consola y su perfil de trofeos. Los demás miembros ven sus juegos de Steam, también marcados como «Steam».
  - Al elegir un juego se ven sus logros. Si el juego lo tenéis varios, cada logro dice **quién lo tiene** (por ejemplo «Frank ✓   Tú ✗»), y arriba cuántos tenéis cada uno y cuántos los dos.
  - «← Sus juegos» o `Esc` vuelve a la lista.
  - Los logros de los demás se leen con tu clave de la Web API de Steam, así que hace falta que sus perfiles dejen ver los «Detalles de juegos».
- La **foto de cada miembro** ocupa ahora **toda su casilla** en el menú de Steam familiar.

## [0.6.12] - 2026-10-23

- **Perfil de trofeos** para los juegos que no tienen logros de Steam. Va en el sitio de la barra de logros y abre la misma página, con la misma animación.
  - **Crear trofeo**: con título, descripción opcional y dificultad (bronce, plata, oro o platino).
  - **Crear varios**: a la vez y numerados, en líneas o seguidos («1. Trofeo primero, 2. Trofeo segundo…»). Uno con otra dificultad la lleva entre paréntesis al final («4. Jefe final (oro)»), y «Título: descripción» separa la descripción. Los números tienen que ir seguidos, así un «nivel 10» no parte el trofeo.
  - Con **clic derecho** en un trofeo tuyo se cambia su dificultad o se elimina.
  - **Activación manual** (botón para encenderla o apagarla): encendida, un clic en un trofeo lo da por conseguido y otro clic lo quita.
- **RetroAchievements**: inicias sesión con tu usuario y la clave de la Web API de tu cuenta, eliges el juego y se traen sus logros con tu progreso. Se reparten en bronce, plata y oro según cuántos jugadores los tienen.
- **RPCS3**: eliges la carpeta del emulador y Basilisco lee los trofeos que guarda de cada juego de PS3, con su tipo, cuáles tienes y sus iconos.
- **PlayStation · PSN Profiles**: PSN Profiles no deja que los programas lean su web, así que los trofeos se leen de **PlayStation Network**, que es de donde los saca PSN Profiles. Entras con el código de tu sesión (npsso), que solo se guarda en memoria. El botón «Abrir en PSN Profiles» lleva al juego en su web.
- **Traducción al español**: los trofeos que vienen en inglés se traducen con MyMemory, tanto los de estos servicios como los logros de Steam que no tienen español. Cada texto se traduce una vez y se guarda en `basilisco-data/translations.json`.

## [0.6.11] - 2026-10-22

- **Página de logros**: al pulsar el trofeo (o la barra de logros), la escena sube y desde abajo entra la página con **todos los logros** del juego.
  - Cada logro sale con su icono, nombre, descripción, fecha en que lo conseguiste y cuántos jugadores lo tienen.
  - Los conseguidos **brillan** del color de su trofeo; los que faltan van **en gris**. Los ocultos dicen «Logro oculto» hasta que los consigues.
  - Se separan por trofeos según el porcentaje mundial de jugadores que los tienen:

    | Trofeo | Jugadores que lo tienen | Dificultad |
    |---|---|---|
    | Bronce | la mitad o más | fáciles |
    | Plata | del 15 al 50 % | dificultad media |
    | Oro | menos del 15 % | difíciles |
    | Platino | — | se consigue al tener todos los logros |

  - Arriba se ve el resumen: cuántos tienes de cada trofeo.
  - Para volver: «↑ Volver», `Esc`, la flecha arriba (desde el principio de la página) o B en el mando. Se recorre con la rueda, las flechas o el mando.

## [0.6.10] - 2026-10-22

- Los controles del carrusel suben: la **alineación** y la **altura** quedan arriba a la izquierda, debajo de «Atajos de teclado», y la **descarga de metadatos** y el **zoom**, arriba a la derecha, a los lados del título. Abajo solo queda la barra de logros.
- Con la altura en **Auto**, las cajas tampoco pisan estos controles.

## [0.6.9] - 2026-10-22

- Las **pistas de teclas** dejan la barra de abajo: ahora están en el botón **«Atajos de teclado»**, debajo de «Basilisco», que abre un menú con todas, en dos filas. Con el mando enseña sus botones.
- El **tiempo de juego** va con las etiquetas del juego, en lugar de «Nunca jugado» (que no cuadraba con las horas de Steam). Si un juego no tiene tiempo, sigue diciendo «Nunca jugado» o cuántas veces se ha abierto.
- **Logros de Steam**: abajo, en el centro, una barra se llena con el porcentaje de logros del juego seleccionado.
  - Mientras falten logros, la barra es **azul con degradado**, como en Steam, y el trofeo está en gris.
  - Con todos, pone **100 %**, se vuelve **dorada** y el trofeo **brilla**.
  - Se leen de tu cuenta con la clave de la Web API de Steam. Lo último que se supo se guarda en `basilisco-data/achievements.json` para que salga al momento, y se vuelve a preguntar a Steam cada 30 minutos.
- La **descarga masiva de metadatos** pasa a la línea de la altura y el zoom.

## [0.6.8] - 2026-10-22

- **Buscador de juegos** en la barra de arriba (también con `Ctrl+F`). Al escribir aparece un menú con los juegos cuyo nombre más se parece a lo escrito. Da igual que no lleve tildes o mayúsculas, que tenga alguna letra mal («holow knigt») o que solo escribas el principio de cada palabra («zelda oca»). Los números romanos y en cifras valen igual.
  - Con un clic, o con las flechas y `Enter`, el carrusel va **directo** a ese juego.
  - Si no está en lo que estás viendo (otra consola, otro filtro…), pasa a «Todos los juegos».
  - `Esc` cierra el buscador.
- Arreglado el **lomo de Steam Game Covers**, que en algunas carátulas salía mal recortado o con media portada dentro. Pasaba con los dibujos claros, que se confundían con el papel, y con los que tienen zonas blancas. Ahora la zona del dibujo se corrige con la proporción que debe tener la carátula y se centra en la hoja, como van en las plantillas de la web. También en la trasera de los jewel case.

## [0.6.7] - 2026-10-22

- Nuevo menú **Steam familiar** en la barra de arriba, junto a Consolas y Filtros, con la misma franja negra.
  - Sale cada miembro de tu familia de Steam, **tú incluido**, con su imagen (su foto de perfil de Steam, que se pone sola) o su nombre.
  - Al elegir un miembro se ven **todos sus juegos**. Un juego que tienen varios miembros (tú y otro, varios o toda la familia) sale en la ficha de **cada uno** de ellos.
  - Con **clic derecho**, o con «Personalizar miembro», se le pone otra imagen: su foto de perfil de Steam, una tuya o ninguna.
  - Para saber de quién es cada juego, Basilisco lee ahora la biblioteca completa de la familia, con todos los dueños de cada juego, también de los que ya tenías importados. Basta con volver a «Buscar juegos» en Steam con Steam Familiar marcado. Hasta entonces, el menú se arma con lo que ya se sabía: tus juegos y los que comparte cada miembro.
  - El filtro **«Steam Familiar»** deja de existir: se quita de Filtros. Los juegos siguen en la biblioteca.
  - La ficha de un juego dice todos los miembros que lo tienen.
- **Descarga automática de portadas**: en el formato **cuadrado**, SteamGridDB va siempre primero. En el **rectangular** sigue como antes: LaunchBox primero, salvo en PC.
- Soporte para **pantallas panorámicas 21:9** (y más anchas): las cajas se ven igual de grandes que en 16:9, sin cortarse por arriba ni por abajo, y el carrusel llega hasta los bordes de la pantalla.
- En GitHub, cada versión trae un **.zip**: al extraerlo sale una carpeta con `Basilisco.exe` y su carpeta de datos `basilisco-data`. El `.exe` suelto se sigue publicando, porque es el que usa «Actualizar».

## [0.6.6] - 2026-10-22

- Las **contraportadas** se **adaptan** por defecto al tamaño de la portada: se estiran justo a la medida de la cara (600 × 900 en las cajas de PC), sin bandas negras.
- En **Personalizar → Ajustar contraportada** hay un interruptor **«Adaptada»**, encendido por defecto. Al apagarlo, la contraportada vuelve a su proporción original, con el zoom y la posición que tuviera. La imagen guardada no se toca nunca, así que se puede encender y apagar cuando quieras.

## [0.6.5] - 2026-10-22

- **Cajas de Steam**: los juegos de PC que vienen de **Steam** y están en formato **rectangular** llevan arriba, en la portada y en el lomo, la **franja negra de Steam**, como las cajas de PC reales.
  - En la portada va la marca en un círculo blanco y «STEAM» en letras gruesas; en el lomo, solo la marca.
  - Debajo de la franja, la carátula ocupa una zona exactamente **2:3**: las portadas de SteamGridDB (600 × 900) caben justas, sin bordes negros ni recortes. La caja mide 19 cm de alto y se estrecha lo justo para que salga esa proporción.
  - Si quieres usar tu propia franja, guárdala en `basilisco-data/borders` como `steam.png` (portada) y `steam-lomo.png` (lomo).

## [0.6.4] - 2026-10-22

- **Descarga automática de todo**: portada, contraportada, lomo, interior de la caja, disco y manual. Cada parte se busca en sus servicios por orden de prioridad:
  - **Portada**: LaunchBox, luego SteamGridDB y por último Steam Game Covers. En **PC**, SteamGridDB va antes que LaunchBox.
  - **Contraportada**: LaunchBox, luego SteamGridDB y por último Steam Game Covers. SteamGridDB no tiene contraportadas, así que en la práctica se salta.
  - **Lomo**: LaunchBox, luego The Spine Archive y por último Steam Game Covers. En **PC**, The Spine Archive, luego Steam Game Covers y por último LaunchBox.
  - **Interior de la caja**: SteamGridDB.
  - **Disco y manual**: LaunchBox.
- Los lomos de **The Spine Archive** son de cajas de Switch: en los juegos de otras consolas se les quita sola la franja de arriba con el **logotipo de Nintendo Switch**. Las partes de **Steam Game Covers** se ponen tal cual salen de la carátula, sin encuadrar.
- Nuevo botón **Descarga Metadatos Actualización** (abajo a la derecha): busca para todos los juegos lo que les falte y lo pone solo, sin tocar lo que ya tienen. Antes dice a cuántos juegos les falta algo y pide confirmación. Mientras descarga, el mismo botón la cancela.
- Al cambiar de formato, las imágenes por defecto también incluyen el lomo.

## [0.6.3] - 2026-10-21

- **Animación del carrusel más fluida**:
  - Las cajas se pintan por adelantado en **bloques de 50**, mientras el carrusel está quieto. Al llegar a la caja 35 de un bloque se pinta ya el siguiente, así que al deslizar casi todas las cajas están listas.
  - Todas las cajas se pintan a la misma resolución, así que ya no se vuelven a pintar a mitad del deslizamiento.
- Los **reflejos en el suelo** vienen quitados: la animación va más suave. Se pueden volver a poner en Apariencia → Rendimiento.

## [0.6.2] - 2026-10-21

- **Arranque mucho más rápido y menos memoria**: con una biblioteca de 155 juegos, la ventana sale en unos **3 segundos** (antes unos 25) y la aplicación ocupa unos **600 MB** en lugar de 1,5 GB.
  - Las cajas solo se pintan cuando se ven, unas pocas en cada fotograma, y las que quedan lejos sueltan sus imágenes. La del centro y sus vecinas van a resolución completa; las del fondo, a la mitad.
  - Cada imagen se lee una sola vez, ya al tamaño que se va a ver, en lugar de leerse entera y reducirse después.
  - La base de datos de LaunchBox se suelta de la memoria si no se usa en 3 minutos y se vuelve a cargar sola cuando hace falta.
  - Las imágenes de los menús de filtros y consolas se guardan ya preparadas, así que abrirlos otra vez es instantáneo.
- Nueva sección **Rendimiento** en Apariencia:
  - **Calidad de las cajas** (Alta, Media o Baja). En Baja la memoria baja unos 100 MB más.
  - **Reflejos en el suelo**: se pueden quitar.
  - **Fondos en vídeo en movimiento**: sin esta opción, el vídeo se queda quieto en el primer fotograma y gasta mucho menos procesador.
  - Botón **Optimizar datos…**: reduce las imágenes de `basilisco-data` que son más grandes de lo que se llega a ver (mismo nombre y formato) y borra los archivos que ya no usa ningún juego. Antes de tocar nada, dice cuánto se ahorra y pide confirmación.

## [0.6.1] - 2026-10-20

- Arreglada la **alineación moderna**: en lugar de bajar las cajas de los lados (que acababan por debajo del suelo), ahora todas las cajas quedan **de frente, sin girar y a la misma profundidad**, una al lado de otra en una **fila recta horizontal**, como la biblioteca de Steam en modo Big Picture. Las bases y los altos quedan en línea con cualquier zoom.

## [0.6.0] - 2026-10-20

- **Alineación moderna** del carrusel (botón abajo a la derecha, junto a la altura): las cajas de los lados bajan lo justo para que la base de todas quede en **línea recta** con la del centro. Sin él, la alineación clásica (las del fondo más arriba, en curva). Se recuerda al cerrar.
- **Botones rectangulares** en lugar de redondeados en toda la aplicación (la barra de arriba, los menús, los paneles y las opciones).
- El menú de **Consolas** es ahora la misma **franja negra** que el de filtros, con la **imagen de cada consola** en grande (o su nombre en blanco) y el borde dorado. Con «Personalizar consola» (o clic derecho) se le pone imagen: la foto de la consola de la base de datos de **LaunchBox**, una imagen propia o ninguna. Se guardan en `basilisco-data/platforms/`.
- Si el servidor de imágenes no responde a la primera (pasa a veces con el de LaunchBox), la descarga se reintenta sola.

## [0.5.9] - 2026-10-20

- Las imágenes de los **filtros** se pueden buscar en **SteamGridDB**: en el editor de filtros, «Buscar en SteamGridDB» busca los **logotipos** del juego (PNG con fondo transparente, hechos por la comunidad) con el nombre del filtro. Las miniaturas de los logotipos se ven sobre fondo oscuro, porque muchos son blancos.
- El buscador de imágenes se abre ahora por encima del editor de filtros.

## [0.5.8] - 2026-10-20

- Nuevo **menú de filtros**: al pulsar «Filtros» se despliega bajo la barra de arriba una **franja negra** de lado a lado con cada filtro en grande, con su **logotipo** (o su nombre en letras blancas si aún no tiene) y cuántos juegos tiene. Al pasar por encima (o al moverse con las flechas o el mando) la casilla se destaca con un **borde dorado**, y el filtro que se está viendo lleva el borde dorado fino. Al final están «+ Nuevo filtro» y «Editar filtro» (que pasa a elegir qué filtro editar); con clic derecho se edita un filtro directamente. Se cierra con Esc, B o haciendo clic fuera.

## [0.5.7] - 2026-10-20

- Nueva barra **Altura** (abajo a la derecha, junto al zoom) para subir o bajar las cajas del carrusel, y botón **Auto**: con él, cuanto más zoom, más bajan las cajas, lo justo para que el nombre del juego y sus etiquetas nunca las pisen. Viene activado; al quitarlo, la barra se queda donde estaba y se mueve a mano. Se recuerda al cerrar.
- La altura y el zoom van en su propia línea, encima de la barra de abajo, para que quepan todas las pistas de teclas.

## [0.5.6] - 2026-10-20

- El **nombre del juego** queda **fijo**, centrado justo debajo de la barra de arriba, con sus etiquetas debajo: ya no se mueve con el zoom ni con el tamaño de la caja seleccionada.
- Para dejar libre el centro, el menú **Orden** pasa a la derecha de la barra; a la izquierda quedan «Todos los juegos», «Consolas» y «Filtros».

## [0.5.5] - 2026-10-20

- Nuevas consolas: **NES**, **Super Nintendo**, **Nintendo Switch** y **Arcade**, con su franja dibujada en la portada, el color de su caja y sus medidas reales (caja de cartón de NES, caja apaisada de Super Nintendo y estuche rojo de Switch). LaunchBox y Vimm's Lair las reconocen.
- **Cartuchos** en la caja abierta: los juegos de **Nintendo 64**, **Nintendo DS** y **Nintendo Switch** llevan su cartucho en lugar de un disco, dibujado como el de verdad y con la imagen del juego en la pegatina (la de «Disco o cartucho» si tiene, o la portada). Va en un hueco a su medida en la bandeja y, al darle a Jugar, sale de la caja hacia arriba en lugar de girar.

## [0.5.4] - 2026-10-19

- **Ajustar** el manual y el interior de la caja: en Personalizar, «Ajustar…» junto a la imagen del manual y a la del interior abre el editor de encuadre (rueda para ampliar, arrastrar para mover) viendo la caja abierta de frente, con el disco y todo lo elegido.
- **Manuales originales de [Vimm's Lair](https://vimm.net/manual)** (The Manual Project): en Personalizar → Manual, «Buscar en Vimm's Lair» busca entre sus miles de manuales escaneados de consola, con la portada, la región y la resolución de cada uno. Al elegir uno se abre su página en el navegador para pulsar «Download pdf» (la web solo deja descargar desde un navegador) y Basilisco recoge el PDF de la carpeta de Descargas en cuanto termina.

## [0.5.3] - 2026-10-19

- **Imagen del interior de la caja**: en Personalizar → «Interior de la caja» se puede poner una imagen (tuya o de **SteamGridDB**, con sus fondos y carátulas) que se ve **nítida** dentro de la caja abierta, de lado a lado: la mitad izquierda en la tapa y la derecha debajo del disco. El plástico translúcido le da un velo del color de la consola y su brillo, sin taparla.
- Si el juego tiene imagen del interior y no tiene manual, ya no se pone el librito de ejemplo, para que se vea la imagen entera.
- Sin imagen del interior, la caja vuelve a ser del plástico de su consola (sin las carátulas desenfocadas de la 0.5.2).

## [0.5.2] - 2026-10-19

- El **interior de las cajas** abiertas es ahora de **plástico translúcido**, del color de cada consola: a través de la bandeja se ve, desenfocada, la contraportada por dentro, y alrededor del manual, la portada (como en las cajas transparentes de verdad).
- En Personalizar → Fondo, nuevo botón **Buscar en SteamGridDB**: fondos del juego hechos por la comunidad (sus «heroes», de 1920 × 620 o 3840 × 1240), con miniaturas apaisadas. Usa la misma clave que las portadas.

## [0.5.1] - 2026-10-19

- **Modo nocturno**: toda la interfaz en colores oscuros (el fondo del carrusel y su suelo, la cabecera, la barra de abajo, los menús, Personalizar, los buscadores y el resto de ventanas), con la barra de título de Windows también oscura.
- Se cambia con el botón de la luna (o el sol) de arriba a la derecha, con la tecla `N` o en **Apariencia → Modo**: Claro, Nocturno o **Como Windows**, que sigue el modo de Windows (también si lo cambias con Basilisco abierto). Por defecto, como Windows.
- El color «Automático» de las letras se adapta al modo: oscuro en el claro y claro en el nocturno.

## [0.5.0] - 2026-10-19

- **Manuales en PDF**: cada juego puede tener su manual de instrucciones en PDF, con texto de verdad (se puede seleccionar, buscar e imprimir). Basilisco lo escribe solo con la información de la **tienda de Steam** (en español: descripción, ficha, requisitos del sistema, compatibilidad con mando, idiomas y capturas) y de **LaunchBox** (descripción, desarrolladora, editora, género, jugadores y capturas, para los juegos que no están en Steam). Lleva la portada del juego en la primera página y la contraportada en la última.
- **Lector de manuales** dentro de Basilisco, como un librito: la portada sola y después dos páginas a la vez. Se pasa de página con las flechas, la rueda, un clic en la página o el mando, y también se puede abrir en el visor de PDF de Windows.
- En la caja abierta, nuevo botón **Leer el manual** (`M`, o X en el mando, o un clic en el librito). Si el juego aún no tiene manual, se crea en ese momento y su portada pasa a ser el librito de la caja.
- En Personalizar → Manual: **Crear manual en PDF**, **Elegir PDF…** (un manual tuyo, por ejemplo el original escaneado), **Leer** y **Quitar PDF**. Se guardan en `basilisco-data/manuals/`.
- La base de datos de LaunchBox guarda ahora también la ficha de cada juego y sus capturas. La primera vez que se cree un manual con ella se vuelve a descargar (unos 100 MB).

## [0.4.1] - 2026-10-19

- La interfaz **se adapta a cualquier resolución**: la cabecera, la barra de abajo, el título del juego, los avisos, los menús y todas las ventanas (Personalizar, la caja abierta, los buscadores…) se escalan con el tamaño de la ventana, tomando 1920 × 1080 como referencia. En pantallas pequeñas ya no se pisan los textos y en las grandes (2K, 4K) no se ven diminutos.
- En Personalizar, las dos columnas se desplazan si la ventana es baja, así que ya no se corta.
- Nuevo botón **Actualizar**: cuando hay una versión nueva en GitHub aparece arriba a la derecha (o con la tecla `U`). Descarga el `.exe` nuevo, lo pone en lugar del actual y vuelve a abrir Basilisco, sin tocar la biblioteca ni los ajustes. Se comprueba al abrir Basilisco y cada 6 horas.

## [0.4.0] - 2026-10-19

- **Caja abierta**: al darle a un juego (`Enter`, clic o A en el mando) ya no se lanza directamente. Primero se abre su caja en 3D: la tapa gira sobre el lomo y deja ver el **manual** a la izquierda y el **disco** a la derecha. Al darle a **Jugar**, el disco empieza a girar, se inicia el juego y la caja se cierra sola. `Esc` (o B) vuelve al carrusel.
- Nuevas imágenes por juego en Personalizar: **disco** y **manual**. Sin disco, se dibuja uno plateado con la portada; sin manual, un librito con el nombre del juego.
- Nueva fuente de carátulas: **[Steam Game Covers](https://www.steamgamecovers.com/)**, sin clave, para la portada, la contraportada, el lomo y el disco. Sus plantillas de impresión se recortan solas, sin márgenes, guías ni textos legales. Las carátulas completas (DVD y Blu-ray) se separan en portada, lomo y contraportada, y rellenan las caras que estén vacías. Las de jewel case traen la trasera con su lomo.
- **LaunchBox** trae ahora también los **discos** y, para el manual, el póster, los fondos y la pantalla de título del juego. La primera búsqueda de un disco o un manual vuelve a descargar su base de datos (unos 100 MB) para incluirlos.
- En la barra de abajo, «Jugar» pasa a ser «Abrir».

## [0.3.9] - 2026-10-18

- La caja rectangular de **Wii U** mide ahora **190 × 135 × 14 mm** (alto × ancho × grosor), como una de DVD.
- El **tiempo de juego** se cuenta mientras el juego siga abierto, aunque vuelvas a Basilisco: se miran los procesos que se ejecutan desde la carpeta del juego (también si se abre con un lanzador que se cierra enseguida, o desde Steam).
- Si cierras Basilisco mientras juegas, la partida queda guardada en `basilisco-data/session.json`: al volver a abrirlo sigue contando si el juego sigue abierto, o apunta lo jugado hasta la última vez que lo vio.
- Los juegos de los que no se puede saber la carpeta (un enlace, por ejemplo) siguen contando hasta que vuelves a Basilisco.

## [0.3.8] - 2026-10-18

- Nuevo botón **Buscar en The Spine Archive** en el lomo (Personalizar): más de 11 000 lomos hechos por la comunidad de [thespinearchive.com](https://thespinearchive.com/), sin clave. El catálogo se descarga la primera vez (unos 7 MB) en `basilisco-data/spinearchive/` y se pone al día solo cada semana (o con «Actualizar catálogo»).
- En el buscador, los lomos se ven tumbados para que se lea el título, con su autor y su consola.
- Si una imagen tarda demasiado en descargarse, ahora se avisa en lugar de no hacer nada.

## [0.3.7] - 2026-10-18

- Los juegos de **Steam** llevan ahora **las mismas horas que en Steam**, también los instalados y los de Steam Familiar. Se leen del Steam de este PC al abrir Basilisco y al volver a él; con la clave de la Web API también se piden a Steam, así que cuenta lo jugado en otros PC.
- Al cerrar un juego de Steam, Basilisco ya no suma su propio tiempo, sino que se pone al día con las horas de Steam, sin contar dos veces.
- La última partida de los juegos de Steam también viene de Steam.
- En «Personalizar», las horas de un juego de Steam no se pueden cambiar a mano.

## [0.3.6] - 2026-10-18

- El **logo** tiene ahora las **esquinas redondeadas** (con transparencia), en el icono del `.exe`, la ventana y el README.

## [0.3.5] - 2026-10-18

- Nuevo **logo**: San Miguel venciendo al basilisco. Es el icono del `.exe`, de la ventana y de la barra de tareas.

## [0.3.4] - 2026-10-18

- Las cajas rectangulares de **PC** pasan a la proporción **600 × 900** (2:3), la de las carátulas verticales de Steam y SteamGridDB, con el mismo alto que una caja de DVD (19 × 12,7 × 1,4 cm).

## [0.3.3] - 2026-10-18

- Arreglado: algunos vídeos de fondo no cargaban. Eran vídeos en **VP9, AV1 o HEVC** (lo habitual en descargas de YouTube), que el reproductor de Basilisco no sabe abrir aunque Windows tenga sus extensiones. Ahora Basilisco los **convierte una sola vez a H.264** con el conversor de Windows (unos segundos) y sustituye el archivo por el convertido; después cargan al momento. Los fondos que ya tenías se arreglan solos.
- Se aceptan vídeos `.webm`.
- Si un vídeo no se puede reproducir ni convertir, se avisa en lugar de dejar el fondo vacío.
- Basilisco necesita ahora Windows 10 (versión 2004) o posterior.

## [0.3.2] - 2026-10-17

- Arreglado: con el mando, a veces el carrusel avanzaba dos puestos. Con Steam abierto, su configuración de escritorio convierte el mando en teclas (cruceta en flechas, A en Enter, B en Esc), y la misma pulsación llegaba dos veces: por el mando y como tecla. Ahora, si llegan las dos casi a la vez, solo cuenta una.

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
