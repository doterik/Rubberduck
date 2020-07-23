﻿using System.Collections.Generic;
using Rubberduck.Parsing.Grammar;
using Rubberduck.VBEditor;

namespace Rubberduck.Parsing.Annotations
{
    /// <summary>
    /// @ModuleDescription annotation, indicates the presence of a VB_Description module attribute value providing a docstring for the module. Use the quick-fixes to "Rubberduck Opportunities" code inspections to synchronize annotations and attributes.
    /// </summary>
    /// <parameter name="DocString" type="Text">
    /// This string literal parameter does not support expressions and/or multiline inputs. The string literal is used as-is as the value of the hidden member attribute.
    /// </parameter>
    /// <remarks>
    /// The @Description annotation cannot be used at module level. This separate annotation disambiguates any potential scoping issues that present themselves when the same name is used for both scopes.
    /// This documentation string appears in the VBE's own Object Browser, as well as in various Rubberduck UI elements.
    /// </remarks>
    /// <example>
    /// <module name="Class1" type="Class Module">
    /// <![CDATA[
    /// '@ModuleDescription("Represents an object responsible for doing something.")
    /// Option Explicit
    ///
    /// Public Sub DoSomething()
    /// End Sub
    /// ]]>
    /// </module>
    /// </example>
    public sealed class ModuleDescriptionAnnotation : DescriptionAttributeAnnotationBase
    {
        public ModuleDescriptionAnnotation()
            : base("ModuleDescription", AnnotationTarget.Module, "VB_Description", 1)
        {}
    }
}