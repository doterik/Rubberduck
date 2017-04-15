using Rubberduck.Inspections.Abstract;
using Rubberduck.Parsing.Inspections.Abstract;
using Rubberduck.Parsing.Inspections.Resources;
using Rubberduck.Parsing.Symbols;
using Rubberduck.VBEditor;

namespace Rubberduck.Inspections.Results
{
    public class ImplicitDefaultMemberAssignmentInspectionResult : InspectionResultBase
    {
        private readonly string _assignedIdentifier;
        private readonly string _defaultMemberOf;

        public ImplicitDefaultMemberAssignmentInspectionResult(IInspection inspection, IdentifierReference reference, QualifiedMemberName? qualifiedName)
            :base(inspection, reference.QualifiedModuleName, qualifiedName, reference.Context)
        {
            var module = reference.Declaration.AsTypeDeclaration as ClassModuleDeclaration;
            System.Diagnostics.Debug.Assert(module != null);
            _assignedIdentifier = reference.Declaration.IdentifierName;
            _defaultMemberOf = module.IdentifierName;
        }

        public override string Description
        {
            get
            {
                return string.Format(InspectionsUI.ImplicitDefaultMemberAssignmentInspectionResultFormat, _assignedIdentifier, _defaultMemberOf);
            }
        }
    }
}