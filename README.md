<p align="center">
  <img src="Assets/Brand/LOGO.ICO" alt="Cortex QR Logo" width="96" />
</p>

<h1 align="center">Cortex QR</h1>

<p align="center">
  A modern Windows desktop application for generating fully customizable QR codes — built with C#, WPF, and Material Design.
</p>

<p align="center">
  <a href="https://github.com/SAADX25/Cortex-QR/releases/latest"><img src="https://img.shields.io/github/v/release/SAADX25/Cortex-QR?style=flat-square&color=0A84FF" alt="GitHub Release" /></a>
  <img src="https://img.shields.io/badge/.NET_10-512BD4?style=flat-square&logo=dotnet&logoColor=white" alt=".NET 10" />
  <img src="https://img.shields.io/badge/C%23-239120?style=flat-square&logo=csharp&logoColor=white" alt="C#" />
  <img src="https://img.shields.io/badge/WPF-0078D4?style=flat-square&logo=windows&logoColor=white" alt="WPF" />
  <img src="https://img.shields.io/badge/Platform-Windows_x64-0078D6?style=flat-square&logo=windows&logoColor=white" alt="Windows x64" />
  <a href="https://github.com/SAADX25/Cortex-QR/commits/main"><img src="https://img.shields.io/github/last-commit/SAADX25/Cortex-QR?style=flat-square&color=1A2D4A" alt="Last Commit" /></a>
</p>

---

## About

Cortex QR is a local-first QR code generator for Windows that goes beyond basic black-and-white codes. It gives you full control over colors, shapes, gradients, logos, and eye patterns — and exports results as PNG, JPEG, or SVG.

Whether you need a single branded QR code for a product label, Wi-Fi access cards for your office, or hundreds of codes generated from a CSV file, Cortex QR handles it from your desktop with no cloud dependencies.

---

## Features

All features listed below are implemented and verified in the source code.

### QR Generation
- **Multiple payload types** — Text/URL, Wi-Fi network credentials (WPA/WPA2/WEP), and vCard contact cards
- **Real-time preview** — The QR code updates live as you type or adjust settings
- **High error correction** — Uses QRCoder's `ECCLevel.H` for maximum scan reliability, even with logos

### Styling & Customization
- **Full color control** — Independent colors for foreground, background, finder patterns, and inner eye
- **Gradient support** — Optional linear gradient across QR modules
- **Module shapes** — Squares, Rounded Squares, Dots/Circles, Liquid
- **Eye shapes** — Square, Rounded, Circular, Leaf, Shield
- **Logo overlay** — Embed a custom logo image with adjustable size (10–35%) and optional background padding

### Style Presets
- **Save & load presets** — Persist your favorite color/shape combinations as named presets (stored as JSON in `%LocalAppData%\CortexQR\Presets`)
- **Manage presets** — List, load, overwrite, and delete presets from within the app

### Export
- **Save as PNG or JPEG** via a standard save dialog
- **Export as SVG** — Scalable vector output with proper gradient definitions and embedded logo data URIs

### Batch Processing
- **CSV-driven bulk generation** — Load a CSV file with a `data` column and optional `filename` column
- **Concurrent processing** — Generates up to 4 QR codes in parallel
- **Output format choice** — Batch export as PNG or SVG
- **Progress tracking** — Live progress bar with error count
- **Error logging** — Failed rows are written to a timestamped error log

### General
- **Modern dark UI** — Material Design theme with a polished sidebar navigation
- **Local processing** — All QR generation runs on your machine; no data leaves your device
- **Windows installer** — Inno Setup-based installer with optional desktop shortcut

---

## Screenshots

> **Screenshots not yet available.** To add them, capture the following and place them in `assets/screenshots/`:
>
> | Suggested File | What to Capture |
> |---|---|
> | `assets/screenshots/generator.png` | Main generator view with a styled QR code |
> | `assets/screenshots/styling.png` | Color and shape customization panel |
> | `assets/screenshots/batch.png` | Batch processing panel with progress |
> | `assets/screenshots/presets.png` | Preset management panel |
>
> Then uncomment the section below:

<!--
<p align="center">
  <img src="assets/screenshots/generator.png" alt="Generator View" width="720" />
</p>
-->

---

## Installation

### Download the Installer

