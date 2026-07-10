using System;
using System.Globalization;
using System.Text;
using CodeBrix.Platform.TclTk._Attributes;
using CodeBrix.Platform.TclTk._Components.Public;
using CodeBrix.Platform.TclTk._Containers.Public;

namespace CodeBrix.Platform.TclTk._Components.Private;

/// <summary>
/// This class implements the engine support for the standard Tcl
/// <c>binary</c> command: the <c>format</c> and <c>scan</c> field-specifier
/// mini-language plus the <c>encode</c> / <c>decode</c> transforms
/// (<c>base64</c>, <c>hex</c>, <c>uuencode</c>).  The semantics replicate
/// stock Tcl 8.6: binary data is represented as a Tcl string whose
/// characters are all in the range 0-255 (the high byte of each character
/// is discarded on conversion to bytes, exactly like Tcl's internal
/// byte-array representation).
/// </summary>
[ObjectId("7a9d8f21-3c65-4f0e-9b47-52e8a1d0c9b3")]
internal static class BinaryOps
{
    #region Private Constants
    /// <summary>
    /// The sentinel count value used when a field specifier has no count.
    /// </summary>
    private const int NoCount = -1;

    /// <summary>
    /// The sentinel count value used when a field specifier has the "*"
    /// (all) count.
    /// </summary>
    private const int AllCount = -2;

    /// <summary>
    /// The lowercase hexadecimal digits, indexed by nibble value.
    /// </summary>
    private const string HexDigits = "0123456789abcdef";

    /// <summary>
    /// The error message emitted when a value-consuming field specifier has
    /// no corresponding argument (format) or variable name (scan).
    /// </summary>
    private const string NotEnoughArguments =
        "not enough arguments for all format specifiers";
    #endregion

    ///////////////////////////////////////////////////////////////////////

    #region Binary String Conversions
    /// <summary>
    /// This method converts a Tcl string into its binary (byte array) form
    /// by discarding the high byte of each character, exactly like Tcl's
    /// byte-array representation.
    /// </summary>
    /// <param name="value">
    /// The string to convert.  This parameter may not be null.
    /// </param>
    /// <returns>
    /// The byte array corresponding to the specified string.
    /// </returns>
    public static byte[] GetBytesFromString(
        string value /* in */
        )
    {
        byte[] bytes = new byte[value.Length];

        for (int index = 0; index < value.Length; index++)
            bytes[index] = (byte)value[index];

        return bytes;
    }

    ///////////////////////////////////////////////////////////////////////

    /// <summary>
    /// This method converts a range of bytes into a Tcl string, mapping
    /// each byte to the character with the same value.
    /// </summary>
    /// <param name="bytes">
    /// The byte array to convert.  This parameter may not be null.
    /// </param>
    /// <param name="startIndex">
    /// The index of the first byte to convert.
    /// </param>
    /// <param name="count">
    /// The number of bytes to convert.
    /// </param>
    /// <returns>
    /// The string corresponding to the specified range of bytes.
    /// </returns>
    public static string GetStringFromBytes(
        byte[] bytes,   /* in */
        int startIndex, /* in */
        int count       /* in */
        )
    {
        char[] characters = new char[count];

        for (int index = 0; index < count; index++)
            characters[index] = (char)bytes[startIndex + index];

        return new string(characters);
    }
    #endregion

    ///////////////////////////////////////////////////////////////////////

    #region Field Specifier Parsing
    /// <summary>
    /// This structure describes one parsed field specifier from a
    /// <c>binary format</c> / <c>binary scan</c> template string.
    /// </summary>
    private struct FieldSpec
    {
        /// <summary>
        /// The field type character (e.g. 'a', 'c', 'H', '@').
        /// </summary>
        public char Code;

        /// <summary>
        /// Non-zero when the 'u' (unsigned) flag was present.  The flag is
        /// only meaningful for integer types being scanned; it is accepted
        /// and ignored everywhere else, matching Tcl.
        /// </summary>
        public bool Unsigned;

        /// <summary>
        /// The count for the field: a non-negative number, or the
        /// <see cref="NoCount" /> / <see cref="AllCount" /> sentinels.
        /// </summary>
        public int Count;
    }

    ///////////////////////////////////////////////////////////////////////

    /// <summary>
    /// This method extracts the next field specifier from the template
    /// string, skipping any whitespace.  It mirrors Tcl's GetFormatSpec:
    /// the specifier is a type character, optionally followed by a 'u'
    /// flag, optionally followed by a count ("*" or decimal digits).
    /// </summary>
    /// <param name="template">
    /// The template string being parsed.
    /// </param>
    /// <param name="index">
    /// The current position within the template; advanced past the
    /// extracted specifier.
    /// </param>
    /// <param name="spec">
    /// Upon success, receives the parsed field specifier.
    /// </param>
    /// <param name="done">
    /// Upon return, non-zero when the end of the template was reached
    /// (in which case no specifier was extracted).
    /// </param>
    /// <param name="error">
    /// Upon failure, receives an appropriate error message.
    /// </param>
    /// <returns>
    /// <see cref="ReturnCode.Ok" /> on success (including when the end of
    /// the template was reached); otherwise, <see cref="ReturnCode.Error" />
    /// when the count is too large to represent.
    /// </returns>
    private static ReturnCode TryGetNextSpec(
        string template,    /* in */
        ref int index,      /* in, out */
        ref FieldSpec spec, /* out */
        out bool done,      /* out */
        ref Result error    /* out */
        )
    {
        while ((index < template.Length) &&
            char.IsWhiteSpace(template[index]))
        {
            index++;
        }

        if (index >= template.Length)
        {
            done = true;
            return ReturnCode.Ok;
        }

        done = false;

        spec.Code = template[index++];
        spec.Unsigned = false;
        spec.Count = NoCount;

        while ((index < template.Length) && (template[index] == 'u'))
        {
            spec.Unsigned = true;
            index++;
        }

        if (index < template.Length)
        {
            char character = template[index];

            if (character == '*')
            {
                spec.Count = AllCount;
                index++;
            }
            else if (char.IsDigit(character))
            {
                long count = 0;

                while ((index < template.Length) &&
                    char.IsDigit(template[index]))
                {
                    count = (count * 10) + (template[index] - '0');

                    if (count > int.MaxValue)
                    {
                        error = "integer value too large to represent";
                        return ReturnCode.Error;
                    }

                    index++;
                }

                spec.Count = (int)count;
            }
        }

        return ReturnCode.Ok;
    }

