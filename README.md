# Kensaku for PowerToys Command Palette

**English** · [Español](README.es.md)

[![Build](https://github.com/rhurta/kensaku-command-palette/actions/workflows/build.yml/badge.svg)](https://github.com/rhurta/kensaku-command-palette/actions/workflows/build.yml) [![Release](https://img.shields.io/github/v/release/rhurta/kensaku-command-palette)](https://github.com/rhurta/kensaku-command-palette/releases/latest) [![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

A [PowerToys Command Palette](https://learn.microsoft.com/windows/powertoys/command-palette/) extension that uses [Kensaku](https://github.com/yjid/kensaku) as a local search backend: files, document contents and screenshots (OCR), with Kensaku's query language.

The idea is the same as [EverythingCommandPalette](https://github.com/lin-ycv/EverythingCommandPalette), but powered by Kensaku instead of Everything.

## How it works

- The extension talks to `kensaku-cli serve` over HTTP (loopback only, `127.0.0.1`, Bearer token).
- If nothing is listening on the port (default `8765`), the extension launches `kensaku-cli serve` hidden and keeps it alive while it runs. If you already have one running, it reuses it.
- It uses the HTTP server instead of calling `kensaku-cli search` per query because every CLI invocation has to start a process and open the index (about 1.6 s with a large index), whereas the resident server answers in a fraction of that.
- Searches are debounced (300 ms) and cancelled when a newer query arrives.
- The token is read from `%APPDATA%\kensaku\http.token`.

## Requirements

- Windows 11.
- [PowerToys](https://github.com/microsoft/PowerToys) with Command Palette installed.
- [Kensaku](https://github.com/yjid/kensaku) installed, with its index already built (open the app at least once) and `kensaku-cli` available.

To build from source you also need:

- Visual Studio with the WinUI and Windows App SDK workloads, and the .NET 10 SDK.
- Windows Developer Mode enabled (Settings → For developers), to deploy from Visual Studio.

## Install

The easiest way is to install the signed package from the [latest release](https://github.com/rhurta/kensaku-command-palette/releases/latest).

1. Download the `.msix` and `kensaku.cer` files from the release.
2. Trust the certificate. The package is signed with a self-signed certificate (`CN=rhurta`), so Windows must trust it first. In an **elevated** PowerShell:

   ```powershell
   Import-Certificate -FilePath .\kensaku.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
   ```

3. Install the package (double-click the `.msix`, or run):

   ```powershell
   Add-AppxPackage .\<downloaded-file>.msix
   ```

4. Open Command Palette, run `Reload` (the entry whose subtitle says **Reload Command Palette Extension**) and look for **Kensaku**.

If you previously deployed the extension from Visual Studio, remove that copy first (`Get-AppxPackage *Kensaku* | Remove-AppxPackage`) to avoid a package conflict. To uninstall, use the same command.

## Build and deploy

1. Open the solution (`.sln`) in Visual Studio.
2. Select the **Debug** configuration and the **x64** platform (or ARM64 depending on your machine).
3. Menu **Build → Deploy KensakuCmdPal**.

> **Important:** you must use *Deploy*, not just *Build*. Building alone does not update the installed package. Running the "(Unpackaged)" variant from Visual Studio doesn't work either, because it doesn't deploy the package.

Rebuild and redeploy every time you change the code.

## Reload in Command Palette

Command Palette does not detect packages redeployed from Visual Studio, so you must reload it manually after each deployment:

1. Open Command Palette (`Alt+Space` by default).
2. Run `Reload` and pick the entry whose subtitle says **Reload Command Palette Extension** (there are several Reload entries; don't pick another one).
3. Find **Kensaku** in the command list and open it.

## Usage

Type in the search box; results update as you type. Kensaku's query language is supported, for example:

```
2026 forecast
type:pdf contract
modified:<7d report
```

For each result:

- **Enter:** open the file or folder.
- **Actions menu:** show in folder and copy the path.
- **Details pane:** full path, modification date, size and, when the server provides it, the matching snippet.
- **Tag:** match type (name, content, or both).

The first search is slower because the index is loaded (and the server is started if it wasn't running). Later searches are faster.

## Languages (i18n)

The default UI language is **English**. All user-visible text lives in `.resx` resource files:

- `Strings/Strings.resx`: English (neutral / fallback language).
- `Strings/Strings.es.resx`: Spanish.

The extension follows the Windows display language; if there is no translation for it, English is used. Dates and numbers follow the user's regional format.

**Adding a language:**

1. Copy `Strings/Strings.resx` to `Strings/Strings.<culture>.resx` (for example `Strings.fr.resx` or `Strings.pt-BR.resx`) and translate only the `<value>` elements; don't change the `name` attributes.
2. In `Package.appxmanifest`, inside `<Resources>`, declare the language: `<Resource Language="fr"/>`. If the template ships with `x-generate`, replace it with the explicit list (`en-US`, `es`, `fr`…). Windows uses this list to decide which language the packaged process runs in; if it's missing, the extension may show English even though the translation exists.
3. Redeploy and reload Command Palette.

**Testing a language without changing Windows:** set the user environment variable `KENSAKU_LANG` (for example `es`), then sign out and back in (or restart PowerToys).

**Adding new text:** add the key to `Strings.resx` (English) and to each translation, then use it with `Loc.Get("Key")` or `Loc.Format("Key", args)`. If a key is missing from a translation, the English text is used; if it's missing everywhere, the key name is shown.

## Configuration

For now, configuration lives in code and environment variables:

| What | Where | Default |
|---|---|---|
| Path to `kensaku-cli` | `KENSAKU_CLI` user variable | `kensaku-cli` (must be on the PATH) |
| UI language | `KENSAKU_LANG` user variable (for testing only, e.g. `es`) | Windows display language |
| Server port | `KensakuClient.cs` (`_port`) | `8765` |
| Number of results | `KensakuSearchPage.cs` (`Limit`) | `30` |
| Debounce | `KensakuSearchPage.cs` (`DebounceMs`) | `300` ms |

If you set or change `KENSAKU_CLI`, sign out and back in (or restart PowerToys) so the extension process picks it up.

## Troubleshooting

- **The extension doesn't show up:** make sure you deployed (not just built), Developer Mode is on, and repeat the Reload.
- **The extension appears twice in Command Palette:** one row is the extension command, the other is the MSIX listed as a normal app. `AppListEntry="none"` on `uap:VisualElements` (already set) hides the app entry; redeploy and run `Reload` after changing it.
- **A "Could not query Kensaku" item appears:** its subtitle contains the reason.
  - If it can't find `kensaku-cli`: the packaged process sometimes doesn't see the same PATH as your terminal. Get the path with `where.exe kensaku-cli` and set it in `KENSAKU_CLI`.
  - If `kensaku-cli serve` exits with code 3: there is no index. Open the Kensaku app once to build it.
  - If you get a persistent 401: the token in `%APPDATA%\kensaku\http.token` doesn't match the running server's. Stop that server and let the extension launch a new one.
- **Debugging:** with the project in Debug, start with F5 and open the Output window (`Ctrl+Alt+O`) to see debug messages.
- **Testing the server by hand** (PowerShell; replace `<TOKEN>` with the contents of `http.token`):

  ```powershell
  kensaku-cli serve --port 8765
  ```

  ```powershell
  $h = @{ Authorization = "Bearer <TOKEN>" }
  Invoke-RestMethod http://127.0.0.1:8765/health -Headers $h
  Invoke-RestMethod "http://127.0.0.1:8765/search?q=2026%20Forecast&limit=3" -Headers $h
  ```

  Note: in PowerShell 5.1, `Invoke-RestMethod` may display accents incorrectly (`ImputaciÃ³n`) if the response doesn't declare a `charset`. The extension reads the bytes as UTF-8 and isn't affected.

## `/search` response format

The extension expects this JSON (it differs from `kensaku-cli search --json`):

```json
{
  "as_of": 1793928069,
  "mode": "ranked",
  "results": [
    {
      "match_type": "Both",
      "modified": 1771507347,
      "path": "C:\\Users\\...\\file.pptx",
      "score": 0.94,
      "size": 13631379,
      "snippet": null
    }
  ],
  "returned": 2,
  "total": 8
}
```

The HTTP server doesn't return `filename`, `is_dir`, `group_count` or `group_paths`; the file name is derived from the path.

## CI and releases

Two GitHub Actions workflows live in `.github/workflows/`:

- **`build.yml`** runs on every push to `main` and on pull requests. It builds an **unsigned** x64 MSIX on a Windows runner and uploads it as an artifact. Unsigned packages can't be installed; this run just checks that the project compiles and packages.
- **`release.yml`** runs when you push a tag like `v0.2.0`. It sets the package version from the tag (`v0.2.0` becomes `0.2.0.0`), builds a **signed** MSIX and creates a GitHub release with the `.msix` and `kensaku.cer` attached.

**Publishing a release**

```powershell
git tag v0.2.0
git push origin v0.2.0
gh run watch
```

**Signing setup (one time)**

- The workflow needs two repository secrets: `CERT_PFX_BASE64` (the `.pfx` encoded as Base64) and `CERT_PASSWORD` (its password). Create them with `gh secret set`.
- The certificate subject must match `Publisher` in `KensakuCmdPal/Package.appxmanifest` (currently `CN=rhurta`), or Windows won't install the package.
- The public certificate `kensaku.cer` is committed at the repo root and attached to each release. The private key (`.pfx`) must never be committed.
- The root `kensaku.cer` is the one to trust; copies in `rel/` (created by `gh release download`) are generated artifacts and are gitignored.
- Self-signed certificates are for sideloading from GitHub. Distribution through the Microsoft Store uses a different identity and the Store signs the package; the template's `.github/skills/publish-extension/` notes describe the Store and WinGet routes.

**Checking a release**

```powershell
gh release download v0.2.0 --dir .\rel
Get-AuthenticodeSignature (Get-ChildItem .\rel -Filter *.msix).FullName | Format-List SignerCertificate
```

The signer's thumbprint should match the one printed by `(Get-PfxCertificate .\kensaku.cer).Thumbprint`.

## Project layout

- `KensakuCmdPal/Pages/KensakuClient.cs`: HTTP client, `kensaku-cli serve` process lifecycle, token handling and deserialization (source-generated, AOT-friendly).
- `KensakuCmdPal/Pages/KensakuSearchPage.cs`: dynamic search page (`DynamicListPage`) and the "Show in folder" command.
- `KensakuCmdPal/Pages/Loc.cs`: localization helper on top of `.resx` resources.
- `KensakuCmdPal/Strings/Strings.resx`: English texts (default language). `Strings.<culture>.resx` next to it: translations (currently `es`).
- `KensakuCmdPal/Assets/`: extension icon (a magnifying glass) and the MSIX logo assets.
- `.github/workflows/`: CI and release workflows.
- `LICENSE`: MIT license text.
- Everything else (`Package.appxmanifest`, `Program.cs`, the command provider) comes from the Command Palette template.

## How this project was created

1. The skeleton was generated with the **Create a new extension** command in Command Palette (ExtensionName `KensakuCmdPal`, display name `Kensaku`) and the `.sln` was opened in Visual Studio.
2. `KensakuClient.cs`, `KensakuSearchPage.cs`, `Loc.cs` and the `Strings/` folder were added to the project (Add → Existing Item), adjusting the `namespace` if needed.
3. In the command provider, the template's sample page was replaced with ours:

   ```csharp
   _commands = [
       new CommandItem(new KensakuSearchPage())
       {
           Title = "Kensaku",
           Subtitle = Loc.Get("ProviderSubtitle"),
       },
   ];
   ```

4. It was built and deployed with **Build → Deploy**, and Command Palette was reloaded.

## License

MIT — see [LICENSE](LICENSE).

- Files generated from the Command Palette extension template (`Program.cs`, `KensakuCmdPal.cs`, `KensakuCmdPalCommandsProvider.cs`, `Helpers/SettingsManager.cs`) keep their original `Copyright (c) Microsoft Corporation` MIT headers; the same MIT terms apply to them.
- [Kensaku](https://github.com/yjid/kensaku) is a separate MIT-licensed project. This extension only talks to its local HTTP server; no Kensaku source is vendored here.
- [EverythingCommandPalette](https://github.com/lin-ycv/EverythingCommandPalette) (EPL-2.0) was an inspiration only — no code was copied from it.
- NuGet dependencies and the Windows/MSIX SDK tooling are distributed under their own licenses.
