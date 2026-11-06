# Kensaku para PowerToys Command Palette

Extensión de [PowerToys Command Palette](https://learn.microsoft.com/windows/powertoys/command-palette/) que usa [Kensaku](https://github.com/yjid/kensaku) como backend de búsqueda local: archivos, contenido de documentos y capturas (OCR), con el lenguaje de consultas de Kensaku.

La idea es la misma que [EverythingCommandPalette](https://github.com/lin-ycv/EverythingCommandPalette), pero con Kensaku en lugar de Everything.

## Cómo funciona

- La extensión habla con `kensaku-cli serve` por HTTP (solo loopback, `127.0.0.1`, con token Bearer).
- Si no hay un servidor escuchando en el puerto (por defecto `8765`), la extensión lanza `kensaku-cli serve` oculto y lo mantiene vivo mientras corre. Si ya tienes uno corriendo, lo reutiliza.
- Se usa el servidor HTTP y no `kensaku-cli search` por consulta, porque cada invocación del CLI tiene que arrancar el proceso y abrir el índice (con un índice grande, del orden de 1,6 s), mientras que el servidor residente responde en una fracción de ese tiempo.
- La búsqueda se lanza con un debounce de 300 ms y se cancela si llega una consulta más nueva.
- El token se lee de `%APPDATA%\kensaku\http.token`.

## Requisitos

- Windows 11.
- [PowerToys](https://github.com/microsoft/PowerToys) con Command Palette instalado.
- [Kensaku](https://github.com/yjid/kensaku) instalado, con el índice ya creado (abre la app al menos una vez) y `kensaku-cli` disponible.
- Modo desarrollador de Windows activado (Configuración → Para programadores).
- Visual Studio con las cargas de trabajo de WinUI y Windows App SDK (para compilar y desplegar).

## Compilar y desplegar

1. Abre la solución (`.sln`) en Visual Studio.
2. Selecciona la configuración **Debug** y la plataforma **x64** (o ARM64 según tu equipo).
3. Menú **Build → Deploy KensakuCmdPal**.

> **Importante:** hay que usar *Deploy*, no solo *Build*. Compilar no actualiza el paquete instalado. Tampoco sirve ejecutar la variante "(Unpackaged)" desde Visual Studio, porque no despliega el paquete.

Cada vez que cambies el código, vuelve a compilar y desplegar.

## Recargar en Command Palette

Command Palette no detecta los paquetes que se vuelven a desplegar desde Visual Studio, así que tras cada despliegue hay que recargar manualmente:

1. Abre Command Palette (`Alt+Space` por defecto).
2. Ejecuta `Reload` y elige la opción cuyo subtítulo dice **Reload Command Palette Extension** (hay varias opciones de Reload; no elijas otra).
3. Busca **Kensaku** en la lista de comandos y entra.

## Uso

Escribe en la caja de búsqueda; los resultados se actualizan mientras escribes. Se admite el lenguaje de consultas de Kensaku, por ejemplo:

```
2026 forecast
type:pdf contrato
modified:<7d informe
```

Para cada resultado:

- **Enter:** abre el archivo o carpeta.
- **Menú de acciones:** mostrar en carpeta y copiar la ruta.
- **Panel de detalles:** ruta completa, fecha de modificación, tamaño y, cuando el servidor lo entrega, el fragmento que coincidió.
- **Etiqueta:** tipo de coincidencia (`Name`, `Content`, `Both`…).

La primera búsqueda tarda más, porque se carga el índice (y se arranca el servidor si no estaba corriendo). Las siguientes son más rápidas.

## Configuración

Por ahora la configuración está en el código y en variables de entorno:

| Qué | Dónde | Por defecto |
|---|---|---|
| Ruta de `kensaku-cli` | Variable de usuario `KENSAKU_CLI` | `kensaku-cli` (debe estar en el PATH) |
| Puerto del servidor | `KensakuClient.cs` (`_port`) | `8765` |
| Cantidad de resultados | `KensakuSearchPage.cs` (`Limit`) | `30` |
| Debounce | `KensakuSearchPage.cs` (`DebounceMs`) | `300` ms |

Si defines o cambias `KENSAKU_CLI`, cierra sesión y vuelve a entrar (o reinicia PowerToys) para que el proceso de la extensión la vea.

## Solución de problemas

- **La extensión no aparece:** confirma que desplegaste (no solo compilaste), que el modo desarrollador está activo y repite el Reload.
- **Aparece el item "No se pudo consultar Kensaku":** el subtítulo trae el motivo.
  - Si no encuentra `kensaku-cli`: el proceso empaquetado a veces no ve el mismo PATH que tu terminal. Obtén la ruta con `where.exe kensaku-cli` y defínela en `KENSAKU_CLI`.
  - Si `kensaku-cli serve` termina con código 3: no hay índice. Abre la app Kensaku una vez para crearlo.
  - Si hay un 401 persistente: el token de `%APPDATA%\kensaku\http.token` no coincide con el del servidor que está corriendo. Cierra ese servidor y deja que la extensión lo lance de nuevo.
- **Depurar:** con el proyecto en Debug, inicia con F5 y abre la ventana Output (`Ctrl+Alt+O`) para ver los mensajes de depuración.
- **Probar el servidor a mano** (PowerShell; reemplaza `<TOKEN>` con el contenido de `http.token`):

  ```powershell
  kensaku-cli serve --port 8765
  ```

  ```powershell
  $h = @{ Authorization = "Bearer <TOKEN>" }
  Invoke-RestMethod http://127.0.0.1:8765/health -Headers $h
  Invoke-RestMethod "http://127.0.0.1:8765/search?q=2026%20Forecast&limit=3" -Headers $h
  ```

  Nota: en PowerShell 5.1, `Invoke-RestMethod` puede mostrar mal las tildes (`ImputaciÃ³n`) si la respuesta no declara `charset`. La extensión lee los bytes como UTF-8 y no se ve afectada.

## Formato de la respuesta de `/search`

La extensión espera este JSON (es distinto del de `kensaku-cli search --json`):

```json
{
  "as_of": 1793928069,
  "mode": "ranked",
  "results": [
    {
      "match_type": "Both",
      "modified": 1771507347,
      "path": "C:\\Users\\...\\archivo.pptx",
      "score": 0.94,
      "size": 13631379,
      "snippet": null
    }
  ],
  "returned": 2,
  "total": 8
}
```

El servidor HTTP no entrega `filename`, `is_dir`, `group_count` ni `group_paths`; el nombre se calcula desde la ruta.

## Estructura

- `KensakuClient.cs`: cliente HTTP, ciclo de vida del proceso `kensaku-cli serve`, lectura del token y deserialización (con source generation, compatible con AOT).
- `KensakuSearchPage.cs`: página de búsqueda dinámica (`DynamicListPage`) y comando "Mostrar en carpeta".
- El resto (manifiesto `Package.appxmanifest`, `Program.cs`, provider de comandos) viene de la plantilla de Command Palette.

## Cómo se creó este proyecto

1. Se generó el esqueleto con el comando **Create a new extension** de Command Palette (ExtensionName `KensakuCmdPal`, display name `Kensaku`) y se abrió el `.sln` en Visual Studio.
2. Se agregaron `KensakuClient.cs` y `KensakuSearchPage.cs` al proyecto (Add → Existing Item), ajustando el `namespace` si hacía falta.
3. En el provider de comandos se reemplazó la página de ejemplo por la nuestra:

   ```csharp
   _commands = [
       new CommandItem(new KensakuSearchPage())
       {
           Title = "Kensaku",
           Subtitle = "Buscar archivos, contenido y capturas",
       },
   ];
   ```

4. Se compiló y desplegó con **Build → Deploy**, y se recargó Command Palette.

## Licencia

Por definir. Si copias código de otros proyectos (por ejemplo de EverythingCommandPalette, que es EPL-2.0), revisa sus condiciones. Kensaku es MIT.
