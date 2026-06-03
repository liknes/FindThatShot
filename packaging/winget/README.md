# winget manifest for Find That Shot

These three YAML files are a standard multi-file [winget](https://learn.microsoft.com/windows/package-manager/) manifest that points winget at the **Velopack `Setup.exe`** produced by `scripts/publish.ps1`. winget does not host the binary — it just records a verified pointer to your GitHub release asset.

## Files

| File | Purpose |
| --- | --- |
| `FindThatShot.FindThatShot.yaml` | Version manifest (identity + version) |
| `FindThatShot.FindThatShot.installer.yaml` | Installer URL, hash, silent switches, ARP match |
| `FindThatShot.FindThatShot.locale.en-US.yaml` | Listing text, license, links |

## Before submitting — fill in the placeholders

Search the files for `TODO` / `<OWNER>` / `<REPO>` / `REPLACE_WITH_...` and set:

1. **`<OWNER>` / `<REPO>`** — your GitHub owner and repository name (used in every URL).
2. **`PackageIdentifier`** — currently `FindThatShot.FindThatShot`. Format is `Publisher.AppName`. Change the publisher half if you publish under a different name; it must be identical in all three files.
3. **`InstallerUrl`** — must point at the `Setup.exe` attached to a **published GitHub release** (e.g. tag `v0.9.4`). Confirm the exact filename: run `scripts/publish.ps1`, then look in `.\releases` (it should be `VideoArchiveManager-Setup.exe`).
4. **`InstallerSha256`** — the uppercase SHA256 of that exact file:
   ```powershell
   (Get-FileHash .\releases\VideoArchiveManager-Setup.exe -Algorithm SHA256).Hash
   ```
5. **Publisher / support / license URLs** in the locale file.

Keep `PackageVersion` (and the release tag / asset) in lockstep on every new release.

## Validate and submit

The easiest path is Microsoft's [`wingetcreate`](https://github.com/microsoft/winget-create):

```powershell
winget install Microsoft.WingetCreate

# Validate the local manifest before opening a PR:
wingetcreate validate .\packaging\winget

# For a brand-new version, this interviews you from the live installer URL,
# regenerates the manifest, and opens the PR to the winget-pkgs repo:
wingetcreate new https://github.com/<OWNER>/<REPO>/releases/download/v0.9.4/VideoArchiveManager-Setup.exe
```

Or open a manual pull request against [`microsoft/winget-pkgs`](https://github.com/microsoft/winget-pkgs), placing the files under
`manifests/f/FindThatShot/FindThatShot/0.9.4/`. Automated + human review runs, then it merges and `winget install FindThatShot.FindThatShot` works for everyone.

## Notes / caveats

- **Per-user install, no admin** (`Scope: user`) — matches Velopack's `%LocalAppData%` layout.
- **`winget upgrade`** correlation relies on the `AppsAndFeaturesEntries` matching Velopack's ARP entry (`DisplayName` = `--packTitle`, `Publisher` = `--packAuthors`). If you change `--packTitle`/`--packAuthors` in `publish.ps1`, update those fields here too.
- **SmartScreen:** winget does *not* code-sign your app. The unsigned `Setup.exe` may still show an "Unknown publisher" prompt on first run until you sign it (or publish via the Microsoft Store).
- **GPL:** no conflict — winget only links to your installer, it does not re-host or relicense anything.
