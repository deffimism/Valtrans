namespace Valtrans.TestArena.Models;

public sealed class ArenaScenario
{
    public string Scenario { get; set; } = "";
    public string Resolution { get; set; } = "1920x1080";
    public int Dpi { get; set; } = 100;
    public List<string> Tags { get; set; } = new();
    public ArenaChatSettings Chat { get; set; } = new();
    public ArenaBackgroundSettings Background { get; set; } = new();
    public ArenaFuzzSettings Fuzz { get; set; } = new();
    public ArenaAnimationSettings Animation { get; set; } = new();
    public List<ArenaEvent> Events { get; set; } = new();
    public List<ArenaExpectation> Expectations { get; set; } = new();
}

public sealed class ArenaBackgroundSettings
{
    public string Mode { get; set; } = "static-dark";
    public double Brightness { get; set; } = 1.0;
    public double MotionSpeed { get; set; } = 0;
}

public sealed class ArenaFuzzSettings
{
    public double OpacityMin { get; set; } = 1.0;
    public double OpacityMax { get; set; } = 1.0;
    public double FontSizeJitter { get; set; } = 0;
    public double BrightnessJitter { get; set; } = 0;
    public double BlurRadiusMax { get; set; } = 0;
}

public sealed class ArenaChatSettings
{
    public double FontSize { get; set; } = 16;
    public double Opacity { get; set; } = 0.9;
    public double LineSpacing { get; set; } = 4;
    public int FadeInMs { get; set; } = 0;
    public int MaxVisibleLines { get; set; } = 12;
    public bool ScrollEnabled { get; set; }
}

public sealed class ArenaAnimationSettings
{
    public bool ChatFadeIn { get; set; }
    public bool ChatScroll { get; set; }
    public bool BackgroundMotion { get; set; }
}

public sealed class ArenaEvent
{
    public int AtMs { get; set; }
    public string Speaker { get; set; } = "PlayerA";
    public string Text { get; set; } = "";
    public string Channel { get; set; } = "TEAM";
}

public sealed class ArenaExpectation
{
    public string Source { get; set; } = "";
    public ArenaExpectedFacts? Facts { get; set; }
    public List<string> AcceptedTranslations { get; set; } = new();
}

public sealed class ArenaExpectedFacts
{
    public string? Agent { get; set; }
    public string? State { get; set; }
    public string? Location { get; set; }
    public int? Count { get; set; }
}
