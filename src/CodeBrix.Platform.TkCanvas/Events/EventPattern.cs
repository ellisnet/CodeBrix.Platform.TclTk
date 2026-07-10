using System;
using System.Collections.Generic;

namespace CodeBrix.Platform.TkCanvas.Events;

/// <summary>
/// A parsed Tk event pattern such as <c>&lt;ButtonPress-1&gt;</c>,
/// <c>&lt;Control-KeyPress-s&gt;</c>, <c>&lt;B1-Motion&gt;</c>,
/// <c>&lt;Double-1&gt;</c>, or the virtual <c>&lt;&lt;ListboxSelect&gt;&gt;</c>.
/// Knows how to match an event and how specific it is relative to other
/// matching patterns (Tk fires only the most specific binding per bind tag).
/// </summary>
public sealed class EventPattern : IEquatable<EventPattern>
{
    /// <summary>The event type the pattern demands.</summary>
    public TkEventType Type { get; private set; }

    /// <summary>The modifiers the pattern demands (event may carry more).</summary>
    public EventModifiers Modifiers { get; private set; }

    /// <summary>The demanded button (1-5) for button patterns, or 0 for any.</summary>
    public int Button { get; private set; }

    /// <summary>The demanded keysym for key patterns, or null for any.</summary>
    public string KeySym { get; private set; }

    /// <summary>The virtual event name for <c>&lt;&lt;Name&gt;&gt;</c> patterns, or null.</summary>
    public string VirtualName { get; private set; }

    /// <summary>The original pattern text (normalized input form).</summary>
    public string Text { get; private set; }

    private EventPattern()
    {
    }

    /// <summary>
    /// Parses a Tk event pattern. Supported forms: <c>&lt;Type&gt;</c>,
    /// <c>&lt;Modifier-...-Type-Detail&gt;</c>, the button shorthand
    /// <c>&lt;1&gt;</c>/<c>&lt;Double-1&gt;</c> (a ButtonPress), the
    /// <c>Button</c>/<c>Key</c> aliases, and virtual events
    /// <c>&lt;&lt;Name&gt;&gt;</c>.
    /// </summary>
    /// <param name="text">The pattern text, including angle brackets.</param>
    /// <returns>The parsed pattern.</returns>
    public static EventPattern Parse(string text)
    {
        if (string.IsNullOrEmpty(text)) { throw new ArgumentException("empty event pattern", nameof(text)); }

        var pattern = new EventPattern { Text = text };

        if (text.StartsWith("<<", StringComparison.Ordinal) && text.EndsWith(">>", StringComparison.Ordinal))
        {
            pattern.Type = TkEventType.Virtual;
            pattern.VirtualName = text.Substring(2, text.Length - 4);
            if (pattern.VirtualName.Length == 0) { throw new ArgumentException("empty virtual event name", nameof(text)); }
            return pattern;
        }

        if (!(text.StartsWith("<", StringComparison.Ordinal) && text.EndsWith(">", StringComparison.Ordinal)))
        {
            throw new ArgumentException("event pattern must be enclosed in <>: " + text, nameof(text));
        }

        string body = text.Substring(1, text.Length - 2);
        string[] parts = body.Split('-');

        // Scan modifiers from the left; what remains is type and detail.
        int index = 0;
        while (index < parts.Length)
        {
            EventModifiers modifier;
            if (!TryParseModifier(parts[index], out modifier)) { break; }
            pattern.Modifiers |= modifier;
            index++;
        }

        if (index >= parts.Length)
        {
            throw new ArgumentException("event pattern has no event type: " + text, nameof(text));
        }

        string typeWord = parts[index];
        string detail = (index + 1 < parts.Length) ? string.Join("-", parts, index + 1, parts.Length - index - 1) : null;

        int shorthandButton;
        if (int.TryParse(typeWord, out shorthandButton) && detail == null)
        {
            // <1> — button shorthand for <ButtonPress-1>.
            pattern.Type = TkEventType.ButtonPress;
            pattern.Button = ValidateButton(shorthandButton, text);
            return pattern;
        }

        switch (typeWord)
        {
            case "ButtonPress":
            case "Button":
                pattern.Type = TkEventType.ButtonPress;
                if (detail != null) { pattern.Button = ValidateButton(ParseIntDetail(detail, text), text); }
                break;
            case "ButtonRelease":
                pattern.Type = TkEventType.ButtonRelease;
                if (detail != null) { pattern.Button = ValidateButton(ParseIntDetail(detail, text), text); }
                break;
            case "Motion":
                pattern.Type = TkEventType.Motion;
                break;
            case "KeyPress":
            case "Key":
                pattern.Type = TkEventType.KeyPress;
                if (detail != null) { pattern.KeySym = detail; }
                break;
            case "KeyRelease":
                pattern.Type = TkEventType.KeyRelease;
                if (detail != null) { pattern.KeySym = detail; }
                break;
            case "Enter":
                pattern.Type = TkEventType.Enter;
                break;
            case "Leave":
                pattern.Type = TkEventType.Leave;
                break;
            case "FocusIn":
                pattern.Type = TkEventType.FocusIn;
                break;
            case "FocusOut":
                pattern.Type = TkEventType.FocusOut;
                break;
            case "Configure":
                pattern.Type = TkEventType.Configure;
                break;
            case "Destroy":
                pattern.Type = TkEventType.Destroy;
                break;
            case "Map":
                pattern.Type = TkEventType.Map;
                break;
            case "Unmap":
                pattern.Type = TkEventType.Unmap;
                break;
            case "MouseWheel":
                pattern.Type = TkEventType.MouseWheel;
                break;
            default:
                // A bare keysym pattern like <Escape>, <Return>, <Down>, <a>.
                // Unknown keysyms are accepted (no keysym table; they just
                // never match), but an EMPTY one is malformed.
                if (typeWord.Length == 0)
                {
                    throw new ArgumentException("event pattern has no event type: " + text, nameof(text));
                }
                pattern.Type = TkEventType.KeyPress;
                pattern.KeySym = (detail == null) ? typeWord : typeWord + "-" + detail;
                break;
        }

        return pattern;
    }

