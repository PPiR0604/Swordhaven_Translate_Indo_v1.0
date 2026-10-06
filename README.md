# Swordhaven Indonesian — Manual Vanilla v2.1.0 source

This repository contains the source of the localization-only DLL patches and the optional PowerShell installer distributed with the Manual Vanilla mod. It contains no game DLLs, compiled executables, or decompiled full game source. Original game files must be supplied from a legitimately owned, compatible installation.

## Build requirements

- .NET SDK 9.0.
- Mono.Cecil 0.11.6 (restored from NuGet).
- The untouched AtomGame.dll and the corresponding Swordhaven_Data/Managed directory.
- Original DLL SHA256: d614f88984d843726c1c3f702aae5eac6455682bc4c8a6393c1d1b0c9533dc76.
- The exact commercial game patch number is unconfirmed. The game's v55 value is a save schema identifier, not a game release number.

## Build steps (PowerShell, from this repository directory)

Use a workspace copy of the game DLL as input. These commands write only into build/; they do not install or overwrite the game.

```powershell
$originalDll = 'C:\path\to\original\AtomGame.dll'
$managedDir = 'C:\path\to\game\Swordhaven_Data\Managed'
dotnet restore .\BaseLocalization\BaseLocalization.csproj
dotnet restore .\FixLocalization\FixLocalization.csproj
dotnet run --project .\BaseLocalization\BaseLocalization.csproj -- $originalDll $managedDir .\build\AtomGame_baseline.dll
dotnet run --project .\FixLocalization\FixLocalization.csproj -- .\build\AtomGame_baseline.dll $managedDir .\build\AtomGame.dll
Get-FileHash .\build\AtomGame.dll -Algorithm SHA256
```

Stop if any command fails. Expected baseline SHA256: b84f6278bf6a57fe0ff2f431dd84644a1ea17f4bb6935928e2e4d55087cc74c0.
Expected final DLL SHA256: 15cc179e53dc1204defc41ccbd7b61186c9cc99301abd57e1773b8742253a436.

The two stages retain the original release build sequence. BaseLocalization adds Indonesian to the language list, sets the default text language, enables external localization, and loads English before translated text for fallback. FixLocalization anchors localization paths to the game directory, retains English voice fallback, restores bundled-resource lookup, prefers bundled English core data, and adds the Bahasa Indonesia menu label. It verifies IL stacks/branches and that methods outside Localization are unchanged by this second stage. No gameplay QoL changes are added.

## Installer behavior

Install_ID.ps1 is the exact optional installer source. It must be run from the released manual package with MANIFEST.json and its payload present, using -GameDirectory. It checks the game directory, running game process, compatible DLL hashes and payload hashes; backs up files, copies the 59 manifest payload files, verifies them, and attempts rollback on errors. It does not download files, contact a server, or modify saves. The installer is not needed to build the DLL.

The translation JSON and subtitles belong to the mod download. Translation credit: Gemini 3.5 Flash Lite. This repository documents the compiled-code build, not a reproducible AI translation pipeline.

## Verification and limitations

See BUILD_VERIFICATION.txt for verification performed on these portable sources. Previous release checks are recorded in Verification.json. Actual gameplay and Vortex deployment have not been tested. A successful build or automated test is not a malware certification; Nexus moderators determine quarantine status.

## Publishing for Nexus review

Upload these source files to a public GitHub repository and give Nexus the repository URL plus the mod page/file link. Do not upload the original game DLL, bin/, obj/, or build/. This source archive has not been published to GitHub automatically.
