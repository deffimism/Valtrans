namespace Valtrans.TestArena.Services;

public sealed class DeterministicRandom
{
    private int _state;

    public DeterministicRandom(int seed) => _state = seed == 0 ? 1 : seed;

    public int NextInt(int minInclusive, int maxExclusive)
    {
        _state = unchecked(_state * 1664525 + 1013904223);
        var range = (uint)(maxExclusive - minInclusive);
        return minInclusive + (int)(_state % range);
    }

    public double NextDouble()
    {
        _state = unchecked(_state * 1664525 + 1013904223);
        return (_state & 0x7FFFFFFF) / (double)int.MaxValue;
    }
}
