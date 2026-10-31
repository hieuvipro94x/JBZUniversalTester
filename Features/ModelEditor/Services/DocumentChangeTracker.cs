using System.Text.Json;
using JBZUniversalTester.ModelEditor.Models;

namespace JBZUniversalTester.ModelEditor.Services;

public sealed class DocumentChangeTracker
{
    private string? _savedContent;
    private string? _initialContent;

    public void Reset(ModelDocument? initialDocument = null)
    {
        _savedContent = null;
        _initialContent = initialDocument is null ? null : Snapshot(initialDocument);
    }

    public void MarkSaved(ModelDocument document) => _savedContent = Snapshot(document);

    public bool HasUnsavedChanges(ModelDocument document) =>
        _savedContent is null || _savedContent != Snapshot(document);

    public bool HasChanges(ModelDocument document)
    {
        var baseline = _savedContent ?? _initialContent;
        return baseline is null || baseline != Snapshot(document);
    }

    private static string Snapshot(ModelDocument document) => JsonSerializer.Serialize(new
    {
        document.Base,
        document.UseImage,
        document.ImageFile,
        document.Connectors,
        Pins = document.Pins.OrderBy(pin => pin.Io).Select(pin => new
        {
            pin.Io,
            pin.Connector,
            pin.PinNo,
            pin.Line,
            pin.Add,
            pin.Thickness,
            pin.Color,
            pin.TestNo,
            pin.MainIo,
            pin.Coupling
        }),
        CommonExtras = document.CommonExtras.OrderBy(pair => pair.Key, StringComparer.Ordinal)
    });
}
