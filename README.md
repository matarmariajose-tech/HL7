# Envío de inmunizaciones HL7

Consola en C# y .NET 8 que envía archivos VXU, correlaciona los ACK y registra todas las transacciones. Incluye un servicio HTTP local para demostrar aceptación, observaciones, rechazo e incertidumbre sin enviar datos a terceros.

SDK utilizado: **8.0.426**, fijado en `global.json`. Runtime: .NET 8. La demo automatizada requiere Python 3.10 o superior; la aplicación y los tests no requieren Python.

## Ejecución local

Desde la raíz del repositorio, con el SDK instalado:

```bash
dotnet restore Immunization.sln --locked-mode
dotnet build Immunization.sln -c Release --no-restore
dotnet test Immunization.sln -c Release --no-build
python3 scripts/demo.py
```

En Windows se puede usar `python` en lugar de `python3`. La demo inicia el simulador en el puerto 5080, genera ocho entradas sintéticas temporales, ejecuta la consola, verifica el informe y detiene el servidor. El puerto debe estar libre.

Resultado esperado de la demo: **2 Accepted, 1 Observed, 1 Rejected y 4 Unknown**. Son respuestas simuladas, no resultados del registro externo. La secuencia se configura en `demo/Immunization.Demo/appsettings.json`.

Para usar los ocho archivos originales, copiarlos a `samples/` sin alterar sus bytes:

```bash
python3 scripts/demo.py --input samples
```

Los archivos originales y los informes se excluyen de Git. La demo incluye un mensaje sintético independiente para poder ejecutarse después de clonar el repositorio.

## Ejecución manual de la demo

Terminal 1:

```bash
dotnet run --project demo/Immunization.Demo -c Release --no-build
```

Terminal 2:

```bash
dotnet run --project src/Immunization.Cli -c Release --no-build -- run config/demo.json samples reports/manual.jsonl
```

El archivo de informe debe ser nuevo. La consola ejecuta primero `echo` con las credenciales de transferencia y exige `echoBack = "echo"`; si falla, no envía mensajes. Este control valida el servicio de echo, no la aceptación de inmunizaciones.

Para la ejecución manual, definir las variables de transferencia con los datos sintéticos de `demo/Immunization.Demo/appsettings.json`. El script automatizado las configura exclusivamente para su proceso hijo.

## Servicio externo

El contrato se obtuvo del Swagger autenticado v1. La aplicación utiliza exclusivamente:

| Operación | Método y ruta |
| --- | --- |
| Echo | `POST /floridashot/api/v1/FloridaShots/echo` |
| Envío | `POST /floridashot/api/v1/FloridaShots/send` |

Swagger: https://test-prxcloud.praxisemr.com/floridashot/swagger/index.html

Una copia acotada del contrato de estas operaciones está en [swagger-contract.json](docs/swagger-contract.json). Los resultados de validación están en [validation.md](docs/validation.md).

1. Copiar `config/live.example.json` a `config/live.json`, archivo ignorado por Git.
2. Colocar los ocho originales en `samples/`.
3. Verificar conectividad HTTPS al host configurado.
4. Configurar las variables siguientes mediante el almacén de secretos o la sesión local. `.env.example` documenta nombres; la aplicación **no carga archivos `.env` automáticamente**.

| Variable | Uso |
| --- | --- |
| `REGISTRY_API_USER` | Usuario de HTTP Basic |
| `REGISTRY_API_PASSWORD` | Contraseña de HTTP Basic |
| `REGISTRY_CLINIC_OID` | Clínica para la solicitud de transferencia |
| `REGISTRY_TRANSFER_USER` | Usuario de transferencia |
| `REGISTRY_TRANSFER_PASSWORD` | Contraseña de transferencia |

Para una sesión Bash local se pueden ingresar sin dejar los valores en el historial:

```bash
read -r -p 'API user: ' REGISTRY_API_USER
read -r -s -p 'API password: ' REGISTRY_API_PASSWORD; printf '\n'
read -r -p 'Clinic OID: ' REGISTRY_CLINIC_OID
read -r -p 'Transfer user: ' REGISTRY_TRANSFER_USER
read -r -s -p 'Transfer password: ' REGISTRY_TRANSFER_PASSWORD; printf '\n'
export REGISTRY_API_USER REGISTRY_API_PASSWORD REGISTRY_CLINIC_OID REGISTRY_TRANSFER_USER REGISTRY_TRANSFER_PASSWORD
```

Una vez confirmado el contrato:

```bash
dotnet run --project src/Immunization.Cli -c Release --no-build -- echo config/live.json samples reports/unused.jsonl
dotnet run --project src/Immunization.Cli -c Release --no-build -- run config/live.json samples reports/live-01.jsonl
```

Un nuevo `run` vuelve a enviar los archivos. El programa no reintenta envíos automáticamente. Si hubo incertidumbre, reconciliar con el operador del registro antes de reenviar. No editar MSH-10 para forzar un reintento sin una decisión explícita sobre idempotencia.

### Contrato de transporte

El adaptador utiliza DTO tipados y nombres JSON explícitos del Swagger: `clinicOID`, `username`, `password`, `hl7`, `nameHl7` y `dateSend`. `hl7` es un arreglo de segmentos; `dateSend` se toma del reloj UTC. Los bytes del archivo original no se alteran. El sobre de respuesta contiene `messageId` y `result`; el segundo se interpreta como ACK, y el identificador del servicio se conserva para seguimiento.

