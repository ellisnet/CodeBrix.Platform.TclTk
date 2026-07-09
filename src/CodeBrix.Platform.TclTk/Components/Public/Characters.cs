/*
 * Characters.cs --
 *
 * Copyright (c) 2007-2012 by Joe Mistachkin.  All rights reserved.
 *
 * See the file "license.terms" for information on usage and redistribution of
 * this file, and for a DISCLAIMER OF ALL WARRANTIES.
 *
 * RCS: @(#) $Id: $
 */

using CodeBrix.Platform.TclTk._Attributes;
using CodeBrix.Platform.TclTk._Containers.Private;
using CodeBrix.Platform.TclTk._Containers.Public;

namespace CodeBrix.Platform.TclTk._Components.Public //was previously: Eagle._Components.Public;
{
    /// <summary>
    /// This class provides a centralized collection of well-known character and string constants used throughout TclTk.
    /// </summary>
    [ObjectId("16571e1f-4c2f-4ce2-95f4-49ebbcae3d79")]
    public static class Characters
    {
        /// <summary>
        /// The null character (U+0000).
        /// </summary>
        public const char Null = '\0';
        /// <summary>
        /// The bell (alert) character (U+0007).
        /// </summary>
        public const char Bell = '\a';
        /// <summary>
        /// The backspace character (U+0008).
        /// </summary>
        public const char Backspace = '\b';
        /// <summary>
        /// The horizontal tab character (U+0009).
        /// </summary>
        public const char HorizontalTab = '\t';
        /// <summary>
        /// The line feed character (U+000A).
        /// </summary>
        public const char LineFeed = '\n';
        /// <summary>
        /// The vertical tab character (U+000B).
        /// </summary>
        public const char VerticalTab = '\v';
        /// <summary>
        /// The form feed character (U+000C).
        /// </summary>
        public const char FormFeed = '\f';
        /// <summary>
        /// The carriage return character (U+000D).
        /// </summary>
        public const char CarriageReturn = '\r';
        /// <summary>
        /// The character used to represent a new line; this is an alias for the line feed character.
        /// </summary>
        public const char NewLine = LineFeed;
        /// <summary>
        /// The end-of-file character (U+001A).
        /// </summary>
        public const char EndOfFile = '\x1A';
        /// <summary>
        /// The end-of-transmission character (U+0004).
        /// </summary>
        public const char EndOfTransmission = '\x04';

        ///////////////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// The next line character (U+0085).
        /// </summary>
        public const char NextLine = '\u0085';
        /// <summary>
        /// The line separator character (U+2028).
        /// </summary>
        public const char LineSeparator = '\u2028';
        /// <summary>
        /// The paragraph separator character (U+2029).
        /// </summary>
        public const char ParagraphSeparator = '\u2029';

