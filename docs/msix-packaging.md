# MSIX packaging & template storage

How the WinUI 3 frontend is packaged for the Microsoft Store, and how the
template/storage model survives that move.

## Templates: pure-seed, user-owned

There are no longer any in-binary "built-in" templates. Every template is a file
in the user's templates folder, discovered and watched by `TemplateCatalog`.

- The shipped defaults live as ordinary files under
  `SimpleText.Core/Templates/Defaults/<Category>/<Variant>.<ext>`, mirroring the
  drop-a-file convention (sub-folder → category, file name → variant, extension →
  mode). Each frontend links them into its output so they sit next to the exe.
- On first run, `TemplateSeeder` copies them into the user's templates folder
  once (tracked by a `.templates-seeded` marker in app-private state). Existing
  files are never overwritten; a deliberately emptied folder is respected.
- After that the folder is entirely the user's. Menu auto-registration (the
  folder watcher → `Changed` → rebuild) governs every template uniformly.

## Storage roots (`AppStorage`)

`AppStorage` is the single seam for the two roots. Frontends call
`AppStorage.Configure(state, templates)` once at startup.

| Data | Packaged WinUI | Unpackaged / Avalonia |
| --- | --- | --- |
| App state (session, theme, seed marker) | `ApplicationData.Current.LocalFolder` | `%LocalAppData%\SimpleText` |
| User templates | `Documents\SimpleText\Templates` | `%LocalAppData%\SimpleText\Templates` |

Why split them: an MSIX package redirects `%LocalAppData%` writes into a buried,
per-package `LocalCache` that is wiped on uninstall. App state belongs in the
package-private store (via the API, intentionally); user content belongs in a
durable, visible location. A `runFullTrust` packaged app reaches `Documents`
through normal Win32 access — no `documentsLibrary`/`broadFileSystemAccess`
capability required. `StorageBootstrap.IsPackaged()` picks the layout at runtime.

## Packaging

Single-project MSIX (`EnableMsixTooling`), no separate `.wapproj`:

- `Package.appxmanifest` — identity, `runFullTrust`, visual elements, and file-type
  associations for `.txt/.text/.md/.markdown/.rst/.adoc/.asciidoc` (native
  Open-with / default-editor integration). File launches arrive as an
  `ExtendedActivationKind.File` activation, handled in `App` for both cold start
  and the single-instance redirect path.
- `x64` + `ARM64`, self-contained (Windows App SDK + .NET) so sideloaded packages
  install without extra runtimes.
- Visual assets are generated from `branding/simpletext.svg` by
  `build/generate-winui-assets.sh` (git-ignored; produced in CI before packaging).

### Identity & signing

`Package.appxmanifest` ships placeholder identity (`Name`, `Publisher`).

- **Store:** replace with the Partner Center values, build with
  `UapAppxPackageBuildMode=StoreUpload` and `AppxPackageSigningEnabled=false`
  (the Store signs), upload the `.msixupload`.
- **Sideload / GitHub Release:** the package must be signed and `Publisher` must
  equal the signing certificate's subject. `release.yml` uses the cert from the
  `SIGNING_CERTIFICATE_BASE64`/`SIGNING_CERTIFICATE_PASSWORD` secrets if present,
  otherwise generates a self-signed test cert (`CN=SimpleText`, matching the
  manifest) and exports `SimpleText.cer` for users to trust. For public releases,
  prefer **Azure Trusted Signing / Artifact Signing** (CA-trusted, no hardware
  token) via `azure/trusted-signing-action`.

> Single-project MSIX does not emit a `.msixbundle` natively; the release builds
> one `.msix` per architecture. Combine with the MSIX Bundler action / `makeappx`
> if a single-file bundle is wanted.

## CI/CD

- **`ci.yml`** (PR, pushes to `main`/`claude/**`): a fast cross-platform `core`
  build, an `assets` job that renders the visual assets on Linux, and the real
  check — building the packaged MSIX **unsigned** on Windows for `x64` and
  `ARM64`. The Avalonia frontend is intentionally excluded.
- **`release.yml`** (tag `v*`): renders assets, builds **signed** `x64` + `ARM64`
  packages with one certificate, and publishes them to a GitHub Release.

## Not done / follow-ups

- Run the Windows App Certification Kit (WACK) before Store submission.
- Optional: extended/custom title bar, taskbar JumpList of recent files,
  per-scale tile polish, and a `.msixbundle` for single-file distribution.
