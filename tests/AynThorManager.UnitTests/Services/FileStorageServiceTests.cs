using AynThorManager.Core.Interfaces;
using AynThorManager.Core.Models;
using AynThorManager.Services.FileStorage;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AynThorManager.UnitTests.Services;

public sealed class FileStorageServiceTests
{
    private readonly ICommandQueue _commandQueue = Substitute.For<ICommandQueue>();
    private readonly IAdbConnectionManager _connectionManager = Substitute.For<IAdbConnectionManager>();
    private readonly FileStorageService _sut;

    public FileStorageServiceTests()
    {
        _connectionManager.IsConnected.Returns(true);
        _sut = new FileStorageService(
            _commandQueue,
            _connectionManager,
            NullLogger<FileStorageService>.Instance);
    }

    private static CommandResult MakeSuccess(string stdout, string stderr = "") =>
        new(true, stdout, stderr, 0, TimeSpan.FromMilliseconds(50));

    private static CommandResult MakeFailure(string stderr, int exitCode = 1) =>
        new(false, "", stderr, exitCode, TimeSpan.FromMilliseconds(50));

    #region ListDirectoryAsync

    [Fact]
    public async Task ListDirectoryAsync_ExistingDirWithFiles_ReturnsSortedEntries()
    {
        // Arrange
        var lsOutput = """
            total 24
            drwxrwx--x   3 root sdcard_rw     4096 2024-01-15 10:30 Games
            -rw-rw----   1 root sdcard_rw  1048576 2024-01-10 08:15 readme.txt
            drwxrwx--x   2 root sdcard_rw     4096 2024-01-12 14:00 Backups
            -rw-rw----   1 root sdcard_rw  2097152 2024-01-14 16:45 game.zip
            """;

        _commandQueue.EnqueueAsync(Arg.Any<AdbCommand>(), CommandPriority.Normal, Arg.Any<CancellationToken>())
            .Returns(Result<CommandResult>.Success(MakeSuccess(lsOutput)));

        // Act
        var result = await _sut.ListDirectoryAsync("/sdcard/ROMs", CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var response = result.Value!;
        response.Entries.Should().HaveCount(4);
        response.Path.Should().Be("/sdcard/ROMs");
        response.IsTruncated.Should().BeFalse();

        // Directories first, alphabetical
        response.Entries[0].Name.Should().Be("Backups");
        response.Entries[0].Type.Should().Be(FileEntryType.Directory);
        response.Entries[1].Name.Should().Be("Games");
        response.Entries[1].Type.Should().Be(FileEntryType.Directory);

        // Files second, alphabetical
        response.Entries[2].Name.Should().Be("game.zip");
        response.Entries[2].Type.Should().Be(FileEntryType.File);
        response.Entries[3].Name.Should().Be("readme.txt");
        response.Entries[3].Type.Should().Be(FileEntryType.File);
    }

    [Fact]
    public async Task ListDirectoryAsync_EmptyDir_ReturnsEmptyListNotTruncated()
    {
        // Arrange
        var lsOutput = "total 0\n";

        _commandQueue.EnqueueAsync(Arg.Any<AdbCommand>(), CommandPriority.Normal, Arg.Any<CancellationToken>())
            .Returns(Result<CommandResult>.Success(MakeSuccess(lsOutput)));

        // Act
        var result = await _sut.ListDirectoryAsync("/sdcard/EmptyFolder", CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Entries.Should().BeEmpty();
        result.Value.IsTruncated.Should().BeFalse();
        result.Value.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task ListDirectoryAsync_NotFound_ReturnsPathNotFoundError()
    {
        // Arrange
        _commandQueue.EnqueueAsync(Arg.Any<AdbCommand>(), CommandPriority.Normal, Arg.Any<CancellationToken>())
            .Returns(Result<CommandResult>.Success(
                new CommandResult(false, "", "ls: /sdcard/NonExistent: No such file or directory", 1, TimeSpan.Zero)));

        // Act
        var result = await _sut.ListDirectoryAsync("/sdcard/NonExistent", CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("PATH_NOT_FOUND");
    }

    [Fact]
    public async Task ListDirectoryAsync_PermissionDenied_ReturnsPermissionDeniedError()
    {
        // Arrange
        _commandQueue.EnqueueAsync(Arg.Any<AdbCommand>(), CommandPriority.Normal, Arg.Any<CancellationToken>())
            .Returns(Result<CommandResult>.Success(
                new CommandResult(false, "", "ls: /sdcard/Private: Permission denied", 1, TimeSpan.Zero)));

        // Act
        var result = await _sut.ListDirectoryAsync("/sdcard/Private", CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("PERMISSION_DENIED");
    }

    [Fact]
    public async Task ListDirectoryAsync_Timeout_ReturnsTimeoutError()
    {
        // Arrange
        _commandQueue.EnqueueAsync(Arg.Any<AdbCommand>(), CommandPriority.Normal, Arg.Any<CancellationToken>())
            .Returns(Result<CommandResult>.Failure(new Error("TIMEOUT", "Command timed out")));

        // Act
        var result = await _sut.ListDirectoryAsync("/sdcard/SlowDir", CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("TIMEOUT");
    }

    [Fact]
    public async Task ListDirectoryAsync_DeviceNotConnected_ReturnsDeviceNotConnectedError()
    {
        // Arrange
        _connectionManager.IsConnected.Returns(false);

        // Act
        var result = await _sut.ListDirectoryAsync("/sdcard/ROMs", CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("DEVICE_NOT_CONNECTED");

        // Verify no command was enqueued
        await _commandQueue.DidNotReceive()
            .EnqueueAsync(Arg.Any<AdbCommand>(), Arg.Any<CommandPriority>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region CreateDirectoryAsync

    [Fact]
    public async Task CreateDirectoryAsync_Success_ReturnsFullPath()
    {
        // Arrange
        _commandQueue.EnqueueAsync(Arg.Any<AdbCommand>(), CommandPriority.Normal, Arg.Any<CancellationToken>())
            .Returns(Result<CommandResult>.Success(MakeSuccess("")));

        // Act
        var result = await _sut.CreateDirectoryAsync("/sdcard/ROMs", "NewFolder", CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.FullPath.Should().Be("/sdcard/ROMs/NewFolder");
    }

    [Fact]
    public async Task CreateDirectoryAsync_ParentNotFound_ReturnsPathNotFoundError()
    {
        // Arrange
        _commandQueue.EnqueueAsync(Arg.Any<AdbCommand>(), CommandPriority.Normal, Arg.Any<CancellationToken>())
            .Returns(Result<CommandResult>.Success(
                new CommandResult(false, "", "mkdir: /sdcard/NonExistent/Sub: No such file or directory", 1, TimeSpan.Zero)));

        // Act
        var result = await _sut.CreateDirectoryAsync("/sdcard/NonExistent", "Sub", CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("PATH_NOT_FOUND");
    }

    [Fact]
    public async Task CreateDirectoryAsync_NameConflict_ReturnsNameAlreadyExistsError()
    {
        // Arrange
        _commandQueue.EnqueueAsync(Arg.Any<AdbCommand>(), CommandPriority.Normal, Arg.Any<CancellationToken>())
            .Returns(Result<CommandResult>.Success(
                new CommandResult(false, "", "mkdir: /sdcard/ROMs/Games: File exists", 1, TimeSpan.Zero)));

        // Act
        var result = await _sut.CreateDirectoryAsync("/sdcard/ROMs", "Games", CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("NAME_ALREADY_EXISTS");
    }

    [Fact]
    public async Task CreateDirectoryAsync_InvalidName_ReturnsInvalidNameError()
    {
        // Act — name with invalid characters
        var result = await _sut.CreateDirectoryAsync("/sdcard/ROMs", "bad:name", CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("INVALID_NAME");

        // Verify no command was enqueued
        await _commandQueue.DidNotReceive()
            .EnqueueAsync(Arg.Any<AdbCommand>(), Arg.Any<CommandPriority>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateDirectoryAsync_PathTooLong_ReturnsPathTooLongError()
    {
        // Arrange — parent path under 4096 chars, name under 255 bytes,
        // but combined (parent + "/" + name) exceeds 4096
        var longParent = "/sdcard/" + new string('a', 3900);
        var name = new string('b', 200);

        // Act
        var result = await _sut.CreateDirectoryAsync(longParent, name, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("PATH_TOO_LONG");
    }

    #endregion

    #region RenameAsync

    [Fact]
    public async Task RenameAsync_Success_ReturnsNewPath()
    {
        // Arrange
        _commandQueue.EnqueueAsync(Arg.Any<AdbCommand>(), CommandPriority.Normal, Arg.Any<CancellationToken>())
            .Returns(Result<CommandResult>.Success(MakeSuccess("")));

        // Act
        var result = await _sut.RenameAsync("/sdcard/ROMs/old-game.zip", "new-game.zip", CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.NewPath.Should().Be("/sdcard/ROMs/new-game.zip");
    }

    [Fact]
    public async Task RenameAsync_ItemNotFound_ReturnsPathNotFoundError()
    {
        // Arrange
        _commandQueue.EnqueueAsync(Arg.Any<AdbCommand>(), CommandPriority.Normal, Arg.Any<CancellationToken>())
            .Returns(Result<CommandResult>.Success(
                new CommandResult(false, "No such file or directory", "", 1, TimeSpan.Zero)));

        // Act
        var result = await _sut.RenameAsync("/sdcard/ROMs/missing.zip", "renamed.zip", CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("PATH_NOT_FOUND");
    }

    [Fact]
    public async Task RenameAsync_NameConflict_ReturnsNameAlreadyExistsError()
    {
        // Arrange
        _commandQueue.EnqueueAsync(Arg.Any<AdbCommand>(), CommandPriority.Normal, Arg.Any<CancellationToken>())
            .Returns(Result<CommandResult>.Success(
                new CommandResult(false, "cannot move: already exists", "", 1, TimeSpan.Zero)));

        // Act
        var result = await _sut.RenameAsync("/sdcard/ROMs/game.zip", "existing.zip", CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("NAME_ALREADY_EXISTS");
    }

    [Fact]
    public async Task RenameAsync_DeviceDisconnected_ReturnsDeviceNotConnectedError()
    {
        // Arrange
        _connectionManager.IsConnected.Returns(false);

        // Act
        var result = await _sut.RenameAsync("/sdcard/ROMs/game.zip", "renamed.zip", CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("DEVICE_NOT_CONNECTED");

        // Verify no command was enqueued
        await _commandQueue.DidNotReceive()
            .EnqueueAsync(Arg.Any<AdbCommand>(), Arg.Any<CommandPriority>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RenameAsync_InvalidName_ReturnsInvalidNameError()
    {
        // Act — name with path separator
        var result = await _sut.RenameAsync("/sdcard/ROMs/game.zip", "bad/name.zip", CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("INVALID_NAME");
    }

    #endregion

    #region DeleteAsync

    [Fact]
    public async Task DeleteAsync_FileSuccess_ReturnsDeletedPath()
    {
        // Arrange
        _commandQueue.EnqueueAsync(Arg.Any<AdbCommand>(), CommandPriority.Normal, Arg.Any<CancellationToken>())
            .Returns(Result<CommandResult>.Success(MakeSuccess("")));

        // Act
        var result = await _sut.DeleteAsync("/sdcard/ROMs/game.zip", CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.DeletedPath.Should().Be("/sdcard/ROMs/game.zip");
    }

    [Fact]
    public async Task DeleteAsync_RecursiveDirectory_ReturnsDeletedPath()
    {
        // Arrange — rm -rf on a directory with contents
        _commandQueue.EnqueueAsync(Arg.Any<AdbCommand>(), CommandPriority.Normal, Arg.Any<CancellationToken>())
            .Returns(Result<CommandResult>.Success(MakeSuccess("")));

        // Act
        var result = await _sut.DeleteAsync("/sdcard/ROMs/PSX", CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.DeletedPath.Should().Be("/sdcard/ROMs/PSX");

        // Verify the command used rm -rf
        await _commandQueue.Received(1).EnqueueAsync(
            Arg.Is<AdbCommand>(cmd => cmd.Arguments.Contains("rm -rf")),
            CommandPriority.Normal,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_PathNotFound_ReturnsPathNotFoundError()
    {
        // Arrange
        _commandQueue.EnqueueAsync(Arg.Any<AdbCommand>(), CommandPriority.Normal, Arg.Any<CancellationToken>())
            .Returns(Result<CommandResult>.Success(
                new CommandResult(false, "", "rm: /sdcard/ghost: No such file or directory", 1, TimeSpan.Zero)));

        // Act
        var result = await _sut.DeleteAsync("/sdcard/ghost", CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("PATH_NOT_FOUND");
    }

    [Fact]
    public async Task DeleteAsync_PermissionDenied_ReturnsPermissionDeniedError()
    {
        // Arrange
        _commandQueue.EnqueueAsync(Arg.Any<AdbCommand>(), CommandPriority.Normal, Arg.Any<CancellationToken>())
            .Returns(Result<CommandResult>.Success(
                new CommandResult(false, "", "rm: /sdcard/protected: Permission denied", 1, TimeSpan.Zero)));

        // Act
        var result = await _sut.DeleteAsync("/sdcard/protected", CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("PERMISSION_DENIED");
    }

    [Fact]
    public async Task DeleteAsync_PathTraversal_ReturnsPathNotAllowedError()
    {
        // Act — path with traversal sequence
        var result = await _sut.DeleteAsync("/sdcard/../etc/passwd", CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("PATH_NOT_ALLOWED");

        // Verify no command was enqueued
        await _commandQueue.DidNotReceive()
            .EnqueueAsync(Arg.Any<AdbCommand>(), Arg.Any<CommandPriority>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_PathOutsideAllowedPrefix_ReturnsPathNotAllowedError()
    {
        // Act — path outside allowed storage prefixes
        var result = await _sut.DeleteAsync("/system/app/SystemApp.apk", CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be("PATH_NOT_ALLOWED");

        // Verify no command was enqueued
        await _commandQueue.DidNotReceive()
            .EnqueueAsync(Arg.Any<AdbCommand>(), Arg.Any<CommandPriority>(), Arg.Any<CancellationToken>());
    }

    #endregion
}