        ///////////////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// The space character (' ').
        /// </summary>
        public const char Space = ' ';
        /// <summary>
        /// The underscore character ('_').
        /// </summary>
        public const char Underscore = '_';
        /// <summary>
        /// The colon character (':').
        /// </summary>
        public const char Colon = ':';
        /// <summary>
        /// The comma character (',').
        /// </summary>
        public const char Comma = ',';
        /// <summary>
        /// The semicolon character (';').
        /// </summary>
        public const char SemiColon = ';';
        /// <summary>
        /// The backslash character ('\').
        /// </summary>
        public const char Backslash = '\\';
        /// <summary>
        /// The at sign character ('@').
        /// </summary>
        public const char AtSign = '@';
        /// <summary>
        /// The number sign (hash) character ('#').
        /// </summary>
        public const char NumberSign = '#';
        /// <summary>
        /// The dollar sign character ('$').
        /// </summary>
        public const char DollarSign = '$';
        /// <summary>
        /// The open parenthesis character ('(').
        /// </summary>
        public const char OpenParenthesis = '(';
        /// <summary>
        /// The close parenthesis character (')').
        /// </summary>
        public const char CloseParenthesis = ')';
        /// <summary>
        /// The circumflex accent (caret) character ('^').
        /// </summary>
        public const char CircumflexAccent = '^';
        /// <summary>
        /// The vertical line (pipe) character ('|').
        /// </summary>
        public const char VerticalLine = '|';
        /// <summary>
        /// The open brace character ('{').
        /// </summary>
        public const char OpenBrace = '{';
        /// <summary>
        /// The close brace character ('}').
        /// </summary>
        public const char CloseBrace = '}';
        /// <summary>
        /// The quotation mark character ('"').
        /// </summary>
        public const char QuotationMark = '"';
        /// <summary>
        /// The apostrophe (single quote) character.
        /// </summary>
        public const char Apostrophe = '\'';
        /// <summary>
        /// The open bracket character ('[').
        /// </summary>
        public const char OpenBracket = '[';
        /// <summary>
        /// The close bracket character (']').
        /// </summary>
        public const char CloseBracket = ']';
        /// <summary>
        /// The percent sign character ('%').
        /// </summary>
        public const char PercentSign = '%';
        /// <summary>
        /// The question mark character ('?').
        /// </summary>
        public const char QuestionMark = '?';
        /// <summary>
        /// The less-than sign character ('&lt;').
        /// </summary>
        public const char LessThanSign = '<';
        /// <summary>
        /// The equal sign character ('=').
        /// </summary>
        public const char EqualSign = '=';
        /// <summary>
        /// The greater-than sign character ('&gt;').
        /// </summary>
        public const char GreaterThanSign = '>';
        /// <summary>
        /// The exclamation mark character ('!').
        /// </summary>
        public const char ExclamationMark = '!';
        /// <summary>
        /// The ampersand character ('&amp;').
        /// </summary>
        public const char Ampersand = '&';
        /// <summary>
        /// The grave accent character ('`').
        /// </summary>
        public const char GraveAccent = '`';
        /// <summary>
        /// The tilde character ('~').
        /// </summary>
        public const char Tilde = '~';
        /// <summary>
        /// The asterisk character ('*').
        /// </summary>
        public const char Asterisk = '*';
        /// <summary>
        /// The plus sign character ('+').
        /// </summary>
        public const char PlusSign = '+';
        /// <summary>
        /// The minus sign (hyphen) character ('-').
        /// </summary>
        public const char MinusSign = '-';
        /// <summary>
        /// The slash (forward slash) character ('/').
        /// </summary>
        public const char Slash = '/';
        /// <summary>
        /// The period (dot) character ('.').
        /// </summary>
        public const char Period = '.';
        /// <summary>
        /// The delete character (U+007F).
        /// </summary>
        public const char Delete = '';

        ///////////////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// The copyright sign character (U+00A9).
        /// </summary>
        public const char Copyright = '©'; // U+00A9
        /// <summary>
        /// The section sign character (U+00A7).
        /// </summary>
        public const char SectionSign = '§'; // U+00A7
        /// <summary>
        /// The pilcrow (paragraph) sign character (U+00B6).
        /// </summary>
        public const char PilcrowSign = '¶'; // U+00B6
        /// <summary>
        /// The symbol for bell character (U+2407).
        /// </summary>
        public const char BellSymbol = '␇'; // U+2407
        /// <summary>
        /// The right angle with downwards zigzag arrow character (U+237C).
        /// </summary>
        public const char Angzarr = '⍼'; // U+237C

        ///////////////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// The infinity character (U+221E).
        /// </summary>
        public const char Infinity = '∞'; // U+221E

        ///////////////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// The maximum non-surrogate character (U+D7FF).
        /// </summary>
        public const char MaximumNonSurrogate = '퟿'; // U+D7FF
        /// <summary>
        /// The minimum private-use-area character (U+E000).
        /// </summary>
        public const char MinimumPrivateUse = ''; // U+E000
        /// <summary>
        /// The symbol for backspace character (U+2408).
        /// </summary>
        public const char BackspaceSymbol = '␈'; // U+2408
        /// <summary>
        /// The object replacement character (U+FFFC).
        /// </summary>
        public const char ObjectReplacement = '￼'; // U+FFFC
        /// <summary>
        /// The replacement character (U+FFFD).
        /// </summary>
        public const char Replacement = '�'; // U+FFFD
        /// <summary>
        /// A non-character code point (U+FFFE).
        /// </summary>
        public const char NonCharacter1 = '￾'; // U+FFFE
        /// <summary>
        /// A non-character code point (U+FFFF).
        /// </summary>
        public const char NonCharacter2 = '￿'; // U+FFFF

