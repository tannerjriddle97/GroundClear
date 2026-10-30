# GroundClear: project setup

Recovered working project. Source and assets retain the delivered source-package layout. The original package is preserved separately from future builds.

- Mod ID: `groundclear`; package version: `1.0.0`.
- Recorded on October 29, 2026; installed at inventory: True.
- `releases/` holds local package snapshots; it is ignored by Git. Do not overwrite them.
- `.local/provenance.json` records original paths, imported file hashes, and package hash; it is ignored by Git.
- Read the original README and build script before development. Build outputs belong in ignored folders inside this project.
- Runtime audit/probe scripts are historical tools, not permission to launch the game. This setup did not run them or change installed mods.
- Before GitHub publication: resolve source gaps, confirm the license for original code and any reused assets, retain third-party notices, document dependency versions, and verify a build from the clean project. No new license has been assigned by this setup.
- Dedicated source repository: [GroundClear](https://github.com/tannerjriddle97/GroundClear), branch `main`. The parent directory remains a local project index.

## Build from this folder

`./build.ps1 -Dotnet $dotnet10`

Compilation passed on October 29, 2026. Use a .NET 10 SDK for SDK-style projects; the remaining direct-compiler scripts retain local compiler/runtime defaults. No game or server was launched. The setup verification report at the workspace root records pure checks and remaining publication preparation.

## GitHub source publication

Public source publication was requested October 30, 2026. Current source version: `1.0.0`. Historical package versions above identify preserved snapshots. No new license is assigned; existing licenses and third-party notices remain applicable. Game/mod binaries, local provenance, profiles, saves and generated evidence stay excluded. Source publication does not establish runtime acceptance.
