using CATHODE.Scripting.Internal;
using System;
using System.Collections.Generic;
using System.Text;

namespace CATHODE.Scripting
{
    /// <summary>
    /// A parameter which consists of a name and data, with a variant for how it is used
    /// </summary>
    [Serializable]
    public class Parameter
    {
        public Parameter(string name, ParameterData data, ParameterVariant var = ParameterVariant.PARAMETER)
        {
            this.name = ShortGuidUtils.Generate(name);
            content = data;
            variant = var;
        }
        public Parameter(ShortGuid id, ParameterData data, ParameterVariant var = ParameterVariant.PARAMETER)
        {
            name = id;
            content = data;
            variant = var;
        }

        public ShortGuid name; 
        public ParameterData content = null;
        public ParameterVariant variant = ParameterVariant.PARAMETER;

        ~Parameter()
        {
            content = null;
        }
    }
}
