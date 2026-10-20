# Contagem de linhas do projeto

Gerado por `scripts/count-lines.sh` (conta `.cs` + `.axaml` em `src/`, ignora `bin/`/`obj/`).

- **BRUTO** = todas as linhas (igual `wc -l`)
- **SO-CODIGO** = sem linhas em branco (nao ha comentarios no projeto)

## Resumo por solution

| Solution | Arquivos | Bruto | So-codigo |
|---|---:|---:|---:|
| Infrastructure | 21 | 2329 | 1897 |
| Desktop | 12 | 1170 | 1000 |
| Services | 10 | 1037 | 846 |
| Core | 26 | 509 | 400 |
| **TOTAL** | **69** | **5045** | **4143** |

---

## AynThorManager.Infrastructure — 2329 / 1897 (21 arquivos)

| Arquivo | Bruto | So-codigo |
|---|---:|---:|
| Mount/ThorFuseFilesystem.cs | 373 | 314 |
| Mount/LibFuseInterop.cs | 326 | 272 |
| Mount/ThorFuseMount.cs | 197 | 152 |
| Files/FileCache.cs | 187 | 144 |
| Streaming/SunshineHost.cs | 153 | 126 |
| Files/AdbFileGateway.cs | 139 | 108 |
| Files/GtkBookmark.cs | 104 | 81 |
| Files/LsOutputParser.cs | 102 | 81 |
| Network/WifiConnectionService.cs | 99 | 81 |
| Adb/AdbCommandExecutor.cs | 89 | 76 |
| Files/ActiveDeviceAdbFileGateway.cs | 87 | 68 |
| Streaming/SunshinePrerequisites.cs | 79 | 66 |
| Streaming/ScrcpyMirror.cs | 67 | 55 |
| Settings/SecretToolStore.cs | 65 | 58 |
| Adb/AdbLocator.cs | 63 | 53 |
| Settings/ConnectionSettingsStore.cs | 54 | 44 |
| Files/FileManagerLauncher.cs | 47 | 38 |
| Processes/ProcessRunner.cs | 46 | 39 |
| Mount/FuseMountPrerequisites.cs | 32 | 24 |
| Settings/UserPaths.cs | 14 | 12 |
| Adb/AdbOptions.cs | 6 | 5 |

## AynThorManager.Desktop — 1170 / 1000 (12 arquivos)

| Arquivo | Bruto | So-codigo |
|---|---:|---:|
| Views/Main/MainWindow.ReverseStreaming.cs | 257 | 213 |
| Views/Main/MainWindow.axaml | 223 | 203 |
| Views/Main/MainWindow.ThorFiles.cs | 119 | 99 |
| Views/Main/MainWindow.axaml.cs | 105 | 87 |
| Composition/AppServices.cs | 93 | 82 |
| Views/Main/MainWindow.DirectConnect.cs | 81 | 70 |
| Views/Main/MainWindow.Streaming.cs | 79 | 62 |
| App.axaml | 79 | 72 |
| Views/Main/MainWindow.NetStats.cs | 55 | 46 |
| Views/Main/MainWindow.Connection.cs | 43 | 37 |
| App.axaml.cs | 20 | 16 |
| Program.cs | 16 | 13 |

## AynThorManager.Services — 1037 / 846 (10 arquivos)

| Arquivo | Bruto | So-codigo |
|---|---:|---:|
| Streaming/ReverseStreamingService.cs | 231 | 192 |
| Mount/ThorMountService.cs | 215 | 181 |
| Connection/DeviceConnection.cs | 183 | 144 |
| Connection/DirectConnectionService.cs | 104 | 83 |
| Streaming/StreamingService.cs | 76 | 63 |
| Telemetry/NetworkMonitor.cs | 74 | 58 |
| Streaming/MoonlightClient.cs | 64 | 51 |
| Connection/AdbOutputParser.cs | 41 | 32 |
| Adb/CommandQueue.cs | 40 | 35 |
| Connection/ThorDevice.cs | 9 | 7 |

## AynThorManager.Core — 509 / 400 (26 arquivos)

| Arquivo | Bruto | So-codigo |
|---|---:|---:|
| Validators/FileNameValidator.cs | 88 | 74 |
| Validators/IpAddressValidator.cs | 59 | 46 |
| Validators/PathValidator.cs | 43 | 32 |
| Models/Result.cs | 33 | 27 |
| Models/MountModels.cs | 31 | 25 |
| Models/ReverseStreamModels.cs | 30 | 26 |
| Helpers/AdbErrorParser.cs | 30 | 24 |
| Interfaces/IAdbFileGateway.cs | 20 | 12 |
| Models/ThorFileEntry.cs | 16 | 14 |
| Helpers/PathHelper.cs | 16 | 14 |
| Models/AdbModels.cs | 13 | 11 |
| Interfaces/IThorMount.cs | 12 | 8 |
| Interfaces/IScreenMirror.cs | 12 | 8 |
| Interfaces/IGameStreamHost.cs | 12 | 8 |
| Interfaces/ISecretStore.cs | 10 | 7 |
| Interfaces/IConnectionSettingsStore.cs | 10 | 7 |
| Interfaces/IAdbCommandExecutor.cs | 10 | 7 |
| Models/NetworkSample.cs | 9 | 8 |
| Interfaces/IWifiConnection.cs | 8 | 6 |
| Interfaces/ISunshinePrerequisites.cs | 8 | 6 |
| Interfaces/ISidebarBookmark.cs | 8 | 6 |
| Interfaces/IMountPrerequisites.cs | 8 | 6 |
| Interfaces/ICommandQueue.cs | 8 | 6 |
| Models/Error.cs | 6 | 5 |
| Interfaces/IFileManagerLauncher.cs | 6 | 5 |
| Models/ConnectionSettings.cs | 3 | 2 |