        ///////////////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// The character used to visually represent a null character (U+00A0).
        /// </summary>
        public const char VisualNull = ' '; // U+00A0
        /// <summary>
        /// The character used to visually represent a horizontal tab character (U+00BB).
        /// </summary>
        public const char VisualHorizontalTab = '»'; // U+00BB
        /// <summary>
        /// The character used to visually represent a vertical tab character (U+00AB).
        /// </summary>
        public const char VisualVerticalTab = '«'; // U+00AB
        /// <summary>
        /// The character used to visually represent a form feed character (U+00B0).
        /// </summary>
        public const char VisualFormFeed = '°'; // U+00B0
        /// <summary>
        /// The character used to visually represent a space character (U+00B7).
        /// </summary>
        public const char VisualSpace = '·'; // U+00B7

        ///////////////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// The leftwards arrow character (U+2190).
        /// </summary>
        public const char LeftwardsArrow = '←'; // U+2190
        /// <summary>
        /// The upwards arrow character (U+2191).
        /// </summary>
        public const char UpwardsArrow = '↑'; // U+2191
        /// <summary>
        /// The rightwards arrow character (U+2192).
        /// </summary>
        public const char RightwardsArrow = '→'; // U+2192
        /// <summary>
        /// The downwards arrow character (U+2193).
        /// </summary>
        public const char DownwardsArrow = '↓'; // U+2193
        /// <summary>
        /// The full block character (U+2588).
        /// </summary>
        public const char FullBlock = '█'; // U+2588

        ///////////////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// The array of Unicode symbol characters treated as white space.
        /// </summary>
        internal static readonly char[] WhiteSpace_Unicode = {
            BellSymbol, BackspaceSymbol, LeftwardsArrow, UpwardsArrow,
            RightwardsArrow, DownwardsArrow, FullBlock
        };

        /// <summary>
        /// The array of extended characters treated as white space.
        /// </summary>
        internal static readonly char[] WhiteSpace_Extended = {
            SectionSign, PilcrowSign, VisualNull, VisualVerticalTab,
            VisualFormFeed, VisualSpace, VisualHorizontalTab
        };

        ///////////////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// The string containing a single period character.
        /// </summary>
        internal const string PeriodString = ".";

        /// <summary>
        /// The string containing a single percent sign character.
        /// </summary>
        internal const string SinglePercentSignString = "%";
        /// <summary>
        /// The string containing two percent sign characters.
        /// </summary>
        internal const string DoublePercentSignString = "%%";

        /// <summary>
        /// The string containing a comma followed by a space.
        /// </summary>
        internal const string CommaSpaceString = ", ";

        /// <summary>
        /// The string containing a single space character.
        /// </summary>
        public const string SpaceString = " ";
        /// <summary>
        /// The ANSI representation of the copyright sign.
        /// </summary>
        public const string CopyrightAnsi = "(c)";

        ///////////////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// The caret character; this is a non-standard name for the circumflex accent character.
        /// </summary>
        public const char Caret = CircumflexAccent; // non-standard name
        /// <summary>
        /// The pipe character; this is a non-standard name for the vertical line character.
        /// </summary>
        public const char Pipe = VerticalLine;      // non-standard name
        /// <summary>
        /// The comment character; this is a non-standard name for the number sign character.
        /// </summary>
        public const char Comment = NumberSign;     // non-standard name
        /// <summary>
        /// The alternate comment character; this is a non-standard name for the semicolon character.
        /// </summary>
        public const char AltComment = SemiColon;   // non-standard name

        ///////////////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// The digit character '0'.
        /// </summary>
        public const char Zero = '0';
        /// <summary>
        /// The digit character '1'.
        /// </summary>
        public const char One = '1';
        /// <summary>
        /// The digit character '2'.
        /// </summary>
        public const char Two = '2';
        /// <summary>
        /// The digit character '3'.
        /// </summary>
        public const char Three = '3';
        /// <summary>
        /// The digit character '4'.
        /// </summary>
        public const char Four = '4';
        /// <summary>
        /// The digit character '5'.
        /// </summary>
        public const char Five = '5';
        /// <summary>
        /// The digit character '6'.
        /// </summary>
        public const char Six = '6';
        /// <summary>
        /// The digit character '7'.
        /// </summary>
        public const char Seven = '7';
        /// <summary>
        /// The digit character '8'.
        /// </summary>
        public const char Eight = '8';
        /// <summary>
        /// The digit character '9'.
        /// </summary>
        public const char Nine = '9';

