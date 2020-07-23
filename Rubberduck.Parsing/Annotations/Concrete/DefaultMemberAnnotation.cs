using System.Collections.Generic;
using System.Linq;
using Rubberduck.Parsing.Grammar;
using Rubberduck.VBEditor;
using Rubberduck.Resources.Registration;

namespace Rubberduck.Parsing.Annotations
{
    /// <summary>
    /// @DefaultMember annotation, indicates that the member should have a VB_UserMemId attribute value (0) making it the default member of that class. Use the quick-fixes to "Rubberduck Opportunities" code inspections to synchronize annotations and attributes.
    /// </summary>
    /// <example>
    /// <module name="Class1" type="Class Module">
    /// <![CDATA[
    /// Option Explicit
    /// Private InternalState As VBA.Collection
    ///
    /// @DefaultMember
    /// Public Property Get Item(ByVal Index As Variant) As Variant
    ///     Item = InternalState(Index)
    /// End Sub
    /// 
    /// 'if the default member is a property, only the Get accessor needs the attribute/annotation.
    /// Public Property Let Item(ByVal Index As Variant, ByVal Value As Variant)
    ///     InternalState(Index) = Value    
    /// End Sub
    /// 
    /// Private Sub Class_Initialize()
    ///     Set InternalState = New VBA.Collection
    /// End Sub
    /// ]]>
    /// </module>
    /// </example>
    public sealed class DefaultMemberAnnotation : FixedAttributeValueAnnotationBase
    {
        public DefaultMemberAnnotation()
            : base("DefaultMember", AnnotationTarget.Member, "VB_UserMemId", new[] { WellKnownDispIds.Value.ToString() })
        {
        }
    }
}
