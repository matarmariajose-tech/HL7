# Envío de Inmunizaciones HL7

Aplicación de consola en C# y .NET 8 para enviar mensajes VXU, interpretar los acuses del registro y consultar el resultado de cada transacción. Utiliza los endpoints `echo` y `send` de la API de pruebas.

## Ejecutar y probar

Requisitos: SDK **8.0.426**, fijado en `global.json`. La demo automatizada también requiere Python 3.10 o superior; la aplicación y las pruebas se ejecutan con .NET.

Desde la raíz del repositorio:

```bash
dotnet restore Immunization.sln --locked-mode
dotnet build Immunization.sln -c Release --no-restore
dotnet test Immunization.sln -c Release --no-build
python3 scripts/demo.py
```

La suite contiene **89 pruebas**. La demo inicia un servicio local en el puerto 5080, genera ocho mensajes sintéticos, verifica sus resultados y detiene el servicio. El resultado esperado es: **2 aceptados, 1 observado, 1 rechazado y 4 sin confirmación**. Los escenarios están definidos en `demo/Immunization.Demo/appsettings.json`.

Al finalizar se imprimen las rutas del diario JSONL y del informe HTML. El informe funciona sin servidor: incluye filtros, búsqueda y el detalle de cada intento. La demo aparece identificada como **Simulación local**.

## Usar la API de pruebas

El contrato v1 consultado está en [docs/swagger-contract.json](docs/swagger-contract.json).

| Operación | Endpoint |
| --- | --- |
| Echo | `POST /floridashot/api/v1/FloridaShots/echo` |
| Envío | `POST /floridashot/api/v1/FloridaShots/send` |

Preparar la configuración y copiar los ocho archivos originales a `samples/`:

```bash
cp config/live.example.json config/live.json
mkdir -p samples
```

Configurar estas variables en la sesión o mediante un gestor de secretos:

| Variable | Función |
| --- | --- |
| `REGISTRY_API_USER` | Usuario de HTTP Basic |
| `REGISTRY_API_PASSWORD` | Contraseña de HTTP Basic |
| `REGISTRY_CLINIC_OID` | Identificador de clínica en la solicitud de transferencia |
| `REGISTRY_TRANSFER_USER` | Usuario de transferencia |
| `REGISTRY_TRANSFER_PASSWORD` | Contraseña de transferencia |

`.env.example` enumera los nombres; la aplicación no carga archivos `.env` automáticamente. Las credenciales HTTP, las credenciales de transferencia y la identidad de la clínica son tipos separados. El OID de la solicitud no reemplaza los identificadores clínicos dentro del HL7.

Para enviar el lote:

```bash
dotnet run --project src/Immunization.Cli -c Release --no-build -- run config/live.json samples reports/envio-01.jsonl
```

La aplicación verifica primero `echoBack = "echo"`. Cada `run` realiza nuevos envíos; el nombre del informe debe ser nuevo. Ante un resultado incierto, consultar su estado con el operador del registro antes de reenviar.

## Interpretación de resultados

La clasificación depende de la respuesta del servicio. Un HTTP exitoso, por sí solo, no confirma la aceptación de una inmunización.

| Respuesta | Resultado |
| --- | --- |
| `AA` correlacionado, sin hallazgos ERR | Aceptado |
| `AA` con observaciones | Con observaciones |
| `AE` o `AR` correlacionado | Rechazado |
| Error de transporte, respuesta inválida o sin correlación | Sin confirmación |
| Acuse de commit (`CA`, `CE`, `CR`) o respuesta contradictoria | Sin confirmación |

MSA-2 debe coincidir con MSH-10. Los ocho originales comparten MSH-10: cada intento recibe un UUID propio y conserva el hash SHA-256 del archivo. El identificador devuelto por la API se registra por separado. No se deduplican archivos ni se modifican mensajes para obtener una aceptación.

## Auditoría y diagnóstico

