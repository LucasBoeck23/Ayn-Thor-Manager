using AynThorManager.Core.Interfaces;
using AynThorManager.Core.Models;

namespace AynThorManager.Services.Tests.Fakes;

/// <summary>
/// Fake PC-side prerequisite checker. Returns a configurable list; by default every
/// check is ok. <see cref="WithMissing"/> flips one entry to not-ok so the orchestrator's
/// gate (REVERSE_PREREQUISITES_MISSING) can be exercised.
/// </summary>
internal sealed class FakeSunshinePrerequisites : ISunshinePrerequisites
{
    private IReadOnlyList<Prerequisite> _result =
    [
        new Prerequisite("Sunshine instalado", true),
        new Prerequisite("Encoder de hardware (VAAPI)", true),
        new Prerequisite("Captura de tela (Portal)", true),
    ];

    public int CallCount { get; private set; }

    public FakeSunshinePrerequisites AllOk() => this;

    public FakeSunshinePrerequisites WithMissing(string name = "Sunshine instalado")
    {
        _result =
        [
            new Prerequisite(name, false, "Instale pelo site oficial e tente novamente."),
            new Prerequisite("Encoder de hardware (VAAPI)", true),
            new Prerequisite("Captura de tela (Portal)", true),
        ];
        return this;
    }

    /// <summary>
    /// Flips the best-effort hardware-encoder check to not-ok (likely software fallback)
    /// while everything blocking stays satisfied. Uses the real backend name so
    /// <see cref="Prerequisite.IsBlocking"/> classifies it as a non-blocking warning
    /// (Req 5.2): the start must still proceed.
    /// </summary>
    public FakeSunshinePrerequisites WithSoftwareEncodeWarning()
    {
        _result =
        [
            new Prerequisite("Sunshine instalado", true),
            new Prerequisite(
                "Encode por hardware (VAAPI)",
                false,
                "O Sunshine pode cair para encode por software, o que degrada a performance."),
            new Prerequisite("Captura via Portal (Wayland)", true),
        ];
        return this;
    }

    public Task<IReadOnlyList<Prerequisite>> CheckAsync(CancellationToken ct = default)
    {
        CallCount++;
        return Task.FromResult(_result);
    }
}
