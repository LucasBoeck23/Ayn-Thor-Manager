# Rotina de Conexão — Diagnóstico de Bugs

Documento de trabalho: a conexão dividida em rotinas, os bugs de cada uma e
o status de correção/teste.

Legenda: 🔴 aberto · 🟡 parcial · 🟢 corrigido · ✅ corrigido e testado no Thor real

## Contexto do device de teste

- **AYN Thor**, com USB (`c26efaeb`) e Wi-Fi (`192.168.1.17:43321`) conectados ao mesmo tempo.
- `adb devices -l` mostra `model:AYN_Thor`, `getprop ro.product.model` mostra `AYN Thor`.
- A porta da "Depuração sem fio" é aleatória (43321), não `5555`.
- Companion **não instalado** no Thor, e o APK **não existe no repo**.

## Módulo de conexão

Toda a conexão agora fica em `src/AynThorManager.Services/Connection/`:

| Arquivo | Papel |
|---|---|
| `DeviceConnection.cs` | Única porta de entrada: `AutoConnectAsync` (Buscar), `ConnectAsync` (IP manual), `DisconnectAsync`, `RunAsync` (todo comando no device, sempre com `-s serial`), evento `StateChanged` |
| `AdbOutputParser.cs` | Parsing puro de `adb devices -l`, `adb mdns services` e erros de "device sumiu" |
| `ThorDevice.cs` | O device em uso: serial exato, USB/Wi-Fi, modelo |

A UI (`MainWindow.*.cs`) não chama mais o adb direto. Saíram `AdbConnectionService`,
`IAdbConnectionManager`, `DeviceStatus` e `DeviceStatusType`.

---

## Rotina 1 — Entrada manual
| # | Bug | Status |
|---|-----|--------|
| 1.1 | Sem porta, assume `:5555`; a "Depuração sem fio" usa porta aleatória. Continua assumindo 5555 (é o padrão do `adb tcpip`), mas o erro agora diz pra usar a porta mostrada no Thor. | 🟡 |
| 1.2 | Porta 7100 recusada sem explicação. | ✅ |

## Rotina 2 — Buscar / descoberta
| # | Bug | Status |
|---|-----|--------|
| 2.1 | Não achava device USB (pulava serial sem `:`). Agora usa USB e Wi-Fi, e prefere USB. | ✅ |
| 2.2 | Aceitava device `offline`/`unauthorized`. Agora só usa `device`, e explica os outros estados. | 🟢 |
| 2.3 | Parse do mDNS pegava nome de serviço. Agora extrai `ip:porta` por regex. Não testado com mDNS real (o Thor já estava conectado). | 🟡 |
| 2.4 | Não usa o mDNS do companion (`_aynthor._tcp.`). | 🔴 |
| 2.5 | Sem identificação do Thor. Agora prioriza o modelo `AYN_Thor`. | ✅ |
| 2.6 | Sem varredura de sub-rede (achar Thor sem mDNS). | 🔴 |
| 2.7 | *Achado pelos testes:* Thor anunciado por mDNS mas não autorizado aparecia como "Nenhum Thor encontrado". Agora mostra o erro real (não autorizado). | 🟢 |

## Rotina 3 — Conectar
| # | Bug | Status |
|---|-----|--------|
| 3.1 | Sucesso por substring "connected". Agora só confia no `adb devices`. | ✅ |
| 3.2 | "Já existe uma conexão ADB ativa" travando reconexão. Guard removido; conectar de novo só troca o device. | ✅ |
| 3.3 | `unauthorized` retornava sucesso. Agora é falha com mensagem clara. | 🟢 |
| 3.4 | Queda não detectada. Agora qualquer comando que receba "device not found/offline" limpa o estado (o ping de 2 s do NetStats detecta a queda). | 🟢 |
| 3.5 | *Achado pelos testes:* IP que não responde (timeout) caía em 3 s extras de espera e voltava "falha genérica". Agora retorna TIMEOUT na hora, com mensagem própria. | 🟢 |

## Rotina 4 — Verificação pós-conexão
| # | Bug | Status |
|---|-----|--------|
| 4.1 | Teardown pelo executor deixava o estado dessincronizado. | ✅ |
| 4.2 | UI falava com o adb por dois caminhos. | ✅ |

## Rotina 5 — Estado
| # | Bug | Status |
|---|-----|--------|
| 5.1 | Duas fontes de verdade. Agora só `DeviceConnection.Current`. | ✅ |
| 5.2 | Alvo `-s` reconstruído com `:5555`. Agora usa o serial exato. | ✅ |

## Rotina 6 — Arquivos
| # | Bug | Status |
|---|-----|--------|
| 6.1 | `ls`/`mkdir` sem `-s` → "more than one device". Testado com USB + Wi-Fi ligados: lista `/storage/` ok. | ✅ |
| 6.2 | Erro genérico "Falha na operação". Agora inclui o detalhe do adb. | 🟢 |

## Rotina 7 — Companion
| # | Bug | Status |
|---|-----|--------|
| 7.1 | Checagem sem `-s`. Agora pelo módulo. | ✅ |
| 7.2 | Caminho do APK com aspas na string de args. | 🟡 |
| 7.3 | APK não existe no repo, instalação impossível pela UI. | 🔴 |

## Rotina 8 — Execução adb
| # | Bug | Status |
|---|-----|--------|
| 8.1 | Saída do adb sem encoding explícito (afeta Windows). | 🟡 |
| 8.2 | `WithArguments(string)` em vez de lista. | 🟡 |
| 8.3 | `adb` fixo no PATH, sem mensagem clara se faltar. | 🔴 |