Cada envío deja un evento `Pending` persistido antes de llamar al servicio y un `Completed` al registrar el resultado. El lector comprueba que el cierre corresponda al mismo intento, archivo, identificador y fecha de inicio. Si falla la persistencia, el lote se detiene.

Un intento sin cierre se muestra como **Sin confirmación**. No hay reintentos automáticos ni garantía de entrega exactamente una vez. Un timeout y una cancelación del usuario se registran como causas distintas.

Los informes conservan los motivos y hallazgos del registro. Las respuestas nuevas también dejan metadatos de diagnóstico: estado HTTP, tipo de contenido, tamaño, huella SHA-256 y formato aparente de `result`. Un error de contrato identifica el campo conocido que falló. Estos datos no incluyen el cuerpo completo de la respuesta ni permiten reconstruirlo.

Para generar otra vista de un diario existente, sin llamadas a la API:

```bash
dotnet run --project src/Immunization.Cli -c Release --no-build -- report reports/envio-01.jsonl reports/resumen.html
```

Los archivos originales, las credenciales y los informes se excluyen de Git. Los informes se crean con permisos `0600` en Unix. El HTML escapa los datos del servicio y conserva las fechas UTC originales junto con su presentación en la zona horaria configurada.

## Organización

| Proyecto | Responsabilidad |
| --- | --- |
| `Domain` | Resultados, motivos e interpretación de ACK |
| `Application` | Orquestación del lote y contratos de envío y auditoría |
| `Infrastructure` | Adaptador HTTP, identidades, archivos, diario e informes |
| `Cli` | Configuración y composición de los componentes |
| `Demo` | Simulador HTTP con escenarios reproducibles |
| `Tests` | Pruebas de dominio, contrato, fallos y persistencia |

La aplicación recibe fallos de entrega definidos por su puerto; las excepciones HTTP se traducen en infraestructura. El cliente HTTP se reutiliza durante el lote y un único plazo cubre el envío y la lectura de cada respuesta. HTTPS es obligatorio fuera del simulador local y las redirecciones están deshabilitadas.

El procesamiento es secuencial y trabaja con lotes acotados. La configuración permite ajustar el máximo de archivos, bytes por archivo y lote, tamaño de respuesta y timeout. Los valores predeterminados son 1 MiB por archivo, 32 MiB de entrada por lote, 1000 archivos, 1 MiB por respuesta y 30 segundos por solicitud. La demo utiliza un timeout menor. La entrada completa se valida antes de iniciar los envíos; el límite también se comprueba durante la lectura.

Para un volumen sostenido, el siguiente cambio sería lectura por flujo y almacenamiento transaccional detrás del puerto de auditoría. Concurrencia y reintentos requieren acordar primero idempotencia con el proveedor.

## Validación y alcance

Se ejecutó un lote real de ocho mensajes: **5 rechazos `AE` y 3 resultados sin confirmación**. En esos tres casos no pudo interpretarse un ACK válido. La versión utilizada en aquel envío no conservaba los metadatos de respuesta incorporados después; su causa exacta no puede reconstruirse desde el diario. No se repitieron esos envíos.

Las pruebas y la demo se ejecutan en CI. El historial de ejecución y los límites conocidos están en [docs/validation.md](docs/validation.md); las decisiones de diseño, en [docs/architecture.md](docs/architecture.md). El lector implementa el subconjunto de ACK necesario para este flujo, no una validación completa de HL7 v2.5.1.

Códigos de salida: `0` aceptados o echo correcto; `1` observaciones o rechazos; `2` resultados inciertos, interrupción o echo fallido; `3` error de ejecución; `64` argumentos inválidos. La demo valida deliberadamente casos inciertos y termina con `0` si sus comprobaciones pasan.

El Dockerfile es opcional. Su construcción quedó bloqueada por HTTP 403 en una descarga del registro oficial; la imagen todavía no está validada. El flujo nativo con .NET está verificado.
