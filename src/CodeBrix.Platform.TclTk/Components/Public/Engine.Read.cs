/*
 * Engine.Read.cs --
 *
 * Copyright (c) 2007-2012 by Joe Mistachkin.  All rights reserved.
 *
 * See the file "license.terms" for information on usage and redistribution of
 * this file, and for a DISCLAIMER OF ALL WARRANTIES.
 *
 * RCS: @(#) $Id: $
 *
 * This file is one partial-class segment of the TclTk execution engine.  It
 * was split verbatim out of Engine.cs (the "Read Methods" region group) so that no
 * single source file grows unmanageably large.  See Engine.cs for the
 * type-level documentation and the [ObjectId] declaration.
 */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

#if NETWORK
using System.Net;
#endif

using System.Reflection;
using System.Text;
using System.Threading;

#if XML
using System.Xml;
#endif

using CodeBrix.Platform.TclTk._Attributes;
using CodeBrix.Platform.TclTk._Components.Private;
using CodeBrix.Platform.TclTk._Components.Public.Delegates;
using CodeBrix.Platform.TclTk._Constants;
using CodeBrix.Platform.TclTk._Containers.Private;
using CodeBrix.Platform.TclTk._Containers.Public;
using CodeBrix.Platform.TclTk._Interfaces.Private;
using CodeBrix.Platform.TclTk._Interfaces.Public;
using RSCD = CodeBrix.Platform.TclTk._Components.Private.ReadScriptClientData;
using GSCD = CodeBrix.Platform.TclTk._Components.Private.GetScriptClientData;
using SharedStringOps = CodeBrix.Platform.TclTk._Components.Shared.StringOps;

#if NET_STANDARD_21
using Index = CodeBrix.Platform.TclTk._Constants.Index;
#endif

