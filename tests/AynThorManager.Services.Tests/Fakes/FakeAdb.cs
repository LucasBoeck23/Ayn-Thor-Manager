using AynThorManager.Core.Interfaces;
using AynThorManager.Core.Models;

namespace AynThorManager.Services.Tests.Fakes;

/// <summary>
/// Scripted stand-in for adb. Each command gets a list of responses consumed in
/// order; the last one repeats. Unscripted commands throw so a test fails loudly
/// instead of silently talking to nothing.
/// </summary>
internal sealed class FakeAdb : ICommandQueue
{
    private readonly Dictionary<string, List<Func<Result<CommandResult>>>> _scripts = new();
    private readonly Dictionary<string, int> _cursor = new();

    public List<string> Calls { get; } = [];

    public FakeAdb On(string arguments, string stdout = "", string stderr = "", int exitCode = 0) =>
        Add(arguments, () => Result<CommandResult>.Success(
            new CommandResult(exitCode == 0, stdout, stderr, exitCode, TimeSpan.FromMilliseconds(5))));

    /// <summary>Simulates the queue/executor itself failing (cancellation, crash).</summary>
    public FakeAdb OnQueueFailure(string arguments, string code = "COMMAND_EXECUTION_FAILED") =>
        Add(arguments, () => Result<CommandResult>.Failure(new Error(code, "fake failure")));

    /// <summary>Same output the real AdbCommandExecutor returns when a command times out.</summary>
    public FakeAdb OnTimeout(string arguments) =>
        On(arguments, stderr: "Command timed out after 10 seconds", exitCode: -1);

    public int CountOf(string arguments) => Calls.Count(c => c == arguments);

    public Task<Result<CommandResult>> EnqueueAsync(AdbCommand command, CommandPriority priority, CancellationToken ct)
    {
        Calls.Add(command.Arguments);

        if (!_scripts.TryGetValue(command.Arguments, out var responses))
            throw new InvalidOperationException($"Unscripted adb call: adb {command.Arguments}");

        var index = _cursor.GetValueOrDefault(command.Arguments);
        _cursor[command.Arguments] = Math.Min(index + 1, responses.Count - 1);
        return Task.FromResult(responses[index]());
    }

    private FakeAdb Add(string arguments, Func<Result<CommandResult>> response)
    {
        if (!_scripts.TryGetValue(arguments, out var list))
            _scripts[arguments] = list = [];
        list.Add(response);
        return this;
    }
}
