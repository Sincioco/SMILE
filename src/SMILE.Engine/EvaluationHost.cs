namespace SMILE.Engine;

public static class SmileKeyCodes
{
    public const long None = 0;
    public const long W = 1;
    public const long A = 2;
    public const long S = 3;
    public const long D = 4;
    public const long Up = 10;
    public const long Down = 11;
    public const long Left = 12;
    public const long Right = 13;
    public const long Enter = 14;
    public const long Escape = 15;
    public const long Space = 16;
    public const long Digit1 = 17;
    public const long Digit2 = 18;
    public const long Other = 19;
    public const long Digit3 = 20;
    public const long Tab = 21;
    public const long Digit4 = 22;
}

public interface ISmileEvaluationHost
{
    long ReadKeyNonBlocking();

    void ClearScreen(string outputSnapshot);

    void MoveCursor(long column, long row, string outputSnapshot);

    void SetTextColor(SmileTextColor foreground, SmileTextColor background);

    void ResetTextColor();

    void WaitMilliseconds(long duration);

    long MonotonicMilliseconds { get; }

    long NextRandomInclusive(long lowerBound, long upperBound);
}

public static class SmileRuntimeRules
{
    public const long MaximumWaitMilliseconds = uint.MaxValue;

    public static long NormalizeWaitMilliseconds(long duration) =>
        Math.Clamp(duration, 0, MaximumWaitMilliseconds);
}

public sealed record SmileEvaluationOptions(
    ISmileEvaluationHost? Host = null,
    long StatementBudget = 1_000_000);

public sealed record SmileTimedKeyEvent(long AtMilliseconds, long KeyCode);

public sealed record SmileCursorMove(long Column, long Row);

public sealed record SmileTextColorChange(
    SmileTextColor? Foreground,
    SmileTextColor? Background,
    bool IsDefault);

// The evaluator uses a virtual host by default so Wait never slows tests and
// Get Key never reaches out to a developer's physical terminal. Tests and
// teaching tools can enqueue an exact event/random script and inspect frames.
public sealed class ScriptedSmileEvaluationHost : ISmileEvaluationHost
{
    private readonly Queue<long> _keys;
    private readonly Queue<SmileTimedKeyEvent> _timedKeys;
    private readonly Queue<long> _randomValues;
    private readonly Random _random;
    private readonly List<string> _screenFrames = new();
    private readonly List<long> _waits = new();
    private readonly List<SmileCursorMove> _cursorMoves = new();
    private readonly List<SmileTextColorChange> _textColorChanges = new();
    private int _lastFrameStart;

    public ScriptedSmileEvaluationHost(
        IEnumerable<long>? keys = null,
        IEnumerable<long>? randomValues = null,
        int randomSeed = 1,
        long initialMilliseconds = 0,
        IEnumerable<SmileTimedKeyEvent>? timedKeys = null)
    {
        _keys = new Queue<long>(keys ?? Array.Empty<long>());
        _timedKeys = new Queue<SmileTimedKeyEvent>(
            (timedKeys ?? Array.Empty<SmileTimedKeyEvent>())
                .OrderBy(item => item.AtMilliseconds));
        _randomValues = new Queue<long>(randomValues ?? Array.Empty<long>());
        _random = new Random(randomSeed);
        MonotonicMilliseconds = initialMilliseconds;
    }

    public IReadOnlyList<string> ScreenFrames => _screenFrames;

    public IReadOnlyList<long> Waits => _waits;

    public IReadOnlyList<SmileCursorMove> CursorMoves => _cursorMoves;

    public IReadOnlyList<SmileTextColorChange> TextColorChanges => _textColorChanges;

    public long MonotonicMilliseconds { get; private set; }

    public int RemainingKeyCount => _keys.Count + _timedKeys.Count;

    public int RemainingRandomCount => _randomValues.Count;

    public long ReadKeyNonBlocking()
    {
        if (_keys.Count > 0)
        {
            return _keys.Dequeue();
        }

        return _timedKeys.Count > 0 && _timedKeys.Peek().AtMilliseconds <= MonotonicMilliseconds
            ? _timedKeys.Dequeue().KeyCode
            : SmileKeyCodes.None;
    }

    public void ClearScreen(string outputSnapshot)
    {
        ArgumentNullException.ThrowIfNull(outputSnapshot);
        int start = Math.Min(_lastFrameStart, outputSnapshot.Length);
        _screenFrames.Add(outputSnapshot[start..]);
        _lastFrameStart = outputSnapshot.Length;
    }

    public void MoveCursor(long column, long row, string outputSnapshot)
    {
        ArgumentNullException.ThrowIfNull(outputSnapshot);
        _cursorMoves.Add(new SmileCursorMove(column, row));
        if (column == 1 && row == 1)
        {
            int start = Math.Min(_lastFrameStart, outputSnapshot.Length);
            _screenFrames.Add(outputSnapshot[start..]);
            _lastFrameStart = outputSnapshot.Length;
        }
    }

    public void SetTextColor(SmileTextColor foreground, SmileTextColor background) =>
        _textColorChanges.Add(new SmileTextColorChange(foreground, background, false));

    public void ResetTextColor() =>
        _textColorChanges.Add(new SmileTextColorChange(null, null, true));

    public void WaitMilliseconds(long duration)
    {
        long effective = SmileRuntimeRules.NormalizeWaitMilliseconds(duration);
        _waits.Add(effective);
        MonotonicMilliseconds = effective > long.MaxValue - MonotonicMilliseconds
            ? long.MaxValue
            : MonotonicMilliseconds + effective;
    }

    public long NextRandomInclusive(long lowerBound, long upperBound)
    {
        if (lowerBound > upperBound)
        {
            return lowerBound;
        }

        if (_randomValues.Count > 0)
        {
            long scripted = _randomValues.Dequeue();
            if (scripted < lowerBound || scripted > upperBound)
            {
                throw new InvalidOperationException(
                    $"Scripted Random value {scripted} is outside {lowerBound} through {upperBound}.");
            }

            return scripted;
        }

        ulong range = unchecked((ulong)upperBound - (ulong)lowerBound + 1UL);
        ulong sample = NextUInt64();
        if (range != 0)
        {
            ulong threshold = unchecked(0UL - range) % range;
            while (sample < threshold)
            {
                sample = NextUInt64();
            }

            sample %= range;
        }

        return unchecked((long)(unchecked((ulong)lowerBound) + sample));
    }

    private ulong NextUInt64()
    {
        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        _random.NextBytes(bytes);
        return BitConverter.ToUInt64(bytes);
    }
}
