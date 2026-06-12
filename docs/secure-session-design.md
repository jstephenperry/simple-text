# Secure Session Persistence — Design Plan

## Why

`session.json` / `session.winui.json` hold the **full text of unsaved documents**, file
paths, and content hashes, in plaintext under `%LOCALAPPDATA%\SimpleText`. Anything the
user types — including secrets pasted into a scratch buffer — lands on disk every 5
seconds and survives until overwritten. That is the asset to protect.

**Threats addressed**

| Threat | Addressed? |
|---|---|
| Other local accounts / admins casually reading the file | Yes |
| Backup/sync tools (OneDrive, corporate backup) capturing plaintext | Yes |
| Offline disk access (no BitLocker, stolen drive, lifted VHD) | Yes (strongest with TPM option) |
| Malware running *as the same user* | **No** — by design, the app must decrypt unattended, so any same-user process can too. No local-at-rest scheme fixes this. |

## On the asymmetric idea

Asymmetric encryption earns its keep when the encryptor and decryptor are *different
parties* or when the private key can live in *hardware*. For a same-app, same-user,
same-machine cache there is no first benefit. There is a real second benefit, though:
an RSA key created in the **TPM (Platform Crypto Provider)** is non-exportable and
never leaves the chip, which kills offline attacks completely. So the plan treats
"asymmetric" as an *optional hardened key-wrap tier*, not the payload cipher —
payloads should always be symmetric (AES-GCM); RSA over multi-megabyte sessions is
the wrong tool.

## Design: envelope encryption with an OS-managed wrap

```
session.stx (binary):
  magic "STSE" | format version (1 byte) | wrap mode (1 byte)
  wrapped DEK (length-prefixed)        <- the only part that differs per tier
  AES-256-GCM: nonce (12) | tag (16) | ciphertext (the JSON payload, unchanged shape)
```

- A fresh random 256-bit **DEK** (data-encryption key) is generated **per save**; no
  long-lived symmetric key sits on disk. AAD = magic + version + wrap mode, so headers
  can't be swapped.
- The DEK is wrapped by an OS-managed key — that's the "auto-managed" part. The user
  never sees, types, or backs up a key.

### Wrap tiers (pick at runtime, record in the header)

1. **Tier 1 — DPAPI (default, ship first).**
   `ProtectedData.Protect(dek, appEntropy, DataProtectionScope.CurrentUser)`.
   Key management is entirely Windows' (derived from the user's logon secret, rotated
   master keys, roaming handled). ~20 lines of code, no new dependencies.
2. **Tier 2 — CNG asymmetric wrap (the requested asymmetric protocol).**
   On first use, auto-create a named, **non-exportable** RSA-3072 key
   `SimpleText.SessionKey` in the user's CNG key store — Platform Crypto Provider
   (TPM-backed) when available, Microsoft Software KSP otherwise. Wrap the DEK with
   RSA-OAEP-SHA256. Decrypt asks CNG to unwrap; the private key never appears in
   process memory (TPM case) or on disk in app-readable form.
   - Gain over Tier 1: offline attacks (password cracking against DPAPI master keys)
     become impossible in the TPM case; key is hardware-bound.
   - Cost: TPM RSA ops are slow (tens of ms — fine at our 5 s cadence), and a TPM
     clear/motherboard swap silently loses the key. Sessions are a best-effort cache,
     so the failure mode is "session not restored," which the loader already treats
     gracefully.
3. **Tier 3 — cross-platform (Avalonia on macOS/Linux), later.**
   `ISessionKeyWrapper` abstraction; DPAPI/CNG implementations on Windows, Keychain /
   libsecret-backed AES key elsewhere; worst-case fallback = plaintext with `0600`
   permissions plus a visible warning in the README.

### Code shape

- `SimpleText.Core/Security/SessionProtector.cs` — `byte[] Protect(byte[] json)` /
  `byte[]? Unprotect(byte[] blob)`; owns format, AES-GCM, tier dispatch.
- `ISessionKeyWrapper` + `DpapiKeyWrapper` (Tier 1), `CngRsaKeyWrapper` (Tier 2).
- `SessionManager` / `WorkspaceSessionManager` change only their read/write lines.

### Operational hardening (all tiers)

- **Atomic writes**: write `session.stx.tmp`, then `File.Replace` — a crash mid-save
  must not destroy the previous good session.
- **Migration**: on load, first bytes `{` or BOM → legacy plaintext JSON: load it,
  immediately re-save encrypted, **delete** the plaintext file. One release later,
  drop the sniffing.
- **Failure = no session**: any decrypt/parse failure returns `null` (existing
  best-effort contract) and deletes the corrupt blob; never crash, never prompt.
- GCM already gives integrity — tampered sessions fail closed rather than restoring
  attacker-modified content (and `OriginalFileHash` keeps its separate role).
- Never log payloads or key material; `theme.json` stays plaintext (not sensitive).

### Suggested order

1. Tier 1 + atomic writes + migration (small, immediate win).
2. Tier 2 behind a header flag once Tier 1 soaks; auto-upgrade wrap mode on TPM machines.
3. Tier 3 only when the Avalonia frontend actually targets non-Windows users.