    ///////////////////////////////////////////////////////////////////////

    /// <summary>
    /// This method returns the size in bytes of the numeric field type
    /// represented by the specified type character, or zero when the
    /// character is not a numeric field type.
    /// </summary>
    /// <param name="code">
    /// The field type character.
    /// </param>
    /// <returns>
    /// The size in bytes of one value of the field type, or zero.
    /// </returns>
    private static int GetNumericSize(
        char code /* in */
        )
    {
        switch (code)
        {
            case 'c':
                return 1;
            case 's':
            case 'S':
            case 't':
                return 2;
            case 'i':
            case 'I':
            case 'n':
            case 'f':
            case 'r':
            case 'R':
                return 4;
            case 'w':
            case 'W':
            case 'm':
            case 'd':
            case 'q':
            case 'Q':
                return 8;
            default:
                return 0;
        }
    }

    ///////////////////////////////////////////////////////////////////////

    /// <summary>
    /// This method determines whether the specified numeric field type
    /// character is a floating-point type.
    /// </summary>
    /// <param name="code">
    /// The field type character.
    /// </param>
    /// <returns>
    /// Non-zero when the field type is floating-point.
    /// </returns>
    private static bool IsFloatingPoint(
        char code /* in */
        )
    {
        switch (code)
        {
            case 'f':
            case 'r':
            case 'R':
            case 'd':
            case 'q':
            case 'Q':
                return true;
            default:
                return false;
        }
    }

    ///////////////////////////////////////////////////////////////////////

    /// <summary>
    /// This method determines whether the specified numeric field type
    /// character stores its bytes big-endian.  The native-order types
    /// ('t', 'n', 'm', 'f', 'd') follow the endianness of the host.
    /// </summary>
    /// <param name="code">
    /// The field type character.
    /// </param>
    /// <returns>
    /// Non-zero when the field type is big-endian.
    /// </returns>
    private static bool IsBigEndian(
        char code /* in */
        )
    {
        switch (code)
        {
            case 'S':
            case 'I':
            case 'W':
            case 'R':
            case 'Q':
                return true;
            case 't':
            case 'n':
            case 'm':
            case 'f':
            case 'd':
                return !BitConverter.IsLittleEndian;
            default:
                return false;
        }
    }
    #endregion

    ///////////////////////////////////////////////////////////////////////

    #region Number Formatting Helpers
    /// <summary>
    /// This method formats a double value as a Tcl floating-point string:
    /// shortest round-trippable representation, lowercase exponent, a
    /// trailing ".0" for whole numbers, and the Tcl spellings "Inf" /
    /// "-Inf" / "NaN" for the special values.
    /// </summary>
    /// <param name="value">
    /// The value to format.
    /// </param>
    /// <returns>
    /// The Tcl string representation of the specified value.
    /// </returns>
    public static string DoubleToString(
        double value /* in */
        )
    {
        if (double.IsNaN(value))
            return "NaN";

        if (double.IsPositiveInfinity(value))
            return "Inf";

        if (double.IsNegativeInfinity(value))
            return "-Inf";

        string result = value.ToString(CultureInfo.InvariantCulture);

        if (result.IndexOf('E') >= 0)
            result = result.Replace("E", "e");

        if ((result.IndexOf('.') < 0) &&
            (result.IndexOf('e') < 0))
        {
            result += ".0";
        }

        return result;
    }

    ///////////////////////////////////////////////////////////////////////

    /// <summary>
    /// This method parses a Tcl integer value for <c>binary format</c>,
    /// producing the Tcl-compatible error message on failure.
    /// </summary>
    /// <param name="text">
    /// The value to parse.
    /// </param>
    /// <param name="cultureInfo">
    /// The culture-specific information to use.
    /// </param>
    /// <param name="value">
    /// Upon success, receives the parsed value.
    /// </param>
    /// <param name="error">
    /// Upon failure, receives an appropriate error message.
    /// </param>
    /// <returns>
    /// <see cref="ReturnCode.Ok" /> on success; otherwise,
    /// <see cref="ReturnCode.Error" />.
    /// </returns>
    private static ReturnCode GetIntegerValue(
        string text,             /* in */
        CultureInfo cultureInfo, /* in */
        ref long value,          /* out */
        ref Result error         /* out */
        )
    {
        if (Value.GetWideInteger2(
                text, ValueFlags.AnyWideInteger | ValueFlags.AnySignedness,
                cultureInfo, ref value) != ReturnCode.Ok)
        {
            error = String.Format(
                "expected integer but got \"{0}\"", text);

            return ReturnCode.Error;
        }

        return ReturnCode.Ok;
    }

    ///////////////////////////////////////////////////////////////////////

    /// <summary>
    /// This method parses a Tcl floating-point value for
    /// <c>binary format</c>, producing the Tcl-compatible error message on
    /// failure.  Any parsed NaN is normalized to the positive quiet NaN,
    /// matching the bit pattern stock Tcl stores for a NaN parsed from a
    /// string.
    /// </summary>
    /// <param name="text">
    /// The value to parse.
    /// </param>
    /// <param name="cultureInfo">
    /// The culture-specific information to use.
    /// </param>
    /// <param name="value">
    /// Upon success, receives the parsed value.
    /// </param>
    /// <param name="error">
    /// Upon failure, receives an appropriate error message.
    /// </param>
    /// <returns>
    /// <see cref="ReturnCode.Ok" /> on success; otherwise,
    /// <see cref="ReturnCode.Error" />.
    /// </returns>
    private static ReturnCode GetDoubleValue(
        string text,             /* in */
        CultureInfo cultureInfo, /* in */
        ref double value,        /* out */
        ref Result error         /* out */
        )
    {
        Result localError = null;

        if (Value.GetDouble(
                text, cultureInfo, ref value,
                ref localError) != ReturnCode.Ok)
        {
            error = String.Format(
                "expected floating-point number but got \"{0}\"", text);

            return ReturnCode.Error;
        }

        if (double.IsNaN(value))
            value = BitConverter.Int64BitsToDouble(0x7FF8000000000000);

        return ReturnCode.Ok;
    }