        ///////////////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// The uppercase letter character 'A'.
        /// </summary>
        public const char A = 'A';
        /// <summary>
        /// The uppercase letter character 'B'.
        /// </summary>
        public const char B = 'B';
        /// <summary>
        /// The uppercase letter character 'C'.
        /// </summary>
        public const char C = 'C';
        /// <summary>
        /// The uppercase letter character 'D'.
        /// </summary>
        public const char D = 'D';
        /// <summary>
        /// The uppercase letter character 'E'.
        /// </summary>
        public const char E = 'E';
        /// <summary>
        /// The uppercase letter character 'F'.
        /// </summary>
        public const char F = 'F';
        /// <summary>
        /// The uppercase letter character 'G'.
        /// </summary>
        public const char G = 'G';
        /// <summary>
        /// The uppercase letter character 'H'.
        /// </summary>
        public const char H = 'H';
        /// <summary>
        /// The uppercase letter character 'I'.
        /// </summary>
        public const char I = 'I';
        /// <summary>
        /// The uppercase letter character 'J'.
        /// </summary>
        public const char J = 'J';
        /// <summary>
        /// The uppercase letter character 'K'.
        /// </summary>
        public const char K = 'K';
        /// <summary>
        /// The uppercase letter character 'L'.
        /// </summary>
        public const char L = 'L';
        /// <summary>
        /// The uppercase letter character 'M'.
        /// </summary>
        public const char M = 'M';
        /// <summary>
        /// The uppercase letter character 'N'.
        /// </summary>
        public const char N = 'N';
        /// <summary>
        /// The uppercase letter character 'O'.
        /// </summary>
        public const char O = 'O';
        /// <summary>
        /// The uppercase letter character 'P'.
        /// </summary>
        public const char P = 'P';
        /// <summary>
        /// The uppercase letter character 'Q'.
        /// </summary>
        public const char Q = 'Q';
        /// <summary>
        /// The uppercase letter character 'R'.
        /// </summary>
        public const char R = 'R';
        /// <summary>
        /// The uppercase letter character 'S'.
        /// </summary>
        public const char S = 'S';
        /// <summary>
        /// The uppercase letter character 'T'.
        /// </summary>
        public const char T = 'T';
        /// <summary>
        /// The uppercase letter character 'U'.
        /// </summary>
        public const char U = 'U';
        /// <summary>
        /// The uppercase letter character 'V'.
        /// </summary>
        public const char V = 'V';
        /// <summary>
        /// The uppercase letter character 'W'.
        /// </summary>
        public const char W = 'W';
        /// <summary>
        /// The uppercase letter character 'X'.
        /// </summary>
        public const char X = 'X';
        /// <summary>
        /// The uppercase letter character 'Y'.
        /// </summary>
        public const char Y = 'Y';
        /// <summary>
        /// The uppercase letter character 'Z'.
        /// </summary>
        public const char Z = 'Z';

