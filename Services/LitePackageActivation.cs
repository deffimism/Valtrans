using System.IO;

namespace Valtrans.Services;

public sealed partial class ValtransLiteService
{
    internal static void ActivateVerifiedPackage(string staging, string destination)
    {
        staging = Path.GetFullPath(staging);
        destination = Path.GetFullPath(destination);
        var parent = Path.GetDirectoryName(destination);
        if (parent is null || !string.Equals(Path.GetDirectoryName(staging), parent, StringComparison.OrdinalIgnoreCase)
            || staging.Equals(destination, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Model staging must be a distinct sibling directory.");
        if (!Directory.Exists(staging)) throw new DirectoryNotFoundException(staging);
        // Refuse indirection before any rename or recursive cleanup.
        foreach (var directory in new[] { staging, destination, parent })
            if (Directory.Exists(directory) && (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Model activation does not accept linked directories.");
        var previous = destination + ".previous-" + Guid.NewGuid().ToString("N");
        var hadPrevious = Directory.Exists(destination);
        if (hadPrevious) Directory.Move(destination, previous);
        try
        {
            Directory.Move(staging, destination);
        }
        catch
        {
            if (hadPrevious && !Directory.Exists(destination)) Directory.Move(previous, destination);
            throw;
        }
        // The previous model is recoverable if a loaded process prevents cleanup.
        // Activation itself has succeeded; do not report it as an install failure.
        if (hadPrevious)
        {
            try { Directory.Delete(previous, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