    ///////////////////////////////////////////////////////////////////////

    /// <summary>
    /// This method writes one numeric value into the buffer at the
    /// specified position using the size and byte order implied by the
    /// field type character.
    /// </summary>
    /// <param name="code">
    /// The numeric field type character.
    /// </param>
    /// <param name="buffer">
    /// The destination buffer.
    /// </param>
    /// <param name="position">
    /// The position to write at.
    /// </param>
    /// <param name="wideValue">
    /// The integer value to write (for integer field types).
    /// </param>
    /// <param name="doubleValue">
    /// The floating-point value to write (for floating-point field types).
    /// </param>
    private static void WriteNumber(
        char code,         /* in */
        byte[] buffer,     /* in, out */
        int position,      /* in */
        long wideValue,    /* in */
        double doubleValue /* in */
        )
    {
        int size = GetNumericSize(code);
        ulong bits;

        if (IsFloatingPoint(code))
        {
            if (size == 4)
            {
                float floatValue;

                if (double.IsNaN(doubleValue))
                {
                    //
                    // NOTE: Match the positive quiet NaN bit pattern that
                    //       stock Tcl produces when converting a NaN to a
                    //       single-precision value.
                    //
                    bits = 0x7FC00000;
                    goto write;
                }
                else if (doubleValue > float.MaxValue)
                {
                    //
                    // NOTE: Stock Tcl clamps out-of-range values, including
                    //       positive infinity, to the largest finite float.
                    //
                    floatValue = float.MaxValue;
                }
                else if (doubleValue < float.MinValue)
                {
                    floatValue = float.MinValue;
                }
                else
                {
                    floatValue = (float)doubleValue;
                }

                bits = (uint)BitConverter.SingleToInt32Bits(floatValue);
            }
            else
            {
                bits = (ulong)BitConverter.DoubleToInt64Bits(doubleValue);
            }
        }
        else
        {
            bits = (ulong)wideValue;
        }

    write:

        if (IsBigEndian(code))
        {
            for (int index = 0; index < size; index++)
            {
                buffer[position + index] =
                    (byte)(bits >> (8 * (size - 1 - index)));
            }
        }
        else
        {
            for (int index = 0; index < size; index++)
                buffer[position + index] = (byte)(bits >> (8 * index));
        }
    }

    ///////////////////////////////////////////////////////////////////////

    /// <summary>
    /// This method reads one numeric value from the buffer at the
    /// specified position and formats it as a Tcl string, honoring the
    /// size, byte order, and (for integers) signedness implied by the
    /// field type character and the 'u' flag.
    /// </summary>
    /// <param name="code">
    /// The numeric field type character.
    /// </param>
    /// <param name="unsigned">
    /// Non-zero when the 'u' (unsigned) flag was present.
    /// </param>
    /// <param name="bytes">
    /// The source buffer.
    /// </param>
    /// <param name="position">
    /// The position to read from.
    /// </param>
    /// <returns>
    /// The Tcl string representation of the value that was read.
    /// </returns>
    private static string ReadNumber(
        char code,     /* in */
        bool unsigned, /* in */
        byte[] bytes,  /* in */
        int position   /* in */
        )
    {
        int size = GetNumericSize(code);
        ulong bits = 0;

        if (IsBigEndian(code))
        {
            for (int index = 0; index < size; index++)
                bits = (bits << 8) | bytes[position + index];
        }
        else
        {
            for (int index = size - 1; index >= 0; index--)
                bits = (bits << 8) | bytes[position + index];
        }

        if (IsFloatingPoint(code))
        {
            double doubleValue;

            if (size == 4)
            {
                doubleValue = BitConverter.Int32BitsToSingle(
                    unchecked((int)(uint)bits));
            }
            else
            {
                doubleValue = BitConverter.Int64BitsToDouble(
                    unchecked((long)bits));
            }

            return DoubleToString(doubleValue);
        }

        if (unsigned)
        {
            return bits.ToString(CultureInfo.InvariantCulture);
        }

        long wideValue;

        switch (size)
        {
            case 1:
                wideValue = unchecked((sbyte)bits);
                break;
            case 2:
                wideValue = unchecked((short)(ushort)bits);
                break;
            case 4:
                wideValue = unchecked((int)(uint)bits);
                break;
            default:
                wideValue = unchecked((long)bits);
                break;
        }

        return wideValue.ToString(CultureInfo.InvariantCulture);
    }
    #endregion

    ///////////////////////////////////////////////////////////////////////

