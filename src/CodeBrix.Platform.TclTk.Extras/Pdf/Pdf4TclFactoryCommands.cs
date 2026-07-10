using System;
using System.Globalization;
using System.Threading;

using CodeBrix.Platform.TclTk._Commands;
using CodeBrix.Platform.TclTk._Components.Public;
using CodeBrix.Platform.TclTk._Containers.Public;
using CodeBrix.Platform.TclTk._Interfaces.Public;

namespace CodeBrix.Platform.TclTk.Extras.Pdf;

/// <summary>
/// The <c>pdf4tcl::new NAME ?options?</c> command: creates a PDF document object and
/// registers NAME (or a generated name for <c>%AUTO%</c>) as its Tcl object command.
/// Options: <c>-paper</c>, <c>-landscape</c>, <c>-margin</c>, <c>-orient</c>,
/// <c>-unit</c>, <c>-file</c>; <c>-rotate</c> and <c>-compress</c> are accepted and
/// ignored (CodeBrix.PdfDocuments manages stream compression itself).
/// </summary>
internal sealed class Pdf4TclNewCommand : Default
{
    private static int _autoCounter;

    public Pdf4TclNewCommand(ICommandData commandData)
        : base(commandData)
    {
    }

    public override ReturnCode Execute(
        Interpreter interpreter, IClientData clientData, ArgumentList arguments, ref Result result)
    {
        if (interpreter == null) { result = "invalid interpreter"; return ReturnCode.Error; }
        if (arguments == null || arguments.Count < 2 || (arguments.Count % 2) != 0)
        {
            result = "wrong # args: should be \"pdf4tcl::new NAME ?option value ...?\"";
            return ReturnCode.Error;
        }

        string objectName = arguments[1];
        if (objectName == "%AUTO%")
        {
            objectName = "pdf4tcl" +
                Interlocked.Increment(ref _autoCounter).ToString(CultureInfo.InvariantCulture);
        }

        string paper = "a4";
        bool landscape = false;
        string margin = "0";
        bool orient = true;
        double unit = 1.0;
        string file = null;

        for (int i = 2; i < arguments.Count; i += 2)
        {
            string option = arguments[i];
            string value = arguments[i + 1];
            switch (option)
            {
                case "-paper":
                    if (!Pdf4TclUnits.TryGetPaperSize(value, 1.0, out double _, out double _))
                    {
                        result = string.Format("papersize {0} is unknown", value);
                        return ReturnCode.Error;
                    }
                    paper = value;
                    break;
                case "-landscape":
                    if (!Pdf4TclObjectCommand.TryParseBoolean(value, out landscape))
                    {
                        result = string.Format("expected boolean but got \"{0}\"", value);
                        return ReturnCode.Error;
                    }
                    break;
                case "-margin":
                    margin = value;
                    break;
                case "-orient":
                    if (!Pdf4TclObjectCommand.TryParseBoolean(value, out orient))
                    {
                        result = string.Format("expected boolean but got \"{0}\"", value);
                        return ReturnCode.Error;
                    }
                    break;
                case "-unit":
                    if (!Pdf4TclUnits.Units.TryGetValue(value, out unit))
                    {
                        result = string.Format("unit {0} is unknown", value);
                        return ReturnCode.Error;
                    }
                    break;
                case "-file":
                    file = value;
                    break;
                case "-rotate":
                case "-compress":
                    break; // accepted, ignored
                default:
                    result = string.Format("unknown option {0}", option);
                    return ReturnCode.Error;
            }
        }

        var command = new Pdf4TclObjectCommand(
            new CommandData(
                objectName, "pdf4tcl", null, null,
                typeof(Pdf4TclObjectCommand).FullName, CommandFlags.None, null, 0),
            paper, landscape, margin, orient, unit, file);

        long token = 0;
        Result addError = null;

        if (interpreter.AddCommand(command, null, ref token, ref addError) != ReturnCode.Ok)
        {
            result = addError;
            return ReturnCode.Error;
        }

        command.Token = token;

        result = objectName;
        return ReturnCode.Ok;
    }
}

/// <summary>
/// The <c>pdf4tcl::loadBaseTrueTypeFont BASEFONTNAME FILENAME ?validate?</c> command:
/// loads a TrueType font file so <c>pdf4tcl::createFont</c> can create usable fonts
/// from it.
/// </summary>
internal sealed class Pdf4TclLoadBaseTrueTypeFontCommand : Default
{
    public Pdf4TclLoadBaseTrueTypeFontCommand(ICommandData commandData)
        : base(commandData)
    {
    }

    public override ReturnCode Execute(
        Interpreter interpreter, IClientData clientData, ArgumentList arguments, ref Result result)
    {
        if (arguments == null || (arguments.Count != 3 && arguments.Count != 4))
        {
            result = "wrong # args: should be \"pdf4tcl::loadBaseTrueTypeFont BASEFONTNAME FILENAME ?validate?\"";
            return ReturnCode.Error;
        }

        try
        {
            PdfFontStore.LoadBaseTrueTypeFont(arguments[1], arguments[2]);
        }
        catch (Exception ex)
        {
            result = ex.Message;
            return ReturnCode.Error;
        }

        result = string.Empty;
        return ReturnCode.Ok;
    }
}

/// <summary>
/// The <c>pdf4tcl::createFont BASEFONTNAME FONTNAME ENCODING</c> command: creates a
/// font usable by <c>setFont</c> from a loaded base font. The encoding argument is
/// accepted for compatibility; fonts embed with Unicode encoding, which covers every
/// single-byte encoding pdf4tcl could subset.
/// </summary>
internal sealed class Pdf4TclCreateFontCommand : Default
{
    public Pdf4TclCreateFontCommand(ICommandData commandData)
        : base(commandData)
    {
    }

    public override ReturnCode Execute(
        Interpreter interpreter, IClientData clientData, ArgumentList arguments, ref Result result)
    {
        if (arguments == null || (arguments.Count != 3 && arguments.Count != 4))
        {
            result = "wrong # args: should be \"pdf4tcl::createFont BASEFONTNAME FONTNAME ?ENCODING?\"";
            return ReturnCode.Error;
        }

        if (!PdfFontStore.TryCreateFont(arguments[1], arguments[2]))
        {
            result = string.Format("base font {0} doesn't exist", (string)arguments[1]);
            return ReturnCode.Error;
        }

        result = string.Empty;
        return ReturnCode.Ok;
    }
}
