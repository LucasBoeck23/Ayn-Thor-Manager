using AynThorManager.Core.Interfaces;
using AynThorManager.Core.Models;
using AynThorManager.Infrastructure.Files;

namespace AynThorManager.Infrastructure.Tests.Files;

public class AdbFileGatewayTests
{
    private const string Serial = "192.168.1.5:5555";

    private sealed class FakeExecutor : IAdbCommandExecutor
    {
        private readonly CommandResult _result;
        public string? LastArguments { get; private set; }

        public FakeExecutor(CommandResult result) => _result = result;

        public Task<CommandResult> ExecuteAsync(string arguments, TimeSpan timeout, CancellationToken ct)
        {
            LastArguments = arguments;
            return Task.FromResult(_result);
        }

        public Task<CommandResult> ExecuteAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken ct)
        {
            LastArguments = string.Join(' ', arguments);
            return Task.FromResult(_result);
        }
    }

    private static CommandResult Ok(string stdout = "") =>
        new(true, stdout, string.Empty, 0, TimeSpan.Zero);

    private static CommandResult Fail(string stderr) =>
        new(false, string.Empty, stderr, 1, TimeSpan.Zero);

    [Fact]
    public async Task List_runs_ls_la_against_the_active_serial_and_parses_entries()
    {
        var executor = new FakeExecutor(Ok(
            "total 8\n-rw-rw---- 1 u0_a123 sdcard_rw 10 2024-01-15 13:45 a.txt\n"));
        var gateway = new AdbFileGateway(executor, Serial);

        var result = await gateway.ListAsync("/sdcard/Download");

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.Equal("a.txt", result.Value![0].Name);
        Assert.Contains($"-s {Serial} shell ls -la", executor.LastArguments);
        Assert.Contains("'/sdcard/Download'", executor.LastArguments);
    }

    [Fact]
    public async Task List_maps_not_found_stderr_to_a_path_not_found_failure()
    {
        var executor = new FakeExecutor(Fail("ls: /nope: No such file or directory"));
        var gateway = new AdbFileGateway(executor, Serial);

        var result = await gateway.ListAsync("/nope");

        Assert.False(result.IsSuccess);
        Assert.Equal("PATH_NOT_FOUND", result.Error!.Code);
    }

    [Fact]
    public async Task Pull_uses_adb_pull_with_both_literal_paths()
    {
        var executor = new FakeExecutor(Ok());
        var gateway = new AdbFileGateway(executor, Serial);

        var result = await gateway.PullAsync("/sdcard/a b.txt", "/tmp/cache/a b.txt");

        Assert.True(result.IsSuccess);
        Assert.Contains($"-s {Serial} pull", executor.LastArguments);
        Assert.Contains("/sdcard/a b.txt", executor.LastArguments);
        Assert.Contains("/tmp/cache/a b.txt", executor.LastArguments);
        Assert.DoesNotContain("'", executor.LastArguments);
    }

    [Fact]
    public async Task Push_uses_adb_push_local_then_remote()
    {
        var executor = new FakeExecutor(Ok());
        var gateway = new AdbFileGateway(executor, Serial);

        var result = await gateway.PushAsync("/tmp/cache/a.txt", "/sdcard/a.txt");

        Assert.True(result.IsSuccess);
        Assert.Contains($"-s {Serial} push /tmp/cache/a.txt /sdcard/a.txt", executor.LastArguments);
    }

    [Fact]
    public async Task Delete_removes_recursively_via_rm_rf()
    {
        var executor = new FakeExecutor(Ok());
        var gateway = new AdbFileGateway(executor, Serial);

        var result = await gateway.DeleteAsync("/sdcard/old");

        Assert.True(result.IsSuccess);
        Assert.Contains($"-s {Serial} shell rm -rf '/sdcard/old'", executor.LastArguments);
    }

    [Fact]
    public async Task MakeDir_uses_mkdir()
    {
        var executor = new FakeExecutor(Ok());
        var gateway = new AdbFileGateway(executor, Serial);

        var result = await gateway.MakeDirAsync("/sdcard/new");

        Assert.True(result.IsSuccess);
        Assert.Contains($"-s {Serial} shell mkdir '/sdcard/new'", executor.LastArguments);
    }

    [Fact]
    public async Task Rename_uses_mv_from_then_to()
    {
        var executor = new FakeExecutor(Ok());
        var gateway = new AdbFileGateway(executor, Serial);

        var result = await gateway.RenameAsync("/sdcard/a.txt", "/sdcard/b.txt");

        Assert.True(result.IsSuccess);
        Assert.Contains($"-s {Serial} shell mv '/sdcard/a.txt' '/sdcard/b.txt'", executor.LastArguments);
    }

    [Fact]
    public async Task Stat_returns_the_leaf_name_for_a_single_node()
    {
        var executor = new FakeExecutor(Ok(
            "drwxrwx--x 4 u0_a123 sdcard_rw 4096 2024-02-01 09:00 /sdcard/Download\n"));
        var gateway = new AdbFileGateway(executor, Serial);

        var result = await gateway.StatAsync("/sdcard/Download");

        Assert.True(result.IsSuccess);
        Assert.Equal("Download", result.Value!.Name);
        Assert.Equal(ThorFileType.Directory, result.Value.Type);
        Assert.Contains($"-s {Serial} shell ls -lad '/sdcard/Download'", executor.LastArguments);
    }

    [Fact]
    public async Task Generic_adb_failure_surfaces_as_a_clear_error_not_an_exception()
    {
        var executor = new FakeExecutor(Fail("some unexpected adb breakage"));
        var gateway = new AdbFileGateway(executor, Serial);

        var result = await gateway.DeleteAsync("/sdcard/x");

        Assert.False(result.IsSuccess);
        Assert.Equal("ADB_COMMAND_FAILED", result.Error!.Code);
        Assert.Contains("unexpected adb breakage", result.Error.Message);
    }

    [Fact]
    public void Constructor_rejects_a_blank_serial()
    {
        Assert.Throws<ArgumentException>(() => new AdbFileGateway(new FakeExecutor(Ok()), "  "));
    }
}