    #region Binary Format
    /// <summary>
    /// This method implements <c>binary format</c>: it builds a binary
    /// string according to the template, consuming one argument per
    /// value-consuming field specifier.  Like stock Tcl, it runs in two
    /// passes: the first validates the template and argument list and
    /// computes the result length; the second writes the bytes.
    /// </summary>
    /// <param name="interpreter">
    /// The interpreter context.  This parameter may not be null.
    /// </param>
    /// <param name="template">
    /// The template (format) string.
    /// </param>
    /// <param name="arguments">
    /// The command argument list.
    /// </param>
    /// <param name="firstArgumentIndex">
    /// The index within <paramref name="arguments" /> of the first value
    /// argument.
    /// </param>
    /// <param name="result">
    /// Upon success, receives the binary string; upon failure, receives an
    /// appropriate error message.
    /// </param>
    /// <returns>
    /// <see cref="ReturnCode.Ok" /> on success; otherwise,
    /// <see cref="ReturnCode.Error" />.
    /// </returns>
    public static ReturnCode Format(
        Interpreter interpreter, /* in */
        string template,         /* in */
        ArgumentList arguments,  /* in */
        int firstArgumentIndex,  /* in */
        ref Result result        /* out */
        )
    {
        CultureInfo cultureInfo = interpreter.InternalCultureInfo;

        //
        // NOTE: Pass 1 -- validate the template and arguments and compute
        //       the final buffer length by simulating the cursor.
        //
        int index = 0;
        int position = 0;
        int maxPosition = 0;
        int argumentIndex = firstArgumentIndex;
        FieldSpec spec = default(FieldSpec);

        while (true)
        {
            bool done;

            if (TryGetNextSpec(template, ref index,
                    ref spec, out done, ref result) != ReturnCode.Ok)
            {
                return ReturnCode.Error;
            }

            if (done)
                break;

            switch (spec.Code)
            {
                case 'a':
                case 'A':
                case 'b':
                case 'B':
                case 'h':
                case 'H':
                    {
                        if (argumentIndex >= arguments.Count)
                        {
                            result = NotEnoughArguments;
                            return ReturnCode.Error;
                        }

                        string text = arguments[argumentIndex++];

                        int count = (spec.Count == NoCount) ? 1 :
                            (spec.Count == AllCount) ? text.Length :
                            spec.Count;

                        switch (spec.Code)
                        {
                            case 'a':
                            case 'A':
                                position += count;
                                break;
                            case 'b':
                            case 'B':
                                position += (count + 7) / 8;
                                break;
                            default:
                                position += (count + 1) / 2;
                                break;
                        }

                        break;
                    }
                case 'c':
                case 's':
                case 'S':
                case 't':
                case 'i':
                case 'I':
                case 'n':
                case 'w':
                case 'W':
                case 'm':
                case 'f':
                case 'r':
                case 'R':
                case 'd':
                case 'q':
                case 'Q':
                    {
                        if (argumentIndex >= arguments.Count)
                        {
                            result = NotEnoughArguments;
                            return ReturnCode.Error;
                        }

                        int count;

                        if (spec.Count == NoCount)
                        {
                            count = 1;
                        }
                        else
                        {
                            StringList list = null;

                            if (ListOps.GetOrCopyOrSplitList(
                                    interpreter, arguments[argumentIndex],
                                    true, ref list,
                                    ref result) != ReturnCode.Ok)
                            {
                                return ReturnCode.Error;
                            }

                            if (spec.Count == AllCount)
                            {
                                count = list.Count;
                            }
                            else if (spec.Count > list.Count)
                            {
                                result = "number of elements in list" +
                                    " does not match count";

                                return ReturnCode.Error;
                            }
                            else
                            {
                                count = spec.Count;
                            }
                        }

                        argumentIndex++;
                        position += GetNumericSize(spec.Code) * count;
                        break;
                    }
                case 'x':
                    {
                        if (spec.Count == AllCount)
                        {
                            result =
                                "cannot use \"*\" in format string with \"x\"";

                            return ReturnCode.Error;
                        }

                        position += (spec.Count == NoCount) ? 1 : spec.Count;
                        break;
                    }
                case 'X':
                    {
                        if (spec.Count == AllCount)
                        {
                            position = 0;
                        }
                        else
                        {
                            int count = (spec.Count == NoCount) ?
                                1 : spec.Count;

                            position = Math.Max(0, position - count);
                        }

                        break;
                    }
                case '@':
                    {
                        if (spec.Count == NoCount)
                        {
                            result =
                                "missing count for \"@\" field specifier";

                            return ReturnCode.Error;
                        }

                        position = (spec.Count == AllCount) ?
                            maxPosition : spec.Count;

                        break;
                    }
                default:
                    {
                        result = String.Format(
                            "bad field specifier \"{0}\"", spec.Code);

                        return ReturnCode.Error;
                    }
            }

            if (position > maxPosition)
                maxPosition = position;
        }

        //
        // NOTE: Pass 2 -- write the bytes.  The cursor follows exactly the
        //       same path as pass 1, so all writes fit within the buffer.
        //       The buffer starts zeroed, which provides the null padding
        //       for any gaps the "@" specifier skips over.
        //
        byte[] buffer = new byte[maxPosition];

        index = 0;
        position = 0;
        maxPosition = 0;
        argumentIndex = firstArgumentIndex;

        while (true)
        {
            bool done;

            if (TryGetNextSpec(template, ref index,
                    ref spec, out done, ref result) != ReturnCode.Ok)
            {
                return ReturnCode.Error;
            }

            if (done)
                break;

            switch (spec.Code)
            {
                case 'a':
                case 'A':
                    {
                        string text = arguments[argumentIndex++];

                        int count = (spec.Count == NoCount) ? 1 :
                            (spec.Count == AllCount) ? text.Length :
                            spec.Count;

                        byte pad = (spec.Code == 'A') ? (byte)0x20 : (byte)0;

                        for (int offset = 0; offset < count; offset++)
                        {
                            buffer[position + offset] =
                                (offset < text.Length) ?
                                    (byte)text[offset] : pad;
                        }

                        position += count;
                        break;
                    }
                case 'b':
                case 'B':
                    {
                        string text = arguments[argumentIndex++];

                        int count = (spec.Count == NoCount) ? 1 :
                            (spec.Count == AllCount) ? text.Length :
                            spec.Count;

                        int byteCount = (count + 7) / 8;

                        //
                        // NOTE: Zero the destination span first; the cursor
                        //       may have been moved back over previously
                        //       written data (matches Tcl's memset).
                        //
                        for (int offset = 0; offset < byteCount; offset++)
                            buffer[position + offset] = 0;

                        for (int offset = 0; offset < count; offset++)
                        {
                            if (offset >= text.Length)
                                break;

                            char character = text[offset];

                            if (character == '1')
                            {
                                int shift = (spec.Code == 'b') ?
                                    offset % 8 : 7 - (offset % 8);

                                buffer[position + (offset / 8)] |=
                                    (byte)(1 << shift);
                            }
                            else if (character != '0')
                            {
                                result = String.Format(
                                    "expected binary string but got" +
                                    " \"{0}\" instead", text);

                                return ReturnCode.Error;
                            }
                        }

                        position += byteCount;
                        break;
                    }
                case 'h':
                case 'H':
                    {
                        string text = arguments[argumentIndex++];

                        int count = (spec.Count == NoCount) ? 1 :
                            (spec.Count == AllCount) ? text.Length :
                            spec.Count;

                        int byteCount = (count + 1) / 2;

                        for (int offset = 0; offset < byteCount; offset++)
                            buffer[position + offset] = 0;

                        for (int offset = 0; offset < count; offset++)
                        {
                            if (offset >= text.Length)
                                break;

                            int nibble = HexDigits.IndexOf(
                                char.ToLowerInvariant(text[offset]));

                            if (nibble < 0)
                            {
                                result = String.Format(
                                    "expected hexadecimal string but got" +
                                    " \"{0}\" instead", text);

                                return ReturnCode.Error;
                            }

                            int shift = (spec.Code == 'h') ?
                                ((offset % 2) == 0 ? 0 : 4) :
                                ((offset % 2) == 0 ? 4 : 0);

                            buffer[position + (offset / 2)] |=
                                (byte)(nibble << shift);
                        }

                        position += byteCount;
                        break;
                    }
                case 'c':
                case 's':
                case 'S':
                case 't':
                case 'i':
                case 'I':
                case 'n':
                case 'w':
                case 'W':
                case 'm':
                case 'f':
                case 'r':
                case 'R':
                case 'd':
                case 'q':
                case 'Q':
                    {
                        int size = GetNumericSize(spec.Code);
                        bool floating = IsFloatingPoint(spec.Code);

                        if (spec.Count == NoCount)
                        {
                            string text = arguments[argumentIndex++];
                            long wideValue = 0;
                            double doubleValue = 0.0;

                            if (floating)
                            {
                                if (GetDoubleValue(text, cultureInfo,
                                        ref doubleValue,
                                        ref result) != ReturnCode.Ok)
                                {
                                    return ReturnCode.Error;
                                }
                            }
                            else if (GetIntegerValue(text, cultureInfo,
                                    ref wideValue,
                                    ref result) != ReturnCode.Ok)
                            {
                                return ReturnCode.Error;
                            }

                            WriteNumber(spec.Code, buffer, position,
                                wideValue, doubleValue);

                            position += size;
                        }
                        else
                        {
                            StringList list = null;

                            if (ListOps.GetOrCopyOrSplitList(
                                    interpreter, arguments[argumentIndex],
                                    true, ref list,
                                    ref result) != ReturnCode.Ok)
                            {
                                return ReturnCode.Error;
                            }

                            argumentIndex++;

                            int count = (spec.Count == AllCount) ?
                                list.Count : spec.Count;

                            for (int offset = 0; offset < count; offset++)
                            {
                                string text = list[offset];
                                long wideValue = 0;
                                double doubleValue = 0.0;

                                if (floating)
                                {
                                    if (GetDoubleValue(text, cultureInfo,
                                            ref doubleValue,
                                            ref result) != ReturnCode.Ok)
                                    {
                                        return ReturnCode.Error;
                                    }
                                }
                                else if (GetIntegerValue(text, cultureInfo,
                                        ref wideValue,
                                        ref result) != ReturnCode.Ok)
                                {
                                    return ReturnCode.Error;
                                }

                                WriteNumber(spec.Code, buffer,
                                    position + (size * offset),
                                    wideValue, doubleValue);
                            }

                            position += size * count;
                        }

                        break;
                    }
                case 'x':
                    {
                        int count = (spec.Count == NoCount) ? 1 : spec.Count;

                        for (int offset = 0; offset < count; offset++)
                            buffer[position + offset] = 0;

                        position += count;
                        break;
                    }
                case 'X':
                    {
                        if (spec.Count == AllCount)
                        {
                            position = 0;
                        }
                        else
                        {
                            int count = (spec.Count == NoCount) ?
                                1 : spec.Count;

                            position = Math.Max(0, position - count);
                        }

                        break;
                    }
                case '@':
                    {
                        position = (spec.Count == AllCount) ?
                            maxPosition : spec.Count;

                        break;
                    }
            }

            if (position > maxPosition)
                maxPosition = position;
        }

        result = GetStringFromBytes(buffer, 0, buffer.Length);
        return ReturnCode.Ok;
    }
    #endregion