## Transversal — Encoding
| Arquivo | Status |
|---|---|
| `IpAddressValidator.cs`, `AdbErrorParser.cs`, `FileStorageService.cs`, `CommandQueue.cs` | ✅ (nenhum mojibake restante em `src/`) |

---

## Testes executados no Thor real (smoke test, sem alterar o device)

| Cenário | Resultado |
|---|---|
| `adb devices -l` com USB + Wi-Fi | 2 devices, ambos `thor=True` |
| Buscar | Conecta via USB `c26efaeb` |
| Listar `/storage/` com 2 devices | OK (`emulated`, `FBAF-7913`, `self`) |
| Conectar manual `192.168.1.17:43321` | OK, troca pro Wi-Fi |
| Listar `/storage/` via Wi-Fi | OK |
| Porta 7100 / texto inválido / vazio | Mensagens claras |
| IP inexistente | Falha em ~3 s com "No route to host", mantém o device anterior |
| Device sumido | stderr real `adb: device '...' not found` é detectado |

## Testes unitários (Wi-Fi)

`tests/AynThorManager.Services.Tests` — 79 testes, rodam em ~0,1 s sem o Thor:
`dotnet test AynThorManager.slnx`.

Um adb falso (`Fakes/FakeAdb.cs`) responde com saídas gravadas do Thor real
(`Fakes/AdbOutputs.cs` marca o que é gravado e o que segue o formato documentado do adb).

| Arquivo | Cobre |
|---|---|
| `WifiConnectTests` | Validação do endereço, primeira conexão, "already connected", device ficando pronto com atraso, troca USB→Wi-Fi, IP inexistente, porta errada, timeout, "already connected" falso, offline, não autorizado, falha da fila, falha mantendo o device anterior |
| `WifiAutoConnectTests` | Buscar com Wi-Fi já conhecido, preferência USB, Thor vs outro Android, mDNS (conectar, ignorar pareamento, tentar o próximo, não autorizado, vazio, sem suporte), só offline / só não autorizado |
| `WifiSessionTests` | `-s` com porta aleatória, quedas (`not found`, `offline`, `closed`), erro de shell não é queda, timeout isolado não é queda, disconnect Wi-Fi/USB, reconectar depois da queda |
| `AdbOutputParserTests` | Parsing de `devices -l`, `mdns services`, detecção de modelo e de device perdido |

Prova de que os testes pegam bug: reintroduzir o `-s` faltando (bug 6.1) derruba 8 testes.

## Próximos passos
1. Cenários que precisam de você: tirar o cabo (só Wi-Fi), Wi-Fi caindo durante o uso, Thor não autorizado.
2. Rotina 7.3: buildar o APK do companion.
3. Rotina 2.4/2.6: descoberta pelo companion e varredura de sub-rede.
4. Rotina 8: encoding/args/adb ausente.

---

## Rotina 9 — Companion APK (Ayn Thor Link)

**Estado:** não instalado no Thor, sem APK no repo, e sem Java/Android SDK no PC.
Análise feita lendo o código Kotlin e comparando com os dados reais do Thor via adb.
Ainda não rodou no device. Os itens marcados como "provável" precisam ser confirmados rodando o app.

Dados reais do Thor: Android 13 (API 33), IP `192.168.1.17`, porta ADB sem fio
`43321` (`getprop service.adb.tls.port`), `adb_wifi_enabled=1`, bateria 96%.

| # | Bug | Local | Certeza | Status |
|---|-----|-------|---------|--------|
| 9.1 | A tela mostra "Porta: 7100", que é a porta do próprio companion. O desktop precisa da porta ADB sem fio (43321) e recusa a 7100, então a tela orienta errado. | `MainActivity.kt:21` | certo | 🔴 |
| 9.2 | Nem a tela nem o mDNS (`_aynthor._tcp.`, porta 7100) informam a porta ADB, que muda a cada vez que a depuração sem fio liga. O PC acha o IP mas não a porta. | `LinkService.kt` `registerMdns` | certo | 🔴 |
| 9.3 | Sem permissão de armazenamento no manifest. No Android 13, `LS /storage/emulated/0` deve voltar vazio ou incompleto, e `MKDIR`/`DELETE` falham fora da pasta do app. É o "não lê os dados reais". | `AndroidManifest.xml`, `listDirectory` | provável | 🔴 |
| 9.4 | Servidor na 7100 aceita qualquer um da rede, sem autenticação, com `DELETE` recursivo. | `LinkService.kt` `startServer`/`deleteItem` | certo | 🔴 segurança |
| 9.5 | `unregisterMdns` cria um listener novo, então o Android lança erro (engolido) e o anúncio nunca é removido. | `LinkService.kt` `unregisterMdns` | certo | 🟡 |
| 9.6 | IP lido por `WifiManager.connectionInfo` (obsoleto desde o Android 12). Funciona hoje, pode retornar `0.0.0.0` em versões futuras. | `MainActivity.kt` `getWifiIp` | provável | 🟡 |
| 9.7 | Sem `POST_NOTIFICATIONS` (exigido no Android 13): a notificação do serviço fica oculta. Não derruba o app. | `AndroidManifest.xml` | certo | 🟡 |
| 9.8 | `targetSdk 34` com serviço `connectedDevice` sem a permissão `FOREGROUND_SERVICE_CONNECTED_DEVICE`: o app crasha em Android 14+. No Thor (13) não acontece. | `AndroidManifest.xml` | certo | 🟡 |
| 9.9 | Lê dados reais corretamente: modelo (`Build.MODEL`) e bateria (`BatteryManager`). | `getDeviceInfo` | certo | 🟢 |
