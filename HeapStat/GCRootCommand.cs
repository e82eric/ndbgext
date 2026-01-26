using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Xml.Linq;
using Microsoft.Diagnostics.Runtime;

namespace HeapStat
{
    public enum OutputType
    {
        Normal = 1,
        Error = 2,
        Warning = 3,
    }
    
    public sealed class CharToLineConverter
    {
        private readonly Action<string> m_callback;
        private readonly StringBuilder m_text = new StringBuilder();

        public CharToLineConverter(Action<string> callback)
        {
            m_callback = callback;
        }

        public void Input(byte[] buffer, int offset, int count)
        {
            for (int i = 0; i < count; i++)
            {
                char c = (char)buffer[offset + i];
                if (c == '\r')
                {
                    continue;
                }
                if (c == '\n')
                {
                    Flush();
                }
                else if (c == '\t' || (c >= (char)0x20 && c <= (char)127))
                {
                    m_text.Append(c);
                }
            }
        }

        public void Input(string text)
        {
            foreach (char c in text)
            {
                if (c == '\r')
                {
                    continue;
                }
                if (c == '\n')
                {
                    Flush();
                }
                else if (c == '\t' || (c >= ((char)0x20) && c <= ((char)127)))
                {
                    m_text.Append(c);
                }
            }
        }

        public void Flush()
        {
            m_callback(m_text.ToString());
            m_text.Clear();
        }
    }
    
    public sealed class ConsoleService : IConsoleService
    {
        private readonly List<StringBuilder> m_history;

        private readonly CharToLineConverter m_consoleConverter;
        private readonly CharToLineConverter m_warningConverter;
        private readonly CharToLineConverter m_errorConverter;

        private string m_prompt = "> ";

        private bool m_shutdown;
        private CancellationTokenSource m_interruptExecutingCommand;

        private string m_clearLine;
        private bool m_interactiveConsole;
        private bool m_outputRedirected;
        private bool m_refreshingLine;
        private StringBuilder m_activeLine;

        private int m_selectedHistory;

        private bool m_modified;
        private int m_cursorPosition;
        private int m_scrollPosition;
        private bool m_insertMode;

        private int m_commandExecuting;
        private string m_lastCommandLine;

        /// <summary>
        /// Create an instance of the console provider
        /// </summary>
        /// <param name="errorColor">error color (default red)</param>
        /// <param name="warningColor">warning color (default yellow)</param>
        public ConsoleService(ConsoleColor errorColor = ConsoleColor.Red, ConsoleColor warningColor = ConsoleColor.Yellow)
        {
            m_history = new List<StringBuilder>();
            m_activeLine = new StringBuilder();
            m_shutdown = false;

            m_consoleConverter = new CharToLineConverter(text => {
                NewOutput(text);
            });

            m_warningConverter = new CharToLineConverter(text => {
                NewOutput(text, warningColor);
            });

            m_errorConverter = new CharToLineConverter(text => {
                NewOutput(text, errorColor);
            });

            // Hook ctrl-C and ctrl-break
            Console.CancelKeyPress += new ConsoleCancelEventHandler(OnCtrlBreakKeyPress);
        }

        /// <summary>
        /// Start input processing and command dispatching
        /// </summary>
        /// <param name="dispatchCommand">Called to dispatch a command on ENTER</param>
        public void Start(Action<string, string, CancellationToken> dispatchCommand)
        {
            m_lastCommandLine = null;
            m_interactiveConsole = !Console.IsInputRedirected;
            m_outputRedirected = Console.IsOutputRedirected;
            RefreshLine();

            // The special prompts for the test runner are built into this
            // console provider when the output has been redirected.
            if (!m_interactiveConsole)
            {
                WriteLine(OutputType.Normal, "<END_COMMAND_OUTPUT>");
            }

            // Start keyboard processing
            while (!m_shutdown)
            {
                if (m_interactiveConsole)
                {
                    ConsoleKeyInfo keyInfo = Console.ReadKey(true);
                    ProcessKeyInfo(keyInfo, dispatchCommand);
                }
                else
                {
                    // The input has been redirected (i.e. testing or in script)
                    string line = Console.ReadLine();
                    if (string.IsNullOrEmpty(line))
                    {
                        continue;
                    }
                    bool result = Dispatch(line, dispatchCommand);
                    if (!m_shutdown)
                    {
                        if (result)
                        {
                            WriteLine(OutputType.Normal, "<END_COMMAND_OUTPUT>");
                        }
                        else
                        {
                            WriteLine(OutputType.Normal, "<END_COMMAND_ERROR>");
                        }
                    }
                }
            }
        }

        public bool Shutdown { get { return m_shutdown; } }

        /// <summary>
        /// Stop input processing/dispatching
        /// </summary>
        public void Stop()
        {
            ClearLine();
            m_shutdown = true;
            // Delete the last command (usually q or exit) that caused Stop() to be
            // called so the history doesn't fill up with exit commands.
            if (m_selectedHistory > 0)
            {
                m_history.RemoveAt(--m_selectedHistory);
            }
            Console.CancelKeyPress -= new ConsoleCancelEventHandler(OnCtrlBreakKeyPress);
        }

        /// <summary>
        /// Returns the current command history for serialization.
        /// </summary>
        public IEnumerable<string> GetCommandHistory()
        {
            return m_history.Select((sb) => sb.ToString());
        }

        /// <summary>
        /// Adds the command history.
        /// </summary>
        /// <param name="commandHistory">command history strings to add</param>
        public void AddCommandHistory(IEnumerable<string> commandHistory)
        {
            m_history.AddRange(commandHistory.Select((s) => new StringBuilder(s)));
            m_selectedHistory = m_history.Count;
        }

        /// <summary>
        /// Change the command prompt
        /// </summary>
        /// <param name="prompt">new prompt</param>
        public void SetPrompt(string prompt)
        {
            m_prompt = prompt;
            RefreshLine();
        }

        /// <summary>
        /// Writes a message with a new line to console.
        /// </summary>
        public void WriteLine(OutputType type, string format, params object[] parameters)
        {
            WriteOutput(type, string.Format(format, parameters) + Environment.NewLine);
        }

        /// <summary>
        /// Write text on the console screen
        /// </summary>
        /// <param name="type">output type</param>
        /// <param name="message">text</param>
        /// <exception cref="OperationCanceledException">ctrl-c interrupted the command</exception>
        public void WriteOutput(OutputType type, string message)
        {
            switch (type)
            {
                case OutputType.Normal:
                    m_consoleConverter.Input(message);
                    break;

                case OutputType.Warning:
                    m_warningConverter.Input(message);
                    break;

                case OutputType.Error:
                    m_errorConverter.Input(message);
                    break;
            }
        }

        /// <summary>
        /// Clear the console screen
        /// </summary>
        public void ClearScreen()
        {
            Console.Clear();
            PrintActiveLine();
        }

        /// <summary>
        /// Write a line to the console.
        /// </summary>
        /// <param name="text">line of text</param>
        /// <param name="color">color of the text</param>
        private void NewOutput(string text, ConsoleColor? color = null)
        {
            ClearLine();

            ConsoleColor? originalColor = null;
            if (color.HasValue)
            {
                originalColor = Console.ForegroundColor;
                Console.ForegroundColor = color.Value;
            }
            Console.WriteLine(text);
            if (originalColor.HasValue)
            {
                Console.ForegroundColor = originalColor.Value;
            }

            PrintActiveLine();
        }

        /// <summary>
        /// This is the ctrl-c/ctrl-break handler
        /// </summary>
        private void OnCtrlBreakKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            if (!m_shutdown && m_interactiveConsole)
            {
                m_interruptExecutingCommand?.Cancel();
                e.Cancel = true;
            }
        }

        private void CommandStarting()
        {
            if (m_commandExecuting == 0)
            {
                ClearLine();
            }
            m_commandExecuting++;
        }

        private void CommandFinished()
        {
            if (--m_commandExecuting == 0)
            {
                RefreshLine();
            }
        }

        private void ClearLine()
        {
            if (!m_interactiveConsole)
            {
                return;
            }

            if (m_commandExecuting != 0)
            {
                return;
            }

            int width = Console.WindowWidth;
            if (m_clearLine == null || width != m_clearLine.Length)
            {
                m_clearLine = "\r" + (width > 0 ? new string(' ', width - 1) : "");
            }

            Console.Write(m_clearLine);

            if (!m_outputRedirected && Console.CursorTop >= 0)
            {
                Console.CursorLeft = 0;
            }
        }

        private void PrintActiveLine()
        {
            if (!m_interactiveConsole)
            {
                return;
            }

            if (m_shutdown)
            {
                return;
            }

            if (m_commandExecuting != 0)
            {
                return;
            }

            string prompt = m_prompt;

            int lineWidth = 80;
            if (Console.WindowWidth > prompt.Length)
            {
                lineWidth = Console.WindowWidth - prompt.Length - 1;
            }
            int scrollIncrement = lineWidth / 3;

            int activeLineLen = m_activeLine.Length;

            m_scrollPosition = Math.Min(Math.Max(m_scrollPosition, 0), activeLineLen);
            m_cursorPosition = Math.Min(Math.Max(m_cursorPosition, 0), activeLineLen);

            while (m_cursorPosition < m_scrollPosition)
            {
                m_scrollPosition = Math.Max(m_scrollPosition - scrollIncrement, 0);
            }

            while (m_cursorPosition - m_scrollPosition > lineWidth - 5)
            {
                m_scrollPosition += scrollIncrement;
            }

            int lineRest = activeLineLen - m_scrollPosition;
            int max = Math.Min(lineRest, lineWidth);
            string text = m_activeLine.ToString(m_scrollPosition, max);

            Console.Write("{0}{1}", prompt, text);

            if (!m_outputRedirected && Console.CursorTop >= 0)
            {
                Console.CursorLeft = prompt.Length + (m_cursorPosition - m_scrollPosition);
            }
        }

        private void RefreshLine()
        {
            // Check for recursions.
            if (m_refreshingLine)
            {
                return;
            }
            m_refreshingLine = true;
            ClearLine();
            PrintActiveLine();
            m_refreshingLine = false;
        }

