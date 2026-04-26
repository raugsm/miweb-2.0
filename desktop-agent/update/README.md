# AriadGSM Update Channel

`ariadgsm-update.json` is the public manifest read by AriadGSM Agent.

To publish an update:

1. Build the package with `desktop-agent\windows-app\build-agent-package.cmd`.
2. Upload the generated `desktop-agent\dist\AriadGSMAgent-<version>.zip` to the update host.
3. Replace `packageUrl`, `sha256`, `version`, and set `autoApply` to `true` only when the package is ready.
4. The running agent will download the package, launch `AriadGSM Updater.exe`, back up the current version, apply the update, and restart itself.
