﻿//------------------------------------------------------------------------------
// <copyright file="EditorClassifier.cs" company="Company">
//     Copyright (c) Company.  All rights reserved.
// </copyright>
//------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Classification;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using NLog;

namespace CoCo
{
    /// <summary>
    /// Classifier that classifies all text as an instance of the "EditorClassifier" classification type.
    /// </summary>
    internal class EditorClassifier : IClassifier
    {
        private readonly IClassificationType _localFieldType;
        private readonly IClassificationType _namespaceType;
        private readonly IClassificationType _parameterType;

#if DEBUG

        // NOTE: Logger is thread-safe
        private static readonly Logger _logger;

        static EditorClassifier()
        {
            NLog.Initialize();
            _logger = LogManager.GetLogger(nameof(_logger));
        }

#endif

        /// <summary>
        /// Initializes a new instance of the <see cref="EditorClassifier"/> class.
        /// </summary>
        /// <param name="registry">Classification registry.</param>
        internal EditorClassifier(IClassificationTypeRegistryService registry)
        {
            //TODO: send ITextBuffer?
            _localFieldType = registry.GetClassificationType(Names.LocalFieldName);
            _namespaceType = registry.GetClassificationType(Names.NamespaceName);
            _parameterType = registry.GetClassificationType(Names.ParameterName);
        }

        #region IClassifier

#pragma warning disable 67

        /// <summary>
        /// An event that occurs when the classification of a span of text has changed.
        /// </summary>
        /// <remarks>
        /// This event gets raised if a non-text change would affect the classification in some way,
        /// for example typing /* would cause the classification to change in C# without directly
        /// affecting the span.
        /// </remarks>
        public event EventHandler<ClassificationChangedEventArgs> ClassificationChanged;

#pragma warning restore 67

        /// <summary>
        /// Gets all the <see cref="ClassificationSpan"/> objects that intersect with the given range
        /// of text.
        /// </summary>
        /// <remarks>
        /// This method scans the given SnapshotSpan for potential matches for this classification.
        /// In this instance, it classifies everything and returns each span as a new ClassificationSpan.
        /// </remarks>
        /// <param name="span">The span currently being classified.</param>
        /// <returns>
        /// A list of ClassificationSpans that represent spans identified to be of this classification.
        /// </returns>
        public IList<ClassificationSpan> GetClassificationSpans(SnapshotSpan span)
        {
#if DEBUG
            _logger.Info("Handle span that start position is={0} and end position is={1}", span.Start.Position, span.End.Position);
#endif
            var result = new List<ClassificationSpan>();

            // NOTE: Workspace can be null for "Using directive is unnecessary". Also workspace can
            // be null when solution/project failed to load and VS gave some reasons of it
            Workspace workspace = span.Snapshot.TextBuffer.GetWorkspace();
            Document document = workspace.GetDocument(span.Snapshot.AsText());

            // TODO:
            SemanticModel semanticModel = document.GetSemanticModelAsync().Result;
            SyntaxTree syntaxTree = semanticModel.SyntaxTree;

            TextSpan textSpan = new TextSpan(span.Start.Position, span.Length);
            var classifiedSpans = Classifier.GetClassifiedSpans(semanticModel, textSpan, workspace)
                .Where(item => item.ClassificationType == "identifier");

            CompilationUnitSyntax unitCompilation = syntaxTree.GetCompilationUnitRoot();
            foreach (var item in classifiedSpans)
            {
                SyntaxNode node = unitCompilation.FindNode(item.TextSpan).SpecificHandle();

                // NOTE: Some kind of nodes, for example ArgumentSyntax, need specific handling
                ISymbol symbol = semanticModel.GetSymbolInfo(node).Symbol ?? semanticModel.GetDeclaredSymbol(node);
                if (symbol == null)
                {
                    // TODO: Log information about the node and semantic model, because semantic model
                    // didn't retrive information from node in this case
                    continue;
                }
                switch (symbol.Kind)
                {
                    case SymbolKind.Alias:
                    case SymbolKind.ArrayType:
                    case SymbolKind.Assembly:
                    case SymbolKind.DynamicType:
                    case SymbolKind.ErrorType:
                    case SymbolKind.Event:
                    case SymbolKind.Field:
                    case SymbolKind.Label:
                    case SymbolKind.Method:
                    case SymbolKind.NetModule:
                    case SymbolKind.NamedType:
                    case SymbolKind.PointerType:
                    case SymbolKind.Property:
                    case SymbolKind.RangeVariable:
                    case SymbolKind.TypeParameter:
                    case SymbolKind.Preprocessing:
                    case SymbolKind.Discard:
                        // TODO: Log input type and span positions here
                        break;

                    case SymbolKind.Local:
                        result.Add(CreateClassificationSpan(span.Snapshot, item.TextSpan, _localFieldType));
                        break;

                    case SymbolKind.Namespace:
                        result.Add(CreateClassificationSpan(span.Snapshot, item.TextSpan, _namespaceType));
                        break;

                    case SymbolKind.Parameter:
                        result.Add(CreateClassificationSpan(span.Snapshot, item.TextSpan, _parameterType));
                        break;

                    default:
                        break;
                }
            }

            return result;
        }

        private ClassificationSpan CreateClassificationSpan(ITextSnapshot snapshot, TextSpan span, IClassificationType type) =>
            new ClassificationSpan(new SnapshotSpan(snapshot, span.Start, span.Length), type);

        #endregion
    }

    // TODO: it's temporary name
    internal static class Help
    {
        // TODO: it's temporary name
        public static SyntaxNode SpecificHandle(this SyntaxNode node) =>
            node.Kind() == SyntaxKind.Argument ? (node as ArgumentSyntax).Expression : node;

        //TODO: Check behavior for document that isn't including in solution
        public static Document GetDocument(this Workspace workspace, SourceText text)
        {
            DocumentId id = workspace.GetDocumentIdInCurrentContext(text.Container);
            if (id == null)
            {
                return null;
            }

            return !workspace.CurrentSolution.ContainsDocument(id)
                ? workspace.CurrentSolution.WithDocumentText(id, text, PreservationMode.PreserveIdentity).GetDocument(id)
                : workspace.CurrentSolution.GetDocument(id);
        }

        public static Document GetOpenDocumentInCurrentContextWithChanges(this SourceText text)
        {
            if (Workspace.TryGetWorkspace(text.Container, out var workspace))
            {
                var id = workspace.GetDocumentIdInCurrentContext(text.Container);
                if (id == null || !workspace.CurrentSolution.ContainsDocument(id))
                {
                    return null;
                }

                var sol = workspace.CurrentSolution.WithDocumentText(id, text, PreservationMode.PreserveIdentity);
                return sol.GetDocument(id);
            }

            return null;
        }
    }
}