        ///////////////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// The lowercase letter character 'a'.
        /// </summary>
        public const char a = 'a';
        /// <summary>
        /// The lowercase letter character 'b'.
        /// </summary>
        public const char b = 'b';
        /// <summary>
        /// The lowercase letter character 'c'.
        /// </summary>
        public const char c = 'c';
        /// <summary>
        /// The lowercase letter character 'd'.
        /// </summary>
        public const char d = 'd';
        /// <summary>
        /// The lowercase letter character 'e'.
        /// </summary>
        public const char e = 'e';
        /// <summary>
        /// The lowercase letter character 'f'.
        /// </summary>
        public const char f = 'f';
        /// <summary>
        /// The lowercase letter character 'g'.
        /// </summary>
        public const char g = 'g';
        /// <summary>
        /// The lowercase letter character 'h'.
        /// </summary>
        public const char h = 'h';
        /// <summary>
        /// The lowercase letter character 'i'.
        /// </summary>
        public const char i = 'i';
        /// <summary>
        /// The lowercase letter character 'j'.
        /// </summary>
        public const char j = 'j';
        /// <summary>
        /// The lowercase letter character 'k'.
        /// </summary>
        public const char k = 'k';
        /// <summary>
        /// The lowercase letter character 'l'.
        /// </summary>
        public const char l = 'l';
        /// <summary>
        /// The lowercase letter character 'm'.
        /// </summary>
        public const char m = 'm';
        /// <summary>
        /// The lowercase letter character 'n'.
        /// </summary>
        public const char n = 'n';
        /// <summary>
        /// The lowercase letter character 'o'.
        /// </summary>
        public const char o = 'o';
        /// <summary>
        /// The lowercase letter character 'p'.
        /// </summary>
        public const char p = 'p';
        /// <summary>
        /// The lowercase letter character 'q'.
        /// </summary>
        public const char q = 'q';
        /// <summary>
        /// The lowercase letter character 'r'.
        /// </summary>
        public const char r = 'r';
        /// <summary>
        /// The lowercase letter character 's'.
        /// </summary>
        public const char s = 's';
        /// <summary>
        /// The lowercase letter character 't'.
        /// </summary>
        public const char t = 't';
        /// <summary>
        /// The lowercase letter character 'u'.
        /// </summary>
        public const char u = 'u';
        /// <summary>
        /// The lowercase letter character 'v'.
        /// </summary>
        public const char v = 'v';
        /// <summary>
        /// The lowercase letter character 'w'.
        /// </summary>
        public const char w = 'w';
        /// <summary>
        /// The lowercase letter character 'x'.
        /// </summary>
        public const char x = 'x';
        /// <summary>
        /// The lowercase letter character 'y'.
        /// </summary>
        public const char y = 'y';
        /// <summary>
        /// The lowercase letter character 'z'.
        /// </summary>
        public const char z = 'z';

        ///////////////////////////////////////////////////////////////////////////////////////////////

        //
        // HACK: Used by Parser to help limit the number of calls to StringBuilder.Append.
        //
        /// <summary>
        /// The array containing the open brace and close brace characters.
        /// </summary>
        internal static readonly char[] OpenBrace_CloseBrace = { OpenBrace, CloseBrace };
        /// <summary>
        /// The array containing the backslash and open brace characters.
        /// </summary>
        internal static readonly char[] Backslash_OpenBrace = { Backslash, OpenBrace };
        /// <summary>
        /// The array containing the backslash and number sign characters.
        /// </summary>
        internal static readonly char[] Backslash_NumberSign = { Backslash, NumberSign };
        /// <summary>
        /// The array containing the backslash and lowercase letter 't' characters.
        /// </summary>
        internal static readonly char[] Backslash_t = { Backslash, t };
        /// <summary>
        /// The array containing the backslash and lowercase letter 'n' characters.
        /// </summary>
        internal static readonly char[] Backslash_n = { Backslash, n };
        /// <summary>
        /// The array containing the backslash and lowercase letter 'v' characters.
        /// </summary>
        internal static readonly char[] Backslash_v = { Backslash, v };
        /// <summary>
        /// The array containing the backslash and lowercase letter 'f' characters.
        /// </summary>
        internal static readonly char[] Backslash_f = { Backslash, f };
        /// <summary>
        /// The array containing the backslash and lowercase letter 'r' characters.
        /// </summary>
        internal static readonly char[] Backslash_r = { Backslash, r };

        ///////////////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// The string containing a backslash followed by the letter 't'.
        /// </summary>
        internal const string Backslash_t_String = "\\t";
        /// <summary>
        /// The string containing a backslash followed by the letter 'n'.
        /// </summary>
        internal const string Backslash_n_String = "\\n";
        /// <summary>
        /// The string containing a backslash followed by the letter 'v'.
        /// </summary>
        internal const string Backslash_v_String = "\\v";
        /// <summary>
        /// The string containing a backslash followed by the letter 'f'.
        /// </summary>
        internal const string Backslash_f_String = "\\f";
        /// <summary>
        /// The string containing a backslash followed by the letter 'r'.
        /// </summary>
        internal const string Backslash_r_String = "\\r";

        ///////////////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// The number of hexadecimal digits needed to represent a single character.
        /// </summary>
        public const int HexChars = sizeof(char) * 2;
        /// <summary>
        /// The number of hexadecimal digits needed to represent two characters.
        /// </summary>
        public const int TwoHexChars = HexChars * 2;