    ///////////////////////////////////////////////////////////////////////

    #region Binary Scan
    /// <summary>
    /// This method implements <c>binary scan</c>: it extracts fields from
    /// the binary string according to the template, assigning each
    /// extracted value to the corresponding variable.  When a field cannot
    /// be satisfied (not enough bytes remain), scanning stops and the
    /// number of successful conversions so far is returned, exactly like
    /// stock Tcl.
    /// </summary>
    /// <param name="interpreter">
    /// The interpreter context.  This parameter may not be null.
    /// </param>
    /// <param name="value">
    /// The binary string to scan.
    /// </param>
    /// <param name="template">
    /// The template (format) string.
    /// </param>
    /// <param name="arguments">
    /// The command argument list.
    /// </param>
    /// <param name="firstVariableIndex">
    /// The index within <paramref name="arguments" /> of the first
    /// variable name argument.
    /// </param>
    /// <param name="result">
    /// Upon success, receives the number of conversions performed; upon
    /// failure, receives an appropriate error message.
    /// </param>
    /// <returns>
    /// <see cref="ReturnCode.Ok" /> on success; otherwise,
    /// <see cref="ReturnCode.Error" />.
    /// </returns>
    public static ReturnCode Scan(
        Interpreter interpreter, /* in */
        string value,            /* in */
        string template,         /* in */
        ArgumentList arguments,  /* in */
        int firstVariableIndex,  /* in */
        ref Result result        /* out */
        )
    {
        byte[] bytes = GetBytesFromString(value);

        int index = 0;
        int position = 0;
        int conversions = 0;
        int variableIndex = firstVariableIndex;
        FieldSpec spec = default(FieldSpec);
        bool stopped = false;

        while (!stopped)
        {
            bool done;

            if (TryGetNextSpec(template, ref index,
                    ref spec, out done, ref result) != ReturnCode.Ok)
            {
                return ReturnCode.Error;
            }

            if (done)
                break;

            string scanned = null;

            switch (spec.Code)
            {
                case 'a':
                case 'A':
                    {
                        if (variableIndex >= arguments.Count)
                        {
                            result = NotEnoughArguments;
                            return ReturnCode.Error;
                        }

                        int count = (spec.Count == NoCount) ? 1 :
                            (spec.Count == AllCount) ?
                                bytes.Length - position : spec.Count;

                        if (position + count > bytes.Length)
                        {
                            stopped = true;
                            break;
                        }

                        int length = count;

                        if (spec.Code == 'A')
                        {
                            while ((length > 0) &&
                                ((bytes[position + length - 1] == 0x20) ||
                                 (bytes[position + length - 1] == 0)))
                            {
                                length--;
                            }
                        }

                        scanned = GetStringFromBytes(
                            bytes, position, length);

                        position += count;
                        break;
                    }
                case 'b':
                case 'B':
                    {
                        if (variableIndex >= arguments.Count)
                        {
                            result = NotEnoughArguments;
                            return ReturnCode.Error;
                        }

                        int count = (spec.Count == NoCount) ? 1 :
                            (spec.Count == AllCount) ?
                                (bytes.Length - position) * 8 : spec.Count;

                        int byteCount = (count + 7) / 8;

                        if (position + byteCount > bytes.Length)
                        {
                            stopped = true;
                            break;
                        }

                        char[] characters = new char[count];

                        for (int offset = 0; offset < count; offset++)
                        {
                            int shift = (spec.Code == 'b') ?
                                offset % 8 : 7 - (offset % 8);

                            characters[offset] = (((bytes[
                                position + (offset / 8)] >> shift) & 1)
                                    != 0) ? '1' : '0';
                        }

                        scanned = new string(characters);
                        position += byteCount;
                        break;
                    }
                case 'h':
                case 'H':
                    {
                        if (variableIndex >= arguments.Count)
                        {
                            result = NotEnoughArguments;
                            return ReturnCode.Error;
                        }

                        int count = (spec.Count == NoCount) ? 1 :
                            (spec.Count == AllCount) ?
                                (bytes.Length - position) * 2 : spec.Count;

                        int byteCount = (count + 1) / 2;

                        if (position + byteCount > bytes.Length)
                        {
                            stopped = true;
                            break;
                        }

                        char[] characters = new char[count];

                        for (int offset = 0; offset < count; offset++)
                        {
                            byte current = bytes[position + (offset / 2)];

                            int nibble = (spec.Code == 'h') ?
                                (((offset % 2) == 0) ?
                                    current & 0xF : current >> 4) :
                                (((offset % 2) == 0) ?
                                    current >> 4 : current & 0xF);

                            characters[offset] = HexDigits[nibble];
                        }

                        scanned = new string(characters);
                        position += byteCount;
                        break;
                    }
                case 'c':
                case 's':
                case 'S':
                case 't':
                case 'i':
                case 'I':
                case 'n':
                case 'w':
                case 'W':
                case 'm':
                case 'f':
                case 'r':
                case 'R':
                case 'd':
                case 'q':
                case 'Q':
                    {
                        if (variableIndex >= arguments.Count)
                        {
                            result = NotEnoughArguments;
                            return ReturnCode.Error;
                        }

                        int size = GetNumericSize(spec.Code);

                        if (spec.Count == NoCount)
                        {
                            if (position + size > bytes.Length)
                            {
                                stopped = true;
                                break;
                            }

                            scanned = ReadNumber(
                                spec.Code, spec.Unsigned, bytes, position);

                            position += size;
                        }
                        else
                        {
                            int count = (spec.Count == AllCount) ?
                                (bytes.Length - position) / size :
                                spec.Count;

                            if (position + (size * count) > bytes.Length)
                            {
                                stopped = true;
                                break;
                            }

                            StringList list = new StringList();

                            for (int offset = 0; offset < count; offset++)
                            {
                                list.Add(ReadNumber(
                                    spec.Code, spec.Unsigned, bytes,
                                    position + (size * offset)));
                            }

                            scanned = list.ToString();
                            position += size * count;
                        }

                        break;
                    }
                case 'x':
                    {
                        int count = (spec.Count == NoCount) ? 1 :
                            (spec.Count == AllCount) ?
                                bytes.Length - position : spec.Count;

                        if (position + count > bytes.Length)
                        {
                            stopped = true;
                            break;
                        }

                        position += count;
                        break;
                    }
                case 'X':
                    {
                        if (spec.Count == AllCount)
                        {
                            position = 0;
                        }
                        else
                        {
                            int count = (spec.Count == NoCount) ?
                                1 : spec.Count;

                            position = Math.Max(0, position - count);
                        }

                        break;
                    }
                case '@':
                    {
                        if (spec.Count == NoCount)
                        {
                            result =
                                "missing count for \"@\" field specifier";

                            return ReturnCode.Error;
                        }

                        if (spec.Count == AllCount)
                        {
                            position = bytes.Length;
                        }
                        else if (spec.Count > bytes.Length)
                        {
                            stopped = true;
                        }
                        else
                        {
                            position = spec.Count;
                        }

                        break;
                    }
                default:
                    {
                        result = String.Format(
                            "bad field specifier \"{0}\"", spec.Code);

                        return ReturnCode.Error;
                    }
            }

            if (scanned != null)
            {
                Result error = null;

                if (interpreter.SetVariableValue(VariableFlags.None,
                        arguments[variableIndex], scanned, null,
                        ref error) != ReturnCode.Ok)
                {
                    result = error;
                    return ReturnCode.Error;
                }

                variableIndex++;
                conversions++;
            }
        }

        result = conversions;
        return ReturnCode.Ok;
    }
    #endregion

