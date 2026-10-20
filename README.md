# Envío de vacunaciones HL7

Aplicación de consola en C# y .NET 8 que envía un lote de mensajes VXU a la API de pruebas y genera un informe por archivo: aceptado, observado, rechazado o sin confirmación. La clasificación se obtiene de la respuesta del servicio.

## Probar localmente

Requisitos: **SDK .NET 8.0.426**, fijado en `global.json`. La demo automatizada requiere además **Python 3.10+** y el puerto **5080** disponible.

Desde la raíz del repositorio:

```bash
dotnet restore Immunization.sln --locked-mode
dotnet build Immunization.sln -c Release --no-restore
dotnet test Immunization.sln -c Release --no-build
python3 scripts/demo.py
```

La suite contiene **89 pruebas**. La demo levanta el simulador, procesa ocho mensajes sintéticos, comprueba los resultados y detiene el servicio. No requiere credenciales de la API externa.

El resultado esperado es **2 aceptados, 1 observado, 1 rechazado y 4 sin confirmación**. El script termina con `DEMO VERIFIED` e imprime la ubicación de los informes:

- **JSONL:** diario de eventos para seguimiento y recuperación.
- **HTML:** resumen del lote, filtros por resultado y detalle de cada transacción. Se abre directamente en el navegador.

El informe de la demo está identificado como **Simulación local**. Sus escenarios se configuran en `demo/Immunization.Demo/appsettings.json`.

## Enviar los ocho originales

1. Copiar los archivos recibidos a `samples/`, sin modificarlos.
2. Preparar la configuración:

   ```bash
   cp config/live.example.json config/live.json
   ```

3. Definir estas variables en el entorno del proceso:

   | Variable | Uso |
   | --- | --- |
   | `REGISTRY_API_USER` | Usuario de HTTP Basic |
   | `REGISTRY_API_PASSWORD` | Contraseña de HTTP Basic |
   | `REGISTRY_CLINIC_OID` | OID de la clínica |
   | `REGISTRY_TRANSFER_USER` | Usuario de transferencia HL7 |
   | `REGISTRY_TRANSFER_PASSWORD` | Contraseña de transferencia HL7 |

   `.env.example` documenta los nombres. La aplicación no carga archivos `.env` automáticamente.

4. Ejecutar el lote con un nombre de informe nuevo:

   ```bash
   dotnet run --project src/Immunization.Cli -c Release --no-build -- \
     run config/live.json samples reports/envio-01.jsonl
   ```

Antes de enviar, la aplicación comprueba `echoBack = "echo"`. Utiliza exclusivamente `echo` y `send` del contrato v1, cuya copia está en [docs/swagger-contract.json](docs/swagger-contract.json).

Cada ejecución de `run` realiza nuevos envíos. Los resultados sin confirmación requieren consultar al operador del registro antes de repetirlos.

## Cómo se determina el resultado

MSA-2 debe coincidir con MSH-10. Un HTTP exitoso no basta para afirmar que una inmunización fue aceptada.

| Respuesta correlacionada | Resultado |
| --- | --- |
| `AA` sin hallazgos ERR | Aceptado |
| `AA` con observaciones | Con observaciones |
| `AE` o `AR` | Rechazado |
| Acuse de commit, respuesta contradictoria, inválida o sin correlación | Sin confirmación |
| Error HTTP, timeout o interrupción de la comunicación | Sin confirmación |

Los ocho originales comparten MSH-10. Cada intento tiene un UUID propio y el hash SHA-256 del archivo; el identificador de la API se registra aparte. Los mensajes se conservan sin cambios y no se deduplican por nombre, identificador o contenido.

## Decisiones de implementación

- **Capas:** el dominio interpreta ACK; la aplicación coordina el lote; infraestructura implementa HTTP, archivos y auditoría. La consola compone los componentes.
- **Identidades separadas:** HTTP Basic, transferencia y clínica tienen tipos propios. El OID de la solicitud no reemplaza los identificadores clínicos del HL7.
- **Auditoría antes del envío:** cada intento se persiste como `Pending` y se cierra con `Completed`. Un fallo de persistencia detiene el lote.
- **Procesamiento secuencial:** adecuado para ocho documentos, con límites configurables de cantidad, tamaño y tiempo. No hay reintentos automáticos que puedan duplicar una inmunización.
- **Diagnóstico restringido:** se conservan motivos, hallazgos y metadatos de respuesta. Los originales, las credenciales y los informes quedan fuera de Git.

El lector cubre el subconjunto de ACK utilizado en este flujo. No implementa una validación clínica completa de HL7 v2.5.1.

## Validación realizada

El lote enviado a la API externa produjo **5 rechazos `AE` y 3 resultados sin confirmación**. No hubo aceptaciones verificadas. Los tres casos inciertos no devolvieron un ACK interpretable y no se reenviaron. El diario de aquella ejecución no permite reconstruir el cuerpo de las respuestas.

La suite y la demo se ejecutan en GitHub Actions. La evidencia, el alcance de las comprobaciones y las limitaciones están documentados en:

- [Validación y resultados](docs/validation.md)
- [Arquitectura y decisiones](docs/architecture.md)
- [Configuración, informes y recuperación](docs/operations.md)
