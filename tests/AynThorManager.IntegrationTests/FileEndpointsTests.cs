using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AynThorManager.Core.DTOs;
using AynThorManager.Core.Interfaces;
using AynThorManager.Core.Models;
using AynThorManager.Core.Models.Responses;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Xunit;

namespace AynThorManager.IntegrationTests;

/// <summary>
/// Integration tests for the /api/files endpoints using WebApplicationFactory
/// with mocked ADB services. Tests the full HTTP pipeline including middleware and error mapping.
/// </summary>
public sealed class FileEndpointsTests : IClassFixture<FileEndpointsTests.FileApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly HttpClient _client;
    private readonly IFileStorageService _fileStorageService;

    public FileEndpointsTests(FileApiFactory factory)
    {
        _fileStorageService = factory.FileStorageService;
        _client = factory.CreateClient();
    }

    #region GET /api/files — List Directory

    [Fact]
    public async Task ListDirectory_ValidPath_ReturnsOkWithEntries()
    {
        // Arrange
        var entries = new List<FileEntry>
        {
            new("ROMs", 0, DateTimeOffset.Parse("2024-01-15T10:30:00Z"), FileEntryType.Directory),
            new("game.zip", 1048576, DateTimeOffset.Parse("2024-02-20T14:00:00Z"), FileEntryType.File)
        };
        var response = new ListDirectoryResponse(entries, "/sdcard/ROMs", false, 2);

        _fileStorageService.ListDirectoryAsync("/sdcard/ROMs", Arg.Any<CancellationToken>())
            .Returns(Result<ListDirectoryResponse>.Success(response));

        // Act
        var httpResponse = await _client.GetAsync("/api/files?path=/sdcard/ROMs");

        // Assert
        httpResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await httpResponse.Content.ReadFromJsonAsync<ListDirectoryResponseDto>(JsonOptions);
        dto.Should().NotBeNull();
        dto!.Path.Should().Be("/sdcard/ROMs");
        dto.IsTruncated.Should().BeFalse();
        dto.TotalCount.Should().Be(2);
        dto.Entries.Should().HaveCount(2);
        dto.Entries[0].Name.Should().Be("ROMs");
        dto.Entries[0].Type.Should().Be("directory");
        dto.Entries[1].Name.Should().Be("game.zip");
        dto.Entries[1].Type.Should().Be("file");
        dto.Entries[1].SizeBytes.Should().Be(1048576);
    }

    [Fact]
    public async Task ListDirectory_PathNotFound_Returns404WithProblemDetails()
    {
        // Arrange
        var error = new Error("PATH_NOT_FOUND", "O caminho não foi encontrado: /sdcard/NonExistent");

        _fileStorageService.ListDirectoryAsync("/sdcard/NonExistent", Arg.Any<CancellationToken>())
            .Returns(Result<ListDirectoryResponse>.Failure(error));

        // Act
        var httpResponse = await _client.GetAsync("/api/files?path=/sdcard/NonExistent");

        // Assert
        httpResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var problem = await httpResponse.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        problem.Should().NotBeNull();
        problem!.Status.Should().Be(404);
        problem.Extensions.Should().ContainKey("code");
        problem.Extensions["code"]!.ToString().Should().Be("PATH_NOT_FOUND");
    }

    [Fact]
    public async Task ListDirectory_PathTraversal_Returns422WithProblemDetails()
    {
        // Arrange
        var error = new Error("PATH_NOT_ALLOWED", "Caminho não permitido: contém travessia de diretório ou prefixo inválido");

        _fileStorageService.ListDirectoryAsync("/sdcard/../etc", Arg.Any<CancellationToken>())
            .Returns(Result<ListDirectoryResponse>.Failure(error));

        // Act
        var httpResponse = await _client.GetAsync("/api/files?path=/sdcard/../etc");

        // Assert
        httpResponse.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var problem = await httpResponse.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        problem.Should().NotBeNull();
        problem!.Status.Should().Be(422);
        problem.Extensions.Should().ContainKey("code");
        problem.Extensions["code"]!.ToString().Should().Be("PATH_NOT_ALLOWED");
    }

    [Fact]
    public async Task ListDirectory_DeviceNotConnected_Returns409()
    {
        // Arrange
        var error = new Error("DEVICE_NOT_CONNECTED", "O dispositivo não está conectado via ADB.");

        _fileStorageService.ListDirectoryAsync("/sdcard/ROMs", Arg.Any<CancellationToken>())
            .Returns(Result<ListDirectoryResponse>.Failure(error));

        // Act
        var httpResponse = await _client.GetAsync("/api/files?path=/sdcard/ROMs");

        // Assert
        httpResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var problem = await httpResponse.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        problem.Should().NotBeNull();
        problem!.Extensions["code"]!.ToString().Should().Be("DEVICE_NOT_CONNECTED");
    }

    #endregion

    #region POST /api/files/directory — Create Directory

    [Fact]
    public async Task CreateDirectory_ValidRequest_Returns201WithFullPath()
    {
        // Arrange
        var request = new CreateDirectoryRequestDto("/sdcard/ROMs", "NewFolder");
        var response = new CreateDirectoryResponse("/sdcard/ROMs/NewFolder");

        _fileStorageService.CreateDirectoryAsync("/sdcard/ROMs", "NewFolder", Arg.Any<CancellationToken>())
            .Returns(Result<CreateDirectoryResponse>.Success(response));

        // Act
        var httpResponse = await _client.PostAsJsonAsync("/api/files/directory", request);

        // Assert
        httpResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var dto = await httpResponse.Content.ReadFromJsonAsync<CreateDirectoryResponseDto>(JsonOptions);
        dto.Should().NotBeNull();
        dto!.FullPath.Should().Be("/sdcard/ROMs/NewFolder");

        httpResponse.Headers.Location.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateDirectory_InvalidName_Returns400WithProblemDetails()
    {
        // Arrange
        var request = new CreateDirectoryRequestDto("/sdcard/ROMs", "invalid:name");
        var error = new Error("INVALID_NAME", "O nome contém caracteres inválidos: :");

        _fileStorageService.CreateDirectoryAsync("/sdcard/ROMs", "invalid:name", Arg.Any<CancellationToken>())
            .Returns(Result<CreateDirectoryResponse>.Failure(error));

        // Act
        var httpResponse = await _client.PostAsJsonAsync("/api/files/directory", request);

        // Assert
        httpResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = await httpResponse.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        problem.Should().NotBeNull();
        problem!.Status.Should().Be(400);
        problem.Extensions.Should().ContainKey("code");
        problem.Extensions["code"]!.ToString().Should().Be("INVALID_NAME");
    }

    [Fact]
    public async Task CreateDirectory_NameAlreadyExists_Returns409()
    {
        // Arrange
        var request = new CreateDirectoryRequestDto("/sdcard/ROMs", "Existing");
        var error = new Error("NAME_ALREADY_EXISTS", "Já existe um item com o nome 'Existing' no diretório especificado.");

        _fileStorageService.CreateDirectoryAsync("/sdcard/ROMs", "Existing", Arg.Any<CancellationToken>())
            .Returns(Result<CreateDirectoryResponse>.Failure(error));

        // Act
        var httpResponse = await _client.PostAsJsonAsync("/api/files/directory", request);

        // Assert
        httpResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var problem = await httpResponse.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        problem.Should().NotBeNull();
        problem!.Extensions["code"]!.ToString().Should().Be("NAME_ALREADY_EXISTS");
    }

    [Fact]
    public async Task CreateDirectory_ParentNotFound_Returns404()
    {
        // Arrange
        var request = new CreateDirectoryRequestDto("/sdcard/NonExistent", "NewFolder");
        var error = new Error("PATH_NOT_FOUND", "O caminho pai não foi encontrado: /sdcard/NonExistent");

        _fileStorageService.CreateDirectoryAsync("/sdcard/NonExistent", "NewFolder", Arg.Any<CancellationToken>())
            .Returns(Result<CreateDirectoryResponse>.Failure(error));

        // Act
        var httpResponse = await _client.PostAsJsonAsync("/api/files/directory", request);

        // Assert
        httpResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var problem = await httpResponse.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        problem.Should().NotBeNull();
        problem!.Extensions["code"]!.ToString().Should().Be("PATH_NOT_FOUND");
    }

    #endregion

    #region PUT /api/files/rename — Rename Item

    [Fact]
    public async Task Rename_ValidRequest_ReturnsOkWithNewPath()
    {
        // Arrange
        var request = new RenameRequestDto("/sdcard/ROMs/old.zip", "new.zip");
        var response = new RenameResponse("/sdcard/ROMs/new.zip");

        _fileStorageService.RenameAsync("/sdcard/ROMs/old.zip", "new.zip", Arg.Any<CancellationToken>())
            .Returns(Result<RenameResponse>.Success(response));

        // Act
        var httpResponse = await _client.PutAsJsonAsync("/api/files/rename", request);

        // Assert
        httpResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await httpResponse.Content.ReadFromJsonAsync<RenameResponseDto>(JsonOptions);
        dto.Should().NotBeNull();
        dto!.NewPath.Should().Be("/sdcard/ROMs/new.zip");
    }

    [Fact]
    public async Task Rename_InvalidName_Returns400()
    {
        // Arrange
        var request = new RenameRequestDto("/sdcard/ROMs/old.zip", "bad*name.zip");
        var error = new Error("INVALID_NAME", "O nome contém caracteres inválidos: *");

        _fileStorageService.RenameAsync("/sdcard/ROMs/old.zip", "bad*name.zip", Arg.Any<CancellationToken>())
            .Returns(Result<RenameResponse>.Failure(error));

        // Act
        var httpResponse = await _client.PutAsJsonAsync("/api/files/rename", request);

        // Assert
        httpResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = await httpResponse.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        problem.Should().NotBeNull();
        problem!.Extensions["code"]!.ToString().Should().Be("INVALID_NAME");
    }

    [Fact]
    public async Task Rename_ItemNotFound_Returns404()
    {
        // Arrange
        var request = new RenameRequestDto("/sdcard/ROMs/missing.zip", "new.zip");
        var error = new Error("PATH_NOT_FOUND", "Item de origem não encontrado");

        _fileStorageService.RenameAsync("/sdcard/ROMs/missing.zip", "new.zip", Arg.Any<CancellationToken>())
            .Returns(Result<RenameResponse>.Failure(error));

        // Act
        var httpResponse = await _client.PutAsJsonAsync("/api/files/rename", request);

        // Assert
        httpResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var problem = await httpResponse.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        problem.Should().NotBeNull();
        problem!.Extensions["code"]!.ToString().Should().Be("PATH_NOT_FOUND");
    }

    [Fact]
    public async Task Rename_NameConflict_Returns409()
    {
        // Arrange
        var request = new RenameRequestDto("/sdcard/ROMs/old.zip", "existing.zip");
        var error = new Error("NAME_ALREADY_EXISTS", "Já existe um item com esse nome no diretório.");

        _fileStorageService.RenameAsync("/sdcard/ROMs/old.zip", "existing.zip", Arg.Any<CancellationToken>())
            .Returns(Result<RenameResponse>.Failure(error));

        // Act
        var httpResponse = await _client.PutAsJsonAsync("/api/files/rename", request);

        // Assert
        httpResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var problem = await httpResponse.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        problem.Should().NotBeNull();
        problem!.Extensions["code"]!.ToString().Should().Be("NAME_ALREADY_EXISTS");
    }

    #endregion

    #region DELETE /api/files — Delete Item

    [Fact]
    public async Task Delete_ValidPath_ReturnsOkWithDeletedPath()
    {
        // Arrange
        var response = new DeleteResponse("/sdcard/ROMs/file.zip");

        _fileStorageService.DeleteAsync("/sdcard/ROMs/file.zip", Arg.Any<CancellationToken>())
            .Returns(Result<DeleteResponse>.Success(response));

        // Act
        var httpResponse = await _client.DeleteAsync("/api/files?path=/sdcard/ROMs/file.zip");

        // Assert
        httpResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await httpResponse.Content.ReadFromJsonAsync<DeleteResponseDto>(JsonOptions);
        dto.Should().NotBeNull();
        dto!.DeletedPath.Should().Be("/sdcard/ROMs/file.zip");
    }

    [Fact]
    public async Task Delete_PathNotAllowed_Returns422()
    {
        // Arrange
        var error = new Error("PATH_NOT_ALLOWED", "Caminho não permitido: prefixo inválido");

        _fileStorageService.DeleteAsync("/system/app", Arg.Any<CancellationToken>())
            .Returns(Result<DeleteResponse>.Failure(error));

        // Act
        var httpResponse = await _client.DeleteAsync("/api/files?path=/system/app");

        // Assert
        httpResponse.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var problem = await httpResponse.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        problem.Should().NotBeNull();
        problem!.Status.Should().Be(422);
        problem.Extensions.Should().ContainKey("code");
        problem.Extensions["code"]!.ToString().Should().Be("PATH_NOT_ALLOWED");
    }

    [Fact]
    public async Task Delete_PathNotFound_Returns404()
    {
        // Arrange
        var error = new Error("PATH_NOT_FOUND", "O caminho não foi encontrado: /sdcard/ROMs/missing.zip");

        _fileStorageService.DeleteAsync("/sdcard/ROMs/missing.zip", Arg.Any<CancellationToken>())
            .Returns(Result<DeleteResponse>.Failure(error));

        // Act
        var httpResponse = await _client.DeleteAsync("/api/files?path=/sdcard/ROMs/missing.zip");

        // Assert
        httpResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var problem = await httpResponse.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        problem.Should().NotBeNull();
        problem!.Extensions["code"]!.ToString().Should().Be("PATH_NOT_FOUND");
    }

    [Fact]
    public async Task Delete_PermissionDenied_Returns403()
    {
        // Arrange
        var error = new Error("PERMISSION_DENIED", "Permissão negada ao excluir: /sdcard/ROMs/protected.zip");

        _fileStorageService.DeleteAsync("/sdcard/ROMs/protected.zip", Arg.Any<CancellationToken>())
            .Returns(Result<DeleteResponse>.Failure(error));

        // Act
        var httpResponse = await _client.DeleteAsync("/api/files?path=/sdcard/ROMs/protected.zip");

        // Assert
        httpResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var problem = await httpResponse.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        problem.Should().NotBeNull();
        problem!.Status.Should().Be(403);
        problem.Extensions.Should().ContainKey("code");
        problem.Extensions["code"]!.ToString().Should().Be("PERMISSION_DENIED");
    }

    #endregion

    #region Test Factory

    /// <summary>
    /// Custom WebApplicationFactory that replaces real services with mocked ones.
    /// This allows testing the full HTTP pipeline (middleware, routing, serialization)
    /// without requiring a real ADB connection.
    /// </summary>
    public sealed class FileApiFactory : WebApplicationFactory<Program>
    {
        public IFileStorageService FileStorageService { get; } = Substitute.For<IFileStorageService>();
        public IAdbConnectionManager ConnectionManager { get; } = Substitute.For<IAdbConnectionManager>();
        public ITransferService TransferService { get; } = Substitute.For<ITransferService>();

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                // Remove real service registrations and replace with mocks
                ReplaceService<IFileStorageService>(services, FileStorageService);
                ReplaceService<IAdbConnectionManager>(services, ConnectionManager);
                ReplaceService<ITransferService>(services, TransferService);

                // Remove HeartbeatService background service to prevent it from running during tests
                var hostedServiceDescriptors = services
                    .Where(d => d.ServiceType == typeof(IHostedService))
                    .ToList();

                foreach (var descriptor in hostedServiceDescriptors)
                {
                    services.Remove(descriptor);
                }
            });

            return base.CreateHost(builder);
        }

        private static void ReplaceService<TService>(IServiceCollection services, TService mock)
            where TService : class
        {
            var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(TService));
            if (descriptor is not null)
                services.Remove(descriptor);

            services.AddSingleton(mock);
        }
    }

    #endregion
}