    ///////////////////////////////////////////////////////////////////////

    #region Hex Encode / Decode
    /// <summary>
    /// This method implements <c>binary encode hex</c>: each byte becomes
    /// two lowercase hexadecimal digits, high nibble first.
    /// </summary>
    /// <param name="data">
    /// The bytes to encode.
    /// </param>
    /// <returns>
    /// The encoded string.
    /// </returns>
    public static string EncodeHex(
        byte[] data /* in */
        )
    {
        StringBuilder builder = new StringBuilder(data.Length * 2);

        foreach (byte current in data)
        {
            builder.Append(HexDigits[current >> 4]);
            builder.Append(HexDigits[current & 0xF]);
        }

        return builder.ToString();
    }

    ///////////////////////////////////////////////////////////////////////

    /// <summary>
    /// This method implements <c>binary decode hex</c>.  Outside of strict
    /// mode, whitespace is skipped; any other non-hexadecimal character is
    /// an error.  A trailing odd digit is silently discarded, matching
    /// stock Tcl.
    /// </summary>
    /// <param name="data">
    /// The string to decode.
    /// </param>
    /// <param name="strict">
    /// Non-zero to reject whitespace.
    /// </param>
    /// <param name="result">
    /// Upon success, receives the decoded binary string; upon failure,
    /// receives an appropriate error message.
    /// </param>
    /// <returns>
    /// <see cref="ReturnCode.Ok" /> on success; otherwise,
    /// <see cref="ReturnCode.Error" />.
    /// </returns>
    public static ReturnCode DecodeHex(
        string data,      /* in */
        bool strict,      /* in */
        ref Result result /* out */
        )
    {
        StringBuilder builder = new StringBuilder(data.Length / 2);
        int pending = 0;
        bool havePending = false;

        for (int index = 0; index < data.Length; index++)
        {
            char character = data[index];
            int nibble = HexDigits.IndexOf(
                char.ToLowerInvariant(character));

            if (nibble < 0)
            {
                if (!strict && char.IsWhiteSpace(character))
                    continue;

                result = String.Format(
                    "invalid hexadecimal digit \"{0}\" at position {1}",
                    character, index);

                return ReturnCode.Error;
            }

            if (havePending)
            {
                builder.Append((char)((pending << 4) | nibble));
                havePending = false;
            }
            else
            {
                pending = nibble;
                havePending = true;
            }
        }

        result = builder.ToString();
        return ReturnCode.Ok;
    }
    #endregion

