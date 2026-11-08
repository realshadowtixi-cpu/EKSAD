# SHADOW CLIENT — REQUIEM

**A lightweight, Windows x64 Minecraft launcher project.** This is a real launcher application, not a screenshot or a mock interface.

## What is implemented

- Microsoft sign-in using the CmlLib Microsoft authentication library.
- Offline username profiles using CmlLib's offline session support. These are for offline-mode servers only; servers using online-mode authentication reject them.
- Switch between Microsoft and offline profiles from the launcher header. The offline username is saved locally for convenience.
- Official Minecraft version discovery and installation through CmlLib.Core.
- Launch of installed vanilla or custom Minecraft version IDs.
- Named profiles that point to installed/available game version IDs.
- Built-in Fabric and Quilt loader installation.
- Modrinth search with game-version and loader filters.
- Mod downloads, including required Modrinth project dependencies.
- Local mod/resource-pack/shader-pack file management: add, enable/disable, remove, open folder.
- One-click low-end Fabric mod set: Sodium, Lithium, FerriteCore, ImmediatelyFast and Entity Culling (only versions returned as compatible by Modrinth are installed).
- Current Java skin preview and skin upload for the signed-in Minecraft Java profile.
- Configurable game directory, Java executable, RAM cap and window size.
- Settings and named profiles saved under `%LOCALAPPDATA%\ShadowClient`.
- Sign-in session stays in memory for the current app run; signing in again may be required after closing the launcher.
- GitHub Actions workflow that builds a self-contained Windows x64 executable and ZIP artifact.

## Account modes and server compatibility

The launcher has two separate account modes:

- **Microsoft account:** uses Microsoft's official authentication and is the mode for online-mode servers and Realms (subject to the account having Minecraft: Java Edition).
- **Offline username:** lets you enter a 3–16 character Minecraft-style username (letters, numbers and underscores) and starts the game with CmlLib's offline session. This works only with servers configured for offline-mode authentication (`online-mode=false`). It does not bypass or authenticate to online-mode servers, and it does not make Realms available.

The launcher does not disable authentication on servers or modify server settings. A server owner must configure their own server for offline-mode play. Offline-mode servers do not verify account ownership, so do not use another player's identity or assume the server protects usernames the way an authenticated server does. Microsoft authentication is required for official skin preview/upload; offline profiles cannot upload a skin to a Minecraft account.

## First build with GitHub Actions

1. Open your GitHub repository and choose **Add file → Upload files**.
2. Extract this ZIP on your computer and upload the *contents* of the extracted `ShadowClientProject` folder to the repository root. Keep the `.github` folder and its `workflows` subfolder.
3. Commit the uploaded files to the `main` branch (or `master`).
4. Open the repository's **Actions** tab.
5. Select **Build Shadow Client (Windows x64)** and click **Run workflow**. A push to `main`/`master` also starts the workflow automatically.
6. When the run completes, open it and download the **ShadowClient-Windows-x64** artifact. It contains `ShadowClient.exe` and `ShadowClient-Windows-x64.zip`.

No local .NET installation or local compilation is required when using GitHub Actions.

## Running the executable

- Extract `ShadowClient-Windows-x64.zip`.
- Run `ShadowClient.exe`.
- Choose **Microsoft login** for authenticated servers, or enter a 3–16 character username in the header and choose **Use offline** for offline-mode servers only.
- Open **Versions & Profiles**, select a game version and choose Vanilla, Fabric or Quilt, then install/create a profile.
- Select the profile on Home and click **Play Now**. Join the server from Minecraft's Multiplayer screen as usual.

The first install downloads official Minecraft game files and can take time depending on your connection. The launcher does not bundle Minecraft game assets or copyrighted game files inside this source ZIP.

## Recommended settings for a 4 GB RAM computer

Start with **1536 MB** maximum memory. Close other heavy programs before launching. Do not assume that allocating more RAM automatically improves FPS. The low-end mod preset is designed for Fabric; create a Fabric profile first and select the matching Minecraft game version in Mod Browser.

## Scope and honest limitations of v0.1.0

This is the first functional launcher release. It is not yet feature-parity with Feather or Lunar Client and does not include a proprietary in-game HUD, cosmetics network, replay system, voice chat, anticheat, or server network. Fabric/Quilt can load compatible community mods; existing Forge/NeoForge/custom version folders may be launched if CmlLib can parse their metadata, but this build does not install Forge or NeoForge. Username changes are managed through the official Minecraft account service, not from this launcher.

## Build locally (optional)

On a Windows machine with the .NET 8 SDK, run `scripts/build-windows.ps1` from PowerShell. This is optional; GitHub Actions is the intended build route for this project.

## Libraries and services

- [CmlLib.Core](https://github.com/CmlLib/CmlLib.Core) — Minecraft version installation and launching.
- [CmlLib.Core.Auth.Microsoft](https://github.com/CmlLib/CmlLib.Core.Auth.Microsoft) — Microsoft/Xbox/Minecraft authentication.
- [Modrinth API](https://docs.modrinth.com/api/) — public mod/resource pack/shader pack discovery and downloads.

Minecraft is a trademark of Microsoft/Mojang. This is an independent third-party launcher and is not affiliated with or endorsed by Mojang or Microsoft.
