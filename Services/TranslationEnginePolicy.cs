namespace Valtrans.Services;

// Hybrid's verified callouts are bundled rules; unverified text requires the selected AI.
// Lite remains a separate, explicitly selected engine, not a readiness substitute.
public static class TranslationEnginePolicy
{
    public static bool RequiresLite(string provider) =>
        provider.Equals("Lite", StringComparison.OrdinalIgnoreCase);

    public static bool RequiresLocalAi(string provider) =>
        provider.Equals("Hybrid", StringComparison.OrdinalIgnoreCase) ||
        provider.Equals("Ollama", StringComparison.OrdinalIgnoreCase);

    public static bool IsReady(string provider, bool liteReady, bool localReady) =>
        RequiresLite(provider) ? liteReady : RequiresLocalAi(provider) && localReady;
}
