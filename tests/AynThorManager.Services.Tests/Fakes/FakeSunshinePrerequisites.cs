using AynThorManager.Core.Interfaces;
using AynThorManager.Core.Models;

namespace AynThorManager.Services.Tests.Fakes;

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