    ///////////////////////////////////////////////////////////////////////

    #region Base64 Encode / Decode
    /// <summary>
    /// This method implements <c>binary encode base64</c>, wrapping the
    /// output with the specified separator every <paramref name="maxLength"
    /// /> characters when that value is greater than zero.
    /// </summary>
    /// <param name="data">
    /// The bytes to encode.
    /// </param>
    /// <param name="maxLength">
    /// The maximum line length, or zero for no wrapping.
    /// </param>
    /// <param name="wrapCharacters">
    /// The separator emitted between lines.
    /// </param>
    /// <returns>
    /// The encoded string.
    /// </returns>
    public static string EncodeBase64(
        byte[] data,          /* in */
        int maxLength,        /* in */
        string wrapCharacters /* in */
        )
    {
        string encoded = Convert.ToBase64String(data);

        if ((maxLength <= 0) || (encoded.Length <= maxLength))
            return encoded;

        StringBuilder builder = new StringBuilder();

        for (int index = 0; index < encoded.Length; index += maxLength)
        {
            if (index > 0)
                builder.Append(wrapCharacters);

            builder.Append(encoded, index,
                Math.Min(maxLength, encoded.Length - index));
        }

        return builder.ToString();
    }

    ///////////////////////////////////////////////////////////////////////

    /// <summary>
    /// This method implements <c>binary decode base64</c>.  Outside of
    /// strict mode, characters that are not part of the Base64 alphabet
    /// are skipped.  A terminating group of fewer than four characters is
    /// decoded to its available bytes; an "=" ends the data, matching
    /// stock Tcl.
    /// </summary>
    /// <param name="data">
    /// The string to decode.
    /// </param>
    /// <param name="strict">
    /// Non-zero to reject characters outside the Base64 alphabet.
    /// </param>
    /// <param name="result">
    /// Upon success, receives the decoded binary string; upon failure,
    /// receives an appropriate error message.
    /// </param>
    /// <returns>
    /// <see cref="ReturnCode.Ok" /> on success; otherwise,
    /// <see cref="ReturnCode.Error" />.
    /// </returns>
    public static ReturnCode DecodeBase64(
        string data,      /* in */
        bool strict,      /* in */
        ref Result result /* out */
        )
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ" +
            "abcdefghijklmnopqrstuvwxyz0123456789+/";

        StringBuilder builder = new StringBuilder();
        int bits = 0;
        int groupLength = 0;
        bool ended = false;

        for (int index = 0; index < data.Length; index++)
        {
            char character = data[index];

            if (!ended && (character == '='))
            {
                ended = true;
                continue;
            }

            int value = alphabet.IndexOf(character);

            if (ended || (value < 0))
            {
                if (!strict)
                    continue;

                result = String.Format(
                    "invalid base64 character \"{0}\" at position {1}",
                    character, index);

                return ReturnCode.Error;
            }

            bits = (bits << 6) | value;
            groupLength++;

            if (groupLength == 4)
            {
                builder.Append((char)((bits >> 16) & 0xFF));
                builder.Append((char)((bits >> 8) & 0xFF));
                builder.Append((char)(bits & 0xFF));

                bits = 0;
                groupLength = 0;
            }
        }