namespace CodeBrix.Platform.TclTk._Components.Public //was previously: Eagle._Components.Public;
{
    public static partial class Engine
    {
        #region Read Methods
        #region Read Post-Script Methods
        /// <summary>
        /// This method removes any leading bytes from the supplied byte
        /// list up to and including the first "soft" end-of-file byte
        /// (if any), leaving only the bytes that follow it (i.e. the
        /// "post-script" bytes).  When the list is null or contains no
        /// "soft" end-of-file byte, it is left unchanged.
        /// </summary>
        /// <param name="bytes">
        /// The list of bytes to modify in place.  Upon return, any bytes at
        /// or before the first "soft" end-of-file have been removed.
        /// </param>
        private static void MaybeRemoveNonPostScriptBytes(
            ref ByteList bytes /* in, out */
            )
        {
            if (bytes == null)
                return;

            int index = bytes.IndexOf((byte)Characters.EndOfFile);

            if (index == Index.Invalid)
                return;

            bytes.RemoveRange(0, index + 1);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method reads the "post-script" bytes (i.e. the raw bytes
        /// that follow the first "soft" end-of-file) from a stream using
        /// the supplied callbacks.  When requested, it first reads one byte
        /// at a time until the first "soft" end-of-file is reached and
        /// then reads the remaining bytes in chunks.  Exceptions thrown by
        /// the callbacks are the responsibility of the caller to handle.
        /// </summary>
        /// <param name="charCallback">
        /// The callback used to read a single byte (as an integer) from the
        /// stream when seeking to the first "soft" end-of-file.  When this
        /// is null, the seeking step cannot be performed.
        /// </param>
        /// <param name="bytesCallback">
        /// The callback used to read a chunk of bytes from the stream.  When
        /// this is null, no bytes can be read.
        /// </param>
        /// <param name="streamLength">
        /// The total length of the stream, when known, used to preallocate
        /// capacity for the resulting list of bytes; otherwise, the invalid
        /// length sentinel.
        /// </param>
        /// <param name="seekSoftEof">
        /// Non-zero to first skip all bytes up to and including the first
        /// "soft" end-of-file before reading; zero when the stream is
        /// already positioned appropriately.
        /// </param>
        /// <param name="bytes">
        /// The list of bytes to receive the post-script bytes.  When it is
        /// non-null upon entry, the bytes read are appended to it; otherwise,
        /// a new list is created and stored here.
        /// </param>
        internal static void ReadPostScriptBytes(
            ReadInt32Callback charCallback,  /* in */
            ReadBytesCallback bytesCallback, /* in */
            long streamLength,               /* in */
            bool seekSoftEof,                /* in */
            ref ByteList bytes               /* in, out */
            )
        {
            //
            // NOTE: If requested, skip all bytes of the stream until we
            //       hit the first "soft" end-of-file, if any.  In the
            //       event the stream is already pre-positioned, this
            //       step can be skipped at the request of the caller.
            //
            if (seekSoftEof)
            {
                //
                // NOTE: If the caller requested skipping all bytes to
                //       the first "soft" end-of-file and there is not
                //       a character callback available, fail.
                //
                if (charCallback == null)
                    return;

                //
                // NOTE: This loop must read a single byte at a time so
                //       it can stop immediately upon hitting the "soft"
                //       end-of-file indicator value.
                //
                while (true)
                {
                    //
                    // NOTE: Attempt to read a byte from the stream.  If
                    //       this throws an exception, our caller will be
                    //       responsible for catching it.
                    //
                    int readByte = charCallback(); /* throw */

                    //
                    // NOTE: Check for a "hard" end-of-stream.
                    //
                    if (readByte == ChannelStream.EndOfFile)
                        return;

                    //
                    // NOTE: Check for a "soft" end-of-file.
                    //
                    if (readByte == Characters.EndOfFile)
                        break;
                }
            }

            //
            // NOTE: If there is no callback specified to read the data
            //       bytes, we cannot read them.
            //
            if (bytesCallback == null)
                return;

            //
            // NOTE: Set the initial number of bytes to read at a time to
            //       the typical default.
            //
            int wantedToRead = ReadPostScriptBufferSize;

            //
            // NOTE: If the caller specified an overall stream length, use
            //       it to preallocate enough capacity for the resulting
            //       list of bytes.
            //
            ByteList localBytes;
            byte[] buffer;

            if (streamLength != Length.Invalid)
            {
                //
                // NOTE: Preallocate enough capacity to hold the entire
                //       contents of the stream (at least as much of it
                //       as we actually plan on reading).
                //
                localBytes = new ByteList((int)streamLength); /* throw */

                //
                // NOTE: If the chunk size is less than the overall stream
                //       length, use it; otherwise, use the overall stream
                //       length to avoid preallocating too much space.
                //
                if (wantedToRead <= streamLength)
                    buffer = new byte[wantedToRead]; /* throw */
                else
                    buffer = new byte[streamLength]; /* throw */
            }
            else
            {
                //
                // NOTE: Since the caller did not specify a stream length,
                //       just preallocate enough capacity to hold a single
                //       chunk.
                //
                localBytes = new ByteList(wantedToRead); /* throw */

                //
                // NOTE: Allocate a byte array buffer large enough to hold
                //       a single chunk.  It is possible for this to throw
                //       an exception under low-memory conditions; however,
                //       that should be fairly rare since this amount is
                //       fixed and relatively small.
                //
                buffer = new byte[wantedToRead]; /* throw */
            }

            //
            // NOTE: This loop will read N fixed-size chunks of bytes from
            //       the stream, where N may be zero.  Then, it may read a
            //       final chunk if the stream had a size not divisible by
            //       the chunk size.
            //
            while (true)
            {
                //
                // NOTE: If the caller specified an overall stream length,
                //       and that is less than the chunk size then reduce
                //       the chunk size to match it.
                //
                if ((streamLength != Length.Invalid) &&
                    (streamLength < wantedToRead))
                {
                    wantedToRead = (int)streamLength;
                }

                //
                // NOTE: Attempt to read the next chunk of bytes from the
                //       stream into the buffer.
                //
                /* throw */
                int actuallyRead = bytesCallback(buffer, 0, wantedToRead);

                //
                // NOTE: If no bytes were read, this is end-of-stream and
                //       we are now completely done with the stream.
                //
                if (actuallyRead == 0)
                    break;

                //
                // NOTE: If less bytes were read than requested, this is
                //       also end-of-stream; however, the bytes actually
                //       read must be copied into the result before we
                //       are done.
                //
                if (actuallyRead < wantedToRead)
                {
                    //
                    // NOTE: Get rid of excess bytes in the chunk buffer
                    //       so it can be added verbatim to the resulting
                    //       byte list.
                    //
                    Array.Resize(ref buffer, actuallyRead);

                    //
                    // NOTE: Add the entire (shrunken) contents of the
                    //       chunk buffer to the resulting byte list.
                    //
                    localBytes.AddRange(buffer);

                    //
                    // NOTE: We are now completely done with the stream.
                    //
                    break;
                }
                else
                {
                    //
                    // NOTE: An entire chunk was read.  Add the entire
                    //       contents of the chunk buffer to the resulting
                    //       byte list.
                    //
                    localBytes.AddRange(buffer);
                }

                //
                // NOTE: If the caller specified an overall stream length,
                //       adjust and check the remaining bytes to be read.
                //
                if (streamLength != Length.Invalid)
                {
                    streamLength -= actuallyRead;

                    if (streamLength == 0)
                        break;
                }
            }

            //
            // NOTE: Commit changes to the output parameter supplied by
            //       the caller.
            //
            if (bytes != null)
                bytes.AddRange(localBytes);
            else
                bytes = localBytes;
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////////////////////

        #region Read Script (Shared) Methods
        /// <summary>
        /// This method attempts to determine the length, in bytes, of a
        /// stream.  It only succeeds when the stream is non-null and
        /// supports seeking.
        /// </summary>
        /// <param name="stream">
        /// The stream whose length is to be queried.
        /// </param>
        /// <param name="length">
        /// Upon success, receives the length of the stream, in bytes.  Upon
        /// failure, receives the invalid length sentinel.
        /// </param>
        /// <returns>
        /// Non-zero if the stream length was successfully determined;
        /// otherwise, zero.
        /// </returns>
        private static bool GetStreamLength(
            Stream stream,  /* in */
            out long length /* out */
            )
        {
            if ((stream != null) && stream.CanSeek)
            {
                length = stream.Length;
                return true;
            }

            length = Length.Invalid;
            return false;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method attempts to determine the length, in bytes, of the
        /// underlying base stream associated with a
        /// <see cref="StreamReader" /> or <see cref="BinaryReader" />.
        /// </summary>
        /// <param name="reader">
        /// The reader object whose underlying base stream length is to be
        /// queried.
        /// </param>
        /// <returns>
        /// The length of the underlying base stream, in bytes, or the
        /// invalid length sentinel when it cannot be determined.
        /// </returns>
        private static long GetStreamLength(
            object reader /* in */
            )
        {
            long length;
            StreamReader streamReader = reader as StreamReader;

            if ((streamReader != null) && GetStreamLength(
                    streamReader.BaseStream, out length))
            {
                return length;
            }

            BinaryReader binaryReader = reader as BinaryReader;

            if ((binaryReader != null) && GetStreamLength(
                    binaryReader.BaseStream, out length))
            {
                return length;
            }

            return Length.Invalid;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method sets up a callback that reads a single character
        /// (as an integer) from the supplied text reader.  When the text
        /// reader is null, the callback is left unchanged.
        /// </summary>
        /// <param name="textReader">
        /// The text reader to read characters from.
        /// </param>
        /// <param name="charCallback">
        /// Upon success, receives a callback that reads a single character
        /// from <paramref name="textReader" />.
        /// </param>
        private static void GetStreamCallback(
            TextReader textReader,             /* in */
            ref ReadInt32Callback charCallback /* out */
            )
        {
            if (textReader == null)
                return;

            charCallback = textReader.Read;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method sets up the callbacks used to read a single
        /// character (as an integer) and a buffer of characters from the
        /// supplied text reader.  When the text reader is null, the
        /// callbacks are left unchanged.
        /// </summary>
        /// <param name="textReader">
        /// The text reader to read characters from.
        /// </param>
        /// <param name="charCallback">
        /// Upon success, receives a callback that reads a single character
        /// from <paramref name="textReader" />.
        /// </param>
        /// <param name="charsCallback">
        /// Upon success, receives a callback that reads a buffer of
        /// characters from <paramref name="textReader" />.
        /// </param>
        private static void GetStreamCallbacks(
            TextReader textReader,              /* in */
            ref ReadInt32Callback charCallback, /* out */
            ref ReadCharsCallback charsCallback /* out */
            )
        {
            if (textReader == null)
                return;

            GetStreamCallback(
                textReader, ref charCallback);

            charsCallback = textReader.Read;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method sets up a callback that reads a buffer of bytes
        /// from the supplied binary reader.  When the binary reader is
        /// null, the callback is left unchanged.
        /// </summary>
        /// <param name="binaryReader">
        /// The binary reader to read bytes from.
        /// </param>
        /// <param name="bytesCallback">
        /// Upon success, receives a callback that reads a buffer of bytes
        /// from <paramref name="binaryReader" />.
        /// </param>
        private static void GetStreamCallback(
            BinaryReader binaryReader,          /* in */
            ref ReadBytesCallback bytesCallback /* out */
            )
        {
            if (binaryReader == null)
                return;

            bytesCallback = binaryReader.Read;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method sets up the callbacks used to read a single byte
        /// (as an integer) and a buffer of bytes from the supplied binary
        /// reader.  When the binary reader is null, the callbacks are left
        /// unchanged.
        /// </summary>
        /// <param name="binaryReader">
        /// The binary reader to read bytes from.
        /// </param>
        /// <param name="charCallback">
        /// Upon success, receives a callback that reads a single byte from
        /// <paramref name="binaryReader" />.
        /// </param>
        /// <param name="bytesCallback">
        /// Upon success, receives a callback that reads a buffer of bytes
        /// from <paramref name="binaryReader" />.
        /// </param>
        private static void GetStreamCallbacks(
            BinaryReader binaryReader,          /* in */
            ref ReadInt32Callback charCallback, /* out */
            ref ReadBytesCallback bytesCallback /* out */
            )
        {
            if (binaryReader == null)
                return;

            charCallback = binaryReader.Read;

            GetStreamCallback(
                binaryReader, ref bytesCallback);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method reads characters one at a time using the supplied
        /// callback until a "hard" end-of-stream is reached -- or until a
        /// "soft" end-of-file is reached, subject to the
        /// <paramref name="forceSoftEof" /> parameter and whether the
        /// "original" buffer is being built.  As characters are read, it
        /// translates end-of-line sequences (carriage-return/line-feed and
        /// bare carriage-return) into a single line-feed in the normal
        /// buffer, while the "original" buffer receives every character
        /// verbatim.  This logic must not be replaced with a bulk file read
        /// because doing so would not preserve the end-of-file character
        /// semantics required by the <c>source</c> command.
        /// </summary>
        /// <param name="charCallback">
        /// The callback used to read a single character (as an integer) from
        /// the stream.
        /// </param>
        /// <param name="builder">
        /// The buffer that receives the script text with its end-of-line
        /// sequences translated; it may be null.
        /// </param>
        /// <param name="originalBuilder">
        /// The buffer that receives every character verbatim (for use by the
        /// policy engine); it may be null.
        /// </param>
        /// <param name="forceSoftEof">
        /// Non-zero to stop reading as soon as the first "soft" end-of-file
        /// is reached even when the "original" buffer is being built; zero
        /// to keep reading the remaining characters into the "original"
        /// buffer.
        /// </param>
        /// <param name="preSoftEofLength">
        /// Upon return, receives the length of the "original" buffer at the
        /// point the first "soft" end-of-file was reached, or the invalid
        /// length sentinel when none was reached.
        /// </param>
        private static void ReadScriptVia(
            ReadInt32Callback charCallback, /* in */
            StringBuilder builder,          /* in, out */
            StringBuilder originalBuilder,  /* in, out */
            bool forceSoftEof,              /* in */
            out int preSoftEofLength        /* out */
            )
        {
            bool hitSoftEof = false;
            int lastCharacter = Characters.Null;
            int character;

            //
            // NOTE: Initially, make sure the length passed by the
            //       caller has a well-defined (and invalid) value.
            //
            preSoftEofLength = Length.Invalid;

            //
            // NOTE: *WARNING* Do NOT "optimize" this code to use
            //       File.ReadAllText because that does not preserve
            //       the end-of-file character semantics for the
            //       [source] command (i.e. "scripted documents").
            //       Also, we must handle end-of-line translations
            //       (Cr/Lf --> Lf) here.  Keep going until we hit a
            //       "hard" end-of-stream (or file) -OR- until we hit
            //       a "soft" end-of-stream (or file) if that flag
            //       has been specified by the caller.
            //
            /* throw */
            while ((character = charCallback()) != ChannelStream.EndOfFile)
            {
                //
                // NOTE: Did we hit a "soft" end-of-file?
                //
                if (character == Characters.EndOfFile)
                {
                    //
                    // NOTE: If we are building the "original" buffer
                    //       we must keep going even after hitting a
                    //       "soft" end-of-file; otherwise, we can
                    //       stop reading now.  However, even if we
                    //       intend to keep going here, we set an
                    //       indicator to prevent the normal buffer
                    //       from being modified after this point.
                    //       Unless we are forbidden from doing any
                    //       of this special handling by the caller.
                    //
                    if (originalBuilder != null)
                    {
                        if (preSoftEofLength == Length.Invalid)
                            preSoftEofLength = originalBuilder.Length;

                        if (!forceSoftEof)
                        {
                            originalBuilder.Append((char)character);
                            hitSoftEof = true;
                        }
                        else
                        {
                            break;
                        }
                    }
                    else
                    {
                        break;
                    }
                }
                else
                {
                    //
                    // NOTE: If we previously hit a "soft" end-of-file,
                    //       skip changing the normal buffer; however,
                    //       always add all available characters to the
                    //       "original" buffer for use by the policy
                    //       engine.
                    //
                    if (!hitSoftEof && (builder != null))
                    {
                        //
                        // NOTE: If the current character is a line-feed
                        //       and the last character that we saw was
                        //       a carriage-return, strip the previous
                        //       character (the carriage-return) from
                        //       the buffer and then simply add the
                        //       current character (the line-feed).
                        //
                        if (lastCharacter == Characters.CarriageReturn)
                        {
                            int length = builder.Length - 1;

                            if (character == Characters.LineFeed)
                            {
                                //
                                // NOTE: To support the DOS end-of-line
                                //       convention we need to remove
                                //       the previous character, thus
                                //       collapsing the carriage-return
                                //       line-feed pair into a single
                                //       line-feed.
                                //
                                builder.Length = length;
                            }
                            else
                            {
                                //
                                // NOTE: To support the Mac end-of-line
                                //       convention we need to replace
                                //       the carriage-return character
                                //       with the line-feed character
                                //       (i.e. the Unix end-of-line
                                //       character).
                                //
                                builder[length] = Characters.LineFeed;
                            }
                        }

                        builder.Append((char)character);
                        lastCharacter = character;
                    }

                    //
                    // NOTE: When available, always add all the original
                    //       characters to the "original" buffer.
                    //
                    if (originalBuilder != null)
                        originalBuilder.Append((char)character);
                }
            }

            //
            // NOTE: Replace the final carriage-return, if any, with a
            //       line-feed.
            //
            if (builder != null)
            {
                int length = builder.Length;

                if ((length > 0) &&
                    (builder[length - 1] == Characters.CarriageReturn))
                {
                    builder[length - 1] = Characters.LineFeed;
                }
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method reads an entire script from the stream using the
        /// supplied callback, returning both the line-ending translated
        /// text and the verbatim original text.  It is a convenience
        /// wrapper that discards the pre-"soft" end-of-file length.
        /// </summary>
        /// <param name="charCallback">
        /// The callback used to read a single character (as an integer) from
        /// the stream.
        /// </param>
        /// <param name="streamLength">
        /// The total length of the stream, when known, used to preallocate
        /// buffer capacity; otherwise, the invalid length sentinel.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags in effect; these determine, among other things,
        /// whether the first "soft" end-of-file forces reading to stop.
        /// </param>
        /// <param name="originalText">
        /// Upon return, receives the verbatim original script text.
        /// </param>
        /// <param name="text">
        /// Upon return, receives the script text with its end-of-line
        /// sequences translated to line-feeds.
        /// </param>
        private static void ReadScriptVia(
            ReadInt32Callback charCallback, /* in */
            long streamLength,              /* in */
            EngineFlags engineFlags,        /* in */
            ref string originalText,        /* out */
            ref string text                 /* out */
            )
        {
            int preSoftEofLength;

            ReadScriptVia(
                charCallback, streamLength, engineFlags,
                ref originalText, ref text, out preSoftEofLength);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method reads an entire script from the stream using the
        /// supplied callback, returning both the line-ending translated
        /// text and the verbatim original text, along with the length of
        /// the original text at the first "soft" end-of-file.
        /// </summary>
        /// <param name="charCallback">
        /// The callback used to read a single character (as an integer) from
        /// the stream.
        /// </param>
        /// <param name="streamLength">
        /// The total length of the stream, when known, used to preallocate
        /// buffer capacity; otherwise, the invalid length sentinel.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags in effect; these determine, among other things,
        /// whether the first "soft" end-of-file forces reading to stop.
        /// </param>
        /// <param name="originalText">
        /// Upon return, receives the verbatim original script text.
        /// </param>
        /// <param name="text">
        /// Upon return, receives the script text with its end-of-line
        /// sequences translated to line-feeds.
        /// </param>
        /// <param name="preSoftEofLength">
        /// Upon return, receives the length of the original text at the point
        /// the first "soft" end-of-file was reached, or the invalid length
        /// sentinel when none was reached.
        /// </param>
        private static void ReadScriptVia(
            ReadInt32Callback charCallback, /* in */
            long streamLength,              /* in */
            EngineFlags engineFlags,        /* in */
            ref string originalText,        /* out */
            ref string text,                /* out */
            out int preSoftEofLength        /* out */
            )
        {
            StringBuilder builder = (streamLength != Length.Invalid) ?
                StringBuilderFactory.Create((int)streamLength) :
                StringBuilderFactory.Create();

            StringBuilder originalBuilder = StringBuilderFactory.Create(
                builder.Capacity);

            bool forceSoftEof = EngineFlagOps.HasForceSoftEof(engineFlags);

            //
            // NOTE: Perform the actual reading of the raw characters
            //       from the text reader.
            //
            ReadScriptVia(
                charCallback, builder, originalBuilder, forceSoftEof,
                out preSoftEofLength);

            //
            // NOTE: Get both the whole buffers as strings (i.e. both
            //       the original and line-ending modified ones).
            //
            originalText = StringBuilderCache.GetStringAndRelease(
                ref originalBuilder);

            text = StringBuilderCache.GetStringAndRelease(ref builder);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method reads (and post-processes) a script from a string
        /// of text already held in memory.  Internally, it wraps the text
        /// in a string reader and processes it as it would a script
        /// stream, performing end-of-line translation and any applicable
        /// policy and XML handling.  This method is thread-safe.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context to use, which supplies the active flags
        /// and policies; it may be null.
        /// </param>
        /// <param name="fileName">
        /// The name to associate with the script being read, used for error
        /// reporting and policy checks; it may be null.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags that control how the script text is read and
        /// processed.
        /// </param>
        /// <param name="text">
        /// Upon entry, contains the script text to read.  Upon success, this
        /// is replaced with the processed script text.
        /// </param>
        /// <param name="error">
        /// Upon failure, receives information about the error that occurred.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise,
        /// <see cref="ReturnCode.Error" />.
        /// </returns>
        public static ReturnCode ReadScriptFromText(
            Interpreter interpreter, /* in */
            string fileName,         /* in */
            EngineFlags engineFlags, /* in */
            ref string text,         /* in, out */
            ref Result error         /* out */
            ) /* ENTRY-POINT, THREAD-SAFE */
        {
            try
            {
                using (StringReader stringReader = new StringReader(text)) /* throw */
                {
                    string localText = null;

                    if (ReadScriptStream(
                            interpreter, fileName, stringReader, 0, Count.Invalid,
                            engineFlags, ref localText, ref error) == ReturnCode.Ok)
                    {
                        text = localText;
                        return ReturnCode.Ok;
                    }
                    else
                    {
                        return ReturnCode.Error;
                    }
                }
            }
            catch (Exception e)
            {
                error = e;
                return ReturnCode.Error;
            }
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////////////////////

        #region Read Script (XML) Methods
#if XML
        /// <summary>
        /// This method reads the payload of a single XML script-block
        /// node.  Depending on the block type recorded on the node, the
        /// payload is treated as verbatim script text, a base64-encoded
        /// script, or a (local or remote) URI pointing to a script file;
        /// the "automatic" block type is resolved to one of these.  This
        /// method is thread-safe.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context to use, which supplies policies and
        /// notifications; it may be null.
        /// </param>
        /// <param name="node">
        /// The XML node whose script-block payload is to be read.
        /// </param>
        /// <param name="encoding">
        /// The text encoding to use when decoding a base64 or URI block;
        /// when null, an encoding is guessed or a context-specific default
        /// is used.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags in effect; these may be modified while reading
        /// the node.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags in effect; these may be modified while
        /// reading the node.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags in effect; these may be modified while reading
        /// the node.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags in effect; these may be modified while
        /// reading the node.
        /// </param>
        /// <param name="originalText">
        /// Upon return, receives the verbatim original script text for the
        /// node.
        /// </param>
        /// <param name="text">
        /// Upon return, receives the processed script text for the node.
        /// </param>
        /// <param name="error">
        /// Upon failure, receives information about the error that occurred.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise,
        /// <see cref="ReturnCode.Error" />.
        /// </returns>
        private static ReturnCode ReadScriptXmlNode(
            Interpreter interpreter,                 /* in */
            XmlNode node,                            /* in */
            Encoding encoding,                       /* in */
            ref EngineFlags engineFlags,             /* in, out */
            ref SubstitutionFlags substitutionFlags, /* in, out */
            ref EventFlags eventFlags,               /* in, out */
            ref ExpressionFlags expressionFlags,     /* in, out */
            ref string originalText,                 /* out */
            ref string text,                         /* out */
            ref Result error                         /* out */
            ) /* THREAD-SAFE */
        {
            XmlBlockType localBlockType;
            string localText;

            if (!ScriptXmlOps.TryGetAttributeValues(
                    node, XmlAttributeListType.Engine,
                    false, out localBlockType,
                    out localText, ref error))
            {
                return ReturnCode.Error;
            }

            switch (localBlockType)
            {
                case XmlBlockType.None:
                    {
                        //
                        // NOTE: Ok, give them nothing.
                        //
                        originalText = null;
                        text = null;

                        return ReturnCode.Ok;
                    }
                case XmlBlockType.Automatic:
                    {
                        //
                        // NOTE: Attempt to "automatically"
                        //       determine the type of block it
                        //       is.
                        //
                        if (StringOps.IsBase64(localText))
                            goto case XmlBlockType.Base64;
                        else if (PathOps.IsUri(localText))
                            goto case XmlBlockType.Uri;
                        else
                            goto case XmlBlockType.Text;
                    }
                case XmlBlockType.Text:
                    {
                        //
                        // NOTE: The element must contain the script text,
                        //       verbatim.
                        //
                        using (StringReader stringReader = new StringReader(
                                localText))
                        {
                            ReadInt32Callback charCallback = null;

                            GetStreamCallback(
                                stringReader, ref charCallback);

                            ReadScriptVia(
                                charCallback, Length.Invalid,
                                engineFlags, ref originalText,
                                ref text);
                        }

                        return ReturnCode.Ok;
                    }
                case XmlBlockType.Base64:
                    {
                        try
                        {
                            //
                            // NOTE: The element must contain the base64
                            //       encoded script in our system default
                            //       text encoding.
                            //
                            byte[] bytes = Convert.FromBase64String(
                                localText);

                            Encoding base64Encoding = (encoding != null) ?
                                encoding : StringOps.GuessOrGetEncoding(
                                    bytes, EncodingType.Base64);

                            if (base64Encoding == null)
                            {
                                error = "base64 encoding not available";
                                return ReturnCode.Error;
                            }

                            using (StringReader stringReader = new StringReader(
                                    base64Encoding.GetString(bytes)))
                            {
                                ReadInt32Callback charCallback = null;

                                GetStreamCallback(
                                    stringReader, ref charCallback);

                                ReadScriptVia(
                                    charCallback, Length.Invalid,
                                    engineFlags, ref originalText,
                                    ref text);
                            }

                            return ReturnCode.Ok;
                        }
                        catch (Exception e)
                        {
                            error = String.Format(
                                "caught exception decoding base64 block: {0}",
                                e);

                            error.Exception = e;

                            SetExceptionErrorCode(interpreter, e);

#if NOTIFY
                            if ((interpreter != null) &&
                                !EngineFlagOps.HasNoNotify(engineFlags))
                            {
                                /* IGNORED */
                                interpreter.CheckNotification(
                                    NotifyType.Engine, NotifyFlags.Exception,
                                    new ObjectPair(node, localBlockType),
                                    interpreter, null, null, e, ref error);
                            }
#endif
                        }
                        break;
                    }
                case XmlBlockType.Uri:
                    {
                        //
                        // NOTE: The element must contain a URI (local
                        //       or remote) pointing to a script file
                        //       in our system default text encoding.
                        //
                        string fileName = localText;

                        //
                        // NOTE: Use the UTF-8 encoding [by default] in
                        //       this context, not the fallback used by
                        //       the Channel subsystem (ISO-8859-1).
                        //
                        Encoding uriEncoding = (encoding != null) ?
                            encoding : GetEncoding(fileName,
                                EncodingType.UnknownUri, null);

                        return ReadOrGetScriptFile(
                            interpreter, uriEncoding, ref fileName,
                            ref engineFlags, ref substitutionFlags,
                            ref eventFlags, ref expressionFlags,
                            ref originalText, ref text, ref error);
                    }
                default:
                    {
                        error = String.Format(
                            "unsupported xml block type {0}",
                            FormatOps.WrapOrNull(localBlockType));

                        break;
                    }
            }

            return ReturnCode.Error;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method appends script text obtained from an XML node to a
        /// buffer, first ensuring that any text already present in the
        /// buffer is terminated with a line-feed.  When the buffer is
        /// null, a new one is created.
        /// </summary>
        /// <param name="text">
        /// The script text to append to the buffer.
        /// </param>
        /// <param name="builder">
        /// The buffer to append to.  When it is null upon entry, a new
        /// buffer is created and stored here.
        /// </param>
        private static void AppendTextFromXmlNode(
            string text,              /* in */
            ref StringBuilder builder /* in, out */
            )
        {
            if (builder != null)
            {
                //
                // NOTE: If we have previously placed script text
                //       into the result, make 100% sure that it
                //       ends with an end-of-line character.
                //
                int length = builder.Length;

                if ((length > 0) &&
                    (builder[length - 1] != Characters.LineFeed))
                {
                    builder.Append(Characters.LineFeed);
                }
            }
            else
            {
                builder = StringBuilderFactory.CreateNoCache(); /* EXEMPT */
            }

            builder.Append(text);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method determines whether an XML script read that failed
        /// with a particular error type should be retried, based on the
        /// set of error types the caller is willing to retry.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context; this parameter is not used.
        /// </param>
        /// <param name="error">
        /// The error that occurred, which may influence the decision; it may
        /// be null.
        /// </param>
        /// <param name="errorType">
        /// The type of error that occurred.
        /// </param>
        /// <param name="retryTypes">
        /// The set of error types for which a retry is permitted.
        /// </param>
        /// <param name="default">
        /// The default result to use when the decision cannot otherwise be
        /// determined.
        /// </param>
        /// <returns>
        /// Non-zero if the failed XML read should be retried; otherwise,
        /// zero.
        /// </returns>
        private static bool CanRetryScriptXml(
            Interpreter interpreter,  /* in: NOT USED */
            Result error,             /* in: OPTIONAL */
            XmlErrorTypes errorType,  /* in */
            XmlErrorTypes retryTypes, /* in */
            bool @default             /* in */
            )
        {
            return XmlOps.ShouldRetryError(
                error, errorType, retryTypes, @default);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method returns the portion of the original script text
        /// that precedes the first "soft" end-of-file, which is the
        /// portion to be treated as XML.  When the text is null or empty,
        /// or when the length is the invalid sentinel, the original text
        /// is returned unchanged.
        /// </summary>
        /// <param name="originalText">
        /// The verbatim original script text.
        /// </param>
        /// <param name="preSoftEofLength">
        /// The length of the original text at the first "soft" end-of-file,
        /// or the invalid length sentinel.
        /// </param>
        /// <returns>
        /// The leading portion of the original text up to the first
        /// "soft" end-of-file, or the original text itself when no
        /// truncation is needed.
        /// </returns>
        private static string GetScriptXml(
            string originalText, /* in */
            int preSoftEofLength /* in */
            )
        {
            if (String.IsNullOrEmpty(originalText))
                return originalText;

            if (preSoftEofLength == Length.Invalid)
                return originalText;

            return originalText.Substring(0, preSoftEofLength);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method parses an XML script document and reads the script
        /// blocks it contains, returning the combined (flattened) script
        /// text.  It is a convenience wrapper that does not expose the
        /// resulting client data or list of script objects.  This method
        /// is thread-safe.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context to use, which supplies policies and
        /// notifications; it may be null.
        /// </param>
        /// <param name="encoding">
        /// The text encoding to use for decoding script blocks; when null,
        /// an encoding is determined from the document or guessed.
        /// </param>
        /// <param name="xml">
        /// The XML script document to parse.
        /// </param>
        /// <param name="retryTypes">
        /// The set of error types for which a retry is permitted, also used
        /// to carry flags such as whether to flatten the script text.
        /// </param>
        /// <param name="validate">
        /// Non-zero to validate the XML document against the schema before
        /// reading it.
        /// </param>
        /// <param name="relaxed">
        /// Non-zero to perform relaxed (rather than strict) validation.
        /// </param>
        /// <param name="all">
        /// Non-zero to read all script blocks in the document; zero to read
        /// only the first one.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags in effect; these may be modified while reading.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags in effect; these may be modified while
        /// reading.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags in effect; these may be modified while reading.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags in effect; these may be modified while
        /// reading.
        /// </param>
        /// <param name="text">
        /// Upon success, receives the combined script text read from the
        /// document.
        /// </param>
        /// <param name="canRetry">
        /// Upon failure, receives non-zero when the operation may be retried
        /// (for example, by treating the input as non-XML).
        /// </param>
        /// <param name="error">
        /// Upon failure, receives information about the error that occurred.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise,
        /// <see cref="ReturnCode.Error" />.
        /// </returns>
        private static ReturnCode ReadScriptXml(
            Interpreter interpreter,                 /* in */
            Encoding encoding,                       /* in */
            string xml,                              /* in */
            XmlErrorTypes retryTypes,                /* in */
            bool validate,                           /* in */
            bool relaxed,                            /* in */
            bool all,                                /* in */
            ref EngineFlags engineFlags,             /* in, out */
            ref SubstitutionFlags substitutionFlags, /* in, out */
            ref EventFlags eventFlags,               /* in, out */
            ref ExpressionFlags expressionFlags,     /* in, out */
            ref string text,                         /* out */
            ref bool canRetry,                       /* out */
            ref Result error                         /* out */
            ) /* ENTRY-POINT, THREAD-SAFE */
        {
            IClientData clientData = null;
            IEnumerable<IScript> scripts = null;

            return ReadScriptXml(
                interpreter, encoding, xml, retryTypes, validate,
                relaxed, all, ref engineFlags, ref substitutionFlags,
                ref eventFlags, ref expressionFlags, ref clientData,
                ref scripts, ref text, ref canRetry, ref error);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method parses an XML script document and reads the script
        /// blocks it contains, returning the script objects created from
        /// those blocks and, when requested, the combined (flattened)
        /// script text.  Each script block is subjected to the "before
        /// script" policy check before being accepted.  This method is
        /// thread-safe.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context to use, which supplies policies and
        /// notifications; it may be null.
        /// </param>
        /// <param name="encoding">
        /// The text encoding to use for decoding script blocks; when null,
        /// an encoding is determined from the document or guessed.
        /// </param>
        /// <param name="xml">
        /// The XML script document to parse.
        /// </param>
        /// <param name="retryTypes">
        /// The set of error types for which a retry is permitted, also used
        /// to carry flags such as whether to flatten the script text.
        /// </param>
        /// <param name="validate">
        /// Non-zero to validate the XML document against the schema before
        /// reading it.
        /// </param>
        /// <param name="relaxed">
        /// Non-zero to perform relaxed (rather than strict) validation.
        /// </param>
        /// <param name="all">
        /// Non-zero to read all script blocks in the document; zero to read
        /// only the first one.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags in effect; these may be modified while reading.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags in effect; these may be modified while
        /// reading.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags in effect; these may be modified while reading.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags in effect; these may be modified while
        /// reading.
        /// </param>
        /// <param name="clientData">
        /// The client data associated with the read operation; it may be
        /// modified during reading.
        /// </param>
        /// <param name="scripts">
        /// Upon success, receives the script objects created from the script
        /// blocks in the document.
        /// </param>
        /// <param name="text">
        /// Upon success, receives the combined script text read from the
        /// document when text flattening was requested.
        /// </param>
        /// <param name="canRetry">
        /// Upon failure, receives non-zero when the operation may be retried
        /// (for example, by treating the input as non-XML).
        /// </param>
        /// <param name="error">
        /// Upon failure, receives information about the error that occurred.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise,
        /// <see cref="ReturnCode.Error" />.
        /// </returns>
        internal static ReturnCode ReadScriptXml(
            Interpreter interpreter,                 /* in */
            Encoding encoding,                       /* in */
            string xml,                              /* in */
            XmlErrorTypes retryTypes,                /* in */
            bool validate,                           /* in */
            bool relaxed,                            /* in */
            bool all,                                /* in */
            ref EngineFlags engineFlags,             /* in, out */
            ref SubstitutionFlags substitutionFlags, /* in, out */
            ref EventFlags eventFlags,               /* in, out */
            ref ExpressionFlags expressionFlags,     /* in, out */
            ref IClientData clientData,              /* in, out */
            ref IEnumerable<IScript> scripts,        /* out */
            ref string text,                         /* out */
            ref bool canRetry,                       /* out */
            ref Result error                         /* out */
            ) /* ENTRY-POINT, THREAD-SAFE */
        {
            XmlDocument document = null;

            if (XmlOps.LoadString(
                    xml, ref document,
                    ref error) != ReturnCode.Ok)
            {
                canRetry = CanRetryScriptXml(
                    interpreter, error,
                    XmlErrorTypes.LoadXml,
                    retryTypes, false);

                return ReturnCode.Error;
            }

            if (validate)
            {
                if (XmlOps.Validate(
                        null, null, document, relaxed,
                        ref error) != ReturnCode.Ok)
                {
                    canRetry = CanRetryScriptXml(
                        interpreter, error,
                        XmlErrorTypes.Validate,
                        retryTypes, false);

                    return ReturnCode.Error;
                }
            }

            if (encoding == null)
            {
                if (XmlOps.GetEncoding(
                        document, FlagOps.HasFlags(retryTypes,
                        XmlErrorTypes.StrictEncoding, true),
                        ref encoding, ref error) != ReturnCode.Ok)
                {
                    canRetry = CanRetryScriptXml(
                        interpreter, error,
                        XmlErrorTypes.Encoding,
                        retryTypes, false);

                    return ReturnCode.Error;
                }
            }

            XmlNodeList nodeList = null;

            if (XmlOps.GetScriptBlockNodeList(
                    document, ref nodeList,
                    ref error) != ReturnCode.Ok)
            {
                canRetry = CanRetryScriptXml(
                    interpreter, error,
                    XmlErrorTypes.Nodes,
                    retryTypes, false);

                return ReturnCode.Error;
            }

            if ((nodeList == null) || (nodeList.Count == 0))
            {
                error = "no script blocks were found";

                canRetry = CanRetryScriptXml(
                    interpreter, error,
                    XmlErrorTypes.Empty,
                    retryTypes, true);

                return ReturnCode.Error;
            }

            ReturnCode code = ReturnCode.Ok;
            StringBuilder builder = null;
            IList<IScript> localScripts = null;

            foreach (XmlNode node in nodeList)
            {
                if (node == null)
                    continue;

#if NOTIFY
                if ((interpreter != null) &&
                    !EngineFlagOps.HasNoNotify(engineFlags))
                {
                    /* IGNORED */
                    interpreter.CheckNotification(
                        NotifyType.Xml, NotifyFlags.Read,
                        new ObjectTriplet(xml, node, builder),
                        interpreter, clientData, null, null,
                        ref error);
                }
#endif

                ///////////////////////////////////////////////////////////////

                //
                // NOTE: Attempt to create a script object from
                //       the XML node.
                //
                IScript script = Script.CreateFromXmlNode(
                    ScriptTypes.Block, node,
                    EngineMode.EvaluateScript, ScriptFlags.None,
                    engineFlags, substitutionFlags, eventFlags,
                    expressionFlags, clientData, ref error);

                //
                // NOTE: Make sure the script object creation
                //       succeeded.  It can fail due to policy,
                //       etc.  In that case, we will have to raise
                //       an error.
                //
                if (script == null)
                {
                    canRetry = CanRetryScriptXml(
                        interpreter, error,
                        XmlErrorTypes.Create,
                        retryTypes, true);

                    code = ReturnCode.Error;
                    break;
                }

                //
                // HACK: *SECURITY* Due to its use by the policy engine,
                //       an IScript created from XML cannot be modified.
                //
                script.MakeImmutable();

                ///////////////////////////////////////////////////////////////

                ReturnCode beforeScriptCode = ReturnCode.Ok;
                PolicyDecision beforeScriptDecision = PolicyDecision.None;
                Result beforeScriptPolicyResult = null;

                try
                {
                    #region Policy Checking: "Before Script"
                    if ((interpreter != null) &&
                        !EngineFlagOps.HasNoPolicy(engineFlags))
                    {
                        beforeScriptDecision = interpreter.ScriptInitialDecision;

                        beforeScriptCode = interpreter.CheckScriptPolicies(
                            PolicyFlags.EngineBeforeScript, script,
                            encoding, null, ref beforeScriptDecision,
                            ref beforeScriptPolicyResult);

                        interpreter.ScriptFinalDecision = PolicyOps.FinalDecision(
                            PolicyFlags.EngineBeforeScript, beforeScriptCode,
                            beforeScriptDecision);

                        if (!PolicyOps.IsSuccess(
                                beforeScriptCode, beforeScriptDecision))
                        {
                            canRetry = CanRetryScriptXml(
                                interpreter, error,
                                XmlErrorTypes.Policy,
                                retryTypes, false);

                            if (beforeScriptPolicyResult != null)
                            {
                                error = beforeScriptPolicyResult;
                            }
                            else
                            {
                                error = String.Format(
                                    "script {0} cannot be used, denied by policy",
                                    FormatOps.WrapOrNull(EntityOps.GetId(script)));
                            }

                            code = ReturnCode.Error;
                            break;
                        }
                    }
                    #endregion
                }
                finally
                {
#if POLICY_TRACE
                    TraceOps.MaybeWritePolicyTrace("ReadScriptXml", interpreter,
                        !PolicyContext.GetForceTraceFull(), "encoding", encoding,
                        "xml", xml, "retryTypes", retryTypes, "validate",
                        validate, "relaxed", relaxed, "all", all, "script",
                        script, "clientData", clientData, "engineFlags",
                        engineFlags, "substitutionFlags", substitutionFlags,
                        "eventFlags", eventFlags, "expressionFlags",
                        expressionFlags, "beforeScriptCode", beforeScriptCode,
                        "beforeScriptDecision", beforeScriptDecision,
                        "beforeScriptPolicyResult", beforeScriptPolicyResult,
                        "code", code, "canRetry", canRetry, "error", error);
#endif
                }

                ///////////////////////////////////////////////////////////////

                //
                // NOTE: Ok, add this script to the list of scripts
                //       from this XML document.
                //
                if (localScripts == null)
                    localScripts = new List<IScript>();

                localScripts.Add(script);

#if NOTIFY
                if ((interpreter != null) &&
                    !EngineFlagOps.HasNoNotify(engineFlags))
                {
                    /* IGNORED */
                    interpreter.CheckNotification(NotifyType.Xml |
                        NotifyType.Script, NotifyFlags.Added,
                        new ObjectTriplet(node, scripts, script),
                        interpreter, clientData, null, null,
                        ref error);
                }
#endif

                //
                // NOTE: Read the payload of the XML node.  If
                //       necessary, this will base64 decode it or
                //       fetch it from the specified remote URI.
                //
                string originalText = null;
                string localText = null;

                code = ReadScriptXmlNode(
                    interpreter, node, encoding, ref engineFlags,
                    ref substitutionFlags, ref eventFlags,
                    ref expressionFlags, ref originalText,
                    ref localText, ref error);

                if (code != ReturnCode.Ok)
                {
                    canRetry = CanRetryScriptXml(
                        interpreter, error,
                        XmlErrorTypes.Read,
                        retryTypes, false);

                    break;
                }

                //
                // HACK: While the "FlattenText" flag is technically
                //       optional for an interpreter, this method is
                //       of limited usefulness without it, i.e. it
                //       will only return a logical list of IScript
                //       objects, which most callers will ignore.
                //
                if (FlagOps.HasFlags(
                        retryTypes, XmlErrorTypes.FlattenText, true))
                {
                    AppendTextFromXmlNode(localText, ref builder);
                }

#if NOTIFY
                if ((interpreter != null) &&
                    !EngineFlagOps.HasNoNotify(engineFlags))
                {
                    /* IGNORED */
                    interpreter.CheckNotification(NotifyType.Xml |
                        NotifyType.XmlBlock, NotifyFlags.Read,
                        new ObjectList(node, builder, originalText,
                        localText), interpreter, clientData, null,
                        null, ref error);
                }
#endif

                //
                // NOTE: Only load the first result?  If so, get ready
                //       to break out of this loop now.
                //
                if (!all)
                    break;
            }

            //
            // NOTE: Upon success, get the whole buffer as a string.
            //       Also, put the list of scripts into the client data.
            //
            if (code == ReturnCode.Ok)
            {
                //
                // HACK: While the "FlattenText" flag is technically
                //       optional for an interpreter, this method is
                //       of limited usefulness without it, i.e. it
                //       will only return a logical list of IScript
                //       objects, which most callers will ignore.
                //
                if (FlagOps.HasFlags(
                        retryTypes, XmlErrorTypes.FlattenText, true) &&
                    (builder != null))
                {
                    text = StringBuilderCache.GetStringAndRelease(
                        ref builder);
                }

                scripts = localScripts;

                canRetry = false; /* SUCCESS? */
            }

            return code;
        }
#endif
        #endregion

        ///////////////////////////////////////////////////////////////////////////////////////

        #region Read Script (Stream) Methods
        /// <summary>
        /// This method reads (and post-processes) a script from a text
        /// reader.  It queries the active flags from the interpreter (or
        /// uses defaults when there is none), then reads the requested
        /// characters, performing end-of-line translation and any
        /// applicable policy and XML handling.  This method is
        /// thread-safe.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context to use, which supplies the active flags
        /// and policies; it may be null.
        /// </param>
        /// <param name="name">
        /// The name to associate with the script being read, used for error
        /// reporting and policy checks; it may be null.
        /// </param>
        /// <param name="textReader">
        /// The text reader to read the script from.
        /// </param>
        /// <param name="startIndex">
        /// The starting character index for the read operation.
        /// </param>
        /// <param name="characters">
        /// The number of characters to read, or a negative value to read the
        /// entire stream up to the first "soft" end-of-file.
        /// </param>
        /// <param name="text">
        /// Upon success, receives the script text that was read.
        /// </param>
        /// <param name="error">
        /// Upon failure, receives information about the error that occurred.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise,
        /// <see cref="ReturnCode.Error" />.
        /// </returns>
        public static ReturnCode ReadScriptStream(
            Interpreter interpreter, /* in */
            string name,             /* in */
            TextReader textReader,   /* in */
            int startIndex,          /* in */
            int characters,          /* in */
            ref string text,         /* out */
            ref Result error         /* out */
            ) /* ENTRY-POINT, THREAD-SAFE */
        {
            EngineFlags engineFlags;
            SubstitutionFlags substitutionFlags;
            EventFlags eventFlags;
            ExpressionFlags expressionFlags;

            if (interpreter != null)
            {
                if (!TryQueryAllFlags(interpreter,
                        BlockingFlagsForRead ||
                            BlockingFlagsForReadStream,
                        out engineFlags, out substitutionFlags,
                        out eventFlags, out expressionFlags,
                        ref error))
                {
                    return ReturnCode.Error;
                }
            }
            else
            {
                InitializeAllFlags(
                    out engineFlags, out substitutionFlags,
                    out eventFlags, out expressionFlags);
            }

            ReadInt32Callback charCallback = null;
            ReadCharsCallback charsCallback = null;

            GetStreamCallbacks(
                textReader, ref charCallback, ref charsCallback);

            RSCD readScriptClientData = null;
            bool canRetry = false; /* NOT USED */

            if (ReadScriptStream(
                    interpreter, null, name, charCallback,
                    charsCallback, startIndex, characters,
                    ref engineFlags, ref substitutionFlags,
                    ref eventFlags, ref expressionFlags,
                    ref readScriptClientData, ref canRetry,
                    ref error) == ReturnCode.Ok)
            {
                text = readScriptClientData.Text;
                return ReturnCode.Ok;
            }

            return ReturnCode.Error;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method reads (and post-processes) a script from a text
        /// reader using the supplied engine flags in addition to those
        /// queried from the interpreter.  Upon success, the read-script
        /// client data is returned to the caller.  This method is
        /// thread-safe.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context to use, which supplies the active flags
        /// and policies; it may be null.
        /// </param>
        /// <param name="name">
        /// The name to associate with the script being read, used for error
        /// reporting and policy checks; it may be null.
        /// </param>
        /// <param name="textReader">
        /// The text reader to read the script from.
        /// </param>
        /// <param name="startIndex">
        /// The starting character index for the read operation.
        /// </param>
        /// <param name="characters">
        /// The number of characters to read, or a negative value to read the
        /// entire stream up to the first "soft" end-of-file.
        /// </param>
        /// <param name="engineFlags">
        /// Additional engine flags to combine with those queried from the
        /// interpreter (or the defaults) for this read operation.
        /// </param>
        /// <param name="clientData">
        /// The client data associated with the read operation.  Upon
        /// success, this is replaced with the read-script client data.
        /// </param>
        /// <param name="text">
        /// Upon success, receives the script text that was read.
        /// </param>
        /// <param name="error">
        /// Upon failure, receives information about the error that occurred.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise,
        /// <see cref="ReturnCode.Error" />.
        /// </returns>
        public static ReturnCode ReadScriptStream(
            Interpreter interpreter,    /* in */
            string name,                /* in */
            TextReader textReader,      /* in */
            int startIndex,             /* in */
            int characters,             /* in */
            EngineFlags engineFlags,    /* in */
            ref IClientData clientData, /* in, out */
            ref string text,            /* out */
            ref Result error            /* out */
            ) /* ENTRY-POINT, THREAD-SAFE */
        {
            EngineFlags localEngineFlags;
            SubstitutionFlags substitutionFlags;
            EventFlags eventFlags;
            ExpressionFlags expressionFlags;

            if (interpreter != null)
            {
                if (!TryQueryAllFlags(interpreter,
                        BlockingFlagsForRead ||
                            BlockingFlagsForReadStream,
                        out localEngineFlags, out substitutionFlags,
                        out eventFlags, out expressionFlags,
                        ref error))
                {
                    return ReturnCode.Error;
                }
            }
            else
            {
                InitializeAllFlags(
                    out localEngineFlags, out substitutionFlags,
                    out eventFlags, out expressionFlags);
            }

            engineFlags |= localEngineFlags;

            ReadInt32Callback charCallback = null;
            ReadCharsCallback charsCallback = null;

            GetStreamCallbacks(
                textReader, ref charCallback, ref charsCallback);

            RSCD readScriptClientData = clientData as RSCD;
            bool canRetry = false; /* NOT USED */

            if (ReadScriptStream(
                    interpreter, null, name, charCallback,
                    charsCallback, startIndex, characters,
                    ref engineFlags, ref substitutionFlags,
                    ref eventFlags, ref expressionFlags,
                    ref readScriptClientData, ref canRetry,
                    ref error) == ReturnCode.Ok)
            {
                clientData = readScriptClientData;
                text = readScriptClientData.Text;

                return ReturnCode.Ok;
            }

            return ReturnCode.Error;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method reads (and post-processes) a script from a text
        /// reader using the supplied engine flags.  It is a convenience
        /// wrapper over the overload that exposes the read-script client
        /// data.  This method is thread-safe.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context to use, which supplies the active flags
        /// and policies; it may be null.
        /// </param>
        /// <param name="name">
        /// The name to associate with the script being read, used for error
        /// reporting and policy checks; it may be null.
        /// </param>
        /// <param name="textReader">
        /// The text reader to read the script from.
        /// </param>
        /// <param name="startIndex">
        /// The starting character index for the read operation.
        /// </param>
        /// <param name="characters">
        /// The number of characters to read, or a negative value to read the
        /// entire stream up to the first "soft" end-of-file.
        /// </param>
        /// <param name="engineFlags">
        /// Additional engine flags to combine with those queried from the
        /// interpreter (or the defaults) for this read operation.
        /// </param>
        /// <param name="text">
        /// Upon success, receives the script text that was read.
        /// </param>
        /// <param name="error">
        /// Upon failure, receives information about the error that occurred.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise,
        /// <see cref="ReturnCode.Error" />.
        /// </returns>
        private static ReturnCode ReadScriptStream(
            Interpreter interpreter, /* in */
            string name,             /* in */
            TextReader textReader,   /* in */
            int startIndex,          /* in */
            int characters,          /* in */
            EngineFlags engineFlags, /* in */
            ref string text,         /* out */
            ref Result error         /* out */
            ) /* ENTRY-POINT, THREAD-SAFE */
        {
            IClientData clientData = null;

            return ReadScriptStream(
                interpreter, name, textReader, startIndex, characters,
                engineFlags, ref clientData, ref text, ref error);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method reads (and post-processes) a script from a text
        /// reader, returning both the verbatim original text and the
        /// processed text as well as whether the operation may be
        /// retried.  This method is thread-safe.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context to use, which supplies the active flags
        /// and policies; it may be null.
        /// </param>
        /// <param name="name">
        /// The name to associate with the script being read, used for error
        /// reporting and policy checks; it may be null.
        /// </param>
        /// <param name="textReader">
        /// The text reader to read the script from.
        /// </param>
        /// <param name="startIndex">
        /// The starting character index for the read operation.
        /// </param>
        /// <param name="characters">
        /// The number of characters to read, or a negative value to read the
        /// entire stream up to the first "soft" end-of-file.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags in effect; these may be modified while reading.
        /// </param>
        /// <param name="originalText">
        /// Upon success, receives the verbatim original script text.
        /// </param>
        /// <param name="text">
        /// Upon success, receives the processed script text.
        /// </param>
        /// <param name="canRetry">
        /// Upon failure, receives non-zero when the operation may be retried
        /// (for example, by treating the input as non-XML).
        /// </param>
        /// <param name="error">
        /// Upon failure, receives information about the error that occurred.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise,
        /// <see cref="ReturnCode.Error" />.
        /// </returns>
        internal static ReturnCode ReadScriptStream(
            Interpreter interpreter,     /* in */
            string name,                 /* in */
            TextReader textReader,       /* in */
            int startIndex,              /* in */
            int characters,              /* in */
            ref EngineFlags engineFlags, /* in, out */
            ref string originalText,     /* out */
            ref string text,             /* out */
            ref bool canRetry,           /* out */
            ref Result error             /* out */
            ) /* THREAD-SAFE */
        {
            ReadInt32Callback charCallback = null;
            ReadCharsCallback charsCallback = null;

            GetStreamCallbacks(
                textReader, ref charCallback, ref charsCallback);

            RSCD readScriptClientData = null;

            if (ReadScriptStream(
                    interpreter, name, charCallback, charsCallback,
                    startIndex, characters, ref engineFlags,
                    ref readScriptClientData, ref canRetry,
                    ref error) == ReturnCode.Ok)
            {
                originalText = readScriptClientData.OriginalText;
                text = readScriptClientData.Text;

                return ReturnCode.Ok;
            }

            return ReturnCode.Error;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method reads (and post-processes) a script using the
        /// supplied stream callbacks.  It queries the substitution,
        /// event, and expression flags from the interpreter (or uses
        /// defaults) before delegating to the core stream-reading method.
        /// This method is thread-safe.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context to use, which supplies the active flags
        /// and policies; it may be null.
        /// </param>
        /// <param name="name">
        /// The name to associate with the script being read, used for error
        /// reporting and policy checks; it may be null.
        /// </param>
        /// <param name="charCallback">
        /// The callback used to read a single character (as an integer) from
        /// the stream.
        /// </param>
        /// <param name="charsCallback">
        /// The callback used to read a buffer of characters from the
        /// stream.
        /// </param>
        /// <param name="startIndex">
        /// The starting character index for the read operation.
        /// </param>
        /// <param name="characters">
        /// The number of characters to read, or a negative value to read the
        /// entire stream up to the first "soft" end-of-file.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags in effect; these may be modified while reading.
        /// </param>
        /// <param name="readScriptClientData">
        /// The read-script client data; it may be supplied upon entry and is
        /// updated upon success with the results of the read operation.
        /// </param>
        /// <param name="canRetry">
        /// Upon failure, receives non-zero when the operation may be retried
        /// (for example, by treating the input as non-XML).
        /// </param>
        /// <param name="error">
        /// Upon failure, receives information about the error that occurred.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise,
        /// <see cref="ReturnCode.Error" />.
        /// </returns>
        private static ReturnCode ReadScriptStream(
            Interpreter interpreter,         /* in */
            string name,                     /* in */
            ReadInt32Callback charCallback,  /* in */
            ReadCharsCallback charsCallback, /* in */
            int startIndex,                  /* in */
            int characters,                  /* in */
            ref EngineFlags engineFlags,     /* in, out */
            ref RSCD readScriptClientData,   /* in, out */
            ref bool canRetry,               /* out */
            ref Result error                 /* out */
            ) /* THREAD-SAFE */
        {
            EngineFlags localEngineFlags; /* NOT USED */
            SubstitutionFlags substitutionFlags;
            EventFlags eventFlags;
            ExpressionFlags expressionFlags;

            if (interpreter != null)
            {
                if (!TryQueryAllFlags(interpreter,
                        BlockingFlagsForRead ||
                            BlockingFlagsForReadStream,
                        out localEngineFlags, out substitutionFlags,
                        out eventFlags, out expressionFlags,
                        ref error))
                {
                    return ReturnCode.Error;
                }
            }
            else
            {
                InitializeAllFlags(
                    out localEngineFlags, out substitutionFlags,
                    out eventFlags, out expressionFlags);
            }

            return ReadScriptStream(
                interpreter, null, name, charCallback,
                charsCallback, startIndex, characters,
                ref engineFlags, ref substitutionFlags,
                ref eventFlags, ref expressionFlags,
                ref readScriptClientData, ref canRetry,
                ref error);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method is the core implementation that reads (and
        /// post-processes) a script using the supplied stream callbacks.
        /// It performs the "before stream", "before script", and
        /// "after stream" policy checks, reads either the entire stream
        /// or a fixed number of characters, performs end-of-line
        /// translation, and optionally recognizes and processes an
        /// embedded XML script document.  This method is thread-safe.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context to use, which supplies policies and
        /// notifications; it may be null.
        /// </param>
        /// <param name="script">
        /// The pre-existing script object being read, when applicable; when
        /// non-null, it indicates there is no underlying file and the
        /// file-oriented policy checks are skipped.  It may be null.
        /// </param>
        /// <param name="name">
        /// The name to associate with the script being read, used for error
        /// reporting and policy checks; it may be null.
        /// </param>
        /// <param name="charCallback">
        /// The callback used to read a single character (as an integer) from
        /// the stream.
        /// </param>
        /// <param name="charsCallback">
        /// The callback used to read a buffer of characters from the
        /// stream.
        /// </param>
        /// <param name="startIndex">
        /// The starting character index for the read operation.
        /// </param>
        /// <param name="characters">
        /// The number of characters to read; a negative value reads the
        /// entire stream up to the first "soft" end-of-file, while a
        /// positive value reads exactly that many characters.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags in effect; these may be modified while reading.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags in effect; this parameter is not used.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags in effect; these may be modified while reading.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags in effect; this parameter is not used.
        /// </param>
        /// <param name="readScriptClientData">
        /// Upon success, receives the read-script client data describing the
        /// results of the read operation.
        /// </param>
        /// <param name="canRetry">
        /// Upon failure, receives non-zero when the operation may be retried
        /// (for example, by treating the input as non-XML).
        /// </param>
        /// <param name="error">
        /// Upon failure, receives information about the error that occurred.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise,
        /// <see cref="ReturnCode.Error" />.
        /// </returns>
        private static ReturnCode ReadScriptStream(
            Interpreter interpreter,                 /* in */
            IScript script,                          /* in */
            string name,                             /* in */
            ReadInt32Callback charCallback,          /* in */
            ReadCharsCallback charsCallback,         /* in */
            int startIndex,                          /* in */
            int characters,                          /* in */
            ref EngineFlags engineFlags,             /* in, out */
            ref SubstitutionFlags substitutionFlags, /* in, out: NOT USED */
            ref EventFlags eventFlags,               /* in, out */
            ref ExpressionFlags expressionFlags,     /* in, out: NOT USED */
            ref RSCD readScriptClientData,           /* out */
            ref bool canRetry,                       /* out */
            ref Result error                         /* out */
            ) /* THREAD-SAFE */
        {
            if ((charCallback == null) || (charsCallback == null))
            {
                error = "invalid stream callbacks";
                return ReturnCode.Error;
            }

            //
            // NOTE: This is the overall result of this method.  It is
            //       used to allow the finally block to detect success.
            //
            ReturnCode code = ReturnCode.Ok;

            //
            // NOTE: If we have an interpreter context, check to make
            //       sure this stream can be read (and then evaluated,
            //       presumably) according to any script stream
            //       policies that may be active.
            //
            ReturnCode beforeStreamCode = ReturnCode.Ok;
            ReturnCode beforeScriptCode = ReturnCode.Ok;
            ReturnCode afterStreamCode = ReturnCode.Ok;

            PolicyDecision beforeStreamDecision = PolicyDecision.None;
            PolicyDecision beforeScriptDecision = PolicyDecision.None;
            PolicyDecision afterStreamDecision = PolicyDecision.None;

            Result beforeStreamPolicyResult = null;
            Result beforeScriptPolicyResult = null;
            Result afterStreamPolicyResult = null;

            try
            {
                #region Policy Checking: "Before Stream"
                //
                // HACK: The "script" parameter should only be non-null
                //       when being called from the EvaluateScript method
                //       overload that accepts an IScript.  In that case,
                //       there is no underlying file, so all those policy
                //       checks must be skipped.  Similarly, this "name"
                //       parameter check here relies upon the fact that
                //       (eventually) the ExtractContextAndFileName helper
                //       method will always fail when a file name is null;
                //       therefore, there is (almost) no point in checking
                //       a stream policy for those cases.  Hence, a null
                //       "name" parameter is used to indiate that a script
                //       being read has no associated file name, e.g. from
                //       a memory stream, etc.
                //
                if ((interpreter != null) &&
                    (script == null) && (name != null) &&
                    !EngineFlagOps.HasNoPolicy(engineFlags))
                {
                    beforeStreamDecision = interpreter.StreamInitialDecision;

                    beforeStreamCode = interpreter.CheckBeforeStreamPolicies(
                        PolicyFlags.EngineBeforeStream, name, null, null,
                        ref beforeStreamDecision,
                        ref beforeStreamPolicyResult);

                    interpreter.StreamFinalDecision = PolicyOps.FinalDecision(
                        PolicyFlags.EngineBeforeStream, beforeStreamCode,
                        beforeStreamDecision);

                    if (!PolicyOps.IsSuccess(
                            beforeStreamCode, beforeStreamDecision))
                    {
                        canRetry = false;

                        if (beforeStreamPolicyResult != null)
                        {
                            error = beforeStreamPolicyResult;
                        }
                        else
                        {
                            error = String.Format(
                                "script stream {0} cannot be read, denied by policy",
                                FormatOps.DisplayName(name));
                        }

                        code = ReturnCode.Error;
                        return code;
                    }
                }
                #endregion

                ///////////////////////////////////////////////////////////////

                if (characters < 0)
                {
                    //
                    // NOTE: Get both the whole buffers as strings (i.e.
                    //       both the original and line-ending modified
                    //       ones).
                    //
                    string originalText = null;
                    string localText = null;
                    int preSoftEofLength;

                    ReadScriptVia(
                        charCallback, Length.Invalid,
                        engineFlags, ref originalText,
                        ref localText, out preSoftEofLength);

                    ///////////////////////////////////////////////////////////

                    #region Optional Script Xml Handling
#if XML
                    //
                    // NOTE: Are we allowed to see if it is actually XML?
                    //       Check and see if the script text looks like
                    //       XML script document unless prevented from
                    //       doing so by our caller.
                    //
                    if (!EngineFlagOps.HasNoXml(engineFlags) &&
                        XmlOps.LooksLikeDocument(originalText))
                    {
                        XmlErrorTypes retryXml = XmlErrorTypes.None;
                        bool validateXml = false;
                        bool relaxedXml = false;
                        bool allXml = false;

                        if (interpreter != null)
                        {
                            interpreter.QueryXmlProperties(
                                ref retryXml, ref validateXml,
                                ref relaxedXml, ref allXml);
                        }

                        code = ReadScriptXml(
                            interpreter, null, GetScriptXml(
                                originalText, preSoftEofLength),
                            retryXml, validateXml, relaxedXml,
                            allXml, ref engineFlags,
                            ref substitutionFlags, ref eventFlags,
                            ref expressionFlags, ref localText,
                            ref canRetry, ref error);

                        if (code != ReturnCode.Ok)
                            return code;
                    }
#endif
                    #endregion

                    ///////////////////////////////////////////////////////////

                    #region Policy Checking: "Before Script"
                    if ((interpreter != null) &&
                        !EngineFlagOps.HasNoPolicy(engineFlags) &&
                        EngineFlagOps.HasExternalScript(engineFlags))
                    {
                        //
                        // NOTE: Attempt to create a "stream-based" script
                        //       object for use by the policy engine -OR-
                        //       the pre-existing IScript provided by the
                        //       caller.
                        //
                        IScript localScript = null;
                        Result scriptCreateError = null;

                        if (script != null)
                        {
                            localScript = Script.CreateForPolicy(
                                script, originalText, ref scriptCreateError);

                            if (localScript == null)
                            {
                                canRetry = false;

                                if (scriptCreateError != null)
                                    error = scriptCreateError;
                                else
                                    error = "could not copy script for policy";

                                code = ReturnCode.Error;
                                return code;
                            }
                        }
                        else
                        {
                            localScript = Script.CreateForPolicy(
                                name, ScriptTypes.Stream, originalText,
                                engineFlags, substitutionFlags, eventFlags,
                                expressionFlags, ref scriptCreateError);

                            if (localScript == null)
                            {
                                canRetry = false;

                                if (scriptCreateError != null)
                                    error = scriptCreateError;
                                else
                                    error = "could not create script for policy";

                                code = ReturnCode.Error;
                                return code;
                            }
                        }

                        //
                        // HACK: *SECURITY* Due to its use by the policy
                        //       engine, this IScript cannot be modified.
                        //
                        localScript.MakeImmutable();

                        beforeScriptDecision = interpreter.ScriptInitialDecision;

                        beforeScriptCode = interpreter.CheckScriptPolicies(
                            PolicyFlags.EngineBeforeScript, localScript,
                            null, null, ref beforeScriptDecision,
                            ref beforeScriptPolicyResult);

                        interpreter.ScriptFinalDecision = PolicyOps.FinalDecision(
                            PolicyFlags.EngineBeforeScript, beforeScriptCode,
                            beforeScriptDecision);

                        if (!PolicyOps.IsSuccess(
                                beforeScriptCode, beforeScriptDecision))
                        {
                            canRetry = false;

                            if (beforeScriptPolicyResult != null)
                            {
                                error = beforeScriptPolicyResult;
                            }
                            else
                            {
                                error = String.Format(
                                    "script {0} cannot be used, denied by policy",
                                    FormatOps.WrapOrNull(EntityOps.GetId(localScript)));
                            }

                            code = ReturnCode.Error;
                            return code;
                        }
                    }
                    #endregion

                    ///////////////////////////////////////////////////////////

                    #region Policy Checking: "After Stream"
                    //
                    // NOTE: Did we succeed in post-processing the text, if
                    //       necessary?
                    //
                    // HACK: The "script" parameter should only be non-null
                    //       when being called from the EvaluateScript method
                    //       overload that accepts an IScript.  In that case,
                    //       there is no underlying file, so all those policy
                    //       checks must be skipped.  Similarly, this "name"
                    //       parameter check here relies upon the fact that
                    //       (eventually) the ExtractContextAndFileName helper
                    //       method will always fail when a file name is null;
                    //       therefore, there is (almost) no point in checking
                    //       a stream policy for those cases.  Hence, a null
                    //       "name" parameter is used to indiate that a script
                    //       being read has no associated file name, e.g. from
                    //       a memory stream, etc.
                    //
                    RSCD localReadScriptClientData = new RSCD(
                        name, originalText, localText, null, RSCD.IsSilent(
                        readScriptClientData));

                    if ((interpreter != null) &&
                        (script == null) && (name != null) &&
                        !EngineFlagOps.HasNoPolicy(engineFlags))
                    {
                        afterStreamDecision = interpreter.StreamInitialDecision;

                        afterStreamCode = interpreter.CheckAfterStreamPolicies(
                            PolicyFlags.EngineAfterStream, name,
                            originalText, null, null,
                            ref afterStreamDecision,
                            ref afterStreamPolicyResult);

                        interpreter.StreamFinalDecision = PolicyOps.FinalDecision(
                            PolicyFlags.EngineAfterStream, afterStreamCode,
                            afterStreamDecision);

                        if (!PolicyOps.IsSuccess(
                                afterStreamCode, afterStreamDecision))
                        {
                            canRetry = false;

                            if (afterStreamPolicyResult != null)
                            {
                                error = afterStreamPolicyResult;
                            }
                            else
                            {
                                error = String.Format(
                                    "script {0} cannot be returned, denied by policy",
                                    FormatOps.DisplayName(name));
                            }

                            code = ReturnCode.Error;
                            return code;
                        }
                    }
                    #endregion

                    ///////////////////////////////////////////////////////////

                    readScriptClientData = localReadScriptClientData;

#if NOTIFY
                    if ((interpreter != null) &&
                        !EngineFlagOps.HasNoNotify(engineFlags))
                    {
                        /* IGNORED */
                        interpreter.CheckNotification(
                            NotifyType.Stream, NotifyFlags.Read,
                            new ObjectList(charCallback, charsCallback,
                                startIndex, characters, readScriptClientData),
                            interpreter, null, null, null, ref error);
                    }
#endif

                    canRetry = false; /* SUCCESS? */
                    return code;
                }
                else if (characters > 0)
                {
                    char[] buffer = new char[characters];

                    int read = charsCallback(
                        buffer, 0, characters); /* throw */

                    if (read == characters)
                    {
                        //
                        // NOTE: Create a string from the buffer of
                        //       characters we read.
                        //
                        string originalText = new string(buffer);
                        string localText = originalText;

                        //
                        // NOTE: Were we able to read any characters?
                        //
                        if (!String.IsNullOrEmpty(localText))
                        {
                            //
                            // NOTE: Check for an embedded "soft"
                            //       end-of-file character.
                            //
                            int softEofIndex = localText.IndexOf(
                                Characters.EndOfFile, 0);

                            //
                            // NOTE: Create a string builder with
                            //       at least enough space to hold
                            //       the entire script.
                            //
                            StringBuilder builder;

                            //
                            // NOTE: If there is an embedded "soft"
                            //       end-of-file character, truncate
                            //       the script at that point.
                            //
                            if (softEofIndex != Index.Invalid)
                            {
                                builder = StringBuilderFactory.Create(
                                    localText, 0, softEofIndex);
                            }
                            else
                            {
                                builder = StringBuilderFactory.Create(
                                    localText, localText.Length);
                            }

                            //
                            // NOTE: Perform fixups and/or character
                            //       (or sub-string) replacements
                            //       within the text.  Typically,
                            //       this is only used to perform
                            //       end -of-line translations.
                            //
                            StringOps.FixupLineEndings(builder);

                            //
                            // NOTE: Get the whole buffer as a
                            //       string (i.e. the one with
                            //       its line-endings modified).
                            //
                            localText =
                                StringBuilderCache.GetStringAndRelease(
                                    ref builder);
                        }

                        ///////////////////////////////////////////////////////

                        #region Optional Script Xml Handling
#if XML
                        //
                        // NOTE: Are we allowed to see if it is actually XML?
                        //       Check and see if the script text looks like
                        //       XML script document unless prevented from
                        //       doing so by our caller.
                        //
                        if (!EngineFlagOps.HasNoXml(engineFlags) &&
                            XmlOps.LooksLikeDocument(originalText))
                        {
                            XmlErrorTypes retryXml = XmlErrorTypes.None;
                            bool validateXml = false;
                            bool relaxedXml = false;
                            bool allXml = false;

                            if (interpreter != null)
                            {
                                interpreter.QueryXmlProperties(
                                    ref retryXml, ref validateXml,
                                    ref relaxedXml, ref allXml);
                            }

                            code = ReadScriptXml(
                                interpreter, null, GetScriptXml(
                                    originalText, Length.Invalid),
                                retryXml, validateXml, relaxedXml,
                                allXml, ref engineFlags,
                                ref substitutionFlags, ref eventFlags,
                                ref expressionFlags, ref localText,
                                ref canRetry, ref error);

                            if (code != ReturnCode.Ok)
                                return code;
                        }
#endif
                        #endregion

                        ///////////////////////////////////////////////////////

                        #region Policy Checking: "Before Script"
                        if ((interpreter != null) &&
                            !EngineFlagOps.HasNoPolicy(engineFlags) &&
                            EngineFlagOps.HasExternalScript(engineFlags))
                        {
                            //
                            // NOTE: Attempt to create a "stream-based" script
                            //       object for use by the policy engine -OR-
                            //       the pre-existing IScript provided by the
                            //       caller.
                            //
                            IScript localScript = null;
                            Result scriptCreateError = null;

                            if (script != null)
                            {
                                localScript = Script.CreateForPolicy(
                                    script, originalText, ref scriptCreateError);

                                if (localScript == null)
                                {
                                    canRetry = false;

                                    if (scriptCreateError != null)
                                        error = scriptCreateError;
                                    else
                                        error = "could not copy script for policy";

                                    code = ReturnCode.Error;
                                    return code;
                                }
                            }
                            else
                            {
                                localScript = Script.CreateForPolicy(
                                    name, ScriptTypes.Stream, originalText,
                                    engineFlags, substitutionFlags, eventFlags,
                                    expressionFlags, ref scriptCreateError);

                                if (localScript == null)
                                {
                                    canRetry = false;

                                    if (scriptCreateError != null)
                                        error = scriptCreateError;
                                    else
                                        error = "could not create script for policy";

                                    code = ReturnCode.Error;
                                    return code;
                                }
                            }

                            //
                            // HACK: *SECURITY* Due to its use by the policy
                            //       engine, this IScript cannot be modified.
                            //
                            localScript.MakeImmutable();

                            beforeScriptDecision = interpreter.ScriptInitialDecision;

                            beforeScriptCode = interpreter.CheckScriptPolicies(
                                PolicyFlags.EngineBeforeScript, localScript,
                                null, null, ref beforeScriptDecision,
                                ref beforeScriptPolicyResult);

                            interpreter.ScriptFinalDecision = PolicyOps.FinalDecision(
                                PolicyFlags.EngineBeforeScript, beforeScriptCode,
                                beforeScriptDecision);

                            if (!PolicyOps.IsSuccess(
                                    beforeScriptCode, beforeScriptDecision))
                            {
                                canRetry = false;

                                if (beforeScriptPolicyResult != null)
                                {
                                    error = beforeScriptPolicyResult;
                                }
                                else
                                {
                                    error = String.Format(
                                        "script {0} cannot be used, denied by policy",
                                        FormatOps.WrapOrNull(EntityOps.GetId(localScript)));
                                }

                                code = ReturnCode.Error;
                                return code;
                            }
                        }
                        #endregion

                        ///////////////////////////////////////////////////////

                        #region Policy Checking: "After Stream"
                        //
                        // NOTE: Did we succeed in post-processing the text, if
                        //       necessary?
                        //
                        // HACK: The "script" parameter should only be non-null
                        //       when being called from the EvaluateScript method
                        //       overload that accepts an IScript.  In that case,
                        //       there is no underlying file, so all those policy
                        //       checks must be skipped.  Similarly, this "name"
                        //       parameter check here relies upon the fact that
                        //       (eventually) the ExtractContextAndFileName helper
                        //       method will always fail when a file name is null;
                        //       therefore, there is (almost) no point in checking
                        //       a stream policy for those cases.  Hence, a null
                        //       "name" parameter is used to indiate that a script
                        //       being read has no associated file name, e.g. from
                        //       a memory stream, etc.
                        //
                        RSCD localReadScriptClientData = new RSCD(
                            name, originalText, localText, null, RSCD.IsSilent(
                            readScriptClientData));

                        if ((interpreter != null) &&
                            (script == null) && (name != null) &&
                            !EngineFlagOps.HasNoPolicy(engineFlags))
                        {
                            afterStreamDecision = interpreter.StreamInitialDecision;

                            afterStreamCode = interpreter.CheckAfterStreamPolicies(
                                PolicyFlags.EngineAfterStream, name,
                                originalText, null, null,
                                ref afterStreamDecision,
                                ref afterStreamPolicyResult);

                            interpreter.StreamFinalDecision = PolicyOps.FinalDecision(
                                PolicyFlags.EngineAfterStream, afterStreamCode,
                                afterStreamDecision);

                            if (!PolicyOps.IsSuccess(
                                    afterStreamCode, afterStreamDecision))
                            {
                                canRetry = false;

                                if (afterStreamPolicyResult != null)
                                {
                                    error = afterStreamPolicyResult;
                                }
                                else
                                {
                                    error = String.Format(
                                        "script {0} cannot be returned, denied by policy",
                                        FormatOps.DisplayName(name));
                                }

                                code = ReturnCode.Error;
                                return code;
                            }
                        }
                        #endregion

                        ///////////////////////////////////////////////////////

                        readScriptClientData = localReadScriptClientData;

#if NOTIFY
                        if ((interpreter != null) &&
                            !EngineFlagOps.HasNoNotify(engineFlags))
                        {
                            /* IGNORED */
                            interpreter.CheckNotification(
                                NotifyType.Stream, NotifyFlags.Read,
                                new ObjectList(charCallback, charsCallback,
                                    startIndex, characters, readScriptClientData),
                                interpreter, null, null, null, ref error);
                        }
#endif

                        canRetry = false; /* SUCCESS? */
                        return code;
                    }
                    else
                    {
                        //
                        // NOTE: We did not read the right number
                        //       of characters (which they specified
                        //       exactly instead of using -1), this
                        //       is considered an error.
                        //
                        canRetry = false;

                        error = String.Format(
                            "unexpected end-of-stream, read {0} " +
                            "characters, wanted {1} characters, " +
                            "result discarded", read, characters);

                        code = ReturnCode.Error;
                        return code;
                    }
                }
                else
                {
                    //
                    // NOTE: Use zero characters?  Surely.
                    //
                    string originalText = String.Empty;
                    string localText = originalText;

                    ///////////////////////////////////////////////////////////

                    #region Policy Checking: "After Stream"
                    //
                    // HACK: The "script" parameter should only be non-null
                    //       when being called from the EvaluateScript method
                    //       overload that accepts an IScript.  In that case,
                    //       there is no underlying file, so all those policy
                    //       checks must be skipped.  Similarly, this "name"
                    //       parameter check here relies upon the fact that
                    //       (eventually) the ExtractContextAndFileName helper
                    //       method will always fail when a file name is null;
                    //       therefore, there is (almost) no point in checking
                    //       a stream policy for those cases.  Hence, a null
                    //       "name" parameter is used to indiate that a script
                    //       being read has no associated file name, e.g. from
                    //       a memory stream, etc.
                    //
                    RSCD localReadScriptClientData = new RSCD(
                        name, originalText, localText, null, RSCD.IsSilent(
                        readScriptClientData));

                    if ((interpreter != null) &&
                        (script == null) && (name != null) &&
                        !EngineFlagOps.HasNoPolicy(engineFlags))
                    {
                        afterStreamDecision = interpreter.StreamInitialDecision;

                        afterStreamCode = interpreter.CheckAfterStreamPolicies(
                            PolicyFlags.EngineAfterStream, name,
                            originalText, null, null,
                            ref afterStreamDecision,
                            ref afterStreamPolicyResult);

                        interpreter.StreamFinalDecision = PolicyOps.FinalDecision(
                            PolicyFlags.EngineAfterStream, afterStreamCode,
                            afterStreamDecision);

                        if (!PolicyOps.IsSuccess(
                                afterStreamCode, afterStreamDecision))
                        {
                            canRetry = false;

                            if (afterStreamPolicyResult != null)
                            {
                                error = afterStreamPolicyResult;
                            }
                            else
                            {
                                error = String.Format(
                                    "script {0} cannot be returned, denied by policy",
                                    FormatOps.DisplayName(name));
                            }

                            code = ReturnCode.Error;
                            return code;
                        }
                    }
                    #endregion

                    ///////////////////////////////////////////////////////////

                    readScriptClientData = localReadScriptClientData;

#if NOTIFY
                    if ((interpreter != null) &&
                        !EngineFlagOps.HasNoNotify(engineFlags))
                    {
                        /* IGNORED */
                        interpreter.CheckNotification(
                            NotifyType.Stream, NotifyFlags.Read,
                            new ObjectList(charCallback, charsCallback,
                                startIndex, characters, readScriptClientData),
                            interpreter, null, null, null, ref error);
                    }
#endif

                    canRetry = false; /* SUCCESS? */
                    return code;
                }
            }
            catch (Exception e)
            {
                error = String.Format(
                    "caught exception reading script stream: {0}",
                    e);

                error.Exception = e;

                SetExceptionErrorCode(interpreter, e);

#if NOTIFY
                if ((interpreter != null) &&
                    !EngineFlagOps.HasNoNotify(engineFlags))
                {
                    /* IGNORED */
                    interpreter.CheckNotification(
                        NotifyType.Engine, NotifyFlags.Exception,
                        new ObjectList(
                            name, charCallback, charsCallback,
                            startIndex, characters),
                        interpreter, null, null, e, ref error);
                }
#endif

                canRetry = false;
                code = ReturnCode.Error;

                return code;
            }
            finally
            {
#if POLICY_TRACE
                TraceOps.MaybeWritePolicyTrace("ReadScriptStream", interpreter,
                    !PolicyContext.GetForceTraceFull(), "name", name,
                    "engineFlags", engineFlags, "substitutionFlags",
                    substitutionFlags, "eventFlags", eventFlags,
                    "expressionFlags", expressionFlags,
                    "readScriptClientData", readScriptClientData,
                    "beforeStreamCode", beforeStreamCode,
                    "beforeStreamDecision", beforeStreamDecision,
                    "beforeStreamPolicyResult", beforeStreamPolicyResult,
                    "beforeScriptCode", beforeScriptCode,
                    "beforeScriptDecision", beforeScriptDecision,
                    "beforeScriptPolicyResult", beforeScriptPolicyResult,
                    "afterStreamCode", afterStreamCode,
                    "afterStreamDecision", afterStreamDecision,
                    "afterStreamPolicyResult", afterStreamPolicyResult,
                    "code", code, "canRetry", canRetry, "error", error);
#endif
            }
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////////////////////

        #region Read Script (File) Methods
        /// <summary>
        /// This method determines the text encoding to use for a script
        /// file, based on its name and a fallback encoding type.  It is a
        /// convenience wrapper that discards the detected byte order mark
        /// preamble size.
        /// </summary>
        /// <param name="fileName">
        /// The name of the script file, which may be a local path or a
        /// remote URI.
        /// </param>
        /// <param name="type">
        /// The fallback encoding type to use when an encoding cannot be
        /// detected from the file content.
        /// </param>
        /// <param name="remoteUri">
        /// Non-zero when the file name is known to be a remote URI, zero
        /// when it is known to be local, or null to determine this
        /// automatically.
        /// </param>
        /// <returns>
        /// The text encoding to use for the script file, or null when one
        /// cannot be determined.
        /// </returns>
        internal static Encoding GetEncoding(
            string fileName,
            EncodingType type,
            bool? remoteUri
            )
        {
            int preambleSize = 0;

            return GetEncoding(
                fileName, type, remoteUri, ref preambleSize);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method determines the text encoding to use for a script
        /// file, based on its name and a fallback encoding type.  For an
        /// existing local file, it inspects the leading bytes (and, for an
        /// XML document, the XML declaration) to detect the encoding and
        /// any byte order mark; otherwise, it falls back to the encoding
        /// for the supplied type.
        /// </summary>
        /// <param name="fileName">
        /// The name of the script file, which may be a local path or a
        /// remote URI.
        /// </param>
        /// <param name="type">
        /// The fallback encoding type to use when an encoding cannot be
        /// detected from the file content.
        /// </param>
        /// <param name="remoteUri">
        /// Non-zero when the file name is known to be a remote URI, zero
        /// when it is known to be local, or null to determine this
        /// automatically.
        /// </param>
        /// <param name="preambleSize">
        /// Upon return, receives the size, in bytes, of the byte order mark
        /// preamble detected at the start of the file, when any.
        /// </param>
        /// <returns>
        /// The text encoding to use for the script file, or null when one
        /// cannot be determined.
        /// </returns>
        internal static Encoding GetEncoding(
            string fileName,
            EncodingType type,
            bool? remoteUri,
            ref int preambleSize
            )
        {
            bool localRemoteUri;

            if (remoteUri != null)
                localRemoteUri = (bool)remoteUri;
            else
                localRemoteUri = PathOps.IsRemoteUri(fileName);

            if (localRemoteUri || !File.Exists(fileName))
                return StringOps.GetEncoding(type);

#if XML
            if (XmlOps.CouldBeDocument(fileName))
            {
                Encoding encoding = null;

                if (XmlOps.GetEncoding(
                        fileName, null, null, false, true,
                        ref encoding) == ReturnCode.Ok)
                {
                    return encoding;
                }
            }
#endif

            int minimumCount = 0;
            int maximumCount = 0;

            /* NO RESULT */
            StringOps.GetPreambleSizes(
                ref minimumCount, ref maximumCount);

            int count = maximumCount;
            byte[] bytes = null;

            while (true)
            {
                if ((count <= 0) || (count < minimumCount))
                    break;

                bytes = FileOps.GetFileBytes(fileName, count);

                if (bytes != null)
                    break;

                count--;
            }

            return StringOps.GuessOrGetEncoding(
                bytes, type, ref preambleSize);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method opens a stream for reading a script identified by a
        /// path, which may be a local file or a remote URI.  It first asks
        /// the interpreter host for the stream, then falls back to opening
        /// a local file (for relative or file-scheme URIs) or, when
        /// permitted, a remote URI via the web subsystem.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context to use, which may provide the stream via
        /// its host; it may be null.
        /// </param>
        /// <param name="path">
        /// The path or URI of the script to open.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags in effect; these control, for example, whether
        /// remote URIs are allowed.
        /// </param>
        /// <param name="fullPath">
        /// Upon success, receives the fully resolved path of the stream that
        /// was opened.
        /// </param>
        /// <param name="error">
        /// Upon failure, receives information about the error that occurred.
        /// </param>
        /// <returns>
        /// The opened stream, or null when the stream could not be
        /// opened.
        /// </returns>
        private static Stream OpenScriptStream(
            Interpreter interpreter,
            string path,
            EngineFlags engineFlags,
            ref string fullPath,
            ref Result error
            )
        {
            if (!String.IsNullOrEmpty(path))
            {
                Uri uri = null;
                UriKind uriKind = UriKind.RelativeOrAbsolute;

                if (PathOps.TryCreateUri(path, ref uri, ref uriKind))
                {
                    try
                    {
                        //
                        // NOTE: First, try to acquire the stream from the
                        //       interpreter host.
                        //
                        HostStreamFlags hostStreamFlags =
                            HostStreamFlags.EngineScript;

                        Stream stream = null;
                        Result localError = null;

                        if ((interpreter != null) &&
                            (interpreter.GetStream(
                                path, FileMode.Open, FileAccess.Read,
                                ref hostStreamFlags, ref fullPath, ref stream,
                                ref localError) == ReturnCode.Ok))
                        {
                            //
                            // NOTE: Just in case the host returns Ok and a
                            //       null stream, make sure the real error
                            //       message, if any, is given to the caller.
                            //
                            if (stream == null)
                                error = localError;

                            return stream;
                        }
                        //
                        // NOTE: If the URI is relative, always treat it as
                        //       a local file name.
                        //
                        else if ((uriKind == UriKind.Relative) ||
                            PathOps.IsFileUriScheme(uri))
                        {
                            //
                            // NOTE: This file name is local, use a normal
                            //       stream object.
                            //
                            localError = null;

                            if (RuntimeOps.NewStream(
                                    interpreter, path, FileMode.Open,
                                    FileAccess.Read, ref hostStreamFlags, ref fullPath,
                                    ref stream, ref localError) == ReturnCode.Ok)
                            {
                                //
                                // NOTE: Just in case the method returns Ok and
                                //       a null stream, make sure the real error
                                //       message, if any, is given to the caller.
                                //
                                if (stream == null)
                                    error = localError;

                                return stream;
                            }
                        }
                        else if (!EngineFlagOps.HasNoRemote(engineFlags))
                        {
#if NETWORK
#if TEST
                            if (EngineFlagOps.HasSetSecurityProtocol(engineFlags))
                            {
                                localError = null;

                                if (WebOps.SetSecurityProtocol(
                                        false, false, ref localError) != ReturnCode.Ok)
                                {
                                    error = localError;
                                    return null;
                                }
                            }
#endif

                            //
                            // NOTE: This file name is remote, use a standard
                            //       web client object to open a stream on it.
                            //
                            localError = null;

                            stream = WebOps.OpenScriptStream(
                                interpreter, ClientData.Empty, uri,
                                null, WebOps.GetTimeout(interpreter,
                                TimeoutType.Network), ref localError);

                            if (stream == null)
                                error = localError;

                            return stream;
#else
                            error = "remote uri not supported";
#endif
                        }
                        else
                        {
                            error = "remote uri not allowed";
                        }
                    }
                    catch (Exception e)
                    {
                        error = String.Format(
                            "caught exception getting script stream: {0}",
                            e);

                        error.Exception = e;

                        SetExceptionErrorCode(interpreter, e);

#if NOTIFY
                        if ((interpreter != null) &&
                            !EngineFlagOps.HasNoNotify(engineFlags))
                        {
                            /* IGNORED */
                            interpreter.CheckNotification(
                                NotifyType.Engine, NotifyFlags.Exception,
                                new ObjectList(path, uri, uriKind, fullPath),
                                interpreter, null, null, e, ref error);
                        }
#endif
                    }
                }
                else
                {
                    error = String.Format(
                        "invalid uri {0}", FormatOps.WrapOrNull(uri));
                }
            }
            else
            {
                error = "invalid path";
            }

            return null;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method reads (and post-processes) a script from a file,
        /// which may be a local file or a remote URI.  It queries the
        /// active flags from the interpreter (or uses defaults when there
        /// is none) and performs any applicable policy and XML handling.
        /// This method is thread-safe.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context to use, which supplies the active flags
        /// and policies; it may be null.
        /// </param>
        /// <param name="fileName">
        /// The name of the script file to read, which may be a local path or
        /// a remote URI.
        /// </param>
        /// <param name="text">
        /// Upon success, receives the script text that was read.
        /// </param>
        /// <param name="error">
        /// Upon failure, receives information about the error that occurred.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise,
        /// <see cref="ReturnCode.Error" />.
        /// </returns>
        public static ReturnCode ReadScriptFile(
            Interpreter interpreter, /* in */
            string fileName,         /* in */
            ref string text,         /* out */
            ref Result error         /* out */
            ) /* ENTRY-POINT, THREAD-SAFE */
        {
            EngineFlags engineFlags;
            SubstitutionFlags substitutionFlags;
            EventFlags eventFlags;
            ExpressionFlags expressionFlags;

            if (interpreter != null)
            {
                if (!TryQueryAllFlags(interpreter,
                        BlockingFlagsForRead ||
                            BlockingFlagsForReadFile,
                        out engineFlags, out substitutionFlags,
                        out eventFlags, out expressionFlags,
                        ref error))
                {
                    return ReturnCode.Error;
                }
            }
            else
            {
                InitializeAllFlags(
                    out engineFlags, out substitutionFlags,
                    out eventFlags, out expressionFlags);
            }

            RSCD readScriptClientData = null;
            bool canRetry = false; /* NOT USED */

            if (ReadScriptFile(
                    interpreter, null, fileName, ref engineFlags,
                    ref substitutionFlags, ref eventFlags,
                    ref expressionFlags, ref readScriptClientData,
                    ref canRetry, ref error) == ReturnCode.Ok)
            {
                text = readScriptClientData.Text;
                return ReturnCode.Ok;
            }

            return ReturnCode.Error;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method reads (and post-processes) a script from a file,
        /// which may be a local file or a remote URI, returning the
        /// read-script client data to the caller.  This method is
        /// thread-safe.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context to use, which supplies the active flags
        /// and policies; it may be null.
        /// </param>
        /// <param name="fileName">
        /// The name of the script file to read, which may be a local path or
        /// a remote URI.
        /// </param>
        /// <param name="clientData">
        /// The client data associated with the read operation.  Upon
        /// success, this is replaced with the read-script client data.
        /// </param>
        /// <param name="text">
        /// Upon success, receives the script text that was read.
        /// </param>
        /// <param name="error">
        /// Upon failure, receives information about the error that occurred.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise,
        /// <see cref="ReturnCode.Error" />.
        /// </returns>
        public static ReturnCode ReadScriptFile(
            Interpreter interpreter,    /* in */
            string fileName,            /* in */
            ref IClientData clientData, /* in, out */
            ref string text,            /* out */
            ref Result error            /* out */
            ) /* ENTRY-POINT, THREAD-SAFE */
        {
            EngineFlags engineFlags;
            SubstitutionFlags substitutionFlags;
            EventFlags eventFlags;
            ExpressionFlags expressionFlags;

            if (interpreter != null)
            {
                if (!TryQueryAllFlags(interpreter,
                        BlockingFlagsForRead ||
                            BlockingFlagsForReadFile,
                        out engineFlags, out substitutionFlags,
                        out eventFlags, out expressionFlags,
                        ref error))
                {
                    return ReturnCode.Error;
                }
            }
            else
            {
                InitializeAllFlags(
                    out engineFlags, out substitutionFlags,
                    out eventFlags, out expressionFlags);
            }

            RSCD readScriptClientData = clientData as RSCD;
            bool canRetry = false; /* NOT USED */

            if (ReadScriptFile(
                    interpreter, null, fileName, ref engineFlags,
                    ref substitutionFlags, ref eventFlags,
                    ref expressionFlags, ref readScriptClientData,
                    ref canRetry, ref error) == ReturnCode.Ok)
            {
                clientData = readScriptClientData;
                text = readScriptClientData.Text;

                return ReturnCode.Ok;
            }

            return ReturnCode.Error;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method reads (and post-processes) a script from a file,
        /// which may be a local file or a remote URI, using the supplied
        /// engine flags in addition to those queried from the
        /// interpreter.  This method is thread-safe.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context to use, which supplies the active flags
        /// and policies; it may be null.
        /// </param>
        /// <param name="fileName">
        /// The name of the script file to read, which may be a local path or
        /// a remote URI.
        /// </param>
        /// <param name="engineFlags">
        /// Additional engine flags to combine with those queried from the
        /// interpreter (or the defaults) for this read operation.
        /// </param>
        /// <param name="text">
        /// Upon success, receives the script text that was read.
        /// </param>
        /// <param name="error">
        /// Upon failure, receives information about the error that occurred.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise,
        /// <see cref="ReturnCode.Error" />.
        /// </returns>
        public static ReturnCode ReadScriptFile(
            Interpreter interpreter, /* in */
            string fileName,         /* in */
            EngineFlags engineFlags, /* in */
            ref string text,         /* out */
            ref Result error         /* out */
            ) /* ENTRY-POINT, THREAD-SAFE */
        {
            EngineFlags localEngineFlags;
            SubstitutionFlags substitutionFlags;
            EventFlags eventFlags;
            ExpressionFlags expressionFlags;

            if (interpreter != null)
            {
                if (!TryQueryAllFlags(interpreter,
                        BlockingFlagsForRead ||
                            BlockingFlagsForReadFile,
                        out localEngineFlags, out substitutionFlags,
                        out eventFlags, out expressionFlags,
                        ref error))
                {
                    return ReturnCode.Error;
                }
            }
            else
            {
                InitializeAllFlags(
                    out localEngineFlags, out substitutionFlags,
                    out eventFlags, out expressionFlags);
            }

            engineFlags |= localEngineFlags;

            RSCD readScriptClientData = null;
            bool canRetry = false; /* NOT USED */

            if (ReadScriptFile(
                    interpreter, null, fileName, ref engineFlags,
                    ref substitutionFlags, ref eventFlags,
                    ref expressionFlags, ref readScriptClientData,
                    ref canRetry, ref error) == ReturnCode.Ok)
            {
                text = readScriptClientData.Text;
                return ReturnCode.Ok;
            }

            return ReturnCode.Error;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method reads (and post-processes) a script from a file,
        /// which may be a local file or a remote URI, using the supplied
        /// engine flags in addition to those queried from the
        /// interpreter, returning the read-script client data to the
        /// caller.  This method is thread-safe.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context to use, which supplies the active flags
        /// and policies; it may be null.
        /// </param>
        /// <param name="fileName">
        /// The name of the script file to read, which may be a local path or
        /// a remote URI.
        /// </param>
        /// <param name="engineFlags">
        /// Additional engine flags to combine with those queried from the
        /// interpreter (or the defaults) for this read operation.
        /// </param>
        /// <param name="clientData">
        /// The client data associated with the read operation.  Upon
        /// success, this is replaced with the read-script client data.
        /// </param>
        /// <param name="text">
        /// Upon success, receives the script text that was read.
        /// </param>
        /// <param name="error">
        /// Upon failure, receives information about the error that occurred.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise,
        /// <see cref="ReturnCode.Error" />.
        /// </returns>
        public static ReturnCode ReadScriptFile(
            Interpreter interpreter,    /* in */
            string fileName,            /* in */
            EngineFlags engineFlags,    /* in */
            ref IClientData clientData, /* in, out */
            ref string text,            /* out */
            ref Result error            /* out */
            ) /* ENTRY-POINT, THREAD-SAFE */
        {
            EngineFlags localEngineFlags;
            SubstitutionFlags substitutionFlags;
            EventFlags eventFlags;
            ExpressionFlags expressionFlags;

            if (interpreter != null)
            {
                if (!TryQueryAllFlags(interpreter,
                        BlockingFlagsForRead ||
                            BlockingFlagsForReadFile,
                        out localEngineFlags, out substitutionFlags,
                        out eventFlags, out expressionFlags,
                        ref error))
                {
                    return ReturnCode.Error;
                }
            }
            else
            {
                InitializeAllFlags(
                    out localEngineFlags, out substitutionFlags,
                    out eventFlags, out expressionFlags);
            }

            engineFlags |= localEngineFlags;

            RSCD readScriptClientData = clientData as RSCD;
            bool canRetry = false; /* NOT USED */

            if (ReadScriptFile(
                    interpreter, null, fileName, ref engineFlags,
                    ref substitutionFlags, ref eventFlags,
                    ref expressionFlags, ref readScriptClientData,
                    ref canRetry, ref error) == ReturnCode.Ok)
            {
                clientData = readScriptClientData;
                text = readScriptClientData.Text;

                return ReturnCode.Ok;
            }

            return ReturnCode.Error;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method is the core implementation that reads (and
        /// post-processes) a script from a file, which may be a local file
        /// or a remote URI.  It resolves and substitutes the file name,
        /// detects the encoding when one is not supplied, performs the
        /// "before file", "before script", and "after file" policy
        /// checks, and reads the script (optionally including its
        /// post-script bytes).  This method is thread-safe.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context to use, which supplies policies and
        /// notifications; it may be null.
        /// </param>
        /// <param name="encoding">
        /// The text encoding to use when reading the file; when null, an
        /// encoding is detected from the file content or guessed.
        /// </param>
        /// <param name="fileName">
        /// The name of the script file to read, which may be a local path or
        /// a remote URI.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags in effect; these may be modified while reading.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags in effect; these may be modified while
        /// reading.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags in effect; these may be modified while reading.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags in effect; these may be modified while
        /// reading.
        /// </param>
        /// <param name="readScriptClientData">
        /// Upon success, receives the read-script client data describing the
        /// results of the read operation.
        /// </param>
        /// <param name="canRetry">
        /// Upon failure, receives non-zero when the operation may be retried
        /// (for example, by treating the input as non-XML).
        /// </param>
        /// <param name="error">
        /// Upon failure, receives information about the error that occurred.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise,
        /// <see cref="ReturnCode.Error" />.
        /// </returns>
        internal static ReturnCode ReadScriptFile(
            Interpreter interpreter,                 /* in */
            Encoding encoding,                       /* in */
            string fileName,                         /* in */
            ref EngineFlags engineFlags,             /* in, out */
            ref SubstitutionFlags substitutionFlags, /* in, out */
            ref EventFlags eventFlags,               /* in, out */
            ref ExpressionFlags expressionFlags,     /* in, out */
            ref RSCD readScriptClientData,           /* out */
            ref bool canRetry,                       /* out */
            ref Result error                         /* out */
            ) /* THREAD-SAFE */
        {
            string localFileName = fileName;

            if (String.IsNullOrEmpty(localFileName))
            {
                canRetry = true;
                error = "invalid file name";

                return ReturnCode.Error;
            }

            //
            // NOTE: Check if the file name is really a remote URI.
            //       Also, the file name may be changed as a result
            //       of environment and tilde substitution.
            //
            bool remoteUri = false;

            localFileName = PathOps.SubstituteOrResolvePath(
                interpreter, localFileName, false, ref remoteUri);

            //
            // NOTE: The file name may have changed.  Make sure it
            //       is still not null or an empty string.
            //
            if (String.IsNullOrEmpty(localFileName))
            {
                canRetry = true;
                error = "invalid file name";

                return ReturnCode.Error;
            }

            //
            // NOTE: The file name must refer to an existing local
            //       file -OR- it must be a remote URI.
            //
            if (!remoteUri && !File.Exists(localFileName))
            {
                canRetry = true;

                error = String.Format(
                    "couldn't read file {0}: no such file or directory",
                    FormatOps.DisplayName(localFileName));

                return ReturnCode.Error;
            }

            //
            // NOTE: If necessary, attempt to guess the encoding based
            //       on the first X bytes of the (local) file name,
            //       which may contain a byte order mark; otherwise,
            //       fallback to the default script encoding.
            //
            if (encoding == null)
            {
                encoding = GetEncoding(
                    localFileName, EncodingType.Script, remoteUri);
            }

            //
            // NOTE: At this point, there must be an encoding of some
            //       kind.
            //
            if (encoding == null)
            {
                canRetry = true;
                error = "script encoding not available";

                return ReturnCode.Error;
            }

            //
            // NOTE: This is the overall result of this method.  It is
            //       used to allow the finally block to detect success.
            //
            ReturnCode code = ReturnCode.Ok;

            //
            // NOTE: If we have an interpreter context, check to make
            //       sure this file can be read (and then evaluated,
            //       presumably) according to any script file policies
            //       that may be active.
            //
            ReturnCode beforeFileCode = ReturnCode.Ok;
            ReturnCode beforeScriptCode = ReturnCode.Ok;
            ReturnCode afterFileCode = ReturnCode.Ok;

            PolicyDecision beforeFileDecision = PolicyDecision.None;
            PolicyDecision beforeScriptDecision = PolicyDecision.None;
            PolicyDecision afterFileDecision = PolicyDecision.None;

            Result beforeFilePolicyResult = null;
            Result beforeScriptPolicyResult = null;
            Result afterFilePolicyResult = null;

            try
            {
                #region Policy Checking: "Before File"
                if ((interpreter != null) &&
                    !EngineFlagOps.HasNoPolicy(engineFlags))
                {
                    beforeFileDecision = interpreter.FileInitialDecision;

                    beforeFileCode = interpreter.CheckBeforeFilePolicies(
                        PolicyFlags.EngineBeforeFile, localFileName,
                        encoding, null, ref beforeFileDecision,
                        ref beforeFilePolicyResult);

                    interpreter.FileFinalDecision = PolicyOps.FinalDecision(
                        PolicyFlags.EngineBeforeFile, beforeFileCode,
                        beforeFileDecision);

                    if (!PolicyOps.IsSuccess(
                            beforeFileCode, beforeFileDecision))
                    {
                        canRetry = false;

                        if (beforeFilePolicyResult != null)
                        {
                            error = beforeFilePolicyResult;
                        }
                        else
                        {
                            error = String.Format(
                                "script file {0} cannot be read, denied by policy",
                                FormatOps.DisplayName(localFileName));
                        }

                        code = ReturnCode.Error;
                        return code;
                    }
                }
                #endregion

                ///////////////////////////////////////////////////////////////

                //
                // NOTE: Open the stream object for this "file" (which may
                //       actually be a web URI).  If the resulting stream
                //       object is null, the error argument will contain a
                //       reason why the open operation failed.
                //
                using (Stream stream = OpenScriptStream(
                        interpreter, localFileName, engineFlags,
                        ref localFileName, ref error))
                {
                    if (stream == null)
                    {
                        canRetry = false;
                        code = ReturnCode.Error;

                        return code;
                    }

                    //
                    // NOTE: Create stream reader for the stream we just
                    //       opened in order to read characters from it.
                    //
                    using (StreamReader streamReader = new StreamReader(
                            stream, encoding))
                    {
                        #region Optional Stream Length Detection
                        long streamLength = GetStreamLength(streamReader);
                        #endregion

                        ///////////////////////////////////////////////////////

                        #region Optional Post-Script Bytes Setup
                        //
                        // NOTE: Starting with the engine flags specified by
                        //       the caller, possibly modify them to include
                        //       the "ForceSoftEof" flag.  This must be done
                        //       if the "PostScriptBytes" flag is set, so we
                        //       can be at the correct position within the
                        //       stream to read the post-script bytes.
                        //
                        EngineFlags readEngineFlags = engineFlags;

                        bool postScriptBytes =
                            EngineFlagOps.HasPostScriptBytes(readEngineFlags);

                        if (postScriptBytes)
                            readEngineFlags |= EngineFlags.ForceSoftEof;
                        #endregion

                        ///////////////////////////////////////////////////////

                        ReadInt32Callback charCallback = null; /* REUSED */

                        GetStreamCallback(streamReader, ref charCallback);

                        ///////////////////////////////////////////////////////

                        #region Script Read Operation
                        //
                        // NOTE: Get both the whole buffers as strings
                        //       (i.e. both the original and line-ending
                        //       modified ones).
                        //
                        string localOriginalText = null;
                        string localText = null;
                        int preSoftEofLength;

                        ReadScriptVia(
                            charCallback, streamLength,
                            readEngineFlags, ref localOriginalText,
                            ref localText, out preSoftEofLength);
                        #endregion

                        ///////////////////////////////////////////////////////

                        #region Optional Post-Script Bytes Read Operation
                        //
                        // NOTE: The post-script bytes occur after the soft
                        //       end-of-file.  They are optionally read, by
                        //       request of the caller, and will be sent to
                        //       the "After File" policy check (below).
                        //
                        ByteList localBytes = null;

                        if (postScriptBytes)
                        {
                            //
                            // HACK: This method relies upon internals of the
                            //       .NET Framework (or Mono / .NET Core); it
                            //       is being used because there is no other
                            //       nice way to accomplish this task: using
                            //       a stream reader to obtain the raw bytes
                            //       already buffered by it when reading an
                            //       entire stream.  The other ways of doing
                            //       this would be less efficient.
                            //
                            if (!FileOps.TryGrabByteBuffer(
                                    streamReader, ref localBytes, ref error))
                            {
                                canRetry = false;
                                code = ReturnCode.Error;

                                return code;
                            }

                            //
                            // HACK: This call mutates the underlying buffer
                            //       grabbed from the stream reader to remove
                            //       all content except the post-script bytes
                            //       from it.  If soft end-of-file is absent,
                            //       this will do nothing.
                            //
                            // BUGBUG: Perhaps if the soft end-of-file is not
                            //         found, this should completely clear the
                            //         buffer?  Since these bytes are really
                            //         only used for policy checking, and end
                            //         up changing the cryptographic hash, the
                            //         current handling is probably fine.
                            //
                            /* NO RESULT */
                            MaybeRemoveNonPostScriptBytes(ref localBytes);

                            using (BinaryReader binaryReader = new BinaryReader(
                                    stream, encoding))
                            {
                                charCallback = null;

                                ReadBytesCallback bytesCallback = null;

                                GetStreamCallbacks(
                                    binaryReader, ref charCallback,
                                    ref bytesCallback);

                                ReadPostScriptBytes(
                                    charCallback, bytesCallback,
                                    Length.Invalid,
                                    EngineFlagOps.HasSeekSoftEof(
                                        readEngineFlags),
                                    ref localBytes);
                            }
                        }
                        #endregion

                        ///////////////////////////////////////////////////////

                        #region Optional Script Xml Handling
#if XML
                        //
                        // NOTE: Are we allowed to see if it is actually XML?
                        //       Check and see if the script text looks like
                        //       XML script document unless prevented from
                        //       doing so by our caller.
                        //
                        if (!EngineFlagOps.HasNoXml(engineFlags) &&
                            XmlOps.LooksLikeDocument(localOriginalText))
                        {
                            XmlErrorTypes retryXml = XmlErrorTypes.None;
                            bool validateXml = false;
                            bool relaxedXml = false;
                            bool allXml = false;

                            if (interpreter != null)
                            {
                                interpreter.QueryXmlProperties(
                                    ref retryXml, ref validateXml,
                                    ref relaxedXml, ref allXml);
                            }

                            code = ReadScriptXml(
                                interpreter, encoding, GetScriptXml(
                                    localOriginalText, preSoftEofLength),
                                retryXml, validateXml, relaxedXml,
                                allXml, ref engineFlags,
                                ref substitutionFlags, ref eventFlags,
                                ref expressionFlags, ref localText,
                                ref canRetry, ref error);

                            if (code != ReturnCode.Ok)
                                return code;
                        }
#endif
                        #endregion

                        ///////////////////////////////////////////////////////

                        #region Policy Checking: "Before Script"
                        if ((interpreter != null) &&
                            !EngineFlagOps.HasNoPolicy(engineFlags) &&
                            EngineFlagOps.HasExternalScript(engineFlags))
                        {
                            //
                            // NOTE: Attempt to create a "stream-based" script
                            //       object for use by the policy engine.
                            //
                            IScript script;
                            Result scriptCreateError = null;

                            script = Script.CreateForPolicy(
                                localFileName, ScriptTypes.File,
                                localOriginalText, engineFlags,
                                substitutionFlags, eventFlags,
                                expressionFlags, ref scriptCreateError);

                            if (script == null)
                            {
                                canRetry = false;

                                if (scriptCreateError != null)
                                    error = scriptCreateError;
                                else
                                    error = "could not create script for policy";

                                code = ReturnCode.Error;
                                return code;
                            }

                            //
                            // HACK: *SECURITY* Due to its use by the policy
                            //       engine, this IScript cannot be modified.
                            //
                            script.MakeImmutable();

                            beforeScriptDecision = interpreter.ScriptInitialDecision;

                            beforeScriptCode = interpreter.CheckScriptPolicies(
                                PolicyFlags.EngineBeforeScript, script,
                                null, null, ref beforeScriptDecision,
                                ref beforeScriptPolicyResult);

                            interpreter.ScriptFinalDecision = PolicyOps.FinalDecision(
                                PolicyFlags.EngineBeforeScript, beforeScriptCode,
                                beforeScriptDecision);

                            if (!PolicyOps.IsSuccess(
                                    beforeScriptCode, beforeScriptDecision))
                            {
                                canRetry = false;

                                if (beforeScriptPolicyResult != null)
                                {
                                    error = beforeScriptPolicyResult;
                                }
                                else
                                {
                                    error = String.Format(
                                        "script {0} cannot be used, denied by policy",
                                        FormatOps.WrapOrNull(EntityOps.GetId(script)));
                                }

                                code = ReturnCode.Error;
                                return code;
                            }
                        }
                        #endregion

                        ///////////////////////////////////////////////////////

                        #region Policy Checking: "After File"
                        //
                        // NOTE: Did we succeed in post-processing the text, if
                        //       necessary?
                        //
                        RSCD localReadScriptClientData = new RSCD(
                            localFileName, localOriginalText, localText,
                            localBytes, RSCD.IsSilent(readScriptClientData));

                        if ((interpreter != null) &&
                            !EngineFlagOps.HasNoPolicy(engineFlags))
                        {
                            afterFileDecision = interpreter.FileInitialDecision;

                            afterFileCode = interpreter.CheckAfterFilePolicies(
                                PolicyFlags.EngineAfterFile,
                                localFileName, localOriginalText,
                                encoding, localReadScriptClientData,
                                ref afterFileDecision,
                                ref afterFilePolicyResult);

                            interpreter.FileFinalDecision = PolicyOps.FinalDecision(
                                PolicyFlags.EngineAfterFile, afterFileCode,
                                afterFileDecision);

                            if (!PolicyOps.IsSuccess(
                                    afterFileCode, afterFileDecision))
                            {
                                canRetry = false;

                                if (afterFilePolicyResult != null)
                                {
                                    error = afterFilePolicyResult;
                                }
                                else
                                {
                                    error = String.Format(
                                        "script file {0} cannot be read, denied by policy",
                                        FormatOps.DisplayName(localFileName));
                                }

                                code = ReturnCode.Error;
                                return code;
                            }
                        }
                        #endregion

                        ///////////////////////////////////////////////////////

                        readScriptClientData = localReadScriptClientData;

#if NOTIFY
                        if ((interpreter != null) &&
                            !EngineFlagOps.HasNoNotify(engineFlags))
                        {
                            /* IGNORED */
                            interpreter.CheckNotification(
                                NotifyType.File, NotifyFlags.Read,
                                new ObjectList(
                                    encoding, localReadScriptClientData),
                                interpreter, null, null, null, ref error);
                        }
#endif

                        canRetry = false; /* SUCCESS? */
                        return code;
                    }
                }
            }
            catch (Exception e)
            {
                error = String.Format(
                    "caught exception reading script file: {0}",
                    e);

                error.Exception = e;

                SetExceptionErrorCode(interpreter, e);

#if NOTIFY
                if ((interpreter != null) &&
                    !EngineFlagOps.HasNoNotify(engineFlags))
                {
                    /* IGNORED */
                    interpreter.CheckNotification(
                        NotifyType.Engine, NotifyFlags.Exception,
                        new ObjectPair(encoding, fileName), interpreter,
                        null, null, e, ref error);
                }
#endif

                canRetry = false;
                code = ReturnCode.Error;

                return code;
            }
            finally
            {
#if POLICY_TRACE
                TraceOps.MaybeWritePolicyTrace("ReadScriptFile", interpreter,
                    !PolicyContext.GetForceTraceFull(), "encoding", encoding,
                    "fileName", fileName, "engineFlags", engineFlags,
                    "substitutionFlags", substitutionFlags, "eventFlags",
                    eventFlags, "expressionFlags", expressionFlags,
                    "readScriptClientData", readScriptClientData,
                    "localFileName", localFileName, "beforeFileCode",
                    beforeFileCode, "beforeFileDecision", beforeFileDecision,
                    "beforeFilePolicyResult", beforeFilePolicyResult,
                    "beforeScriptCode", beforeScriptCode,
                    "beforeScriptDecision", beforeScriptDecision,
                    "beforeScriptPolicyResult", beforeScriptPolicyResult,
                    "afterFileCode", afterFileCode, "afterFileDecision",
                    afterFileDecision, "afterFilePolicyResult",
                    afterFilePolicyResult, "code", code, "canRetry", canRetry,
                    "error", error);
#endif
            }
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method returns the verbatim original script text recorded
        /// in the read-script client data produced by a previous file
        /// read operation.
        /// </summary>
        /// <param name="clientData">
        /// The client data returned by a previous script file read
        /// operation; it may be null.
        /// </param>
        /// <returns>
        /// The verbatim original script text, or null when it is not
        /// available.
        /// </returns>
        public static string GetReadScriptFileOriginalText(
            IClientData clientData /* in */
            )
        {
            if (clientData == null)
                return null;

            RSCD readScriptClientData = clientData as RSCD;

            if (readScriptClientData == null)
                return null;

            return readScriptClientData.OriginalText;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method obtains a script by querying the interpreter host,
        /// trying the exact file name first and then, when permitted,
        /// alternate candidate names (the file name without its directory
        /// and without its extension).  When the host returns a file, that
        /// file is read; otherwise, the script content returned by the
        /// host is used.  This method is thread-safe.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context to use, which is queried for the script;
        /// it may be null, in which case an error is returned.
        /// </param>
        /// <param name="encoding">
        /// The text encoding to use when reading a returned script file;
        /// when null, an encoding is detected or guessed.
        /// </param>
        /// <param name="fileName">
        /// The name of the script to obtain.
        /// </param>
        /// <param name="scriptFlags">
        /// The script flags that control how the host is queried; upon
        /// success, these are updated to reflect the script that was
        /// obtained.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags in effect; these may be modified while reading.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags in effect; these may be modified while
        /// reading.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags in effect; these may be modified while reading.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags in effect; these may be modified while
        /// reading.
        /// </param>
        /// <param name="readScriptClientData">
        /// Upon success, receives the read-script client data describing the
        /// script that was obtained.
        /// </param>
        /// <param name="canRetry">
        /// Upon failure, receives non-zero when the operation may be
        /// retried.
        /// </param>
        /// <param name="errors">
        /// Upon failure, receives the list of errors that occurred while
        /// attempting to obtain the script, unless error reporting is
        /// silenced.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise,
        /// <see cref="ReturnCode.Error" />.
        /// </returns>
        internal static ReturnCode GetScriptFile(
            Interpreter interpreter,
            Encoding encoding,
            string fileName,
            ref ScriptFlags scriptFlags,
            ref EngineFlags engineFlags,
            ref SubstitutionFlags substitutionFlags,
            ref EventFlags eventFlags,
            ref ExpressionFlags expressionFlags,
            ref RSCD readScriptClientData,
            ref bool canRetry,
            ref ResultList errors
            ) /* THREAD-SAFE */
        {
            bool silent = RSCD.IsSilent(readScriptClientData);

            if (interpreter == null)
            {
                if (!silent)
                {
                    if (errors == null)
                        errors = new ResultList();

                    errors.Add("invalid interpreter");
                }

                return ReturnCode.Error;
            }

            if (String.IsNullOrEmpty(fileName))
            {
                if (!silent)
                {
                    if (errors == null)
                        errors = new ResultList();

                    errors.Add("invalid file name");
                }

                return ReturnCode.Error;
            }

            //
            // NOTE: First, try for the exact file name given
            //       to us by our caller.
            //
            StringList names = new StringList(new string[] {
                fileName
            });

            //
            // BUGFIX: By default, when presented with fully
            //         qualified (i.e. absolute?) paths by
            //         our caller, do not attempt to search
            //         for alternate file names.
            //
            if (EngineFlagOps.HasIgnoreRootedFileName(engineFlags) ||
                !Path.IsPathRooted(fileName))
            {
                //
                // NOTE: If the file name is qualified with
                //       a directory name, try removing it.
                //       The caller can block this behavior
                //       by specifying the NoFileNameOnly
                //       engine flag.
                //
                if (!EngineFlagOps.HasNoFileNameOnly(engineFlags) &&
                    PathOps.HasDirectory(fileName))
                {
                    string fileNameOnly;

                    try
                    {
                        fileNameOnly = Path.GetFileName(
                            fileName); /* throw */
                    }
                    catch (Exception e)
                    {
                        TraceOps.DebugTrace(
                            e, typeof(Engine).Name,
                            TracePriority.FileSystemError);

                        fileNameOnly = null;
                    }

                    if (fileNameOnly != null)
                        names.Add(fileNameOnly);
                }

                //
                // NOTE: If the file name has an extension,
                //       try removing it.  The caller can
                //       block this behavior by specifying
                //       the NoRawName engine flag.
                //
                if (!EngineFlagOps.HasNoRawName(engineFlags) &&
                    PathOps.HasExtension(fileName))
                {
                    string rawNameOnly;

                    try
                    {
                        rawNameOnly = Path.GetFileNameWithoutExtension(
                            fileName); /* throw */
                    }
                    catch (Exception e)
                    {
                        TraceOps.DebugTrace(
                            e, typeof(Engine).Name,
                            TracePriority.FileSystemError);

                        rawNameOnly = null;
                    }

                    if (rawNameOnly != null)
                        names.Add(rawNameOnly);
                }
            }

            //
            // NOTE: Try each candidate "file" name until
            //       we are able to get the script or we
            //       run out of options.
            //
            foreach (string name in names)
            {
                ScriptFlags localScriptFlags = scriptFlags;
                IClientData clientData = ClientData.Empty;
                Result localResult = null;

                if (interpreter.GetScript(
                        name, ref localScriptFlags, ref clientData,
                        ref localResult) == ReturnCode.Ok)
                {
                    if (FlagOps.HasFlags(
                            localScriptFlags, ScriptFlags.File, true))
                    {
                        string localFileName = localResult;
                        RSCD localReadScriptClientData = null;
                        bool localCanRetry = false;

                        if (ReadScriptFile(
                                interpreter, encoding, localFileName,
                                ref engineFlags, ref substitutionFlags,
                                ref eventFlags, ref expressionFlags,
                                ref localReadScriptClientData,
                                ref localCanRetry,
                                ref localResult) == ReturnCode.Ok)
                        {
                            scriptFlags = localScriptFlags;
                            readScriptClientData = localReadScriptClientData;

                            return ReturnCode.Ok;
                        }
                        else
                        {
                            if (!silent)
                            {
                                if (errors == null)
                                    errors = new ResultList();

                                errors.Add(localResult);
                            }

                            if (!localCanRetry)
                            {
                                scriptFlags = localScriptFlags;
                                canRetry = localCanRetry;

                                return ReturnCode.Error;
                            }
                        }
                    }
                    else
                    {
                        GSCD getScriptClientData = clientData as GSCD;

                        if (getScriptClientData != null)
                        {
                            scriptFlags = localScriptFlags;

                            readScriptClientData = new RSCD(
                                null, getScriptClientData, name);

                            return ReturnCode.Ok;
                        }
                        else
                        {
                            if (!silent)
                            {
                                if (errors == null)
                                    errors = new ResultList();

                                errors.Add("invalid get script client data");
                            }
                        }
                    }
                }
                else
                {
                    if (!silent)
                    {
                        if (errors == null)
                            errors = new ResultList();

                        errors.Add(localResult);
                    }
                }
            }

            //
            // NOTE: If this point is reached, the search
            //       failed to find any appropriate script
            //       content.
            //
            if (!silent)
            {
                if (errors == null)
                    errors = new ResultList();

                //
                // NOTE: Insert primary error message at
                //       the start of the list.
                //
                errors.Insert(0, String.Format(
                    "couldn't get file {0}: no such file or directory",
                    FormatOps.DisplayName(fileName)));
            }

            return ReturnCode.Error;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method reads a script file directly or, failing that,
        /// obtains it by querying the interpreter host.  It is a
        /// convenience wrapper that discards the verbatim original script
        /// text.  This method is thread-safe.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context to use; it may be null, in which case an
        /// error is returned.
        /// </param>
        /// <param name="encoding">
        /// The text encoding to use when reading the file; when null, an
        /// encoding is detected or guessed.
        /// </param>
        /// <param name="fileName">
        /// The name of the script file to read or obtain.  Upon success,
        /// this is updated to reflect the file that was actually used.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags in effect; these may be modified while reading.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags in effect; these may be modified while
        /// reading.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags in effect; these may be modified while reading.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags in effect; these may be modified while
        /// reading.
        /// </param>
        /// <param name="text">
        /// Upon success, receives the script text that was read.
        /// </param>
        /// <param name="error">
        /// Upon failure, receives information about the error that occurred.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise,
        /// <see cref="ReturnCode.Error" />.
        /// </returns>
        internal static ReturnCode ReadOrGetScriptFile(
            Interpreter interpreter,
            Encoding encoding,
            ref string fileName,
            ref EngineFlags engineFlags,
            ref SubstitutionFlags substitutionFlags,
            ref EventFlags eventFlags,
            ref ExpressionFlags expressionFlags,
            ref string text,
            ref Result error
            ) /* THREAD-SAFE */
        {
            string originalText = null; /* NOT USED */

            return ReadOrGetScriptFile(
                interpreter, encoding, ref fileName, ref engineFlags,
                ref substitutionFlags, ref eventFlags, ref expressionFlags,
                ref originalText, ref text, ref error);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method reads a script file directly or, failing that,
        /// obtains it by querying the interpreter host, returning both
        /// the verbatim original text and the processed text.  The script
        /// flags are derived from the interpreter.  This method is
        /// thread-safe.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context to use; it may be null, in which case an
        /// error is returned.
        /// </param>
        /// <param name="encoding">
        /// The text encoding to use when reading the file; when null, an
        /// encoding is detected or guessed.
        /// </param>
        /// <param name="fileName">
        /// The name of the script file to read or obtain.  Upon success,
        /// this is updated to reflect the file that was actually used.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags in effect; these may be modified while reading.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags in effect; these may be modified while
        /// reading.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags in effect; these may be modified while reading.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags in effect; these may be modified while
        /// reading.
        /// </param>
        /// <param name="originalText">
        /// Upon success, receives the verbatim original script text.
        /// </param>
        /// <param name="text">
        /// Upon success, receives the script text that was read.
        /// </param>
        /// <param name="error">
        /// Upon failure, receives information about the error that occurred.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise,
        /// <see cref="ReturnCode.Error" />.
        /// </returns>
        private static ReturnCode ReadOrGetScriptFile(
            Interpreter interpreter,
            Encoding encoding,
            ref string fileName,
            ref EngineFlags engineFlags,
            ref SubstitutionFlags substitutionFlags,
            ref EventFlags eventFlags,
            ref ExpressionFlags expressionFlags,
            ref string originalText,
            ref string text,
            ref Result error
            ) /* THREAD-SAFE */
        {
            if (interpreter == null)
            {
                error = "invalid interpreter";
                return ReturnCode.Error;
            }

            ScriptFlags scriptFlags = ScriptOps.GetFlags(
                interpreter, interpreter.ScriptFlags, true, false);

            return ReadOrGetScriptFile(
                interpreter, encoding, ref scriptFlags, ref fileName,
                ref engineFlags, ref substitutionFlags, ref eventFlags,
                ref expressionFlags, ref originalText, ref text,
                ref error);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method reads a script file directly or, failing that,
        /// obtains it by querying the interpreter host using the supplied
        /// script flags.  It is a convenience wrapper that discards the
        /// verbatim original script text.  This method is thread-safe.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context to use; it may be null, in which case an
        /// error is returned.
        /// </param>
        /// <param name="encoding">
        /// The text encoding to use when reading the file; when null, an
        /// encoding is detected or guessed.
        /// </param>
        /// <param name="scriptFlags">
        /// The script flags that control how the host is queried; these may
        /// be updated to reflect the script that was used.
        /// </param>
        /// <param name="fileName">
        /// The name of the script file to read or obtain.  Upon success,
        /// this is updated to reflect the file that was actually used.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags in effect; these may be modified while reading.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags in effect; these may be modified while
        /// reading.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags in effect; these may be modified while reading.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags in effect; these may be modified while
        /// reading.
        /// </param>
        /// <param name="text">
        /// Upon success, receives the script text that was read.
        /// </param>
        /// <param name="error">
        /// Upon failure, receives information about the error that occurred.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise,
        /// <see cref="ReturnCode.Error" />.
        /// </returns>
        internal static ReturnCode ReadOrGetScriptFile(
            Interpreter interpreter,
            Encoding encoding,
            ref ScriptFlags scriptFlags,
            ref string fileName,
            ref EngineFlags engineFlags,
            ref SubstitutionFlags substitutionFlags,
            ref EventFlags eventFlags,
            ref ExpressionFlags expressionFlags,
            ref string text,
            ref Result error
            ) /* THREAD-SAFE */
        {
            string originalText = null; /* NOT USED */

            return ReadOrGetScriptFile(
                interpreter, encoding, ref scriptFlags, ref fileName, ref engineFlags,
                ref substitutionFlags, ref eventFlags, ref expressionFlags, ref originalText,
                ref text, ref error);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method reads a script file directly or, failing that,
        /// obtains it by querying the interpreter host using the supplied
        /// script flags, returning both the verbatim original text and the
        /// processed text.  For security, a remote-to-local transition
        /// (and any script denied by policy) is blocked.  This method is
        /// thread-safe.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context to use; it may be null, in which case an
        /// error is returned.
        /// </param>
        /// <param name="encoding">
        /// The text encoding to use when reading the file; when null, an
        /// encoding is detected or guessed.
        /// </param>
        /// <param name="scriptFlags">
        /// The script flags that control how the host is queried; these may
        /// be updated to reflect the script that was used.
        /// </param>
        /// <param name="fileName">
        /// The name of the script file to read or obtain.  Upon success,
        /// this is updated to reflect the file that was actually used.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags in effect; these may be modified while reading.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags in effect; these may be modified while
        /// reading.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags in effect; these may be modified while reading.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags in effect; these may be modified while
        /// reading.
        /// </param>
        /// <param name="originalText">
        /// Upon success, receives the verbatim original script text.
        /// </param>
        /// <param name="text">
        /// Upon success, receives the script text that was read.
        /// </param>
        /// <param name="error">
        /// Upon failure, receives information about the error that occurred.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise,
        /// <see cref="ReturnCode.Error" />.
        /// </returns>
        internal static ReturnCode ReadOrGetScriptFile(
            Interpreter interpreter,
            Encoding encoding,
            ref ScriptFlags scriptFlags,
            ref string fileName,
            ref EngineFlags engineFlags,
            ref SubstitutionFlags substitutionFlags,
            ref EventFlags eventFlags,
            ref ExpressionFlags expressionFlags,
            ref string originalText,
            ref string text,
            ref Result error
            ) /* THREAD-SAFE */
        {
            RSCD readScriptClientData = null;
            ResultList errors = null;
            bool canRetry = false;
            Result localError = null;

            //
            // NOTE: First, try to read the file directly (either from a
            //       local file or from a remote host).
            //
            if (ReadScriptFile(
                    interpreter, encoding, fileName, ref engineFlags,
                    ref substitutionFlags, ref eventFlags,
                    ref expressionFlags, ref readScriptClientData,
                    ref canRetry, ref localError) == ReturnCode.Ok)
            {
                fileName = readScriptClientData.ScriptFileName;
                originalText = readScriptClientData.OriginalText;
                text = readScriptClientData.Text;

                return ReturnCode.Ok;
            }
            //
            // NOTE: *SECURITY* Block remote-to-local transition.  Also,
            //       block any script denied by policy.
            //
            else if (canRetry &&
                !EngineFlagOps.HasNoHost(engineFlags) &&
                !String.IsNullOrEmpty(fileName) &&
                !PathOps.IsRemoteUri(fileName))
            {
                //
                // NOTE: Now, try to get the script file by querying the
                //       interpreter host for it.
                //
                if (GetScriptFile(
                        interpreter, encoding, fileName,
                        ref scriptFlags, ref engineFlags,
                        ref substitutionFlags, ref eventFlags,
                        ref expressionFlags, ref readScriptClientData,
                        ref canRetry, ref errors) == ReturnCode.Ok)
                {
                    fileName = readScriptClientData.ScriptFileName;
                    originalText = readScriptClientData.OriginalText;
                    text = readScriptClientData.Text;

                    return ReturnCode.Ok;
                }
            }

            //
            // NOTE: Build the most complete error message we can for the
            //       caller.
            //
            if (EngineFlagOps.HasAllErrors(engineFlags) &&
                (errors != null))
            {
                if (localError != null)
                    errors.Insert(0, localError);

                error = errors;
            }
            else if (EngineFlagOps.HasNoDefaultError(engineFlags) &&
                (localError != null))
            {
                error = localError;
            }
            else
            {
                //
                // NOTE: Nobody gave us an error message -OR- we are not
                //       allowed to use it?  Ok, use the default one.
                //
                error = String.Format(
                    "couldn't read or get file {0}: no such file or directory",
                    FormatOps.DisplayName(fileName));
            }

            return ReturnCode.Error;
        }
        #endregion

        ///////////////////////////////////////////////////////////////////////////////////////

        #region Read Script (Bytes) Methods
        /// <summary>
        /// This method reads (and post-processes) a script from an array
        /// of bytes already held in memory.  It is a convenience wrapper
        /// over the overload that exposes the client data.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context to use, which supplies the active flags
        /// and policies; it may be null.
        /// </param>
        /// <param name="name">
        /// The name to associate with the script being read, used for error
        /// reporting and policy checks; it may be null.
        /// </param>
        /// <param name="bytes">
        /// The array of bytes containing the script to read.
        /// </param>
        /// <param name="text">
        /// Upon success, receives the script text that was read.
        /// </param>
        /// <param name="error">
        /// Upon failure, receives information about the error that occurred.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise,
        /// <see cref="ReturnCode.Error" />.
        /// </returns>
        internal static ReturnCode ReadScriptBytes(
            Interpreter interpreter, /* in */
            string name,             /* in */
            byte[] bytes,            /* in */
            ref string text,         /* out */
            ref Result error         /* out */
            )
        {
            IClientData clientData = null;

            return ReadScriptBytes(
                interpreter, name, bytes, ref clientData, ref text,
                ref error);
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        //
        // NOTE: For now, this event is private only; however, it may
        //       eventually be exposed.
        //
        /// <summary>
        /// This method reads (and post-processes) a script from an array
        /// of bytes already held in memory.  It queries the active flags
        /// from the interpreter (or uses defaults) and performs any
        /// applicable policy and XML handling.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context to use, which supplies the active flags
        /// and policies; it may be null.
        /// </param>
        /// <param name="name">
        /// The name to associate with the script being read, used for error
        /// reporting and policy checks; it may be null.
        /// </param>
        /// <param name="bytes">
        /// The array of bytes containing the script to read.
        /// </param>
        /// <param name="clientData">
        /// The client data associated with the read operation.  Upon
        /// success, this is replaced with the read-script client data.
        /// </param>
        /// <param name="text">
        /// Upon success, receives the script text that was read.
        /// </param>
        /// <param name="error">
        /// Upon failure, receives information about the error that occurred.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise,
        /// <see cref="ReturnCode.Error" />.
        /// </returns>
        private static ReturnCode ReadScriptBytes(
            Interpreter interpreter,    /* in */
            string name,                /* in */
            byte[] bytes,               /* in */
            ref IClientData clientData, /* in, out */
            ref string text,            /* out */
            ref Result error            /* out */
            )
        {
            EngineFlags engineFlags;
            SubstitutionFlags substitutionFlags;
            EventFlags eventFlags;
            ExpressionFlags expressionFlags;

            if (interpreter != null)
            {
                if (!TryQueryAllFlags(interpreter,
                        BlockingFlagsForRead ||
                            BlockingFlagsForReadBytes,
                        out engineFlags, out substitutionFlags,
                        out eventFlags, out expressionFlags,
                        ref error))
                {
                    return ReturnCode.Error;
                }
            }
            else
            {
                InitializeAllFlags(
                    out engineFlags, out substitutionFlags,
                    out eventFlags, out expressionFlags);
            }

            RSCD readScriptClientData = clientData as RSCD;
            bool canRetry = false; /* NOT USED */

            if (ReadScriptBytes(interpreter,
                    null, null, name, bytes, 0, Count.Invalid,
                    ref engineFlags, ref substitutionFlags,
                    ref eventFlags, ref expressionFlags,
                    ref readScriptClientData, ref canRetry,
                    ref error) == ReturnCode.Ok)
            {
                clientData = readScriptClientData;
                text = readScriptClientData.Text;

                return ReturnCode.Ok;
            }

            return ReturnCode.Error;
        }

        ///////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// This method reads (and post-processes) a script from an array
        /// of bytes by wrapping the bytes in a memory stream and reading
        /// them through a stream reader.  When no encoding is supplied,
        /// one is guessed from the bytes.  This method is thread-safe.
        /// </summary>
        /// <param name="interpreter">
        /// The interpreter context to use, which supplies policies and
        /// notifications; it may be null.
        /// </param>
        /// <param name="script">
        /// The pre-existing script object being read, when applicable; it
        /// may be null.
        /// </param>
        /// <param name="encoding">
        /// The text encoding to use when interpreting the bytes; when null,
        /// an encoding is guessed from the bytes.
        /// </param>
        /// <param name="name">
        /// The name to associate with the script being read, used for error
        /// reporting and policy checks; it may be null.
        /// </param>
        /// <param name="bytes">
        /// The array of bytes containing the script to read.
        /// </param>
        /// <param name="startIndex">
        /// The starting character index for the read operation.
        /// </param>
        /// <param name="characters">
        /// The number of characters to read, or a negative value to read the
        /// entire stream up to the first "soft" end-of-file.
        /// </param>
        /// <param name="engineFlags">
        /// The engine flags in effect; these may be modified while reading.
        /// </param>
        /// <param name="substitutionFlags">
        /// The substitution flags in effect; these may be modified while
        /// reading.
        /// </param>
        /// <param name="eventFlags">
        /// The event flags in effect; these may be modified while reading.
        /// </param>
        /// <param name="expressionFlags">
        /// The expression flags in effect; these may be modified while
        /// reading.
        /// </param>
        /// <param name="readScriptClientData">
        /// The read-script client data; it may be supplied upon entry and is
        /// updated upon success with the results of the read operation.
        /// </param>
        /// <param name="canRetry">
        /// Upon failure, receives non-zero when the operation may be retried
        /// (for example, by treating the input as non-XML).
        /// </param>
        /// <param name="error">
        /// Upon failure, receives information about the error that occurred.
        /// </param>
        /// <returns>
        /// <see cref="ReturnCode.Ok" /> on success; otherwise,
        /// <see cref="ReturnCode.Error" />.
        /// </returns>
        private static ReturnCode ReadScriptBytes(
            Interpreter interpreter,                 /* in */
            IScript script,                          /* in */
            Encoding encoding,                       /* in */
            string name,                             /* in */
            byte[] bytes,                            /* in */
            int startIndex,                          /* in */
            int characters,                          /* in */
            ref EngineFlags engineFlags,             /* in, out */
            ref SubstitutionFlags substitutionFlags, /* in, out */
            ref EventFlags eventFlags,               /* in, out */
            ref ExpressionFlags expressionFlags,     /* in, out */
            ref RSCD readScriptClientData,           /* in, out */
            ref bool canRetry,                       /* out */
            ref Result error                         /* out */
            ) /* THREAD-SAFE */
        {
            if (bytes == null)
            {
                error = "invalid bytes";
                return ReturnCode.Error;
            }

            using (MemoryStream stream = new MemoryStream(bytes))
            {
                Encoding bytesEncoding = (encoding != null) ?
                    encoding : StringOps.GuessOrGetEncoding(
                        bytes, EncodingType.Script);

                if (bytesEncoding == null)
                {
                    error = "script encoding not available";
                    return ReturnCode.Error;
                }

                using (StreamReader streamReader = new StreamReader(
                        stream, bytesEncoding))
                {
                    ReadInt32Callback charCallback = null;
                    ReadCharsCallback charsCallback = null;

                    GetStreamCallbacks(
                        streamReader, ref charCallback, ref charsCallback);

                    return ReadScriptStream(
                        interpreter, script, name, charCallback,
                        charsCallback, startIndex, characters,
                        ref engineFlags, ref substitutionFlags,
                        ref eventFlags, ref expressionFlags,
                        ref readScriptClientData, ref canRetry,
                        ref error);
                }
            }
        }
        #endregion
        #endregion
    }
}
