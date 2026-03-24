# ImageZeus

Fast, lightweight Windows image viewer built with C# and Avalonia.

[![Release](https://github.com/iopowerflower/ImageZeus/actions/workflows/release.yml/badge.svg)](https://github.com/iopowerflower/ImageZeus/actions/workflows/release.yml)

## Download

Grab the latest `ImageZeusSetup.exe` from the [Releases](https://github.com/iopowerflower/ImageZeus/releases) page.

## Features

- WebP, GIF, PNG, JPEG, BMP, TIFF support with animated GIF/WebP playback
- Mini panel scrubber for fast folder navigation
- Screen capture tool with aspect ratio, fixed size, and resize constraints
- Non-destructive rotate and flip with save-back
- Background daemon for near-instant open from Explorer
- Registers as a Windows Default App for image file types

## Build from Source

### Prerequisites
- [.NET SDK 8.x](https://dotnet.microsoft.com/download)
- Windows 10/11

```powershell
dotnet build ImageZeus.sln
dotnet run --project ImageViewer.App -- "C:\path\to\image.jpg"
```

### Self-contained executable
```powershell
.\publish.bat
# output: publish\ImageZeus.exe
```

### Installer
Requires [Inno Setup 6.x](https://jrsoftware.org/isinfo.php). Run `publish.bat` first, then:
```
iscc installer\ImageZeus.iss
# output: installer_output\ImageZeusSetup.exe
```

## License

MIT — see [LICENSE](LICENSE).
