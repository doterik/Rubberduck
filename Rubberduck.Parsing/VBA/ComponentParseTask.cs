﻿using Antlr4.Runtime;
using Antlr4.Runtime.Misc;
using Antlr4.Runtime.Tree;
using NLog;
using Rubberduck.Parsing.Annotations;
using Rubberduck.Parsing.Grammar;
using Rubberduck.Parsing.Symbols;
using Rubberduck.VBEditor;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Rubberduck.Parsing.PreProcessing;
using Rubberduck.Parsing.Rewriter;
using Rubberduck.Parsing.Symbols.ParsingExceptions;
using Rubberduck.VBEditor.ComManagement;
using Rubberduck.VBEditor.SafeComWrappers.Abstract;

namespace Rubberduck.Parsing.VBA
{
    class ComponentParseTask
    {
        private readonly QualifiedModuleName _module;
        private readonly TokenStreamRewriter _rewriter;
        private readonly IAttributeParser _attributeParser;
        private readonly ISourceCodeHandler _sourceCodeHandler;
        private readonly IVBAPreprocessor _preprocessor;
        private readonly VBAModuleParser _parser;
        private readonly IProjectsProvider _projectsProvider;
        private readonly IModuleRewriterFactory _moduleRewriterFactory;

        public event EventHandler<ParseCompletionArgs> ParseCompleted;
        public event EventHandler<ParseFailureArgs> ParseFailure;
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        private readonly Guid _taskId;

        public ComponentParseTask(QualifiedModuleName module, IVBAPreprocessor preprocessor, IAttributeParser attributeParser, ISourceCodeHandler sourceCodeHandler, IProjectsProvider projectsProvider, IModuleRewriterFactory moduleRewriterFactory,TokenStreamRewriter rewriter = null)
        {
            _taskId = Guid.NewGuid();

            _moduleRewriterFactory = moduleRewriterFactory;
            _attributeParser = attributeParser;
            _sourceCodeHandler = sourceCodeHandler;
            _preprocessor = preprocessor;
            _module = module;
            _rewriter = rewriter;
            _projectsProvider = projectsProvider;
            _parser = new VBAModuleParser();
        }
        