    private static bool TryParseModifier(string word, out EventModifiers modifier)
    {
        switch (word)
        {
            case "Shift": modifier = EventModifiers.Shift; return true;
            case "Lock": modifier = EventModifiers.Lock; return true;
            case "Control": modifier = EventModifiers.Control; return true;
            case "Alt": case "Mod1": case "M1": modifier = EventModifiers.Alt; return true;
            case "Meta": case "M": modifier = EventModifiers.Meta; return true;
            case "Command": case "Cmd": modifier = EventModifiers.Command; return true;
            case "B1": case "Button1": modifier = EventModifiers.Button1; return true;
            case "B2": case "Button2": modifier = EventModifiers.Button2; return true;
            case "B3": case "Button3": modifier = EventModifiers.Button3; return true;
            case "B4": case "Button4": modifier = EventModifiers.Button4; return true;
            case "B5": case "Button5": modifier = EventModifiers.Button5; return true;
            case "Double": modifier = EventModifiers.Double; return true;
            case "Triple": modifier = EventModifiers.Triple; return true;
            case "Quadruple": modifier = EventModifiers.Quadruple; return true;
            default: modifier = EventModifiers.None; return false;
        }
    }

    private static int ParseIntDetail(string detail, string pattern)
    {
        int value;
        if (!int.TryParse(detail, out value))
        {
            throw new ArgumentException("bad button detail in event pattern: " + pattern, nameof(pattern));
        }
        return value;
    }

    private static int ValidateButton(int button, string pattern)
    {
        if (button < 1 || button > 5)
        {
            throw new ArgumentException("bad button number in event pattern: " + pattern, nameof(pattern));
        }
        return button;
    }

    /// <summary>
    /// Whether this pattern matches <paramref name="tkEvent"/>: the type must
    /// agree, every demanded modifier must be present in the event state,
    /// a demanded detail (button/keysym/virtual name) must agree, and a
    /// demanded click count (Double/Triple) must be satisfied.
    /// </summary>
    /// <param name="tkEvent">The event to test.</param>
    /// <returns>True when the pattern applies to the event.</returns>
    public bool Matches(TkEvent tkEvent)
    {
        if (Type != tkEvent.Type) { return false; }

        if (Type == TkEventType.Virtual)
        {
            return string.Equals(VirtualName, tkEvent.VirtualName, StringComparison.Ordinal);
        }

        // Modifier demands (excluding the click-count pseudo-modifiers).
        EventModifiers demanded = Modifiers & ~(EventModifiers.Double | EventModifiers.Triple | EventModifiers.Quadruple);
        if ((tkEvent.State & demanded) != demanded) { return false; }

        // Click-count demands.
        int neededClicks = 1;
        if ((Modifiers & EventModifiers.Double) != 0) { neededClicks = 2; }
        else if ((Modifiers & EventModifiers.Triple) != 0) { neededClicks = 3; }
        else if ((Modifiers & EventModifiers.Quadruple) != 0) { neededClicks = 4; }
        if (neededClicks > 1 && tkEvent.ClickCount < neededClicks) { return false; }

        if (Button != 0 && Button != tkEvent.Button) { return false; }

        if (KeySym != null && !string.Equals(KeySym, tkEvent.KeySym, StringComparison.Ordinal)) { return false; }

        return true;
    }

    /// <summary>
    /// How specific this pattern is; when several bindings of one bind tag
    /// match the same event, the highest specificity fires (a demanded
    /// detail outranks any number of modifiers, like Tk).
    /// </summary>
    /// <returns>The specificity score.</returns>
    public int Specificity()
    {
        int score = 0;
        if (Button != 0 || KeySym != null || VirtualName != null) { score += 100; }
        if ((Modifiers & EventModifiers.Quadruple) != 0) { score += 40; }
        else if ((Modifiers & EventModifiers.Triple) != 0) { score += 30; }
        else if ((Modifiers & EventModifiers.Double) != 0) { score += 20; }

        EventModifiers state = Modifiers & ~(EventModifiers.Double | EventModifiers.Triple | EventModifiers.Quadruple);
        while (state != 0)
        {
            state &= state - 1;
            score++;
        }
        return score;
    }

    /// <inheritdoc/>
    public bool Equals(EventPattern other)
    {
        if (other == null) { return false; }
        return Type == other.Type
                && Modifiers == other.Modifiers
                && Button == other.Button
                && string.Equals(KeySym, other.KeySym, StringComparison.Ordinal)
                && string.Equals(VirtualName, other.VirtualName, StringComparison.Ordinal);
    }

    /// <inheritdoc/>
    public override bool Equals(object obj)
    {
        return Equals(obj as EventPattern);
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        int hash = ((int)Type * 397) ^ (int)Modifiers;
        hash = (hash * 31) ^ Button;
        if (KeySym != null) { hash = (hash * 31) ^ StringComparer.Ordinal.GetHashCode(KeySym); }
        if (VirtualName != null) { hash = (hash * 31) ^ StringComparer.Ordinal.GetHashCode(VirtualName); }
        return hash;
    }

    /// <summary>Returns the original pattern text.</summary>
    /// <returns>The pattern as written.</returns>
    public override string ToString()
    {
        return Text;
    }
}
