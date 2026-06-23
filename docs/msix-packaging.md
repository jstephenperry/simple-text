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

The signing certificate's **subject must exactly equal** the manifest `Publisher`
(same RDNs, same order, case-sensitive) or Windows refuses the package with
`0x8007000B`. Identity is baked in at **pack** time, so the manifest must be
correct *before* building — a mismatch can't be fixed by re-signing.

Three signing paths, by audience:

- **Signed GitHub Release (default for distribution): Azure Artifact Signing.**
  `release.yml` builds the packages **unsigned**, then signs them with
  [Azure Artifact Signing](https://learn.microsoft.com/azure/artifact-signing/)
  (formerly "Trusted Signing") via `azure/artifact-signing-action`. Its
  certificate chains to a Microsoft public root **trusted out of the box**, so the
  package installs with no warning and no per-machine trust step — and the in-app
  updater's silent `PackageManager` staging path works on any machine. Because the
  subject is assigned by Azure (not `CN=SimpleText`), the workflow stamps the real
  subject into the manifest `Publisher` from a secret before packaging (the
  committed manifest stays generic for local builds). Required repo secrets:

  | Secret | Value |
  | --- | --- |
  | `ARTIFACT_SIGNING_PUBLISHER` | The **exact** cert subject DN (Azure portal → Certificate profiles → *profile* → *Subject name*), e.g. `CN=Your Org, O=Your Org, L=City, S=State, C=US`. Stamped into the manifest `Publisher`. |
  | `ARTIFACT_SIGNING_ENDPOINT` | Region endpoint, e.g. `https://eus.codesigning.azure.net/` (must match the account/profile region, else signing 403s). |
  | `ARTIFACT_SIGNING_ACCOUNT` | The signing account name. |
  | `ARTIFACT_SIGNING_PROFILE` | The certificate profile name. |
  | `AZURE_CLIENT_ID` / `AZURE_TENANT_ID` / `AZURE_SUBSCRIPTION_ID` | App registration used by `azure/login` (OIDC). Needs a GitHub **federated credential** and the **Artifact Signing Certificate Profile Signer** role on the profile. |

  Without `ARTIFACT_SIGNING_PUBLISHER` the release fails fast — there's no silent
  self-signed fallback, which would defeat the point.

- **Local self-signed sideload (your own PC): `build/sign-msix.ps1`.** Mints a
  persistent self-signed cert whose subject matches the manifest `Publisher`
  (`CN=SimpleText`) and signs locally. Still warns until trusted — see below.

- **Microsoft Store:** replace `Name`/`Publisher`/`PublisherDisplayName` with the
  Partner Center values, build with `UapAppxPackageBuildMode=StoreUpload` and
  `AppxPackageSigningEnabled=false` (the Store signs), upload the `.msixupload`.

> **Publisher is the app's update identity.** `Name` + `Publisher` →
> `PublisherId` → `PackageFamilyName`. Changing `Publisher` between releases makes
> Windows treat the package as a *different* app: existing installs won't
> auto-update and their per-user data is orphaned. So pick the Artifact Signing
> subject **before first public distribution** and keep it frozen. Migrating an
> already-shipped app to a new subject needs the
> [MSIX persistent identity](https://learn.microsoft.com/windows/msix/package/persistent-identity)
> (publisher-bridging) workflow.

> Single-project MSIX does not emit a `.msixbundle` natively; the release builds
> one `.msix` per architecture. Combine with the MSIX Bundler action / `makeappx`
> if a single-file bundle is wanted.

### Why a self-signed (local) package warns — and how to stop it

This applies to **local** `build/sign-msix.ps1` builds. Signed *releases* already
avoid the warning by using Azure Artifact Signing (above) — its public root is
trusted everywhere, so this section isn't needed for them.

Signing alone does **not** remove the *"this app may potentially harm your
computer" / untrusted-publisher* warning. Windows shows it precisely because a
self-signed certificate isn't trusted: anyone could mint a `CN=SimpleText` cert,
so the OS can't vouch for it. The warning clears only when the certificate is
**trusted on the installing machine** — imported into
`LocalMachine\TrustedPeople`, the store App Installer checks when sideloading.

So there are two honest paths:

| Goal | Certificate | Per-machine setup |
| --- | --- | --- |
| Install on **your own** PC, no warning | self-signed (free) | trust the cert once (admin) |
| Install on **anyone's** PC, no setup | CA-issued — **[Azure Artifact Signing](https://learn.microsoft.com/azure/artifact-signing/)** (cheap, no hardware token; this is what `release.yml` uses) or a paid OV/EV cert | none |

There is no way to get "no warning **and** no per-machine trust step" from a
self-signed cert — that would defeat code signing. For the self-signed path:

```powershell
# Build + self-sign with a persistent cert, and trust it in one go (UAC prompt):
pwsh build/sign-msix.ps1 -Trust              # add -Platform ARM64 as needed

# Then install the produced package:
Add-AppxPackage .\AppPackages\...\SimpleText.Editor_*.msix   # or double-click it
```

To move a self-signed package to **another of your own machines**, trust its
certificate there once (every `build/sign-msix.ps1` run drops a `SimpleText.cer`
next to the package):

```powershell
pwsh build/trust-cert.ps1 -Path .\SimpleText.cer     # one-time, elevates itself
Add-AppxPackage .\SimpleText.Editor_1.0.1.0_x64.msix
```

`trust-cert.ps1` also accepts the `.msix` directly and extracts the signer cert
from it (`-Path .\SimpleText.Editor_*.msix`).

**Trust once, forever:** `build/sign-msix.ps1` keeps its self-signed cert in your
per-user store and reuses it, so every local build shares one publisher — trust it
once and future local builds install cleanly too.

> Downloaded **GitHub Releases** need none of this: they're signed by Azure
> Artifact Signing, whose root Windows already trusts, so they install (and
> auto-update) with no `.cer` and no trust step.

## CI/CD

- **`ci.yml`** (PR, pushes to `main`/`claude/**`): a fast cross-platform `core`
  build, an `assets` job that renders the visual assets on Linux, and the real
  check — building the packaged MSIX **unsigned** on Windows for `x64` and
  `ARM64`. The Avalonia frontend is intentionally excluded.
- **`release.yml`** (manual **Run workflow**): renders assets, builds the `x64` +
  `ARM64` MSIX **unsigned**, signs them with **Azure Artifact Signing**, and
  publishes them to a GitHub Release. A second job (`pack-avalonia`) builds the
  Velopack installers + update feed for the Avalonia frontend on **Windows and
  Linux** and attaches them to the same Release. It stamps an auto-incrementing
  build number (the workflow run number) as the 4th version component, so the
  release is tagged `vX.Y.Z.<build>` and the MSIX Identity, Velopack package, and
  git tag all carry that same version — which is what the in-app updaters compare
  against. See [Versioning](../README.md#versioning).

  **macOS is excluded from `pack-avalonia`:** Velopack auto-update on macOS
  requires an Apple Developer ID signature + notarization (Gatekeeper quarantines
  unsigned downloaded updates), which this project doesn't set up. Mac users build
  the Avalonia app from source; auto-update is unsupported there until notarization
  is wired in (`vpk pack --signAppIdentity … --notaryProfile …`).

## Not done / follow-ups

- Run the Windows App Certification Kit (WACK) before Store submission.
- Optional: taskbar JumpList of recent files, per-scale tile polish, and a
  `.msixbundle` for single-file distribution.