Echo recibe `user` y `password` y valida `echoBack`. Tanto echo como send usan HTTP Basic además de las credenciales de transferencia en el cuerpo. No se concatenan manualmente documentos JSON ni se interpretan cadenas arbitrarias como ACK.

Las rutas, URL base, timeout y límites de tamaño son configuración. `TimeoutSeconds`, `MaxInputBytes` y `MaxResponseBytes` limitan tiempo y tamaño. HTTPS es obligatorio fuera de loopback. No se siguen redirecciones ni se desactiva la validación TLS. La configuración rechaza rutas ajenas a echo y send.

## Clasificación

Se usa exclusivamente la respuesta del servicio; no se deducen rechazos a partir de los datos de vacunación.

| Respuesta | Resultado |
| --- | --- |
| ACK correlacionado, MSA-1 `AA`, sin ERR | Accepted |
| `AA` con ERR de observación | Observed |
| `AE` o `AR` correlacionado | Rejected |
| `AA` con ERR severidad `E` o `F` | Unknown: respuesta contradictoria |
| `CA`, `CE`, `CR` | Unknown: acuse de commit, no aceptación de aplicación |
| MSA-2 distinto, ausente o múltiples MSA | Unknown |
| Error HTTP, timeout, fallo de transporte o cuerpo inválido | Unknown |

Los motivos de MSA-3 y los detalles ERR-2, ERR-3, ERR-4, ERR-7 y ERR-8 quedan en el informe restringido. La consola solo muestra identificadores internos y conteos. El parser soporta delimitadores declarados y separadores CR, LF y CRLF; es un lector del subconjunto de ACK utilizado, no un validador completo HL7 v2.5.1.

Los ocho originales comparten MSH-10. Cada archivo conserva ese valor y recibe un UUID de transacción distinto y un hash SHA-256. Los envíos son secuenciales: la respuesta HTTP y MSA-2 permiten correlacionar cada intento sin fusionar archivos con el mismo identificador clínico.

## Informe y recuperación

`reports/*.jsonl` contiene un evento `Pending` persistido **antes** del envío y un evento `Completed` con resultado, motivo, hallazgos y timestamps UTC. Cada línea es JSON independiente. El archivo se crea con acceso exclusivo de creación y permisos `0600` en Unix; los ACL de Windows dependen del directorio del usuario.

Además del diario JSONL, cada ejecución completa genera un informe HTML restringido en `<informe.jsonl>.html`, con archivo, resultado, motivo, hallazgos y trazabilidad. El contenido del servicio se escapa antes de generar HTML.

Para generar una vista de un diario existente sin conectarse a la API:

```bash
dotnet run --project src/Immunization.Cli -c Release --no-build -- report reports/live-01.jsonl reports/resumen.html
```

La lectura verifica que cada cierre corresponde al intento registrado. Un diario truncado genera un error explícito; no se descarta silenciosamente la evidencia. Los intentos pendientes aparecen como `Unknown · Pending`.

Un `Pending` sin `Completed` después de una interrupción indica entrega incierta. No implica que el registro no recibió el mensaje. No existe recuperación automática ni garantía de exactly-once. Los errores de persistencia detienen el lote para no continuar sin auditoría. Ctrl+C registra el intento activo y detiene los siguientes; los archivos que no comenzaron no se presentan como enviados.

Códigos de salida:

| Código | Significado |
| --- | --- |
| 0 | Todos aceptados, o echo exitoso |
| 1 | Lote completo con observaciones o rechazos, sin incertidumbre |
| 2 | Echo fallido, resultado desconocido o lote interrumpido |
| 3 | Error de ejecución/configuración/persistencia |
| 64 | Uso inválido |

La demo automatizada espera el código 2 del lote simulado y finaliza con 0 únicamente si valida todas sus comprobaciones.

## Arquitectura y validación

Ver [arquitectura](docs/architecture.md) y [evidencia de ejecución](docs/validation.md).

- `Domain`: resultados e interpretación de ACK, sin HTTP ni almacenamiento.
- `Application`: orquestación secuencial y puertos de transporte y auditoría.
- `Infrastructure`: HTTP, configuración, entrada de archivos y diario JSONL.
- `Cli`: composición y presentación de resultados.
- `Demo`: servidor HTTP independiente con escenarios configurables.
- `Tests`: pruebas de dominio, transporte, aislamiento de transacciones y persistencia.

Los lockfiles de NuGet se versionan. CI restaura en modo bloqueado, compila con advertencias como errores, ejecuta tests y demo. Los archivos clínicos originales y las credenciales no forman parte del repositorio.

## Contenedor opcional

```bash
docker build -t immunization-cli .
```

El contenedor ejecuta la CLI como usuario no privilegiado. Montar configuración y entradas en solo lectura y un directorio de informes escribible por ese usuario. Inyectar variables desde un almacén de secretos, sin incluirlas en la imagen. La construcción del contenedor es una alternativa de despliegue y se informa separadamente de las pruebas locales.

La imagen Docker todavía no fue validada en este entorno: el registro oficial redirige a `westus.data.mcr.microsoft.com` y la política de red rechazó esa descarga con HTTP 403. El flujo nativo de .NET está validado.
