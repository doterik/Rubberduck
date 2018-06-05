﻿using System.Collections.Generic;
using System.Linq;
using Rubberduck.Parsing.Symbols;
using Rubberduck.Parsing.VBA;
using Rubberduck.VBEditor;

namespace Rubberduck.Refactorings.Rename
{
    public class RenameModel
    {
        private readonly IList<Declaration> _declarations;
        public IEnumerable<Declaration> Declarations => _declarations;

        private Declaration _target;
        public Declaration Target
        {
            get => _target;
            set => _target = value;
        }

        public QualifiedSelection Selection { get; }

        public RubberduckParserState State { get; }

        public string NewName { get; set; }

        public RenameModel(RubberduckParserState state, QualifiedSelection selection)
        {
            State = state;
            _declarations = state.AllDeclarations.ToList();
            Selection = selection;

            AcquireTarget(out _target, Selection);
        }

        private void AcquireTarget(out Declaration target, QualifiedSelection selection)
        {
            target = _declarations
                .Where(item => item.IsUserDefined)
                .FirstOrDefault(item => item.IsSelected(selection) || item.References.Any(r => r.IsSelected(selection)));
        }
    }
}
