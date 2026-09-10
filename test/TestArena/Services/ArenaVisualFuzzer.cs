using Valtrans.TestArena.Models;

namespace Valtrans.TestArena.Services;

public sealed class FuzzedChatStyle
{
    public double FontSize { get; set; }
    public double Opacity { get; set; }
    public double BlurRadius { get; set; }
}

public static class ArenaVisualFuzzer
{
    public static FuzzedChatStyle FuzzLine(ArenaChatSettings chat, ArenaFuzzSettings fuzz, DeterministicRandom random, int lineIndex)
    {
        var style = new FuzzedChatStyle
        {
            FontSize = chat.FontSize,
            Opacity = chat.Opacity,
            BlurRadius = 0
        };
        if (fuzz.FontSizeJitter <= 0 && fuzz.OpacityMin >= fuzz.OpacityMax && fuzz.BrightnessJitter <= 0)
            return style;

        var roll = random.NextDouble();
        var jitter = fuzz.FontSizeJitter;
        style.FontSize = Math.Clamp(chat.FontSize + (roll * 2 - 1) * chat.FontSize * jitter, 10, 32);
        style.Opacity = Math.Clamp(
            chat.Opacity + (random.NextDouble() * 2 - 1) * fuzz.BrightnessJitter,
            fuzz.OpacityMin,
            fuzz.OpacityMax);
        if (fuzz.BlurRadiusMax > 0)
            style.BlurRadius = random.NextDouble() * fuzz.BlurRadiusMax;
        return style;
    }
}