        ///////////////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// The string used as a single unit of indentation.
        /// </summary>
        public static readonly string Indent = "  ";

        /// <summary>
        /// The string containing a single horizontal tab character.
        /// </summary>
        internal const string HorizontalTabString = "\t";
        /// <summary>
        /// The string containing a single line feed character.
        /// </summary>
        internal const string LineFeedString = "\n";
        /// <summary>
        /// The string containing a single vertical tab character.
        /// </summary>
        internal const string VerticalTabString = "\v";
        /// <summary>
        /// The string containing a single form feed character.
        /// </summary>
        internal const string FormFeedString = "\f";
        /// <summary>
        /// The string containing a single carriage return character.
        /// </summary>
        internal const string CarriageReturnString = "\r";

        /// <summary>
        /// The bytes that indicate the presence of a new line (carriage return and line feed).
        /// </summary>
        internal static readonly byte[] DoesNewLineBytes = {
            (int)CarriageReturn, (int)LineFeed
        };

        /// <summary>
        /// The bytes that represent a form feed.
        /// </summary>
        internal static readonly byte[] FormFeedBytes = {
            (int)FormFeed
        };

        /// <summary>
        /// The new line sequence used on DOS and Windows (carriage return followed by line feed).
        /// </summary>
        public static readonly string DosNewLine =
            CarriageReturnString + LineFeedString;

        /// <summary>
        /// The new line sequence used on Acorn OS (line feed followed by carriage return).
        /// </summary>
        public static readonly string AcornOsNewLine =
            LineFeedString + CarriageReturnString;

        /// <summary>
        /// The new line sequence used on Unix (a single line feed).
        /// </summary>
        public static readonly string UnixNewLine =
            LineFeedString;

        ///////////////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// The array of decimal digit characters.
        /// </summary>
        internal static readonly char[] DigitChars = {
            Zero, One, Two, Three, Four,
            Five, Six, Seven, Eight, Nine
        };

        ///////////////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// The array of uppercase alphabetic characters.
        /// </summary>
        internal static readonly char[] UpperAlphabetChars = {
            A, B, C, D, E, F, G, H, I, J, K, L, M,
            N, O, P, Q, R, S, T, U, V, W, X, Y, Z
        };

        ///////////////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// The array of lowercase alphabetic characters.
        /// </summary>
        internal static readonly char[] LowerAlphabetChars = {
            a, b, c, d, e, f, g, h, i, j, k, l, m,
            n, o, p, q, r, s, t, u, v, w, x, y, z
        };

        ///////////////////////////////////////////////////////////////////////////////////////////////

        #region Dead Code
#if DEAD_CODE
        /// <summary>
        /// The array of characters reserved by the core language.
        /// </summary>
        private static readonly char[] CoreReservedChars = {
            Null, HorizontalTab, LineFeed, VerticalTab, FormFeed,
            CarriageReturn, Space, QuotationMark, NumberSign,
            DollarSign, OpenParenthesis, CloseParenthesis, SemiColon,
            OpenBracket, Backslash, CloseBracket, OpenBrace, CloseBrace
        };

        ///////////////////////////////////////////////////////////////////////////////////////////////

        //
        // NOTE: These characters have special meaning in patterns for the
        //       [glob] command.
        //
        /// <summary>
        /// The array of characters that have special meaning in patterns for the [glob] command.
        /// </summary>
        private static readonly char[] GlobReservedChars = {
            QuestionMark, Asterisk, OpenBracket, Backslash, CloseBracket,
            OpenBrace, CloseBrace
        };
#endif
        #endregion

        ///////////////////////////////////////////////////////////////////////////////////////////////

#if TEST
        //
        // NOTE: Where "reserved" means quoted by the ConvertElement function.
        //
        /// <summary>
        /// The array of characters that are quoted when forming a list element.
        /// </summary>
        private static readonly char[] ListReservedChars = {
            HorizontalTab, LineFeed, VerticalTab, FormFeed, CarriageReturn,
            Space, QuotationMark, NumberSign, DollarSign, SemiColon,
            OpenBracket, Backslash, CloseBracket, OpenBrace, CloseBrace
        };
#endif

        ///////////////////////////////////////////////////////////////////////////////////////////////

