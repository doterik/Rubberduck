﻿using Rubberduck.VBEditor.SafeComWrappers.Abstract;
using VB = Microsoft.Vbe.Interop.VB6;

namespace Rubberduck.VBEditor.SafeComWrappers.VB6
{
    public class VB6AddInProvider : ISafeComWrapperProvider<IAddIn>
    {
        public bool CanProvideFor(object comObject)
        {
            return comObject is VB.AddIn;
        }

        public IAddIn Provide(object comObject)
        {
            return new AddIn((VB.AddIn)comObject);
        }
    }
}
