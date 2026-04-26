# Windows Bridge

This folder is reserved for the C#/.NET Windows bridge.

The bridge will replace PowerShell for:

- Windows UI Automation;
- browser window discovery for Chrome, Edge and Firefox;
- mouse and keyboard control;
- fast screen capture;
- process supervision.

It must write visible-only observations that match:

```text
desktop-agent\contracts\windows-bridge-event.schema.json
```

Current PC note: only the .NET runtime is installed right now. A .NET SDK is needed before compiling the C# bridge.
