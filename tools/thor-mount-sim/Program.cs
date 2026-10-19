// Harness de simulação: exercita a rotina de montagem FUSE->ADB passo a passo,
// capturando o erro de cada etapa sem derrubar o processo. Não é teste automatizado;
// é um "driver" para reproduzir o que o botão "Abrir arquivos do Thor" faz.
//
// Uso:
//   dotnet run --project tools/thor-mount-sim -- [caminhoRemoto]
// Ex.: dotnet run --project tools/thor-mount-sim -- /sdcard

using AynThorManager.Core.Interfaces;
using AynThorManager.Core.Models;
using AynThorManager.Infrastructure.Adb;
using AynThorManager.Infrastructure.Files;
using AynThorManager.Infrastructure.Mount;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

static void Step(string name) => Console.WriteLine($"\n=== {name} ===");
static void Ok(string msg) => Console.WriteLine($"  [OK]   {msg}");
static void Err(string msg) => Console.WriteLine($"  [ERRO] {msg}");

var remoteRoot = args.Length > 0 ? args[0] : "/sdcard";
var adbPath = Environment.GetEnvironmentVariable("ADB_PATH") ?? "adb";

Step("0. Ambiente");
Console.WriteLine($"  adb      : {adbPath}");
Console.WriteLine($"  remote   : {remoteRoot}");

// --- 1. adb + device -----------------------------------------------------------
Step("1. Resolver device ativo (adb devices -l)");
var options = Options.Create(new AdbOptions { AdbPath = adbPath });
var executor = new AdbCommandExecutor(options, NullLogger<AdbCommandExecutor>.Instance);

string? serial = null;
try
{
    var res = await executor.ExecuteAsync("devices -l", TimeSpan.FromSeconds(10), CancellationToken.None);
    Console.WriteLine(res.StandardOutput.TrimEnd());
    foreach (var raw in res.StandardOutput.Split('\n'))
    {
        var line = raw.Trim();
        if (line.Length == 0 || line.StartsWith("List of devices") || line.StartsWith('*')) continue;
        var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 && parts[1] == "device") { serial = parts[0]; break; }
    }
    if (serial is null) Err("Nenhum device no estado 'device' (sem Thor conectado). Seguindo mesmo assim para testar o mount loop.");
    else Ok($"serial ativo = {serial}");
}
catch (Exception ex) { Err($"adb falhou: {ex.Message}"); }

// --- 2. Gateway (lazy ls) ------------------------------------------------------
Step("2. Gateway: ls lazy via ADB");
IAdbFileGateway gateway = new ActiveDeviceAdbFileGateway(executor, () => serial);
try
{
    var list = await gateway.ListAsync(remoteRoot);
    if (list.IsSuccess) Ok($"ls {remoteRoot}: {list.Value!.Count} entradas");
    else Err($"ls falhou: {list.Error!.Code} - {list.Error.Message}");
}
catch (Exception ex) { Err($"exceção no ls: {ex.Message}"); }

// --- 3. Mount FUSE -------------------------------------------------------------
Step("3. Montar FUSE (ThorFuseMount.MountAsync)");
var cache = new FileCache(gateway);
var fs = new ThorFuseFilesystem(gateway, cache);
var mount = new ThorFuseMount(fs, cache);
var mounted = false;
try
{
    await mount.MountAsync(serial ?? "no-device", CancellationToken.None);
    mounted = mount.IsMounted;
    if (mounted) Ok($"montado em {mount.MountPoint}");
    else Err("MountAsync retornou sem montar (IsMounted=false).");
}
catch (Exception ex)
{
    Err($"{ex.GetType().Name}: {ex.Message}");
    if (ex.InnerException is { } inner) Err($"  inner: {inner.GetType().Name}: {inner.Message}");
}

// --- 4. Operações no ponto montado --------------------------------------------
if (mounted && Environment.GetEnvironmentVariable("HOLD") == "1" && mount.MountPoint is { } holdMp)
{
    var holdSecs = int.TryParse(Environment.GetEnvironmentVariable("HOLD_SECS"), out var hs) ? hs : 30;
    Console.WriteLine($"\n[HOLD] montado em {holdMp} por {holdSecs}s. Rode seus testes agora.");
    Console.Out.Flush();
    await Task.Delay(TimeSpan.FromSeconds(holdSecs));
}

if (mounted && mount.MountPoint is { } mp)
{
    Step("4. Operações no mount (readdir / read / create / write / rename / delete)");

    // readdir (lazy)
    string[] entries = [];
    try
    {
        entries = Directory.GetFileSystemEntries(mp);
        Ok($"readdir: {entries.Length} itens em {remoteRoot}");
    }
    catch (Exception ex) { Err($"readdir falhou: {ex.Message}"); }

    // read (open -> pull -> ler do cache): pega o primeiro arquivo (não-dir) e lê o começo
    try
    {
        var firstFile = entries.FirstOrDefault(e => File.Exists(e));
        if (firstFile is null) Console.WriteLine("  [skip] nenhum arquivo regular na raiz para ler");
        else
        {
            var buf = new byte[64];
            using var fsr = new FileStream(firstFile, FileMode.Open, FileAccess.Read);
            var n = fsr.Read(buf, 0, buf.Length);
            Ok($"read: {n} bytes de {Path.GetFileName(firstFile)}");
        }
    }
    catch (Exception ex) { Err($"read falhou: {ex.Message}"); }

    // create + write + flush (push no close)
    var probe = Path.Combine(mp, "thor-mount-sim-probe.txt");
    var payload = $"ayn-thor-mount-sim {DateTime.UtcNow:O}\n";
    try
    {
        File.WriteAllText(probe, payload);
        Ok($"create+write+flush: {probe}");
    }
    catch (Exception ex) { Err($"create/write falhou: {ex.Message}"); }

    // read-back (confirma push de volta ao Thor): relê via mount
    try
    {
        var back = File.ReadAllText(probe);
        if (back.StartsWith("ayn-thor-mount-sim")) Ok($"read-back do que foi escrito: {back.Trim()}");
        else Err($"read-back divergente: '{back}'");
    }
    catch (Exception ex) { Err($"read-back falhou: {ex.Message}"); }

    // rename
    var probe2 = Path.Combine(mp, "thor-mount-sim-probe-renamed.txt");
    try
    {
        File.Move(probe, probe2, overwrite: true);
        Ok($"rename -> {Path.GetFileName(probe2)}");
    }
    catch (Exception ex) { Err($"rename falhou: {ex.Message}"); probe2 = probe; }

    // delete
    try
    {
        File.Delete(probe2);
        Ok($"delete {Path.GetFileName(probe2)}");
    }
    catch (Exception ex) { Err($"delete falhou: {ex.Message}"); }

    // bookmark (Req 1.5): o mount em si não adiciona; é o ThorMountService.
    // Aqui só reportamos o arquivo de bookmarks para inspeção manual.
    var bm = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config/gtk-3.0/bookmarks");
    Console.WriteLine($"  [info] bookmarks file: {bm} (existe={File.Exists(bm)})");
}

// --- 5. Desmontar --------------------------------------------------------------
Step("5. Desmontar");
try
{
    await mount.UnmountAsync(CancellationToken.None);
    Ok($"desmontado; IsMounted={mount.IsMounted}");
}
catch (Exception ex) { Err($"unmount falhou: {ex.Message}"); }

Step("Fim");
Console.WriteLine("Verifique acima a PRIMEIRA etapa com [ERRO]: é onde a rotina quebra.");
