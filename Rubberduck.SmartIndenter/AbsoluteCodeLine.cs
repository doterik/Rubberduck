﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Rubberduck.SmartIndenter
{
    public class AbsoluteCodeLine
    {
        private const string StupidLineEnding = ": _";
        private static readonly Regex LineNumberRegex = new Regex(@"^(?<number>(-?\d+)|(&H[0-9A-F]{1,8}))(?<separator>:)?\s+(?<code>.*)", RegexOptions.ExplicitCapture | RegexOptions.IgnoreCase);
        private static readonly Regex EndOfLineCommentRegex = new Regex(@"^(?!(Rem\s)|('))(?<code>[^']*)(\s(?<comment>'.*))$", RegexOptions.ExplicitCapture | RegexOptions.IgnoreCase);
        private static readonly Regex ProcedureStartRegex = new Regex(@"^(Public\s|Private\s|Friend\s)?(Static\s)?(Sub|Function|Property\s(Let|Get|Set))\s", RegexOptions.IgnoreCase);
        
        private static readonly Regex PropertyStartRegex = new Regex(@"^(Public\s|Private\s|Friend\s)?(Static\s)?(Property\s(Let|Get|Set))\s", RegexOptions.IgnoreCase);

        private static readonly Regex ProcedureStartIgnoreRegex = new Regex(@"^[LR]?Set\s|^Let\s|^(Public|Private)\sDeclare\s(Function|Sub)", RegexOptions.IgnoreCase);
        private static readonly Regex ProcedureEndRegex = new Regex(@"^End\s(Sub|Function|Property)", RegexOptions.IgnoreCase);
        private static readonly Regex TypeEnumStartRegex = new Regex(@"^(Public\s|Private\s)?(Enum\s|Type\s)", RegexOptions.IgnoreCase);
        private static readonly Regex TypeEnumEndRegex = new Regex(@"^End\s(Enum|Type)", RegexOptions.IgnoreCase);
        private static readonly Regex InProcedureInRegex = new Regex(@"^(Else)?If\s.*\sThen$|^Else:?$|^Case\s|^With|^For\s|^Do$|^Do\s|^While$|^While\s|^Select Case", RegexOptions.IgnoreCase);
        private static readonly Regex InProcedureOutRegex = new Regex(@"^Else(If)?|^Case\s|^End With|^Next\s|^Next$|^Loop$|^Loop\s|^Wend$|^End If$|^End Select", RegexOptions.IgnoreCase);
        private static readonly Regex DeclarationRegex = new Regex(@"^(Dim|Const|Static|Public|Private)\s(.*(\sAs\s)?|_)", RegexOptions.IgnoreCase);
        private static readonly Regex PrecompilerInRegex = new Regex(@"^#(Else)?If\s.+Then$|^#Else$", RegexOptions.IgnoreCase);
        private static readonly Regex PrecompilerOutRegex = new Regex(@"^#ElseIf\s.+Then|^#Else$|#End\sIf$", RegexOptions.IgnoreCase);
        private static readonly Regex SingleLineElseIfRegex = new Regex(@"^ElseIf\s.*\sThen\s.*", RegexOptions.IgnoreCase);

        private readonly IIndenterSettings _settings;
        private int _lineNumber;
        private bool _lineNumberSeparator;
        private bool _numbered;
        private string _code;
        private readonly bool _stupidLineEnding;
        private readonly string[] _segments;
        private readonly StringLiteralAndBracketEscaper _escaper;

        public AbsoluteCodeLine(string code, IIndenterSettings settings) : this(code, settings, null) { }

        public AbsoluteCodeLine(string code, IIndenterSettings settings, AbsoluteCodeLine previous)
        {
            _settings = settings;
            Previous = previous;

            if (code.EndsWith(StupidLineEnding))
            {
                _code = code.Substring(0, code.Length - StupidLineEnding.Length);
                _stupidLineEnding = true;
            }
            else
            {
                _code = code;
            }
            
            Original = code;

            _escaper = new StringLiteralAndBracketEscaper(_code);
            _code = _escaper.EscapedString;

            ExtractLineNumber();
            ExtractEndOfLineComment();

            _segments = _code.Split(new[] { ": " }, StringSplitOptions.None);
        }

        private void ExtractLineNumber()
        {
            if (Previous == null || !Previous.HasContinuation)
            {
                var match = LineNumberRegex.Match(_code);
                if (match.Success)
                {
                    _code = match.Groups["code"].Value;
                    _numbered = true;
                    _lineNumberSeparator = match.Groups["separator"].Value != string.Empty;
                    var number = match.Groups["number"].Value;
                    if (!int.TryParse(number, out _lineNumber))
                    {
                        int.TryParse(number.Replace("&H", string.Empty), NumberStyles.HexNumber, 
                                     CultureInfo.InvariantCulture, out _lineNumber);
                    }                  
                }
            }
            _code = _code.Trim();
        }

        private void ExtractEndOfLineComment()
        {
            var match = EndOfLineCommentRegex.Match(_code);
            if (!match.Success)
            {
                EndOfLineComment = string.Empty;
                return;
            }
            _code = match.Groups["code"].Value.Trim();
            EndOfLineComment = match.Groups["comment"].Value.Trim();
        }

        public AbsoluteCodeLine Previous { get; }

        public string Original { get; }

        public string Escaped
        {
            get
            {
                // ReSharper disable LoopCanBeConvertedToQuery
                var output = Original;
                foreach (var item in _escaper.EscapedStrings)

                {
                    output = output.Replace(item, new string(StringLiteralAndBracketEscaper.StringPlaceholder, item.Length));
                }
                foreach (var item in _escaper.EscapedBrackets)
                {
                    output = output.Replace(item, new string(StringLiteralAndBracketEscaper.BracketPlaceholder, item.Length));
                }
                // ReSharper restore LoopCanBeConvertedToQuery
                return output;
            }
        }

        public string EndOfLineComment { get; private set; }

        public IEnumerable<string> Segments => _segments;

        public string ContinuationRebuildText
        {
            get
            {
                var output = ($"{_code} {EndOfLineComment}").Trim();
                return HasContinuation ? output.Substring(0, output.Length - 1) : output;
            }
        }

        public bool ContainsOnlyComment => _code.StartsWith("'") || _code.StartsWith("Rem ");

        public bool IsDeclaration => !IsEmpty && (!IsProcedureStart && !ProcedureStartIgnoreRegex.IsMatch(_code)) && DeclarationRegex.IsMatch(_code);

        public bool IsDeclarationContinuation { get; set; }

        public bool HasDeclarationContinuation
        {
            get
            {
                if (!string.IsNullOrEmpty(EndOfLineComment)
                        || ContainsOnlyComment
                        || IsProcedureStart
                        || !HasContinuation
                        || ProcedureStartIgnoreRegex.IsMatch(_code))
                {
                    return false;
                }

                return (IsDeclarationContinuation && Segments.Count() == 1)
                        || DeclarationRegex.IsMatch(Segments.Last());
            }
        }

        public bool HasContinuation => _code.Equals("_") || _code.EndsWith(" _") || EndOfLineComment.EndsWith(" _");

        public bool IsPrecompilerDirective => _code.TrimStart().StartsWith("#");

        public bool IsBareDebugPrintStatement => _code.StartsWith("Debug.Print");
        public bool IsBareDebugAssertStatement => _code.StartsWith("Debug.Assert");
        public bool IsBareStopStatement => _code.Equals("Stop");

        public int EnumOrTypeStarts
        {
            get { return _segments.Count(s => TypeEnumStartRegex.IsMatch(s)); }
        }

        public int EnumOrTypeEnds
        {
            get { return _segments.Count(s => TypeEnumEndRegex.IsMatch(s)); }
        }

        public bool IsProcedureStart
        {
            get { return _segments.Any(s => ProcedureStartRegex.IsMatch(s)) && !_segments.Any(s => ProcedureStartIgnoreRegex.IsMatch(s)); }
        }

        public bool IsPropertyStart
        {
            get { return _segments.Any(s => PropertyStartRegex.IsMatch(s)); }
        }
        public bool IsProcedureEnd
        {
            get { return _segments.Any(s => ProcedureEndRegex.IsMatch(s)); }
        }

        public bool IsEmpty => Original.Trim().Length == 0;

        public int NextLineIndents
        {
            get
            {
                var adjust = _settings.IndentCase && _segments.Any(s => s.TrimStart().StartsWith("Select Case")) ? 1 : 0;
                var ins = _segments.Count(s => InProcedureInRegex.IsMatch(s.Trim())) + (IsProcedureStart && _settings.IndentEntireProcedureBody ? 1 : 0) + adjust;

                ins += _segments.Count(s => SingleLineElseIfRegex.IsMatch(s));
                ins -= MultipleCaseAdjustment();

                if (_settings.IndentCompilerDirectives && PrecompilerInRegex.IsMatch(_segments[0].Trim()))
                {
                    ins += 1;
                }
                return ins - Outdents;
            }
        }

        public int Outdents
        {
            get
            {
                var adjust = _settings.IndentCase && _segments.Any(s => s.TrimStart().StartsWith("End Select")) ? 1 : 0;
                var outs = _segments.Count(s => InProcedureOutRegex.IsMatch(s.Trim())) + (IsProcedureEnd && _settings.IndentEntireProcedureBody ? 1 : 0) + adjust;

                outs -= MultipleCaseAdjustment();

                if (_settings.IndentCompilerDirectives && PrecompilerOutRegex.IsMatch(_segments[0].Trim()))
                {
                    outs += 1;
                }
                return outs;
            }
        }

        private int MultipleCaseAdjustment()
        {
            var cases = _segments.Count(s => s.StartsWith("Case "));
            return (cases > 1 && _segments.Length % 2 != 0) ? cases - 1 : 0;
        }

        public string Indent(int indents, bool atProcStart, bool absolute = false)
        {
            if (IsEmpty)
            {
                switch (_settings.EmptyLineHandlingMethod)
                {
                    case EmptyLineHandling.Ignore:
                        return Original;
                    case EmptyLineHandling.Remove:
                        return string.Empty;
                    case EmptyLineHandling.Indent:
                        return new string(' ', _settings.IndentSpaces * indents);
                }
            }

            if (ContainsOnlyComment && !_settings.AlignCommentsWithCode && !absolute)
            {
                return Original;
            }

            if ((IsPrecompilerDirective && _settings.ForceCompilerDirectivesInColumn1) ||
                (IsBareDebugPrintStatement && _settings.ForceDebugPrintInColumn1) ||
                (IsBareDebugAssertStatement && _settings.ForceDebugAssertInColumn1) ||
                (IsBareStopStatement && _settings.ForceStopInColumn1) ||
                (atProcStart && !_settings.IndentFirstCommentBlock && ContainsOnlyComment) ||
                (atProcStart && !_settings.IndentFirstDeclarationBlock && (IsDeclaration || IsDeclarationContinuation)))
            {
                indents = 0;
            }

            var number = _numbered ? _lineNumber.ToString(CultureInfo.InvariantCulture) : string.Empty;
            var separator = _lineNumberSeparator ? ":" : string.Empty;
            var gap = Math.Max((absolute ? indents : _settings.IndentSpaces * indents) - number.Length - separator.Length, number.Length + separator.Length > 0 ? 1 : 0);
            if (_settings.AlignDims && (IsDeclaration || IsDeclarationContinuation))
            {
                AlignDims(gap);
            }

            var code = string.Join(": ", _segments);
            code = string.Join(string.Empty, number, separator, new string(' ', gap), code);
            if (string.IsNullOrEmpty(EndOfLineComment))
            {
                return _escaper.UnescapeIndented(code + (_stupidLineEnding ? StupidLineEnding : string.Empty));
            }

            var position = Original.LastIndexOf(EndOfLineComment, StringComparison.Ordinal);
            switch (_settings.EndOfLineCommentStyle)
            {
                case EndOfLineCommentStyle.Absolute:
                    return _escaper.UnescapeIndented(string.Format("{0}{1}{2}{3}", code, new string(' ', Math.Max(position - code.Length, 1)),
                                                     EndOfLineComment, _stupidLineEnding ? StupidLineEnding : string.Empty));
                case EndOfLineCommentStyle.SameGap:
                    var uncommented = Original.Substring(0, position - 1);
                    return _escaper.UnescapeIndented(string.Format("{0}{1}{2}{3}", code, new string(' ', uncommented.Length - uncommented.TrimEnd().Length + 1), 
                                                     EndOfLineComment, _stupidLineEnding ? StupidLineEnding : string.Empty));
                case EndOfLineCommentStyle.StandardGap:
                    return _escaper.UnescapeIndented(string.Format("{0}{1}{2}{3}", code, new string(' ', _settings.IndentSpaces * 2), EndOfLineComment,
                                                     _stupidLineEnding ? StupidLineEnding : string.Empty));
                case EndOfLineCommentStyle.AlignInColumn:
                    var align = _settings.EndOfLineCommentColumnSpaceAlignment - code.Length;
                    return _escaper.UnescapeIndented(string.Format("{0}{1}{2}{3}", code, new string(' ', Math.Max(align - 1, 1)), EndOfLineComment,
                                                     _stupidLineEnding ? StupidLineEnding : string.Empty));
                default:
                    throw new InvalidEnumArgumentException();
            }
        }

        public override string ToString()
        {
            return Original;
        }

        private void AlignDims(int postition)
        {
            if (_segments[0].Trim().StartsWith("As "))
            {
                _segments[0] = string.Format("{0}{1}", new string(' ', _settings.AlignDimColumn - postition - 1), _segments[0].Trim());
                return;
            }
            var alignTokens = _segments[0].Split(new[] { " As " }, StringSplitOptions.None);
            if (alignTokens.Length <= 1)
            {
                return;
            }
            var gap = Math.Max(_settings.AlignDimColumn - postition - alignTokens[0].Trim().Length - 2, 0);
            _segments[0] = string.Format("{0}{1} As {2}", alignTokens[0].Trim(), new string(' ', gap),
                                         string.Join(" As ", alignTokens.Skip(1)));
        }
    }
}