        //
        // NOTE: These characters have special meaning in patterns for the
        //       [string match] command.
        //
        /// <summary>
        /// The array of characters that have special meaning in patterns for the [string match] command.
        /// </summary>
        internal static readonly char[] StringMatchReservedChars = {
            QuestionMark, Asterisk, OpenBracket, Backslash, CloseBracket
        };

        ///////////////////////////////////////////////////////////////////////////////////////////////

        #region Dead Code
#if DEAD_CODE
        //
        // NOTE: These characters have special meaning in patterns for the
        //       [regexp] command.
        //
        /// <summary>
        /// The array of characters that have special meaning in patterns for the [regexp] command.
        /// </summary>
        private static readonly char[] RegExpReservedChars = {
            ExclamationMark, DollarSign, OpenParenthesis, CloseParenthesis,
            Asterisk, PlusSign, Period, Colon, LessThanSign, EqualSign,
            GreaterThanSign, QuestionMark, OpenBracket, Backslash,
            CloseBracket, CircumflexAccent, OpenBrace, VerticalLine,
            CloseBrace, Tilde
        };

        ///////////////////////////////////////////////////////////////////////////////////////////////

        //
        // NOTE: These characters have special meaning in patterns for the
        //       [regsub] command.
        //
        /// <summary>
        /// The array of characters that have special meaning in patterns for the [regsub] command.
        /// </summary>
        private static readonly char[] RegSubReservedChars = {
            Ampersand, Backslash
        };

        ///////////////////////////////////////////////////////////////////////////////////////////////

        //
        // NOTE: These characters have special meaning in command lines.
        //
        /// <summary>
        /// The array of characters that have special meaning in command lines.
        /// </summary>
        private static readonly char[] CommandLineReservedChars = {
            HorizontalTab, LineFeed, VerticalTab, FormFeed, CarriageReturn,
            Space, QuotationMark, Backslash
        };

        ///////////////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// The array of numeric sign characters.
        /// </summary>
        private static readonly char[] SignChars = {
            PlusSign, MinusSign
        };

        ///////////////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// The array of characters that may appear in an integer value.
        /// </summary>
        private static readonly char[] IntegerChars = {
            Zero, One, Two, Three, Four,
            Five, Six, Seven, Eight, Nine
        };
#endif
        #endregion

        ///////////////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// The array of characters treated as white space.
        /// </summary>
        internal static readonly char[] WhiteSpaceChars = {
            HorizontalTab, LineFeed, VerticalTab, FormFeed, CarriageReturn,
            Space
        };

        ///////////////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// The array of characters that terminate a line.
        /// </summary>
        public static readonly char[] LineTerminatorChars = {
            LineFeed, CarriageReturn
        };

        ///////////////////////////////////////////////////////////////////////////////////////////////

#if TEST
        /// <summary>
        /// The list of characters that are quoted when forming a list element.
        /// </summary>
        internal static readonly CharList ListReservedCharList = new CharList(ListReservedChars);
#endif

        ///////////////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// The list of characters treated as white space.
        /// </summary>
        internal static readonly CharList WhiteSpaceCharList = new CharList(WhiteSpaceChars);

        /// <summary>
        /// The dictionary of characters that terminate a line.
        /// </summary>
        internal static readonly CharDictionary LineTerminatorCharDictionary =
            new CharDictionary(LineTerminatorChars);

        ///////////////////////////////////////////////////////////////////////////////////////////////

        #region Dead Code
#if DEAD_CODE
        /// <summary>
        /// The dictionary of characters treated as white space.
        /// </summary>
        private static readonly CharDictionary WhiteSpaceCharDictionary = new CharDictionary(WhiteSpaceChars);

        /// <summary>
        /// The list of characters that have special meaning in patterns for the [glob] command.
        /// </summary>
        private static readonly CharList GlobReservedCharList = new CharList(GlobReservedChars);

        /// <summary>
        /// The list of characters that have special meaning in patterns for the [string match] command.
        /// </summary>
        private static readonly CharList StringMatchReservedCharList = new CharList(StringMatchReservedChars);

        /// <summary>
        /// The list of characters that terminate a line.
        /// </summary>
        private static readonly CharList LineTerminatorCharList = new CharList(LineTerminatorChars);

        /// <summary>
        /// The dictionary of numeric sign characters.
        /// </summary>
        private static readonly CharDictionary SignCharDictionary = new CharDictionary(SignChars);

