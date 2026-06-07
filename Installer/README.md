# Cortex QR Installer

This folder contains the Inno Setup packaging files.

Build steps:

1. Install `innosetup-6.7.3.exe`.
2. Run this PowerShell script from the project root:

```powershell
.\Installer\build-installer.ps1
```

The installer will be created here:

```text
G:\Cortex QR\Installer\Output\CortexQR-Setup-v1.0.0.exe
```

The installer packages the published x64 self-contained app from:

```text
G:\Cortex QR\publish\win-x64
```