        private void ProcessKeyInfo(ConsoleKeyInfo keyInfo, Action<string, string, CancellationToken> dispatchCommand)
        {
            int activeLineLen = m_activeLine.Length;

            switch (keyInfo.Key)
            {
                case ConsoleKey.Backspace: // The BACKSPACE key.
                    if (m_cursorPosition > 0)
                    {
                        EnsureNewEntry();
                        m_activeLine.Remove(m_cursorPosition - 1, 1);
                        m_cursorPosition--;
                        RefreshLine();
                    }
                    break;

                case ConsoleKey.Insert: // The INS (INSERT) key.
                    m_insertMode = !m_insertMode;
                    RefreshLine();
                    break;

                case ConsoleKey.Delete: // The DEL (DELETE) key.
                    if (m_cursorPosition < activeLineLen)
                    {
                        EnsureNewEntry();
                        m_activeLine.Remove(m_cursorPosition, 1);
                        RefreshLine();
                    }
                    break;

                case ConsoleKey.Enter: // The ENTER key.
                    string newCommand = m_activeLine.ToString();

                    if (m_modified)
                    {
                        m_history.Add(m_activeLine);
                    }
                    m_selectedHistory = m_history.Count;

                    Dispatch(newCommand, dispatchCommand);

                    SwitchToHistoryEntry();
                    break;

                case ConsoleKey.Escape: // The ESC (ESCAPE) key.
                    EnsureNewEntry();
                    m_activeLine.Clear();
                    m_cursorPosition = 0;
                    RefreshLine();
                    break;

                case ConsoleKey.End: // The END key.
                    m_cursorPosition = activeLineLen;
                    RefreshLine();
                    break;

                case ConsoleKey.Home: // The HOME key.
                    m_cursorPosition = 0;
                    RefreshLine();
                    break;

                case ConsoleKey.LeftArrow: // The LEFT ARROW key.
                    if (keyInfo.Modifiers == ConsoleModifiers.Control)
                    {
                        while (m_cursorPosition > 0 && char.IsWhiteSpace(m_activeLine[m_cursorPosition - 1]))
                        {
                            m_cursorPosition--;
                        }

                        while (m_cursorPosition > 0 && !char.IsWhiteSpace(m_activeLine[m_cursorPosition - 1]))
                        {
                            m_cursorPosition--;
                        }
                    }
                    else
                    {
                        m_cursorPosition--;
                    }

                    RefreshLine();
                    break;

                case ConsoleKey.UpArrow: // The UP ARROW key.
                    if (m_selectedHistory > 0)
                    {
                        m_selectedHistory--;
                    }
                    SwitchToHistoryEntry();
                    break;

                case ConsoleKey.RightArrow: // The RIGHT ARROW key.
                    if (keyInfo.Modifiers == ConsoleModifiers.Control)
                    {
                        while (m_cursorPosition < activeLineLen && !char.IsWhiteSpace(m_activeLine[m_cursorPosition]))
                        {
                            m_cursorPosition++;
                        }

                        while (m_cursorPosition < activeLineLen && char.IsWhiteSpace(m_activeLine[m_cursorPosition]))
                        {
                            m_cursorPosition++;
                        }
                    }
                    else
                    {
                        m_cursorPosition++;
                    }

                    RefreshLine();
                    break;

                case ConsoleKey.DownArrow: // The DOWN ARROW key.
                    if (m_selectedHistory < m_history.Count)
                    {
                        m_selectedHistory++;
                    }
                    SwitchToHistoryEntry();

                    RefreshLine();
                    break;

                default:
                    if (keyInfo.KeyChar != 0)
                    {
                        if ((keyInfo.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) == 0)
                        {
                            AppendNewText(new string(keyInfo.KeyChar, 1));
                        }
                    }
                    break;
            }
        }

        private bool Dispatch(string newCommand, Action<string, string, CancellationToken> dispatchCommand)
        {
            bool result = true;
            CommandStarting();
            m_interruptExecutingCommand = new CancellationTokenSource();
            ((IConsoleService)this).CancellationToken = m_interruptExecutingCommand.Token;
            try
            {
                newCommand = newCommand.Trim();
                if (string.IsNullOrEmpty(newCommand) && m_lastCommandLine != null)
                {
                    newCommand = m_lastCommandLine;
                }
                try
                {
                    dispatchCommand(m_prompt, newCommand, m_interruptExecutingCommand.Token);
                    m_lastCommandLine = newCommand;
                }
                catch (OperationCanceledException)
                {
                    // ctrl-c interrupted the command
                    m_lastCommandLine = null;
                }
                catch (Exception ex)
                {
                    if (!string.IsNullOrEmpty(ex.Message))
                    {
                        WriteLine(OutputType.Error, "ERROR: {0}", ex.Message);
                    }
                    // if (ex is CommandParsingException parsingException)
                    // {
                    //     WriteLine(OutputType.Normal, parsingException.DetailedHelp);
                    // }
                    Trace.TraceError(ex.ToString());
                    m_lastCommandLine = null;
                    result = false;
                }
            }
            finally
            {
                m_interruptExecutingCommand = null;
                CommandFinished();
            }
            return result;
        }

        private void AppendNewText(string text)
        {
            EnsureNewEntry();

            foreach (char c in text)
            {
                // Filter unwanted characters.
                switch (c)
                {
                    case '\t':
                    case '\r':
                    case '\n':
                        continue;
                }

                if (m_insertMode && m_cursorPosition < m_activeLine.Length)
                {
                    m_activeLine[m_cursorPosition] = c;
                }
                else
                {
                    m_activeLine.Insert(m_cursorPosition, c);
                }
                m_modified = true;
                m_cursorPosition++;
            }

            RefreshLine();
        }

        private void SwitchToHistoryEntry()
        {
            if (m_selectedHistory < m_history.Count)
            {
                m_activeLine = m_history[m_selectedHistory];
            }
            else
            {
                m_activeLine = new StringBuilder();
            }

            m_cursorPosition = m_activeLine.Length;
            m_modified = false;

            RefreshLine();
        }

        private void EnsureNewEntry()
        {
            if (!m_modified)
            {
                m_activeLine = new StringBuilder(m_activeLine.ToString());
                m_modified = true;
            }
        }

        #region IConsoleService

        void IConsoleService.Write(string text) => WriteOutput(OutputType.Normal, text);

        void IConsoleService.WriteWarning(string text) => WriteOutput(OutputType.Warning, text);

        void IConsoleService.WriteError(string text) => WriteOutput(OutputType.Error, text);

        bool IConsoleService.SupportsDml => false;

        void IConsoleService.WriteDml(string text) => WriteOutput(OutputType.Normal, text);

        void IConsoleService.WriteDmlExec(string text, string _) => WriteOutput(OutputType.Normal, text);

        CancellationToken IConsoleService.CancellationToken { get; set; }

        int IConsoleService.WindowWidth
        {
            get
            {
                try
                {
                    return Console.WindowWidth;
                }
                catch (Exception ex) 
                {
                    return int.MaxValue;
                }
            }
        }

