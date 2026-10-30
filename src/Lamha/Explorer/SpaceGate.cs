namespace Lamha.Explorer;

/// <summary>Decides key by key whether the hook swallows it. Kept free of Win32 so every path can be tested.</summary>
public sealed class SpaceGate
{
    const int TypeAheadMs = 1000;

    bool _down, _swallowing;
    long _lastCharKey = long.MinValue / 2;

    /// <returns>True to swallow the key. <paramref name="fire"/> is the Explorer window to preview, or 0.</returns>
    public bool OnKey(int vk, bool down, bool up, long nowMs, Func<bool> modifierHeld, Func<nint> target, out nint fire)
    {
        fire = 0;
        if (vk != Native.VK_SPACE)
        {
            if (down && IsCharKey(vk)) _lastCharKey = nowMs;
            return false;
        }

        if (down)
        {
            if (!_down) // ignore auto-repeat, otherwise holding Space would flicker the preview
            {
                _down = true;
                _swallowing = false;
                // Typing a file name in the list uses Space too, so it stays with Explorer just after a letter.
                if (nowMs - _lastCharKey > TypeAheadMs && !modifierHeld())
                {
                    fire = target();
                    _swallowing = fire != 0;
                }
            }
            return _swallowing;
        }

        if (up)
        {
            _down = false;
            bool swallow = _swallowing;
            _swallowing = false;
            return swallow;
        }
        return false;
    }

    static bool IsCharKey(int vk) => vk is (>= 0x30 and <= 0x5A) or (>= 0x60 and <= 0x6F) or (>= 0xBA and <= 0xDF);
}