        if (groupLength >= 2)
        {
            bits <<= 6 * (4 - groupLength);

            builder.Append((char)((bits >> 16) & 0xFF));

            if (groupLength >= 3)
                builder.Append((char)((bits >> 8) & 0xFF));
        }

        result = builder.ToString();
        return ReturnCode.Ok;
    }
    #endregion

    ///////////////////////////////////////////////////////////////////////

    #region Uuencode Encode / Decode
    /// <summary>
    /// This method implements <c>binary encode uuencode</c>: raw uuencoded
    /// data lines (no "begin"/"end" framing), each holding the byte-count
    /// character followed by the data characters, each line terminated by
    /// the separator.  A six-bit value of zero is emitted as a backtick,
    /// matching stock Tcl.
    /// </summary>
    /// <param name="data">
    /// The bytes to encode.
    /// </param>
    /// <param name="maxLength">
    /// The maximum line length; the bytes encoded per line are
    /// 3 * ((maxLength - 1) / 4).
    /// </param>
    /// <param name="wrapCharacters">
    /// The separator emitted after every line.
    /// </param>
    /// <returns>
    /// The encoded string.
    /// </returns>
    public static string EncodeUuencode(
        byte[] data,          /* in */
        int maxLength,        /* in */
        string wrapCharacters /* in */
        )
    {
        int bytesPerLine = 3 * ((maxLength - 1) / 4);
        StringBuilder builder = new StringBuilder();

        for (int start = 0; start < data.Length; start += bytesPerLine)
        {
            int lineBytes = Math.Min(bytesPerLine, data.Length - start);

            builder.Append((char)(32 + lineBytes));

            int characterCount = ((lineBytes * 8) + 5) / 6;
            int bits = 0;
            int bitCount = 0;
            int consumed = 0;

            for (int offset = 0; offset < characterCount; offset++)
            {
                while ((bitCount < 6) && (consumed < lineBytes))
                {
                    bits = (bits << 8) | data[start + consumed];
                    bitCount += 8;
                    consumed++;
                }

                int value;

                if (bitCount >= 6)
                {
                    value = (bits >> (bitCount - 6)) & 0x3F;
                    bitCount -= 6;
                }
                else
                {
                    value = (bits << (6 - bitCount)) & 0x3F;
                    bitCount = 0;
                }

                builder.Append((value == 0) ? '`' : (char)(32 + value));
            }

            builder.Append(wrapCharacters);
        }

        return builder.ToString();
    }

    ///////////////////////////////////////////////////////////////////////

    /// <summary>
    /// This method implements <c>binary decode uuencode</c>.  Each line
    /// starts with a byte-count character; the data characters follow.
    /// Characters missing at the end of a line decode as six-bit value 32,
    /// matching stock Tcl.  Outside of strict mode, characters outside
    /// the uuencode range are skipped.
    /// </summary>
    /// <param name="data">
    /// The string to decode.
    /// </param>
    /// <param name="strict">
    /// Non-zero to reject characters outside the uuencode alphabet.
    /// </param>
    /// <param name="result">
    /// Upon success, receives the decoded binary string; upon failure,
    /// receives an appropriate error message.
    /// </param>
    /// <returns>
    /// <see cref="ReturnCode.Ok" /> on success; otherwise,
    /// <see cref="ReturnCode.Error" />.
    /// </returns>
    public static ReturnCode DecodeUuencode(
        string data,      /* in */
        bool strict,      /* in */
        ref Result result /* out */
        )
    {
        StringBuilder builder = new StringBuilder();
        int index = 0;

        while (index < data.Length)
        {
            char countCharacter = data[index];

            //
            // NOTE: Skip blank line separators between data lines.
            //
            if ((countCharacter == '\n') || (countCharacter == '\r'))
            {
                index++;
                continue;
            }

            if ((countCharacter < 32) || (countCharacter > 96))
            {
                if (strict)
                {
                    result = String.Format(
                        "invalid uuencode character \"{0}\" at position" +
                        " {1}", countCharacter, index);

                    return ReturnCode.Error;
                }

                index++;
                continue;
            }

            int lineBytes = (countCharacter - 32) & 0x3F;

            index++;

            int characterCount = ((lineBytes * 8) + 5) / 6;
            int bits = 0;
            int bitCount = 0;
            int collected = 0;
            int emitted = 0;

            while (collected < characterCount)
            {
                if ((index >= data.Length) || (data[index] == '\n'))
                {
                    //
                    // NOTE: A missing character decodes as (0 - 32) & 0x3F,
                    //       i.e. 32, matching stock Tcl reading past the
                    //       end of the line.
                    //
                    bits = (bits << 6) | 32;
                }
                else
                {
                    char character = data[index];

                    if ((character < 32) || (character > 96))
                    {
                        if (strict)
                        {
                            result = String.Format(
                                "invalid uuencode character \"{0}\" at" +
                                " position {1}", character, index);

                            return ReturnCode.Error;
                        }

                        index++;
                        continue;
                    }

                    bits = (bits << 6) | ((character - 32) & 0x3F);
                    index++;
                }

                bitCount += 6;
                collected++;

                if ((bitCount >= 8) && (emitted < lineBytes))
                {
                    builder.Append(
                        (char)((bits >> (bitCount - 8)) & 0xFF));

                    bitCount -= 8;
                    emitted++;
                }
            }

            //
            // NOTE: Skip the remainder of the line, up to and including
            //       the line terminator.
            //
            while ((index < data.Length) && (data[index] != '\n'))
            {
                if (strict && !char.IsWhiteSpace(data[index]))
                {
                    result = String.Format(
                        "invalid uuencode character \"{0}\" at position" +
                        " {1}", data[index], index);

                    return ReturnCode.Error;
                }

                index++;
            }

            if (index < data.Length)
                index++;
        }

        result = builder.ToString();
        return ReturnCode.Ok;
    }
    #endregion
}
