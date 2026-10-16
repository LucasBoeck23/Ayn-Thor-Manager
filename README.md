# Ayn-Thor-Manager
Gerenciador do AYN Thor para PC

Interface desktop multiplataforma (Windows, Linux e macOS) construída com
[Avalonia UI](https://avaloniaui.net/). Conecta no AYN Thor via ADB sem fio,
navega pelos arquivos, instala o companion e espelha a tela com o scrcpy
embutido na própria janela.

## Requisitos

- [.NET SDK 10](https://dotnet.microsoft.com/)
- `adb` (android-tools)
- `scrcpy` (para espelhar a tela)

No Debian/Ubuntu:

```bash
sudo apt install adb scrcpy
```

## Rodar

Linux / macOS:

```bash
./run.sh
```

Windows (PowerShell):

```powershell
./run.ps1
```

Ou direto pelo SDK, em qualquer sistema:

```bash
dotnet run --project src/AynThorManager.Desktop
```

## Espelhamento de tela (scrcpy embutido)

A janela do scrcpy é reparentada para dentro do painel direito do app, dando o
mesmo comportamento em todas as plataformas:

- **Windows:** via `SetParent` (user32).
- **Linux:** via `XReparentWindow` (libX11). Em sessões Wayland funciona
  através do XWayland, então o `scrcpy` e o app compartilham o mesmo display X.

## Estrutura

- `AynThorManager.Core` — modelos, interfaces e validadores (puro .NET).
- `AynThorManager.Services` — conexão ADB, fila de comandos, arquivos.
- `AynThorManager.Infrastructure` — execução do `adb` via CliWrap.
- `AynThorManager.Desktop` — UI em Avalonia + embedding da tela por plataforma.