        public void Start(CancellationToken cancellationToken)
        {
            try
            {
                Logger.Trace($"Starting ParseTaskID {_taskId} on thread {Thread.CurrentThread.ManagedThreadId}.");

                var tokenStream = RewriteAndPreprocess(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();  

                var codePaneParseResults = ParseInternal(_module.ComponentName, tokenStream);
                var codePaneRewriter = _moduleRewriterFactory.CodePaneRewriter(_module, codePaneParseResults.tokenStream);
                cancellationToken.ThrowIfCancellationRequested();

                // temporal coupling... comments must be acquired before we walk the parse tree for declarations
                // otherwise none of the annotations get associated to their respective Declaration
                var commentsAndAnnotation = CommentsAndAnnotations(_module, codePaneParseResults.tree);
                cancellationToken.ThrowIfCancellationRequested();

                var attributesPassParseResults = RunAttributesPass(cancellationToken);
                var attributesRewriter = _moduleRewriterFactory.AttributesRewriter(_module, attributesPassParseResults.tokenStream ?? tokenStream);
                cancellationToken.ThrowIfCancellationRequested();

                var completedHandler = ParseCompleted;
                if (completedHandler != null && !cancellationToken.IsCancellationRequested)
                    completedHandler.Invoke(this, new ParseCompletionArgs
                    {
                        ParseTree = codePaneParseResults.tree,
                        AttributesTree = attributesPassParseResults.tree,
                        CodePaneRewriter = codePaneRewriter,
                        AttributesRewriter = attributesRewriter,
                        Attributes = attributesPassParseResults.attributes,
                        Comments = commentsAndAnnotation.Comments,
                        Annotations = commentsAndAnnotation.Annotations
                    });
            }
            catch (COMException exception)
            {
                Logger.Error(exception, $"COM Exception thrown in thread {Thread.CurrentThread.ManagedThreadId} while parsing module {_module.ComponentName}, ParseTaskID {_taskId}.");
                var failedHandler = ParseFailure;
                failedHandler?.Invoke(this, new ParseFailureArgs
                {
                    Cause = exception
                });
            }
            catch (PreprocessorSyntaxErrorException syntaxErrorException)
            {
                var parsePassText = syntaxErrorException.ParsePass == ParsePass.CodePanePass
                    ? "code pane"
                    : "exported";
                Logger.Error($"Syntax error while preprocessing; offending token '{syntaxErrorException.OffendingSymbol.Text}' at line {syntaxErrorException.LineNumber}, column {syntaxErrorException.Position} in the {parsePassText} version of module {_module.ComponentName}.");
                Logger.Debug(syntaxErrorException, $"SyntaxErrorException thrown in thread {Thread.CurrentThread.ManagedThreadId}, ParseTaskID {_taskId}.");

                ReportException(syntaxErrorException);
            }
            catch (ParsePassSyntaxErrorException syntaxErrorException)
            {
                var parsePassText = syntaxErrorException.ParsePass == ParsePass.CodePanePass
                    ? "code pane"
                    : "exported";
                Logger.Error($"Syntax error; offending token '{syntaxErrorException.OffendingSymbol.Text}' at line {syntaxErrorException.LineNumber}, column {syntaxErrorException.Position} in the {parsePassText} version of module {_module.ComponentName}.");
                Logger.Debug(syntaxErrorException, $"SyntaxErrorException thrown in thread {Thread.CurrentThread.ManagedThreadId}, ParseTaskID {_taskId}.");

                ReportException(syntaxErrorException);
            }
            catch (SyntaxErrorException syntaxErrorException)
            {
                Logger.Error($"Syntax error; offending token '{syntaxErrorException.OffendingSymbol.Text}' at line {syntaxErrorException.LineNumber}, column {syntaxErrorException.Position} in module {_module.ComponentName}.");
                Logger.Debug(syntaxErrorException, $"SyntaxErrorException thrown in thread {Thread.CurrentThread.ManagedThreadId}, ParseTaskID {_taskId}.");

                ReportException(syntaxErrorException);
            }
            catch (OperationCanceledException exception)
            {
                //We report this, so that the calling code knows that the operation actually has been cancelled.
                ReportException(exception);
            }
            catch (Exception exception)
            {
                Logger.Error(exception, $" Unexpected exception thrown in thread {Thread.CurrentThread.ManagedThreadId} while parsing module {_module.ComponentName}, ParseTaskID {_taskId}.");

                ReportException(exception);
            }
        }

        private (IEnumerable<CommentNode> Comments, IEnumerable<IAnnotation> Annotations) CommentsAndAnnotations(QualifiedModuleName module, IParseTree tree)
        {
            var commentListener = new CommentListener();
            var annotationListener = new AnnotationListener(new VBAParserAnnotationFactory(), _module);
            var combinedListener = new CombinedParseTreeListener(new IParseTreeListener[] {commentListener, annotationListener});
            ParseTreeWalker.Default.Walk(combinedListener, tree);
            var comments = QualifyAndUnionComments(module, commentListener.Comments, commentListener.RemComments);
            var annotations = annotationListener.Annotations;
            return (comments, annotations);
        }

        private void ReportException(Exception exception)
        {
            var failedHandler = ParseFailure;
            failedHandler?.Invoke(this, new ParseFailureArgs
            {
                Cause = exception
            });
        }

        private (IParseTree tree, ITokenStream tokenStream, IDictionary<(string scopeIdentifier, DeclarationType scopeType), Attributes> attributes) RunAttributesPass(CancellationToken cancellationToken)
        {
            Logger.Trace($"ParseTaskID {_taskId} begins attributes pass.");
            var attributesParseResults = _attributeParser.Parse(_module, cancellationToken);
            Logger.Trace($"ParseTaskID {_taskId} finished attributes pass.");
            return attributesParseResults;
        }

        private static string GetCode(ICodeModule codeModule)
        {
            var lines = codeModule.CountOfLines;
            if (lines == 0)
            {
                return string.Empty;
            }

            var codeLines = codeModule.GetLines(1, lines);
            var code = string.Concat(codeLines);

            return code;
        }

        private CommonTokenStream RewriteAndPreprocess(CancellationToken cancellationToken)
        {
            var code = _rewriter?.GetText();
            var component = _projectsProvider.Component(_module);
            if (code == null)
            {
                using (var codeModule = component.CodeModule)
                {
                    code = string.Join(Environment.NewLine, GetCode(codeModule));
                }
            }
 
            var tokenStreamProvider = new SimpleVBAModuleTokenStreamProvider();
            var tokens = tokenStreamProvider.Tokens(code);
            using (var project = component.ParentProject)
            {
                _preprocessor.PreprocessTokenStream(project, _module.Name, tokens, new PreprocessorExceptionErrorListener(_module.ComponentName, ParsePass.CodePanePass), cancellationToken);
            }
            return tokens;
        }

        private (IParseTree tree, ITokenStream tokenStream) ParseInternal(string moduleName, CommonTokenStream tokenStream)
        {
            //var errorNotifier = new SyntaxErrorNotificationListener();
            //errorNotifier.OnSyntaxError += ParserSyntaxError;
            return _parser.Parse(moduleName, tokenStream, new MainParseExceptionErrorListener(moduleName, ParsePass.CodePanePass));
        }

        private IEnumerable<CommentNode> QualifyAndUnionComments(QualifiedModuleName qualifiedName, IEnumerable<VBAParser.CommentContext> comments, IEnumerable<VBAParser.RemCommentContext> remComments)
        {
            var commentNodes = comments.Select(comment => new CommentNode(comment.GetComment(), Tokens.CommentMarker, new QualifiedSelection(qualifiedName, comment.GetSelection())));
            var remCommentNodes = remComments.Select(comment => new CommentNode(comment.GetComment(), Tokens.Rem, new QualifiedSelection(qualifiedName, comment.GetSelection())));
            var allCommentNodes = commentNodes.Union(remCommentNodes);
            return allCommentNodes;
        }
        
        public class ParseCompletionArgs
        {
            public IModuleRewriter CodePaneRewriter { get; internal set; }
            public IModuleRewriter AttributesRewriter { get; internal set; }
            public IParseTree ParseTree { get; internal set; }
            public IParseTree AttributesTree { get; internal set; }
            public IDictionary<(string scopeIdentifier, DeclarationType scopeType), Attributes> Attributes { get; internal set; }
            public IEnumerable<CommentNode> Comments { get; internal set; }
            public IEnumerable<IAnnotation> Annotations { get; internal set; }
        }

        public class ParseFailureArgs
        {
            public Exception Cause { get; internal set; }
        }

        private class CommentListener : VBAParserBaseListener
        {
            private readonly IList<VBAParser.RemCommentContext> _remComments = new List<VBAParser.RemCommentContext>();
            public IEnumerable<VBAParser.RemCommentContext> RemComments => _remComments;

            private readonly IList<VBAParser.CommentContext> _comments = new List<VBAParser.CommentContext>();
            public IEnumerable<VBAParser.CommentContext> Comments => _comments;

            public override void ExitRemComment([NotNull] VBAParser.RemCommentContext context)
            {
                _remComments.Add(context);
            }

            public override void ExitComment([NotNull] VBAParser.CommentContext context)
            {
                _comments.Add(context);
            }
        }
    }
}
