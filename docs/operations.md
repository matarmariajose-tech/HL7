# Operación

## Configuración

`config/live.example.json` contiene las rutas del contrato v1. La copia local `config/live.json` se excluye de Git. Las credenciales se leen del entorno; no deben incorporarse a ese archivo.

| Parámetro | Valor predeterminado | Función |
| --- | --- | --- |
| `TimeoutSeconds` | 30 | Plazo compartido por envío y lectura de la respuesta |
| `MaxInputBytes` | 1 MiB | Límite por archivo de entrada |
| `MaxBatchBytes` | 32 MiB | Límite de bytes de entrada del lote |
| `MaxFiles` | 1000 | Cantidad máxima de archivos |
| `MaxResponseBytes` | 1 MiB | Límite por respuesta HTTP |
| `EnvironmentName` | Servicio externo | Nombre presentado en el informe |
| `IsSimulation` | `false` | Identificación explícita de una simulación |
| `ReportTimeZoneId` | `America/Argentina/Buenos_Aires` | Zona horaria de presentación |

La demo utiliza un timeout de dos segundos. Los límites de entrada se verifican antes del primer envío y durante la lectura de los archivos. El lote permanece en memoria: estos presupuestos limitan la entrada, no el consumo total del proceso.

El transporte exige HTTPS fuera de loopback, conserva la verificación TLS y no sigue redirecciones. Las rutas configuradas deben corresponder a `echo` y `send`.

## Contrato HTTP

| Operación | Método y ruta |
| --- | --- |
| Echo | `POST /floridashot/api/v1/FloridaShots/echo` |
| Envío | `POST /floridashot/api/v1/FloridaShots/send` |

Echo recibe `user` y `password` de transferencia y exige `echoBack = "echo"` como respuesta. Send recibe `clinicOID`, `username`, `password`, `hl7`, `nameHl7` y `dateSend`.

`hl7` es un arreglo de segmentos construido a partir del archivo UTF-8; el archivo original no se modifica. La fecha de envío se obtiene del reloj UTC. El sobre de respuesta contiene `messageId` y `result`, que se interpreta como ACK. Ambos endpoints utilizan además autenticación HTTP Basic.

## Informes

Cada ejecución crea un diario JSONL y un informe HTML en `<diario.jsonl>.html`. Los nombres de salida deben ser nuevos: no se sobrescriben informes existentes.

Para generar una nueva vista de un diario, sin volver a enviar mensajes:

```bash
dotnet run --project src/Immunization.Cli -c Release --no-build -- \
  report reports/envio-01.jsonl reports/resumen.html
```

El HTML incluye búsqueda, filtros, detalles desplegables e impresión. Sus recursos están embebidos y no realizan solicitudes externas. El contenido del servicio se escapa antes de renderizarlo. Las fechas UTC originales se conservan junto con la hora de presentación.

Los informes se crean con permisos `0600` en Unix. En Windows, el acceso depende de los ACL del directorio. Pueden contener identificadores y mensajes sensibles del registro; requieren almacenamiento restringido y una política de retención definida por el operador.

## Interrupciones y recuperación

El diario comienza con el contexto del lote. Luego registra `Pending` antes de cada envío y `Completed` al persistir su resultado. El lector comprueba identidad, archivo, hash, identificador de mensaje, fecha de inicio y estado de cada cierre.

Un `Pending` sin cierre indica entrega incierta. Ctrl+C registra el resultado desconocido del intento activo y detiene los siguientes. Si la cancelación ocurrió antes del primer intento, no se realiza ningún envío. Los errores de persistencia y los defectos inesperados detienen la ejecución.

Los diarios truncados o inconsistentes producen un error explícito. Los formatos anteriores siguen siendo legibles; cuando no incluyen contexto, el informe muestra «Origen no registrado».

No existe una transacción distribuida entre el registro y el archivo de auditoría. No hay garantía de entrega exactamente una vez ni recuperación automática. Antes de repetir un resultado incierto, reconciliarlo con el operador del registro mediante sus identificadores.

## Evidencia de respuesta

Las respuestas nuevas conservan estado HTTP, tipo declarado, tamaño y hash SHA-256 del cuerpo. Si el sobre se pudo interpretar, también se registra longitud, huella y formato aparente de `result`. Un error de contrato identifica únicamente un campo conocido.

Estos metadatos ayudan a diferenciar un resultado nulo, vacío, texto, marcado o un posible mensaje HL7. Su forma aparente no determina la clasificación. No se guarda el cuerpo completo ni se reconstruyen respuestas descartadas en ejecuciones anteriores.

## Códigos de salida

| Código | Significado |
| --- | --- |
| `0` | Todos los mensajes aceptados, o echo correcto |
| `1` | Lote completo con observaciones o rechazos, sin incertidumbre |
| `2` | Echo fallido, resultado desconocido o lote interrumpido |
| `3` | Error de ejecución, configuración o persistencia |
| `64` | Argumentos inválidos |

La demo espera el código `2` del lote simulado y devuelve `0` si comprueba todos los resultados previstos.

## Contenedor opcional

```bash
docker build -t immunization-cli .
```

La imagen utiliza un usuario no privilegiado. Requiere montar configuración y entradas en solo lectura, un directorio de informes escribible por ese usuario e inyección de credenciales en ejecución.

Su construcción no pudo completarse en el entorno de validación: la descarga oficial desde `westus.data.mcr.microsoft.com` recibió HTTP 403. La imagen no se presenta como validada; el flujo verificado es la ejecución nativa con .NET.
