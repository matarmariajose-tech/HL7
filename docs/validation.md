# Evidencia de validación

Validación realizada el 20 de octubre de 2026 en Linux x64, con SDK .NET 8.0.426 y runtime 8.0.32.

## Compilación y pruebas

- Restauración NuGet en modo `--locked-mode`: correcta.
- Compilación Release de los seis proyectos: cero errores y cero advertencias.
- Suite xUnit: **63 pruebas aprobadas, 0 fallidas, 0 omitidas**.
- Demo HTTP autocontenida: ocho transacciones verificadas y finalización correcta del servidor.
- Demo con los ocho originales: ocho transacciones verificadas; los hashes de los archivos no cambiaron.

Las pruebas incluyen correlación, delimitadores, clasificación de ACK, contradicciones, errores HTTP, timeout sin reintentos, contrato JSON real, límites de respuesta, aislamiento de IDs repetidos, auditoría previa al envío, cancelación, permisos, escape HTML y recuperación de diarios incompletos.

## Informe de resultados

El informe se rediseñó con tabla de resultados, filtros, búsqueda y auditoría desplegable. Se verificaron la localización de diagnósticos internos, los permisos del archivo y el escape de contenido del servicio. El contexto de simulación sobrevive a la lectura del diario. La revisión con Chromium comprobó ocho filas, filtros, búsqueda, expansión del detalle, estado vacío y ausencia de solicitudes externas. Los registros anteriores mantienen compatibilidad sin inventar su origen.

La demo genera mensajes sintéticos con identificadores propios. Este cambio no modifica los ocho archivos originales ni reejecuta el lote externo documentado a continuación.

## API externa real

Se consultó el Swagger autenticado y se verificó `echoBack = "echo"` usando únicamente el endpoint echo. Luego se ejecutó **un lote real de ocho envíos**, con una llamada a send por archivo y sin reintentos.

| Resultado | Cantidad |
| --- | ---: |
| Accepted | 0 |
| Observed | 0 |
| Rejected | 5 |
| Unknown | 3 |
| Total completado | 8 |

Los cinco rechazos contienen MSA-1 `AE`, correlación MSA-2 y hallazgos ERR del servicio. Los tres Unknown contienen un identificador del servicio, pero su campo `result` no pudo interpretarse como un ACK HL7 válido. No se afirma que estos últimos hayan sido rechazados o aceptados.

Los resultados no se deducen de los nombres de los archivos ni de su contenido clínico. La falta de aceptaciones en este lote no se oculta ni se sustituye por los resultados del simulador.

Los motivos detallados, localización de los campos y trazabilidad de cada intento se conservan en el diario restringido y su informe HTML bajo `reports/`. Se excluyen de Git junto con los archivos originales. No se guardaron las credenciales en archivos de configuración ni en el historial Git.

## Infraestructura opcional

Se comprobó la disponibilidad del motor Docker. La construcción de la imagen no pudo completarse porque la descarga oficial desde `westus.data.mcr.microsoft.com` fue rechazada con HTTP 403. El Dockerfile se entrega como alternativa de despliegue **sin afirmar que su imagen esté validada**. Este bloqueo no afecta la aplicación, los tests ni las demos ejecutados directamente con .NET.

La ejecución de CI en GitHub y la restauración de este entorno en una tarea nueva son verificaciones independientes; no se presentan como realizadas por estas pruebas locales.