        #endregion
    }
    
    internal static class ExtensionMethodHelpers
    {
        public static string ConvertToHumanReadable(this ulong totalBytes) => ConvertToHumanReadable((double)totalBytes);

        public static string ConvertToHumanReadable(this long totalBytes) => ConvertToHumanReadable((double)totalBytes);

        public static string ConvertToHumanReadable(this double totalBytes)
        {
            double updated = totalBytes;

            updated /= 1024;
            if (updated < 1024)
            {
                return $"{updated:0.00}kb";
            }

            updated /= 1024;
            if (updated < 1024)
            {
                return $"{updated:0.00}mb";
            }

            updated /= 1024;
            return $"{updated:0.00}gb";
        }

        public static string ToSignedHexString(this int offset) => offset < 0 ? $"-{Math.Abs(offset):x2}" : offset.ToString("x2");

        internal static ulong FindMostCommonPointer(this IEnumerable<ulong> enumerable)
            => (from ptr in enumerable
                group ptr by ptr into g
                orderby g.Count() descending
                select g.First()).First();
    }
    
    internal static class Dml
    {
        private static DmlDumpObject s_dumpObj;
        private static DmlDumpHeap s_dumpHeap;
        private static DmlBold s_bold;
        private static DmlListNearObj s_listNearObj;
        private static DmlDumpDomain s_dumpDomain;
        private static DmlThread s_thread;

        /// <summary>
        /// Runs !dumpobj on the given pointer or ClrObject.  If a ClrObject is invalid,
        /// this will instead link to !verifyobj.
        /// </summary>
        public static DmlFormat DumpObj => s_dumpObj = new DmlDumpObject();

        /// <summary>
        /// Marks the output in bold.
        /// </summary>
        public static DmlFormat Bold => s_bold = new DmlBold();

        /// <summary>
        /// Dumps the heap.  If given a ClrSegment, ClrSubHeap, or MemoryRange it will
        /// just dump that particular section of the heap.
        /// </summary>
        public static DmlFormat DumpHeap => s_dumpHeap = new DmlDumpHeap();

        /// <summary>
        /// Runs ListNearObj on the given address or ClrObject.
        /// </summary>
        public static DmlFormat ListNearObj => s_listNearObj = new DmlListNearObj();

        /// <summary>
        /// Runs !dumpdomain on the given doman, additionally it will put the domain
        /// name as the hover text.
        /// </summary>
        public static DmlFormat DumpDomain => s_dumpDomain = new DmlDumpDomain();

        /// <summary>
        /// Changes the debugger to the given thread.
        /// </summary>
        public static DmlFormat Thread => s_thread = new DmlThread();

        private sealed class DmlBold : DmlFormat
        {
            public override void FormatValue(StringBuilder sb, string outputText, object value)
            {
                sb.Append("<b>");
                sb.Append(DmlEscape(outputText));
                sb.Append("</b>");
            }
        }

        private abstract class DmlExec : DmlFormat
        {
            public override void FormatValue(StringBuilder sb, string outputText, object value)
            {
                string command = GetCommand(outputText, value);
                if (string.IsNullOrWhiteSpace(command))
                {
                    sb.Append(DmlEscape(outputText));
                    return;
                }

                sb.Append("<exec cmd=\"");
                sb.Append(DmlEscape(command));
                sb.Append('\"');

                string altText = GetAltText(outputText, value);
                if (altText != null)
                {
                    sb.Append(" alt=\"");
                    sb.Append(DmlEscape(altText));
                    sb.Append('"');
                }

                sb.Append('>');
                sb.Append(DmlEscape(outputText));
                sb.Append("</exec>");
            }

            protected abstract string GetCommand(string outputText, object value);
            protected virtual string GetAltText(string outputText, object value) => null;

            protected static bool IsNullOrZeroValue(object obj, out string value)
            {
                if (obj is null)
                {
                    value = null;
                    return true;
                }
                else if (TryGetPointerValue(obj, out ulong ul) && ul == 0)
                {
                    value = "0";
                    return true;
                }

                value = null;
                return false;
            }

            protected static bool TryGetPointerValue(object value, out ulong ulVal)
            {
                if (value is ulong ul)
                {
                    ulVal = ul;
                    return true;
                }
                // else if (value is nint ni)
                // {
                //     unchecked
                //     {
                //         ulVal = (ulong)ni;
                //     }
                //     return true;
                // }
                // else if (value is nuint nuint)
                // {
                //     ulVal = nuint;
                //     return true;
                // }

                ulVal = 0;
                return false;
            }
        }

        private sealed class DmlThread : DmlExec
        {
            protected override string GetCommand(string outputText, object value)
            {
                if (value is uint id)
                {
                    return $"~~[{id:x}]s";
                }

                if (value is ClrThread thread)
                {
                    return $"~~[{thread.OSThreadId:x}]s";
                }

                return null;
            }
        }

        private class DmlDumpObject : DmlExec
        {
            protected override string GetCommand(string outputText, object value)
            {
                bool isValid = true;
                if (value is ClrObject obj)
                {
                    isValid = obj.IsValid;
                }

                value = Format.Unwrap(value);
                if (IsNullOrZeroValue(value, out string result))
                {
                    return result;
                }

                return isValid ? $"!dumpobj /d {value:x}" : $"!verifyobj {value:x}";
            }

            protected override string GetAltText(string outputText, object value)
            {
                if (value is ClrObject obj)
                {
                    if (obj.IsValid)
                    {
                        return obj.Type?.Name;
                    }

                    return "Invalid Object";
                }

                return null;
            }
        }

        private sealed class DmlListNearObj : DmlDumpObject
        {
            protected override string GetCommand(string outputText, object value)
            {
                value = Format.Unwrap(value);
                if (IsNullOrZeroValue(value, out string result))
                {
                    return result;
                }

                return $"!listnearobj {value:x}";
            }
        }

        private sealed class DmlDumpHeap : DmlExec
        {
            protected override string GetCommand(string outputText, object value)
            {
                if (value is null)
                {
                    return null;
                }

                if (TryGetMethodTableOrTypeHandle(value, out ulong mtOrTh))
                {
                    // !dumpheap will only work on a method table
                    if ((mtOrTh & 2) == 2)
                    {
                        // Can't use typehandles
                        return null;
                    }
                    else if ((mtOrTh & 1) == 1)
                    {
                        // Clear mark bit
                        value = mtOrTh & ~1ul;
                    }

                    if (mtOrTh == 0)
                    {
                        return null;
                    }

                    return $"!dumpheap -mt {value:x}";
                }

                if (value is ClrSegment seg)
                {
                    return $"!dumpheap -segment {seg.Address:x}";
                }

                if (value is MemoryRange range)
                {
                    return $"!dumpheap {range.Start:x} {range.End:x}";
                }

                if (value is ClrSubHeap subHeap)
                {
                    return $"!dumpheap -heap {subHeap.Index}";
                }

                Debug.Fail($"Unknown cannot use type {value.GetType().FullName} with DumpObj");
                return null;
            }

            private static bool TryGetMethodTableOrTypeHandle(object value, out ulong mtOrTh)
            {
                if (TryGetPointerValue(value, out mtOrTh))
                {
                    return true;
                }

                if (value is ClrType type)
                {
                    mtOrTh = type.MethodTable;
                    return true;
                }

                mtOrTh = 0;
                return false;
            }

            protected override string GetAltText(string outputText, object value)
            {
                if (value is ClrType type)
                {
                    return type.Name;
                }

                return null;
            }
        }

        private sealed class DmlDumpDomain : DmlExec
        {
            protected override string GetCommand(string outputText, object value)
            {
                value = Format.Unwrap(value);
                if (IsNullOrZeroValue(value, out string result))
                {
                    return result;
                }

                return $"!dumpdomain /d {value:x}";
            }

            protected override string GetAltText(string outputText, object value)
            {
                if (value is ClrAppDomain domain)
                {
                    return domain.Name;
                }

                return null;
            }
        }
    }
    
    internal static class Formats
    {
        private static HexValueFormat s_hexOffsetFormat;
        private static HexValueFormat s_hexValueFormat;
        private static Format s_text;
        private static IntegerFormat s_integerFormat;
        private static TypeOrImageFormat s_typeNameFormat;
        private static TypeOrImageFormat s_imageFormat;
        private static IntegerFormat s_integerWithoutCommaFormat;
        private static HumanReadableFormat s_humanReadableFormat;
        private static RangeFormat s_range;

        static Formats()
        {
            int pointerSize = IntPtr.Size;
            Pointer = new IntegerFormat(pointerSize == 4 ? "x8" : "x12");
        }

        public static Format Pointer { get; }

        public static Format HexOffset => s_hexOffsetFormat = new HexValueFormat(printPrefix: true, signed: true);
        public static Format HexValue => s_hexValueFormat = new HexValueFormat(printPrefix: true, signed: false);
        public static Format Integer => s_integerFormat = new IntegerFormat("n0");
        public static Format IntegerWithoutCommas => s_integerWithoutCommaFormat = new IntegerFormat("");
        public static Format Text => s_text = new Format(true);
        public static Format TypeName => s_typeNameFormat = new TypeOrImageFormat(type: true);
        public static Format Image => s_imageFormat = new TypeOrImageFormat(type: false);
        public static Format HumanReadableSize => s_humanReadableFormat = new HumanReadableFormat();
        public static Format Range => s_range = new RangeFormat();

        private sealed class IntegerFormat : Format
        {
            private readonly string _format;

            public IntegerFormat(string format)
            {
                _format = "{0:" + format + "}";
            }

            public override int FormatValue(StringBuilder result, object value, int maxLength, bool truncateBegin)
            {
                value = Unwrap(value);

                int startLength = result.Length;
                switch (value)
                {
                    case null:
                        break;

                    // case nuint nui:
                    //     result.AppendFormat(_format, (ulong)nui);
                    //     break;
                    //
                    // case nint ni:
                    //     unchecked
                    //     {
                    //         result.AppendFormat(_format, (ulong)ni);
                    //     }
                    //     break;

                    default:
                        result.AppendFormat(_format, value);
                        break;
                }

                TruncateStringBuilder(result, maxLength, result.Length - startLength, truncateBegin);
                return result.Length - startLength;
            }
        }

        /// <summary>
        /// Unlike plain text, this Format always truncates the beginning of the type name or image path,
        /// as the most important part is at the end.
        /// </summary>
        private sealed class TypeOrImageFormat : Format
        {
            private const string UnknownTypeName = "Unknown";
            private readonly bool _type;

            public TypeOrImageFormat(bool type)
                : base(canTruncate: true)
            {
                _type = type;
            }

            public override int FormatValue(StringBuilder sb, object value, int maxLength, bool truncateBegin)
            {
                int startLength = sb.Length;

                if (!_type)
                {
                    sb.Append(value);
                }
                else
                {
                    if (value is null)
                    {
                        sb.Append(UnknownTypeName);
                    }
                    else if (value is ClrType type)
                    {
                        string typeName = type.Name;
                        if (!string.IsNullOrWhiteSpace(typeName))
                        {
                            sb.Append(typeName);
                        }
                        else
                        {
                            string module = type.Module?.Name;
                            if (!string.IsNullOrWhiteSpace(module))
                            {
                                try
                                {
                                    module = System.IO.Path.GetFileNameWithoutExtension(module);
                                    sb.Append(module);
                                    sb.Append('!');
                                }
                                catch (ArgumentException)
                                {
                                }
                            }

                            sb.Append(UnknownTypeName);
                            if (type.MethodTable != 0)
                            {
                                sb.Append($" (MethodTable: ");
                                sb.AppendFormat("{0:x12}", type.MethodTable);
                                sb.Append(')');
                            }
                        }
                    }
                    else
                    {
                        sb.Append(value);
                    }
                }

                TruncateStringBuilder(sb, maxLength, sb.Length - startLength, truncateBegin: true);
                return sb.Length - startLength;
            }
        }

        private sealed class HumanReadableFormat : Format
        {
            public override int FormatValue(StringBuilder sb, object value, int maxLength, bool truncateBegin)
            {
                string humanReadable = null;

                if (value == null)
                {
                    humanReadable = null;
                }
                else if (value is int)
                {
                    humanReadable = ((long)(int)value).ConvertToHumanReadable();
                }
                else if (value is uint)
                {
                    humanReadable = ((ulong)(uint)value).ConvertToHumanReadable();
                }
                else if (value is long)
                {
                    humanReadable = ((long)value).ConvertToHumanReadable();
                }
                else if (value is ulong)
                {
                    humanReadable = ((ulong)value).ConvertToHumanReadable();
                }
                else if (value is float)
                {
                    humanReadable = ((double)(float)value).ConvertToHumanReadable();
                }
                else if (value is double)
                {
                    humanReadable = ((double)value).ConvertToHumanReadable();
                }
                // If you want the old nint/nuint cases on .NET Framework:
                else if (value is IntPtr)
                {
                    long ni = (IntPtr.Size == 8) ? ((IntPtr)value).ToInt64() : ((IntPtr)value).ToInt32();
                    humanReadable = ni.ConvertToHumanReadable();
                }
                else if (value is UIntPtr)
                {
                    ulong nu = (UIntPtr.Size == 8) ? ((UIntPtr)value).ToUInt64() : ((UIntPtr)value).ToUInt32();
                    humanReadable = nu.ConvertToHumanReadable();
                }
                else
                {
                    var s = value as string;
                    if (s != null)
                    {
                        humanReadable = s;
                    }
                    else
                    {
                        throw new NotSupportedException(
                            "Cannot convert '" + value.GetType().FullName + "' to a human readable size.");
                    }
                }

                if (!string.IsNullOrWhiteSpace(humanReadable))
                {
                    return base.FormatValue(sb, humanReadable, maxLength, truncateBegin);
                }

                return 0;
            }
        }

        private sealed class HexValueFormat : Format
        {
            public bool PrintPrefix { get; }
            public bool Signed { get; }

            public HexValueFormat(bool printPrefix, bool signed)
            {
                PrintPrefix = printPrefix;
                Signed = signed;
            }

            private string GetStringValue(long offset)
            {
                if (Signed)
                {
                    if (PrintPrefix)
                    {
                        return offset < 0 ? $"-0x{Math.Abs(offset):x2}" : $"0x{offset:x2}";
                    }
                    else
                    {
                        return offset < 0 ? $"-{Math.Abs(offset):x2}" : $"{offset:x2}";
                    }
                }

                return PrintPrefix ? $"0x{offset:x2}" : offset.ToString("x2");
            }

            private string GetHexOffsetString(object value)
            {
                if (value == null)
                    return "";

                var s = value as string;
                if (s != null)
                    return s;

                // nint -> IntPtr
                if (value is IntPtr)
                {
                    var ip = (IntPtr)value;
                    // match your old GetStringValue(nint) behavior:
                    long ni = (IntPtr.Size == 8) ? ip.ToInt64() : ip.ToInt32();
                    return GetStringValue(ni);
                }

                // nuint -> UIntPtr
                if (value is UIntPtr)
                {
                    var up = (UIntPtr)value;
                    ulong nui = (UIntPtr.Size == 8) ? up.ToUInt64() : up.ToUInt32();
                    return PrintPrefix ? ("0x" + nui.ToString("x2")) : nui.ToString("x2");
                }

                if (value is ulong)
                {
                    ulong ul = (ulong)value;
                    return PrintPrefix ? ("0x" + ul.ToString("x2")) : ul.ToString("x2");
                }

                if (value is long)
                    return GetStringValue((long)value);

                if (value is int)
                    return GetStringValue((int)value);

                if (value is uint)
                {
                    uint u = (uint)value;
                    return PrintPrefix ? ("0x" + u.ToString("x2")) : u.ToString("x2");
                }

                var bytes = value as IEnumerable<byte>;
                if (bytes != null)
                {
                    return (PrintPrefix ? "0x" : "") + string.Join("", bytes.Select(b => b.ToString("x2")));
                }

                throw new InvalidOperationException(
                    "Cannot convert value of type " + value.GetType().FullName + " to a HexOffset");
            }

            public override int FormatValue(StringBuilder sb, object value, int maxLength, bool truncateBegin)
            {
                int startLength = sb.Length;
                sb.Append(GetHexOffsetString(value));
                TruncateStringBuilder(sb, maxLength, sb.Length - startLength, truncateBegin);

                return sb.Length - startLength;
            }
        }

        private sealed class RangeFormat : Format
        {
            public override int FormatValue(StringBuilder sb, object value, int maxLength, bool truncateBegin)
            {
                int startLength = sb.Length;
                if (value is MemoryRange range)
                {
                    sb.AppendFormat("{0:x}", range.Start);
                    sb.Append('-');
                    sb.AppendFormat("{0:x}", range.End);

                    return sb.Length - startLength;
                }

                return base.FormatValue(sb, value, maxLength, truncateBegin);
            }
        }
    }
    
    internal static class ColumnKind
    {
        private static Column s_pointer;
        private static Column s_text;
        private static Column s_image;
        private static Column s_typeName;
        private static Column s_hexOffset;
        private static Column s_hexValue;
        private static Column s_dumpObj;
        private static Column s_integer;
        private static Column s_dumpHeapMT;
        private static Column s_listNearObj;
        private static Column s_dumpDomain;
        private static Column s_thread;
        private static Column s_integerWithoutComma;
        private static Column s_humanReadable;
        private static Column s_range;

        // NOTE/BUGBUG: This assumes IntPtr.Size matches the target process, which it should not do
        private static int PointerLength => IntPtr.Size * 2;

        /// <summary>
        /// A pointer, displayed as hex.
        /// </summary>
        public static Column Pointer => s_pointer = new Column(Align.Right, PointerLength, Formats.Pointer);

        /// <summary>
        /// Raw text which will not be truncated by default.
        /// </summary>
        public static Column Text => s_text = new Column(Align.Left, -1, Formats.Text);

        /// <summary>
        /// A hex value, prefixed with 0x.
        /// </summary>
        public static Column HexValue => s_hexValue = new Column(Align.Right, PointerLength + 2, Formats.HexValue);

        /// <summary>
        /// An offset (potentially negative), prefixed with 0x.  For example: '0x20' or '-0x20'.
        /// </summary>
        public static Column HexOffset => s_hexOffset = new Column(Align.Right, 10, Formats.HexOffset);

        /// <summary>
        /// An integer, with commas.  i.e. i.ToString("n0")
        /// </summary>
        public static Column Integer => s_integer = new Column(Align.Right, 14, Formats.Integer);

        /// <summary>
        /// An integer, without commas.
        /// </summary>
        public static Column IntegerWithoutCommas => s_integerWithoutComma = new Column(Align.Right, 10, Formats.IntegerWithoutCommas);

        /// <summary>
        /// A count of bytes (size).
        /// </summary>
        public static Column ByteCount => Integer;

        /// <summary>
        /// A human readable size count.  e.g. "1.23mb"
        /// </summary>
        public static Column HumanReadableSize => s_humanReadable = new Column(Align.Right, 12, Formats.HumanReadableSize);

        /// <summary>
        /// An object pointer, which we would like to link to !do if Dml is enabled.
        /// </summary>
        public static Column DumpObj => s_dumpObj = new Column(Align.Right, PointerLength, Formats.Pointer, Dml.DumpObj);

        /// <summary>
        /// A link to any number of ClrMD objects (ClrSubHeap, ClrSegment, a MethodTable or ClrType, etc) which will
        /// print an appropriate !dumpheap filter for, if dml is enabled.
        /// </summary>
        public static Column DumpHeap => s_dumpHeapMT = new Column(Align.Right, PointerLength, Formats.Pointer, Dml.DumpHeap);

        /// <summary>
        /// A link to !dumpdomain for the given domain, if dml is enabled.  This also puts the domain's name in the
        /// hover text for the link.
        /// </summary>
        public static Column DumpDomain => s_dumpDomain = new Column(Align.Right, PointerLength, Formats.Pointer, Dml.DumpDomain);

        /// <summary>
        /// The ClrThread address with a link to the OSThreadID to change threads (if dml is enabled).
        /// </summary>
        public static Column Thread => s_thread = new Column(Align.Right, PointerLength, Formats.Pointer, Dml.Thread);

        /// <summary>
        /// A link to !listnearobj for the given ClrObject or address, if dml is enabled.
        /// </summary>
        public static Column ListNearObj => s_listNearObj = new Column(Align.Right, PointerLength, Formats.Pointer, Dml.ListNearObj);

        /// <summary>
        /// The name of a given type.  Note that types are always truncated by removing the beginning of the type's
        /// name instead of truncating based on alignment.  This ensures the most important part of the name (the
        /// actual type name) is preserved instead of the namespace.
        /// </summary>
        public static Column TypeName => s_typeName = new Column(Align.Left, -1, Formats.TypeName);

        /// <summary>
        /// A path to an image on disk.  Note that images are always truncted by removing the beginning of the image's
        /// path instead of the end, preserving the filename.
        /// </summary>
        public static Column Image => s_image = new Column(Align.Left, -1, Formats.Image);

        /// <summary>
        /// A MemoryRange printed as "[start-end]".
        /// </summary>
        public static Column Range => s_range = new Column(Align.Left, PointerLength * 2 + 1, Formats.Range);
    }
    
    internal sealed class StringBuilderPool
    {
        private StringBuilder _stringBuilder;
        private readonly int _initialCapacity;

        public StringBuilderPool(int initialCapacity = 64)
        {
            _initialCapacity = initialCapacity > 0 ? initialCapacity : 0;
        }

        // This code all assumes SOS runs single threaded.  We would want to change this
        // code to use Interlocked.Exchange if that ever changes.
        public StringBuilder Rent()
        {
            StringBuilder sb = _stringBuilder;
            _stringBuilder = null;

            if (sb is null)
            {
                sb = new StringBuilder(_initialCapacity);
            }
            else
            {
                sb.Clear();
            }

            return sb;
        }

        public void Return(StringBuilder sb)
        {
            if (sb.Capacity < 1024)
            {
                _stringBuilder = sb;
            }
        }
    }
    
    internal abstract class DmlFormat
    {
        // intentionally not shared with Format
        private static readonly StringBuilderPool s_stringBuilderPool = new StringBuilderPool();

        public virtual string FormatValue(string outputText, object value)
        {
            StringBuilder sb = s_stringBuilderPool.Rent();

            FormatValue(sb, outputText, value);
            string result = sb.ToString();
            s_stringBuilderPool.Return(sb);
            return result;
        }

        public abstract void FormatValue(StringBuilder sb, string outputText, object value);

        protected static string DmlEscape(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            return new XText(text).ToString();
        }
    }
    
    internal enum Align
    {
        Left,
        Right,
        Center
    }
    
    internal class Format
    {
        private static StringBuilderPool s_stringBuilderPool = new StringBuilderPool();

        /// <summary>
        /// Returns true if a format of this type should never be truncated.  If true,
        /// DEBUG builds of SOS Assert.Fail if attempting to truncate the value of the
        /// column.  In release builds, we will simply not truncate the value, resulting
        /// in a jagged looking table, but usable output.
        /// </summary>
        public bool CanTruncate { get; protected set; }

        public Format() { }
        public Format(bool canTruncate) => CanTruncate = canTruncate;

        // Unwraps an object to get at what should be formatted.
        internal static object Unwrap(object value)
        {
            if (value is ClrObject)
                return ((ClrObject)value).Address;

            if (value is ClrAppDomain)
                return ((ClrAppDomain)value).Address;

            if (value is ClrType)
                return ((ClrType)value).MethodTable;

            if (value is ClrSegment)
                return ((ClrSegment)value).Address;

            if (value is ClrThread)
                return ((ClrThread)value).Address;

            if (value is ClrSubHeap)
                return ((ClrSubHeap)value).Index;

            return value;
        }

        public virtual string FormatValue(object value, int maxLength, bool truncateBegin)
        {
            StringBuilder sb = s_stringBuilderPool.Rent();

            FormatValue(sb, value, maxLength, truncateBegin);
            string result = sb.ToString();

            s_stringBuilderPool.Return(sb);
            return TruncateString(result, maxLength, truncateBegin);
        }

        public virtual int FormatValue(StringBuilder sb, object value, int maxLength, bool truncateBegin)
        {
            int currLength = sb.Length;
            sb.Append(value);
            TruncateStringBuilder(sb, maxLength, sb.Length - currLength, truncateBegin);

            return sb.Length - currLength;
        }

        protected string TruncateString(string result, int maxLength, bool truncateBegin)
        {
            if (maxLength >= 0 && result.Length > maxLength)
            {
                if (CanTruncate)
                {
                    if (maxLength <= 3)
                    {
                        result = new string('.', maxLength);
                    }
                    else if (truncateBegin)
                    {
                        result = "..." + result.Substring(result.Length - (maxLength - 3));
                    }
                    else
                    {
                        result = result.Substring(0, maxLength - 3) + "...";
                    }
                }
                else
                {
                    Debug.Fail("Tried to truncate a column we should never truncate.");
                }
            }

            Debug.Assert(maxLength < 0 || result.Length <= maxLength);
            return result;
        }

        protected void TruncateStringBuilder(StringBuilder result, int maxLength, int lengthWritten, bool truncateBegin)
        {
            Debug.Assert(lengthWritten >= 0);

            if (maxLength >= 0 && lengthWritten > maxLength)
            {
                if (CanTruncate)
                {
                    if (truncateBegin)
                    {
                        int start = result.Length - lengthWritten;
                        int wrote;
                        for (wrote = 0; wrote < 3 && wrote < maxLength; wrote++)
                        {
                            result[start + wrote] = '.';
                        }

                        int gap = lengthWritten - maxLength;
                        for (; wrote < maxLength; wrote++)
                        {
                            result[start + wrote] = result[start + wrote + gap];
                        }

                        result.Length = start + maxLength;
                    }
                    else
                    {
                        result.Length = result.Length - lengthWritten + maxLength;
                        for (int i = 0; i < maxLength && i < 3; i++)
                        {
                            result[result.Length - i - 1] = '.';
                        }
                    }
                }
                else
                {
                    Debug.Fail("Tried to truncate a column we should never truncate.");
                }
            }
        }
    }
    
    internal readonly struct Column
    {
        private static readonly StringBuilderPool s_stringBuilderPool = new StringBuilderPool();
        private static readonly Column s_enum = new Column(Align.Left, -1, new Format(), null);

        public readonly int Width;
        public readonly Format Format;
        public readonly Align Alignment;
        public readonly DmlFormat Dml;

        public Column(Align alignment, int width, Format format, DmlFormat dml = null)
        {
            Alignment = alignment;
            Width = width;
            Format = format ?? throw new ArgumentNullException(nameof(format));
            Dml = dml;
        }

        public Column WithWidth(int width) => new Column(Alignment, width, Format, Dml);
        internal Column WithDml(DmlFormat dml) => new Column(Alignment, Width, Format, dml);
        internal Column WithAlignment(Align align) => new Column(align, Width, Format, Dml);

        public Column GetAppropriateWidth<T>(IEnumerable<T> values, int min = -1, int max = -1)
        {
            int len = 0;

            StringBuilder sb = s_stringBuilderPool.Rent();

            foreach (T value in values)
            {
                sb.Clear();
                Format.FormatValue(sb, value, -1, false);
                len = Math.Max(len, sb.Length);
            }

            s_stringBuilderPool.Return(sb);

            if (len < min)
            {
                len = min;
            }

            if (max > 0 && len > max)
            {
                len = max;
            }

            return WithWidth(len);
        }

        internal static Column ForEnum<TEnum>()
            where TEnum : struct
        {
            int len = 0;
            foreach (TEnum t in Enum.GetValues(typeof(TEnum)))
            {
                len = Math.Max(len, t.ToString().Length);
            }

            return s_enum.WithWidth(len);
        }

        public override string ToString()
        {
            string format = Format?.GetType().Name ?? "null";
            string dml = Dml?.GetType().Name ?? "null";

            return $"align:{Alignment} width:{Width} format:{format} dml:{dml}";
        }
    }
    
    internal class Table
    {
        protected readonly StringBuilderPool _stringBuilderPool = new StringBuilderPool();
        protected string _spacing = " ";
        protected static readonly Column s_headerColumn = new Column(Align.Center, -1, Formats.Text, Dml.Bold);

        public string Indent { get; set; } = "";

        public IConsoleService Console { get; }

        public int TotalWidth => 1 * (Columns.Length - 1) + Columns.Sum(c => Math.Abs(c.Width));

        public Column[] Columns { get; set; }

        public Table(IConsoleService console, params Column[] columns)
        {
            Columns = columns.ToArray();
            Console = console;
        }

        public void SetAlignment(Align align)
        {
            for (int i = 0; i < Columns.Length; i++)
            {
                Columns[i] = Columns[i].WithAlignment(align);
            }
        }

        public virtual void WriteHeader(params string[] values)
        {
            IncreaseColumnWidth(values);
            WriteHeaderFooter(values);
        }

        public virtual void WriteFooter(params object[] values)
        {
            WriteHeaderFooter(values);
        }

        protected void IncreaseColumnWidth(string[] values)
        {
            // Increase column width if too small
            for (int i = 0; i < Columns.Length && i < values.Length; i++)
            {
                if (Columns.Length >= 0 && values[i].Length > Columns.Length)
                {
                    if (Columns[i].Width != -1 && Columns[i].Width < values[i].Length)
                    {
                        Columns[i] = Columns[i].WithWidth(values[i].Length);
                    }
                }
            }
        }

        public virtual void WriteRow(params object[] values)
        {
            StringBuilder rowBuilder = _stringBuilderPool.Rent();
            rowBuilder.Append(Indent);

            WriteRowWorker(values, rowBuilder, _spacing);

            _stringBuilderPool.Return(rowBuilder);
        }

        protected void WriteRowWorker(object[] values, StringBuilder rowBuilder, string spacing, bool writeLine = true)
        {
            bool isRowBuilderDml = false;

            for (int i = 0; i < values.Length; i++)
            {
                if (i != 0)
                {
                    rowBuilder.Append(spacing);
                }

                Column column = i < Columns.Length ? Columns[i] : ColumnKind.Text;

                bool isColumnDml = Console.SupportsDml && column.Dml != null;
                if (isRowBuilderDml != isColumnDml)
                {
                    WriteAndClearRowBuilder(rowBuilder, isRowBuilderDml);
                    isRowBuilderDml = isColumnDml;
                }

                Append(column, rowBuilder, values[i]);
            }

            if (writeLine)
            {
                rowBuilder.AppendLine();
            }

            WriteAndClearRowBuilder(rowBuilder, isRowBuilderDml);
        }

        private void WriteAndClearRowBuilder(StringBuilder rowBuilder, bool dml)
        {
            if (rowBuilder.Length != 0)
            {
                if (dml)
                {
                    Console.WriteDml(rowBuilder.ToString());
                }
                else
                {
                    Console.Write(rowBuilder.ToString());
                }

                rowBuilder.Clear();
            }
        }

        private void Append(Column column, StringBuilder sb, object value)
        {
            DmlFormat dml = null;
            if (Console.SupportsDml)
            {
                dml = column.Dml;
            }

            // Efficient case
            if (dml is null && column.Alignment == Align.Left)
            {
                int written = column.Format.FormatValue(sb, value, column.Width, column.Alignment == Align.Left);
                Debug.Assert(written >= 0);
                if (written < column.Width)
                {
                    sb.Append(' ', column.Width - written);
                }

                return;
            }

            string toWrite = column.Format.FormatValue(value, column.Width, column.Alignment == Align.Left);
            int displayLength = toWrite.Length;
            if (dml != null)
            {
                toWrite = dml.FormatValue(toWrite, value);
            }

            if (column.Width < 0)
            {
                sb.Append(toWrite);
            }
            else
            {
                if (column.Alignment == Align.Left)
                {
                    sb.Append(toWrite);
                    if (displayLength < column.Width)
                    {
                        sb.Append(' ', column.Width - displayLength);
                    }

                    return;
                }
                else if (column.Alignment == Align.Right)
                {
                    sb.Append(' ', column.Width - displayLength);
                    sb.Append(toWrite);
                }
                else
                {
                    Debug.Assert(column.Alignment == Align.Center);

                    int remainder = column.Width - displayLength;
                    int right = remainder >> 1;
                    int left = right + (remainder % 2);

                    sb.Append(' ', left);
                    sb.Append(toWrite);
                    sb.Append(' ', right);
                }
            }
        }

        protected virtual void WriteHeaderFooter(object[] values, bool writeSides = false, bool writeNewline = true)
        {
            StringBuilder rowBuilder = _stringBuilderPool.Rent();
            rowBuilder.Append(Indent);

            if (writeSides)
            {
                rowBuilder.Append(_spacing);
            }

            for (int i = 0; i < values.Length; i++)
            {
                if (i != 0)
                {
                    rowBuilder.Append(_spacing);
                }

                Column curr = i < Columns.Length ? Columns[i] : s_headerColumn;
                if (Console.SupportsDml)
                {
                    curr = curr.WithDml(Dml.Bold);
                }
                else
                {
                    curr = curr.WithDml(null);
                }

                Append(curr, rowBuilder, values[i]);
            }

            if (writeSides)
            {
                rowBuilder.Append(_spacing);
            }

            if (writeNewline)
            {
                rowBuilder.AppendLine();
            }

            if (Console.SupportsDml)
            {
                Console.WriteDml(rowBuilder.ToString());
            }
            else
            {
                Console.Write(rowBuilder.ToString());
            }

            _stringBuilderPool.Return(rowBuilder);
        }
    }
    
    public static class MemoryServiceExtensions
    {
        /// <summary>
        /// Returns the mask to remove any sign extension for 32 bit addresses
        /// </summary>
        public static ulong SignExtensionMask(this IMemoryService memoryService)
        {
            return memoryService.PointerSize == 4 ? uint.MaxValue : ulong.MaxValue;
        }

        /// <summary>
        /// Read memory out of the target process.
        /// </summary>
        /// <param name="memoryService">memory service instance</param>
        /// <param name="address">The address of memory to read</param>
        /// <param name="buffer">The buffer to write to</param>
        /// <param name="bytesRequested">The number of bytes to read</param>
        /// <param name="bytesRead">The number of bytes actually read out of the target process</param>
        /// <returns>true if any bytes were read at all, false if the read failed (and no bytes were read)</returns>
        public static bool ReadMemory(this IMemoryService memoryService, ulong address, byte[] buffer, int bytesRequested, out int bytesRead)
        {
            return memoryService.ReadMemory(address, new Span<byte>(buffer, 0, bytesRequested), out bytesRead);
        }

        /// <summary>
        /// Read memory out of the target process.
        /// </summary>
        /// <param name="memoryService">memory service instance</param>
        /// <param name="address">The address of memory to read</param>
        /// <param name="buffer">The buffer to read memory into</param>
        /// <param name="bytesRequested">The number of bytes to read</param>
        /// <param name="bytesRead">The number of bytes actually read out of the target process</param>
        /// <returns>true if any bytes were read at all, false if the read failed (and no bytes were read)</returns>
        // public static bool ReadMemory(this IMemoryService memoryService, ulong address, IntPtr buffer, int bytesRequested, out int bytesRead)
        // {
        //     unsafe
        //     {
        //         return memoryService.ReadMemory(address, new Span<byte>(buffer.ToPointer(), bytesRequested), out bytesRead);
        //     }
        // }

        /// <summary>
        /// Read a 32 bit value from the memory location
        /// </summary>
        /// <param name="memoryService">memory service instance</param>
        /// <param name="address">address to read</param>
        /// <param name="value">returned value</param>
        /// <returns>true success, false failure</returns>
        public static bool ReadDword(this IMemoryService memoryService, ulong address, out uint value)
        {
            Span<byte> buffer = stackalloc byte[sizeof(uint)];
            if (memoryService.ReadMemory(address, buffer, out int bytesRead))
            {
                if (bytesRead == sizeof(uint))
                {
                    value = MemoryMarshal.Read<uint>(buffer);
                    return true;
                }
            }
            value = default;
            return false;
        }

        public static bool Read<T>(this IMemoryService memoryService, ref ulong address, out T value) where T : unmanaged
        {
            Span<byte> buffer = stackalloc byte[Unsafe.SizeOf<T>()];
            if (memoryService.ReadMemory(address, buffer, out int bytesRead))
            {
                if (bytesRead == Unsafe.SizeOf<T>())
                {
                    value = MemoryMarshal.Read<T>(buffer);
                    address += (ulong)Unsafe.SizeOf<T>();
                    return true;
                }
            }
            value = default;
            return false;
        }

        /// <summary>
        /// Return a pointer sized value from the address.
        /// </summary>
        /// <param name="memoryService">memory service instance</param>
        /// <param name="address">address to read</param>
        /// <param name="value">returned value</param>
        /// <returns>true success, false failure</returns>
        public static bool ReadPointer(this IMemoryService memoryService, ulong address, out ulong value)
        {
            int pointerSize = memoryService.PointerSize;
            Span<byte> buffer = stackalloc byte[pointerSize];
            if (memoryService.ReadMemory(address, buffer, out int bytesRead))
            {
                switch (pointerSize)
                {
                    case 4:
                        value = MemoryMarshal.Read<uint>(buffer);
                        return true;
                    case 8:
                        value = MemoryMarshal.Read<ulong>(buffer);
                        return true;
                }
            }
            value = default;
            return false;
        }

        public static bool ReadPointer(this IMemoryService memoryService, ref ulong address, out ulong value)
        {
            bool ret = memoryService.ReadPointer(address, out value);
            if (ret)
            {
                address += (ulong)memoryService.PointerSize;
            }
            return ret;
        }

        public static bool ReadAnsiString(this IMemoryService memoryService, uint maxLength, ulong address, out string value)
        {
            StringBuilder sb = new StringBuilder();
            byte[] buffer = new byte[maxLength];
            value = null;
            if (memoryService.ReadMemory(address, buffer, out int bytesRead) && bytesRead > 0)
            {
                // convert null terminated ANSI char array to a string
                for (int i = 0; i < buffer.Length; i++)
                {
                    // Read the string one character at a time
                    char c = (char)buffer[i];
                    if (buffer[i] == 0) // Stop at null terminator
                    {
                        value = sb.ToString();
                        break; // Stop reading at null terminator
                    }
                    if (c < 0x20 || c > 0x7E) // Unexpected characters
                    {
                        break;
                    }
                    // Append the character to the string
                    sb.Append(c);
                }
            }
            return !string.IsNullOrEmpty(value);
        }


        /// <summary>
        /// Create a stream for all of memory.
        /// </summary>
        /// <param name="memoryService">memory service instance</param>
        /// <returns>Stream of all of memory</returns>
        public static Stream CreateMemoryStream(this IMemoryService memoryService)
        {
            return new TargetStream(memoryService, 0, long.MaxValue);
        }

        /// <summary>
        /// Create a stream for the address range.
        /// </summary>
        /// <param name="memoryService">memory service instance</param>
        /// <param name="address">address to read</param>
        /// <param name="size">size of stream</param>
        /// <returns>memory range Stream</returns>
        public static Stream CreateMemoryStream(this IMemoryService memoryService, ulong address, ulong size)
        {
            Debug.Assert(address != 0);
            Debug.Assert(size != 0);
            Debug.Assert((address & ~memoryService.SignExtensionMask()) == 0);
            return new TargetStream(memoryService, address, size);
        }

        /// <summary>
        /// Stream implementation to read debugger target memory for in-memory PDBs
        /// </summary>
        private sealed class TargetStream : Stream
        {
            private readonly ulong _address;
            private readonly IMemoryService _memoryService;

            public override long Position { get; set; }
            public override long Length { get; }
            public override bool CanSeek { get { return true; } }
            public override bool CanRead { get { return true; } }
            public override bool CanWrite { get { return false; } }

            public TargetStream(IMemoryService memoryService, ulong address, ulong size)
                : base()
            {
                _memoryService = memoryService;
                _address = address;
                Length = (long)size;
                Position = 0;
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (Position + count > Length)
                {
                    return 0;
                }
                if (_memoryService.ReadMemory(_address + (ulong)Position, new Span<byte>(buffer, offset, count), out int bytesRead))
                {
                    Position += bytesRead;
                }
                return bytesRead;
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                switch (origin)
                {
                    case SeekOrigin.Begin:
                        Position = offset;
                        break;
                    case SeekOrigin.End:
                        Position = Length + offset;
                        break;
                    case SeekOrigin.Current:
                        Position += offset;
                        break;
                }
                return Position;
            }

            public override void Flush()
            {
            }

            public override void SetLength(long value)
            {
                throw new NotImplementedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotImplementedException();
            }
        }
    }
    
    public class DiagnosticsException : Exception
    {
        public DiagnosticsException(string message) : base(message)
        {
        }
    }

    public static class ConsoleServiceExtensions
    {
        /// <summary>
        /// Display a blank line
        /// </summary>
        /// <param name="console"></param>
        public static void WriteLine(this IConsoleService console)
        {
            console.Write(Environment.NewLine);
        }

        /// <summary>
        /// Display text
        /// </summary>
        /// <param name="console">console service instance</param>
        /// <param name="message">text message</param>
        public static void WriteLine(this IConsoleService console, string message)
        {
            console.Write(message + Environment.NewLine);
        }

        /// <summary>
        /// Display formatted text
        /// </summary>
        /// <param name="console">console service instance</param>
        /// <param name="format">format string</param>
        /// <param name="args">arguments</param>
        public static void WriteLine(this IConsoleService console, string format, params object[] args)
        {
            console.Write(string.Format(format, args) + Environment.NewLine);
        }

        /// <summary>
        /// Display formatted warning text
        /// </summary>
        /// <param name="console">console service instance</param>
        /// <param name="format">format string</param>
        /// <param name="args">arguments</param>
        public static void WriteLineWarning(this IConsoleService console, string format, params object[] args)
        {
            console.WriteWarning(string.Format(format, args) + Environment.NewLine);
        }

        /// <summary>
        /// Display formatted error text
        /// </summary>
        /// <param name="console">console service instance</param>
        /// <param name="format">format string</param>
        /// <param name="args">arguments</param>
        public static void WriteLineError(this IConsoleService console, string format, params object[] args)
        {
            console.WriteError(string.Format(format, args) + Environment.NewLine);
        }
    }
    
    public class StaticVariableService
    {
        private Dictionary<ulong, ClrStaticField> _fields;
        private IEnumerator<(ulong Address, ClrStaticField Static)> _enumerator;

        public ClrRuntime Runtime { get; set; }

        public StaticVariableService(ClrRuntime runtime)
        {
            Runtime = runtime;
        }

        /// <summary>
        /// Returns the static field at the given address.
        /// </summary>
        /// <param name="address">The address of the static field.  Note that this is not a pointer to
        /// an object, but rather a pointer to where the CLR runtime tracks the static variable's
        /// location.  In all versions of the runtime, address will live in the middle of a pinned
        /// object[].</param>
        /// <param name="field">The field corresponding to the given address.  Non-null if return
        /// is true.</param>
        /// <returns>True if the address corresponded to a static variable, false otherwise.</returns>
        public bool TryGetStaticByAddress(ulong address, out ClrStaticField field)
        {
            if (_fields is null)
            {
                _fields = new Dictionary<ulong, ClrStaticField>();
                _enumerator = EnumerateStatics().GetEnumerator();
            }

            if (_fields.TryGetValue(address, out field))
            {
                return true;
            }

            // pay for play lookup
            if (_enumerator != null)
            {
                do
                {
                    _fields[_enumerator.Current.Address] = _enumerator.Current.Static;
                    if (_enumerator.Current.Address == address)
                    {
                        field = _enumerator.Current.Static;
                        return true;
                    }
                } while (_enumerator.MoveNext());

                _enumerator = null;
            }

            return false;
        }

        public IEnumerable<(ulong Address, ClrStaticField Static)> EnumerateStatics()
        {
            ClrAppDomain shared = Runtime.SharedDomain;

            foreach (ClrModule module in Runtime.EnumerateModules())
            {
                foreach ((ulong mt, _) in module.EnumerateTypeDefToMethodTableMap())
                {
                    ClrType type = Runtime.GetTypeByMethodTable(mt);
                    if (type is null)
                    {
                        continue;
                    }

                    foreach (ClrStaticField stat in type.StaticFields)
                    {
                        foreach (ClrAppDomain domain in Runtime.AppDomains)
                        {
                            ulong address = stat.GetAddress(domain);
                            if (address != 0)
                            {
                                yield return (address, stat);
                            }
                        }

                        if (shared != null)
                        {
                            ulong address = stat.GetAddress(shared);
                            if (address != 0)
                            {
                                yield return (address, stat);
                            }
                        }
                    }
                }
            }
        }
    }
    
    public interface IConsoleService
    {
        /// <summary>
        /// Write text to console's standard out
        /// </summary>
        /// <param name="value">text</param>
        void Write(string value);

        /// <summary>
        /// Write warning text to console
        /// </summary>
        /// <param name="value"></param>
        void WriteWarning(string value);

        /// <summary>
        /// Write error text to console
        /// </summary>
        /// <param name="value"></param>
        void WriteError(string value);

        /// <summary>Writes Debugger Markup Language (DML) markup text.</summary>
        void WriteDml(string text);

        /// <summary>
        /// Writes an exec tag to the output stream.
        /// </summary>
        /// <param name="text">The display text.</param>
        /// <param name="action">The action to perform.</param>
        void WriteDmlExec(string text, string action);

        /// <summary>Gets whether <see cref="WriteDml"/> is supported.</summary>
        bool SupportsDml { get; }

        /// <summary>
        /// Cancellation token for current command
        /// </summary>
        CancellationToken CancellationToken { get; set; }

        /// <summary>
        /// Screen or window width or 0.
        /// </summary>
        int WindowWidth { get; }
    }
    
    public interface IMemoryService
    {
        /// <summary>
        /// Returns the pointer size of the target
        /// </summary>
        int PointerSize { get; }

        /// <summary>
        /// Read memory out of the target process.
        /// </summary>
        /// <param name="address">The address of memory to read</param>
        /// <param name="buffer">The buffer to read memory into</param>
        /// <param name="bytesRead">The number of bytes actually read out of the target process</param>
        /// <returns>true if any bytes were read at all, false if the read failed (and no bytes were read)</returns>
        bool ReadMemory(ulong address, Span<byte> buffer, out int bytesRead);

        /// <summary>
        /// Write memory into target process for supported targets.
        /// </summary>
        /// <param name="address">The address of memory to write</param>
        /// <param name="buffer">The buffer to write</param>
        /// <param name="bytesWritten">The number of bytes successfully written</param>
        /// <returns>true if any bytes where written, false if write failed</returns>
        bool WriteMemory(ulong address, Span<byte> buffer, out int bytesWritten);
    }
    
    public class MemoryServiceFromDataReader : IMemoryService
    {
        private readonly IDataReader _dataReader;

        /// <summary>
        /// Memory service constructor
        /// </summary>
        /// <param name="dataReader">CLRMD data reader</param>
        public MemoryServiceFromDataReader(IDataReader dataReader)
        {
            _dataReader = dataReader;
        }

        #region IMemoryService

        /// <summary>
        /// Returns the pointer size of the target
        /// </summary>
        public int PointerSize => _dataReader.PointerSize;

        /// <summary>
        /// Read memory out of the target process.
        /// </summary>
        /// <param name="address">The address of memory to read</param>
        /// <param name="buffer">The buffer to read memory into</param>
        /// <param name="bytesRead">The number of bytes actually read out of the target process</param>
        /// <returns>true if any bytes were read at all, false if the read failed (and no bytes were read)</returns>
        public bool ReadMemory(ulong address, Span<byte> buffer, out int bytesRead)
        {
            bytesRead = _dataReader.Read(address, buffer);
            return bytesRead > 0;
        }

        /// <summary>
        /// Write memory into target process for supported targets.
        /// </summary>
        /// <param name="address">The address of memory to write</param>
        /// <param name="buffer">The buffer to write</param>
        /// <param name="bytesWritten">The number of bytes successfully written</param>
        /// <returns>true if any bytes where written, false if write failed</returns>
        public bool WriteMemory(ulong address, Span<byte> buffer, out int bytesWritten)
        {
            bytesWritten = 0;
            return false;
        }

        #endregion
    }
    
    public class RootCacheService
    {
        private List<(ulong Source, ulong Target)> _dependentHandles;
        private ReadOnlyCollection<ClrRoot> _handleRoots;
        private ReadOnlyCollection<ClrRoot> _finalizerRoots;
        private ReadOnlyCollection<ClrRoot> _stackRoots;
        private bool _printedWarning;
        private bool _printedStackWarning;

        public IConsoleService Console { get; set; }

        public ClrRuntime Runtime { get; set; }

        public RootCacheService(ClrRuntime runtime, IConsoleService consoleService)
        {
            Runtime = runtime;
            Console = consoleService;
        }

        public ReadOnlyCollection<(ulong Source, ulong Target)> GetDependentHandles()
        {
            InitializeHandleRoots();

            // We keep _dependentHandles as a List instead of ReadOnlyCollection so we can use
            // List<>.BinarySearch.
            return _dependentHandles.AsReadOnly();
        }

        public bool IsDependentHandleLink(ulong source, ulong target)
        {
            InitializeHandleRoots();

            int i = _dependentHandles.BinarySearch((source, target));
            return i >= 0;
        }

        public IEnumerable<ClrRoot> EnumerateRoots(bool includeFinalizer = true)
        {
            PrintWarning();

            foreach (ClrRoot root in GetHandleRoots())
            {
                Console.CancellationToken.ThrowIfCancellationRequested();
                yield return root;
            }

            if (includeFinalizer)
            {
                foreach (ClrRoot root in GetFinalizerQueueRoots())
                {
                    Console.CancellationToken.ThrowIfCancellationRequested();
                    yield return root;
                }
            }

            // If we made it here without the user breaking out of the enumeration
            // then we've already printed a warning on this command run, we don't
            // need to also print the stack warning.
            _printedStackWarning = true;
            foreach (ClrRoot root in GetStackRoots())
            {
                Console.CancellationToken.ThrowIfCancellationRequested();
                yield return root;
            }
        }

        public ReadOnlyCollection<ClrRoot> GetHandleRoots()
        {
            InitializeHandleRoots();
            return _handleRoots;
        }


        private void InitializeHandleRoots()
        {
            if (_handleRoots != null && _dependentHandles != null)
            {
                return;
            }

            PrintWarning();
            List<(ulong Source, ulong Target)> dependentHandles = new List<(ulong Source, ulong Target)>();
            List<ClrRoot> handleRoots = new List<ClrRoot>();

            foreach (ClrHandle handle in Runtime.EnumerateHandles())
            {
                Console.CancellationToken.ThrowIfCancellationRequested();

                if (handle.HandleKind == ClrHandleKind.Dependent)
                {
                    dependentHandles.Add((handle.Object, handle.Dependent));
                }

                if (!handle.IsStrong)
                {
                    continue;
                }

                handleRoots.Add(handle);
            }

            // Sort dependentHandles so it can be binary searched
            dependentHandles.Sort();

            _handleRoots = handleRoots.AsReadOnly();
            _dependentHandles = dependentHandles;
        }

        public ReadOnlyCollection<ClrRoot> GetFinalizerQueueRoots()
        {
            if (_finalizerRoots != null)
            {
                return _finalizerRoots;
            }

            PrintWarning();

            // This should be fast, there's rarely many FQ roots
            _finalizerRoots = Runtime.Heap.EnumerateFinalizerRoots().ToList().AsReadOnly();
            return _finalizerRoots;
        }

        private void PrintWarning()
        {
            if (!_printedWarning)
            {
                Console.WriteLineWarning("Caching GC roots, this may take a while.");
                Console.WriteLineWarning("Subsequent runs of this command will be faster.");
                Console.WriteLine();
                _printedWarning = true;
            }
        }

        public ReadOnlyCollection<ClrRoot> GetStackRoots()
        {
            if (_stackRoots != null)
            {
                return _stackRoots;
            }

            // Stack roots can take an extra long time to walk, and one mode of !gcroot skips enumerating stack roots.  If the user
            // calls "!gcroot -nostack" they will get a warning the first time, but if they call it again without "-nostack" they
            // may be surprised by a very long pause.  We skip this second message if the user is calling EnumerateRoots().
            if (!_printedStackWarning)
            {
                Console.WriteLineWarning("Caching GC stack roots, this may take a while.");
                Console.WriteLineWarning("Subsequent runs of this command will be faster.");
                Console.WriteLine();
                _printedStackWarning = true;
            }

            List<ClrRoot> stackRoots = new List<ClrRoot>();
            foreach (ClrThread thread in Runtime.Threads.Where(thread => thread.IsAlive))
            {
                Console.CancellationToken.ThrowIfCancellationRequested();

                foreach (ClrRoot root in thread.EnumerateStackRoots())
                {
                    Console.CancellationToken.ThrowIfCancellationRequested();
                    stackRoots.Add(root);
                }
            }

            _stackRoots = stackRoots.AsReadOnly();
            return _stackRoots;
        }
    }
    
    public class GCRootCommand
    {
        private StringBuilder _lineBuilder = new StringBuilder(64);
        private ClrRoot _lastRoot;

        public IMemoryService Memory { get; set; }

        public RootCacheService RootCache { get; set; }

        public StaticVariableService StaticVariables { get; set; }

        //public ManagedFileLineService FileLineService { get; set; }

        public int? AsGCGeneration { get; set; }

        public bool NoStacks { get; set; }

        public ulong TargetAddress { get; set; }

        public int? Limit { get; set; }
        
        private ClrRuntime Runtime;
        
        private IConsoleService Console;

        public GCRootCommand(IMemoryService memoryService, RootCacheService rootCacheService, StaticVariableService staticVariableService, IConsoleService consoleService)
        {
            Memory = memoryService;
            RootCache = rootCacheService;
            RootCache.Console = consoleService;
            StaticVariables = staticVariableService;
            Console = consoleService;
        }

        protected bool TryParseAddress(string addressInHexa, out ulong address)
        {
            if (string.IsNullOrWhiteSpace(addressInHexa))
            {
                address = 0;
                return false;
            }

            // skip 0x or leading 0000 if needed
            if (addressInHexa.StartsWith("0x"))
            {
                addressInHexa = addressInHexa.Substring(2);
            }

            addressInHexa = addressInHexa.TrimStart('0');

            int index = addressInHexa.IndexOf('`');
            if (index >= 0 && index < addressInHexa.Length - 1)
            {
                // Remove up to one instance of ` since that's what WinDbg adds to its x64 addresses.
                addressInHexa = addressInHexa.Substring(0, index) + addressInHexa.Substring(index + 1);
            }

            return ulong.TryParse(addressInHexa, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out address);
        }

        public void Invoke(ClrRuntime runtime)
        {
            Runtime = runtime;
            var address = TargetAddress;

            ClrObject obj = Runtime.Heap.GetObject(address);
            if (!obj.IsValid)
            {
                Console.WriteWarning($"Warning: {address:x} is not a valid object");
            }

            GCRoot gcroot = new GCRoot(Runtime.Heap, (found) =>
            {
                Console.CancellationToken.ThrowIfCancellationRequested();
                return found == address;
            });

            int count;
            int limit = Limit ?? int.MaxValue;

            if (AsGCGeneration.HasValue)
            {
                int gen = AsGCGeneration.Value;

                ClrSegment seg = Runtime.Heap.GetSegmentByAddress(address);
                if (seg is null)
                {
                    throw new DiagnosticsException($"Address {address:x} is not in the managed heap.");
                }

                Generation objectGen = seg.GetGeneration(address);
                if (gen < (int)objectGen)
                {
                    Console.WriteLine($"Object {address:x} will survive this collection:");
                    Console.WriteLine($"    gen({address:x}) = {objectGen} > {gen} = condemned generation.");
                    return;
                }

                if (gen < 0 || gen > 1)
                {
                    // If not gen0 or gen1, treat it as a normal !gcroot
                    if (NoStacks)
                    {
                        count = PrintNonStackRoots(gcroot, limit);
                    }
                    else
                    {
                        count = PrintAllRoots(gcroot, limit);
                    }
                }
                else
                {
                    count = PrintOlderGenerationRoots(gcroot, gen, limit);
                    count += PrintNonStackRoots(gcroot, limit);
                }
            }
            else if (NoStacks)
            {
                count = PrintNonStackRoots(gcroot, limit);
            }
            else
            {
                count = PrintAllRoots(gcroot, limit);
            }

            Console.WriteLine($"Found {count:n0} unique roots.");
        }

        public static string GetDetailedHelp() =>
            @"GCRoot looks for references (or roots) to an object. These can exist in four
places:

   1. On the stack
   2. Within a GC Handle
   3. In an object ready for finalization
   4. As a member of an object found in 1, 2 or 3 above.

First, all stacks will be searched for roots, then handle tables, and finally
the reachable queue of the finalizer. Some caution about the stack roots: 
GCRoot doesn't attempt to determine if a stack root it encountered is valid 
or is old (discarded) data. You would have to use !ClrStack and !U to 
disassemble the frame that the local or argument value belongs to in order to 
determine if it is still in use.

Because people often want to restrict the search to gc handles and reachable
objects, there is a -nostacks option.

The -all option forces all roots to be displayed instead of just the unique roots.
";
        private int PrintOlderGenerationRoots(GCRoot gcroot, int gen, int limit)
        {
            int count = 0;

            bool noInternalRootData = true;
            HashSet<ulong> uniqueRoots = new HashSet<ulong>();

            foreach (ClrSubHeap subheap in Runtime.Heap.SubHeaps)
            {
                MemoryRange internalRootArray = subheap.InternalRootArray;
                if (internalRootArray.Length == 0)
                {
                    continue;
                }

                noInternalRootData = false;

                bool first = true;
                ulong address = internalRootArray.Start;
                while (internalRootArray.Contains(address))
                {
                    if (count >= limit)
                    {
                        break;
                    }

                    Console.CancellationToken.ThrowIfCancellationRequested();

                    if (Memory.ReadPointer(address, out ulong objAddress) && !uniqueRoots.Contains(objAddress))
                    {
                        ClrObject obj = Runtime.Heap.GetObject(objAddress);
                        if (obj.IsValid)
                        {
                            GCRoot.ChainLink path = gcroot.FindPathFrom(obj);
                            if (path != null)
                            {
                                if (first)
                                {
                                    Console.WriteLine("Older Generation:");
                                    first = false;
                                }

                                Console.WriteLine($"    {objAddress:x}");
                                PrintPath(Console, RootCache, StaticVariables, Runtime.Heap, path);
                                Console.WriteLine();

                                uniqueRoots.Add(objAddress);
                                count++;
                            }
                        }
                        else
                        {
                            Console.WriteLineWarning($"Warning: GC internal root array contained invalid object: *{address:x} = {objAddress:x}");
                        }
                    }

                    address += (uint)Memory.PointerSize;
                }
            }

            if (noInternalRootData)
            {
                throw new InvalidDataException("Could not gather needed data, possibly due to memory constraints in the debuggee.\n" +
                                               $"To try again, re-issue the '!findroots -gen {gen}' command.");
            }

            return count;
        }

        private int PrintAllRoots(GCRoot gcroot, int limit)
        {
            int count = 0;
            foreach (ClrRoot root in RootCache.EnumerateRoots())
            {
                if (count >= limit)
                {
                    break;
                }

                Console.CancellationToken.ThrowIfCancellationRequested();
                GCRoot.ChainLink item = gcroot.FindPathFrom(root.Object);
                if (item != null)
                {
                    PrintPath(root, item);
                    count++;
                }
            }

            return count;
        }

        private int PrintNonStackRoots(GCRoot gcroot, int limit)
        {
            int count = 0;
            foreach (ClrRoot root in RootCache.GetHandleRoots())
            {
                if (count >= limit)
                {
                    break;
                }

                Console.CancellationToken.ThrowIfCancellationRequested();
                GCRoot.ChainLink item = gcroot.FindPathFrom(root.Object);
                if (item != null)
                {
                    PrintPath(root, item);
                    count++;
                }
            }

            foreach (ClrRoot root in RootCache.GetFinalizerQueueRoots())
            {
                if (count >= limit)
                {
                    break;
                }

                Console.CancellationToken.ThrowIfCancellationRequested();
                GCRoot.ChainLink item = gcroot.FindPathFrom(root.Object);
                if (item != null)
                {
                    PrintPath(root, item);
                    count++;
                }
            }

            return count;
        }

        private void PrintPath(ClrRoot root, GCRoot.ChainLink link)
        {
            PrintRoot(root);
            PrintPath(Console, RootCache, StaticVariables, Runtime.Heap, link);
            Console.WriteLine();
        }

        public static void PrintPath(IConsoleService console, RootCacheService rootCache, StaticVariableService statics, ClrHeap heap, GCRoot.ChainLink link)
        {
            Table objectOutput = new Table(console, ColumnKind.Text.WithWidth(2), ColumnKind.DumpObj, ColumnKind.TypeName, ColumnKind.Text)
            {
                Indent = new string(' ', 10)
            };

            objectOutput.SetAlignment(Align.Left);

            bool first = true;
            bool isPossibleStatic = true;

            ClrObject firstObj = default;

            ulong prevObj = 0;
            while (link != null)
            {
                ClrObject obj = heap.GetObject(link.Object);

                // Check whether this link is a dependent handle
                string extraText = "";
                bool isDependentHandleLink = rootCache.IsDependentHandleLink(prevObj, link.Object);
                if (isDependentHandleLink)
                {
                    extraText = "(dependent handle)";
                }

                // Print static variable info.  In all versions of the runtime, static variables are stored in
                // a pinned object array.  We check if the first link in the chain is an object[], and if so we
                // check if the second object's address is the location of a static variable.  We could further
                // narrow this by checking the root type, but that needlessly complicates this code...we can't
                // get false positives or negatives here (as nothing points to static variable object[] other
                // than the root).
                if (first)
                {
                    firstObj = obj;
                    isPossibleStatic = firstObj.IsValid && firstObj.IsArray && firstObj.Type.Name == "System.Object[]";
                    first = false;
                }
                else if (isPossibleStatic)
                {
                    if (statics != null && !isDependentHandleLink)
                    {
                        foreach (ClrReference reference in firstObj.EnumerateReferencesWithFields(carefully: false, considerDependantHandles: false))
                        {
                            if (reference.Object == obj)
                            {
                                ulong address = firstObj + (uint)reference.Offset;

                                if (statics.TryGetStaticByAddress(address, out ClrStaticField field))
                                {
                                    extraText = $"(static variable: {field.Type?.Name ?? "Unknown"}.{field.Name})";
                                    break;
                                }
                            }
                        }
                    }

                    // only the first object[] in the chain is possible to be the static array
                    isPossibleStatic = false;
                }

                objectOutput.WriteRow("->", obj, obj.Type, extraText);

                prevObj = link.Object;
                link = link.Next;
            }
        }

        private void PrintRoot(ClrRoot root)
        {
            if (root is ClrStackRoot stackRoot)
            {
                ClrStackRoot lastStackRoot = _lastRoot as ClrStackRoot;

                ClrThread currThread = stackRoot.StackFrame?.Thread;
                if (currThread != null && lastStackRoot?.StackFrame?.Thread != currThread)
                {
                    Console.WriteLine($"Thread {currThread.OSThreadId:x}:");
                }

                ClrStackFrame currFrame = stackRoot.StackFrame;
                if (currFrame != null && lastStackRoot?.StackFrame != currFrame)
                {
                    Console.WriteLine(GetFrameOutput(currFrame));
                }

                Console.WriteLine(GetRegisterOutput(stackRoot));
            }
            else if (root.RootKind == ClrRootKind.FinalizerQueue)
            {
                if (_lastRoot is null || _lastRoot.RootKind != ClrRootKind.FinalizerQueue)
                {
                    Console.WriteLine("Finalizer Queue:");
                }

                Console.WriteLine($"    {root.Address:x16} (finalizer root)");
            }
            else if (root is ClrHandle handle)
            {
                if (_lastRoot == null || !(_lastRoot is ClrHandle))
                {
                    Console.WriteLine("HandleTable:");
                }

                _lineBuilder.Clear();
                _lineBuilder.Append("    ");
                _lineBuilder.Append(root.Address.ToString("x16"));
                _lineBuilder.Append(" (");
                _lineBuilder.Append(NameForHandle(handle.HandleKind));

                if (handle.HandleKind == ClrHandleKind.RefCounted)
                {
                    _lineBuilder.Append(' ');
                    _lineBuilder.Append("RefCount: ");
                    _lineBuilder.Append(handle.ReferenceCount.ToString("n0"));
                }

                _lineBuilder.Append(')');
                Console.WriteLine(_lineBuilder.ToString());
            }
            else
            {
                // There are no other options, but futureproofing in case we add something new
                if (_lastRoot is null || _lastRoot.RootKind != root.RootKind)
                {
                    Console.WriteLine($"{root.RootKind}:");
                }

                Console.WriteLine($"    {root.Address:x16}");
            }

            _lastRoot = root;
        }

        private static string NameForHandle(ClrHandleKind handleKind)
        {
            switch (handleKind)
            {
                case ClrHandleKind.WeakShort:   return "weak short handle";
                case ClrHandleKind.WeakLong:    return "weak long handle";
                case ClrHandleKind.Strong:      return "strong handle";
                case ClrHandleKind.Pinned:      return "pinned handle";
                case ClrHandleKind.RefCounted:  return "ref counted handle";
                case ClrHandleKind.Dependent:   return "dependent handle";
                case ClrHandleKind.AsyncPinned: return "async pinned handle";
                case ClrHandleKind.SizedRef:    return "sized ref handle";
                case ClrHandleKind.WeakWinRT:   return "weak WinRT handle";
                default:                        return handleKind.ToString();
            }
        }

        private string GetFrameOutput(ClrStackFrame currFrame)
        {
            _lineBuilder.Clear();
            _lineBuilder.Append("    ");

            _lineBuilder.Append(currFrame.StackPointer.ToString("x"));

            // InstructionPointer is 0 for coreclr!Frame objects.
            if (currFrame.InstructionPointer != 0)
            {
                _lineBuilder.Append(' ');
                _lineBuilder.Append(currFrame.InstructionPointer.ToString("x"));
            }

            if (currFrame.FrameName != null)
            {
                _lineBuilder.Append(' ');
                _lineBuilder.Append('[');
                _lineBuilder.Append(currFrame.FrameName);
                _lineBuilder.Append("] ");
            }

            if (currFrame.Method != null)
            {
                _lineBuilder.Append(' ');

                if (currFrame.FrameName != null)
                {
                    _lineBuilder.Append('(');
                }

                if (currFrame.Method.Signature != null)
                {
                    _lineBuilder.Append(currFrame.Method.Signature);
                }
                else
                {
                    if (currFrame.Method.Type?.Name != null)
                    {
                        _lineBuilder.Append(currFrame.Method.Type.Name);
                        _lineBuilder.Append('.');
                    }
                    else
                    {
                        _lineBuilder.Append("UnknownType.");
                    }

                    if (currFrame.Method.Name != null)
                    {
                        _lineBuilder.Append(currFrame.Method.Name);
                        _lineBuilder.Append("(...)");
                    }
                    else
                    {
                        _lineBuilder.Append("UnknownMethod(...)");
                    }
                }

                if (currFrame.FrameName != null)
                {
                    _lineBuilder.Append(')');
                }

                //(string source, int line) = FileLineService.GetSourceFromManagedMethod(currFrame.Method, currFrame.InstructionPointer);

                // if (source is not null)
                // {
                //     _lineBuilder.Append(" [");
                //     _lineBuilder.Append(source);
                //     _lineBuilder.Append(" @ ");
                //     _lineBuilder.Append(line);
                //     _lineBuilder.Append(']');
                // }
            }

            return _lineBuilder.ToString();
        }

        private string GetRegisterOutput(ClrStackRoot stackRoot)
        {
            _lineBuilder.Clear();
            _lineBuilder.Append("        ");
            if (stackRoot.RegisterName != null || stackRoot.RegisterOffset != 0)
            {
                _lineBuilder.Append(stackRoot.RegisterName ?? "???");
                if (stackRoot.RegisterOffset > 0)
                {
                    _lineBuilder.Append('+');
                    _lineBuilder.Append(stackRoot.RegisterOffset.ToString("x"));
                }
                else if (stackRoot.RegisterOffset < 0)
                {
                    _lineBuilder.Append('-');
                    _lineBuilder.Append(Math.Abs(stackRoot.RegisterOffset).ToString("x"));
                }

                _lineBuilder.Append(':');
            }

            if (stackRoot.Address != 0)
            {
                _lineBuilder.Append(' ');
                _lineBuilder.Append(stackRoot.Address.ToString("x16"));
            }

            return _lineBuilder.ToString();
        }
    }
}