namespace OmenCore.Linux.Hardware;

/// <summary>
/// Pure payload helpers for the DKMS hp-wmi four-zone interface (<c>fourzone_color</c> /
/// <c>fourzone_brightness</c>), kept free of file access so the wire format is unit-tested without hardware.
///
/// Format and the brightness-gate behaviour come from the OMEN Slim 16 (board 8D40) fork by
/// saikiranworks (MIT), reimplemented here: one 24-character hex string, four zones of RRGGBB, and a
/// separate 0-255 brightness node. Colour writes can succeed while that node reads 0, which leaves the
/// keyboard looking dead.
/// </summary>
public static class LinuxFourZonePayload
{
    public const int Zones = 4;
    public const int PayloadLength = Zones * 6;
    public const int MaxRawBrightness = 255;

    /// <summary>
    /// Exactly 24 lowercase hex characters: short input is zero-padded, long input truncated, and
    /// anything that isn't hex is treated as black so a corrupt read can't poison the next write.
    /// </summary>
    public static string Normalize(string? current)
    {
        var text = (current ?? string.Empty).Trim();
        if (text.Length > PayloadLength) text = text[..PayloadLength];
        if (text.Length < PayloadLength) text = text.PadRight(PayloadLength, '0');
        return text.All(Uri.IsHexDigit) ? text.ToLowerInvariant() : new string('0', PayloadLength);
    }

    public static string Hex(byte r, byte g, byte b) => $"{r:x2}{g:x2}{b:x2}";

    /// <summary>All four zones the same colour.</summary>
    public static string Uniform(byte r, byte g, byte b)
    {
        var hex = Hex(r, g, b);
        return hex + hex + hex + hex;
    }

    /// <summary>Replace one zone (0-3) in an existing payload, leaving the others as they are.</summary>
    public static string WithZone(string? current, int zone, byte r, byte g, byte b)
    {
        if (zone < 0 || zone >= Zones) throw new ArgumentOutOfRangeException(nameof(zone));
        var payload = Normalize(current);
        return payload[..(zone * 6)] + Hex(r, g, b) + payload[((zone + 1) * 6)..];
    }

    public static bool IsBlack(byte r, byte g, byte b) => r == 0 && g == 0 && b == 0;

    public static int PercentToRaw(int percent) =>
        Math.Clamp((int)Math.Round(Math.Clamp(percent, 0, 100) * MaxRawBrightness / 100.0), 0, MaxRawBrightness);

    public static int RawToPercent(int raw) =>
        Math.Clamp((int)Math.Round(Math.Clamp(raw, 0, MaxRawBrightness) * 100.0 / MaxRawBrightness), 0, 100);

    /// <summary>
    /// Whether a colour write should also raise a zero brightness gate. A non-black colour written to a
    /// dark keyboard almost always means "show me this", so the gate is raised unless the user opted out
    /// with OMENCORE_SKIP_BRIGHTNESS_INIT=1 (an intentional "off" they want preserved).
    /// </summary>
    public static bool ShouldRaiseBrightnessGate(int currentRaw, bool colorIsBlack, bool optedOut) =>
        currentRaw == 0 && !colorIsBlack && !optedOut;
}