1. Go to the [Latest Release](https://github.com/SAADX25/Cortex-QR/releases/latest)
2. Download `CortexQR-Setup-v1.0.0.exe`
3. Run the installer and follow the prompts
4. Launch **Cortex QR** from the Start Menu or optional desktop shortcut

### Build from Source

**Prerequisites:**
- [.NET 10 SDK](https://dotnet.microsoft.com/download) (or later)
- Windows x64

```bash
git clone https://github.com/SAADX25/Cortex-QR.git
cd Cortex-QR
dotnet restore
dotnet build
dotnet run
```

To publish a self-contained release and build the installer:

```powershell
cd Installer
.\build-installer.ps1
```

> This requires [Inno Setup 6](https://jrsoftware.org/isdl.php) to be installed.

---

## Usage

1. **Open Cortex QR** from the Start Menu or run the built executable
2. **Choose a payload type** — Select *Text / URL*, *Wi-Fi*, or *vCard* from the tabs
3. **Enter your content** — Type a URL, fill in Wi-Fi credentials, or enter contact details
4. **Customize the style** — Adjust colors, module shapes, eye shapes, and gradient settings
5. **Add a logo** *(optional)* — Browse for an image and adjust the size slider
6. **Save your result** — Click *Save* for PNG/JPEG or *Export SVG* for vector output

### Batch Processing

1. Navigate to the **Batch** section in the sidebar
2. Select a CSV file (must contain a `data` column; `filename` column is optional)
3. Choose an output folder and format (PNG or SVG)
4. Click **Start** — the current style settings are applied to all generated codes

---

## Project Structure

```
Cortex-QR/
├── Views/                  # WPF windows and custom controls
│   ├── MainWindow.xaml     # Main application UI layout
│   ├── ColorPickerPopup    # Custom color picker control
│   └── ColorRow            # Inline color selector row
├── ViewModels/             # MVVM view models and commands
│   ├── BatchProcessingViewModel.cs
│   ├── AsyncRelayCommand.cs
│   └── RelayCommand.cs
├── Services/               # Core business logic
│   ├── QrGenerationService.cs   # QR rendering (bitmap + SVG)
│   ├── PresetStorage.cs         # Preset save/load/delete
│   └── FileDialogService.cs     # File/folder dialog wrappers
├── Models/                 # Data models
│   └── QrStylePreset.cs
├── Helpers/                # Utility classes
│   ├── ImageHelper.cs
│   └── ComboBoxAssist.cs
├── Resources/              # Theme and style resources
│   └── ThemeResources.xaml
├── Assets/Brand/           # App icon and branding
├── Installer/              # Inno Setup script and build script
│   ├── CortexQR.iss
│   └── build-installer.ps1
└── CortexQR.csproj         # Project configuration
```

---

## Tech Stack

| Component | Technology |
|---|---|
| Language | C# (.NET 10) |
| UI Framework | WPF (Windows Presentation Foundation) |
| Design System | [Material Design In XAML Toolkit](https://github.com/MaterialDesignInXAML/MaterialDesignInXamlToolkit) |
| QR Engine | [QRCoder](https://github.com/codebude/QRCoder) 1.8.0 |
| Rendering | System.Drawing (GDI+) for bitmap, custom SVG builder for vector |
| Installer | [Inno Setup 6](https://jrsoftware.org/isinfo.php) |
| Architecture | MVVM (manual implementation with RelayCommand/ObservableObject) |

---

## Roadmap

Planned improvements — these are not implemented yet:

- [ ] Copy QR code to clipboard
- [ ] Drag-and-drop logo import
- [ ] Additional export formats (PDF, EPS)
- [ ] Custom QR code sizes and DPI settings
- [ ] QR code scanning / decoding
- [ ] Custom color themes for the UI
- [ ] Preset import/export (share presets as files)
- [ ] Automated tests

---

## Contributing

Contributions are welcome.

1. **Fork** the repository
2. **Create a branch** for your change (`git checkout -b feature/your-feature`)
3. **Commit** your work with clear messages
4. **Push** to your fork and open a **Pull Request**

Please keep pull requests focused and provide context for your changes.

---

## License

This project does not currently include a LICENSE file.

If you intend this to be open source, consider adding an [MIT License](https://choosealicense.com/licenses/mit/) — it's simple, permissive, and widely understood.

---

## Privacy

Cortex QR is a local-first application. All QR code generation, rendering, and file I/O happen entirely on your machine. No data is sent to any external server or cloud service.

Preset data is stored locally in `%LocalAppData%\CortexQR\Presets`.

---

## Support

If you find Cortex QR useful, consider giving the repository a ⭐ — it helps others discover the project.

