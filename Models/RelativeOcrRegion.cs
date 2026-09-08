namespace Valtrans.Models;

// Relative to the full chat region; follows window movement and region resizing.
public sealed class RelativeOcrRegion
{
    public double X { get; set; }
    public double Y { get; set; } = 0.65;
    public double Width { get; set; } = 1;
    public double Height { get; set; } = 0.35;
}