        /// <summary>
        /// The dictionary of characters that may appear in an integer value.
        /// </summary>
        private static readonly CharDictionary IntegerCharDictionary = new CharDictionary(IntegerChars);
#endif
        #endregion

        ///////////////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// The primary directory separator character.
        /// </summary>
        public static readonly char DirectorySeparator = Backslash;
        /// <summary>
        /// The alternate directory separator character.
        /// </summary>
        public static readonly char AltDirectorySeparator = Slash;

        ///////////////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// The array of directory separator characters.
        /// </summary>
        public static readonly char[] DirectorySeparatorChars = {
            DirectorySeparator, AltDirectorySeparator
        };

        ///////////////////////////////////////////////////////////////////////////////////////////////

        #region Dead Code
#if DEAD_CODE
        /// <summary>
        /// The list of directory separator characters.
        /// </summary>
        private static readonly CharList DirectorySeparatorCharList = new CharList(
            DirectorySeparatorChars);
#endif
        #endregion

        ///////////////////////////////////////////////////////////////////////////////////////////////

        //
        // NOTE: Unicode characters for slashes and asterisk box (0):
        //
        //       "/" = U+002F, "*" = U+002A, "\" = U+005C
        //       " " = U+0020
        //
        //       Unicode characters for slashes and plus/minus box (1):
        //
        //       "/" = U+002F, "+" = U+002B, "-" = U+002D
        //       "|" = U+007C, "\" = U+005C, " " = U+0020
        //
        //       Unicode characters for spaces box (2):
        //
        //       " " = U+0020
        //
        //       Unicode characters for reverse color box (3):
        //
        //       "█" = U+2588, " " = U+0020
        //
        //       Unicode characters for full reverse color box (4):
        //
        //       "█" = U+2588
        //
        //       Unicode characters for single/double line box (5):
        //
        //       "╓" = U+2553, "╥" = U+2565, "╖" = U+2556
        //       "─" = U+2500, "║" = U+2551, "╟" = U+255F
        //       "╫" = U+256B, "╢" = U+2562, "╙" = U+2559
        //       "╨" = U+2568, "╜" = U+255C, " " = U+0020
        //
        //       Unicode characters for double/single line box (6):
        //
        //       "╒" = U+2552, "╤" = U+2564, "╕" = U+2555
        //       "═" = U+2550, "│" = U+2502, "╞" = U+255E
        //       "╪" = U+256A, "╡" = U+2561, "╘" = U+2558
        //       "╧" = U+2567, "╛" = U+255B, " " = U+0020
        //
        //       Unicode characters for single line box (7):
        //
        //       "┌" = U+250C, "┬" = U+252C, "┐" = U+2510
        //       "─" = U+2500, "│" = U+2502, "├" = U+251C
        //       "┼" = U+253C, "┤" = U+2524, "└" = U+2514
        //       "┴" = U+2534, "┘" = U+2518, " " = U+0020
        //
        //       Unicode characters for double line box (8):
        //
        //       "╔" = U+2554, "╦" = U+2566, "╗" = U+2557
        //       "═" = U+2550, "║" = U+2551, "╠" = U+2560
        //       "╬" = U+256C, "╣" = U+2563, "╚" = U+255A
        //       "╩" = U+2569, "╝" = U+255D, " " = U+0020
        //
        /// <summary>
        /// The list of character sets used to draw boxes.
        /// </summary>
        internal static readonly StringList BoxCharacterSets = new StringList(new string[] {
            "/*\\*****\\*/ ", /* 0: slashes and asterisk */
            "/+\\-|+++\\+/ ", /* 1: slashes and plus/minus */
            "            ",   /* 2: spaces */
            "███████████ ",   /* 3: reverse color box */
            "████████████",   /* 4: full reverse color box */
            "╓╥╖─║╟╫╢╙╨╜ ",   /* 5: single horizontal lines and double vertical lines */
            "╒╤╕═│╞╪╡╘╧╛ ",   /* 6: double horizontal lines and single vertical lines */
            "┌┬┐─│├┼┤└┴┘ ",   /* 7: single horizontal lines and single vertical lines */
            "╔╦╗═║╠╬╣╚╩╝ "    /* 8: double horizontal lines and double vertical lines */
        });
    }
}
