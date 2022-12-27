using System;
using System.Collections.Generic;
using System.Text;

namespace CATHODE.Commands
{
    /* A parameter which consists of a name and data, with a variant for how it is used */
    [Serializable]
    public class Parameter
    {
        public Parameter(string name, ParameterData data, ParameterVariant var = ParameterVariant.PARAMETER)
        {
            shortGUID = ShortGuidUtils.Generate(name);
            content = data;
            variant = var;
        }
        public Parameter(ShortGuid id, ParameterData data, ParameterVariant var = ParameterVariant.PARAMETER)
        {
            shortGUID = id;
            content = data;
            variant = var;
        }

        public ShortGuid shortGUID; //The ID of the param in the entity
        public ParameterData content = null;
        public ParameterVariant variant = ParameterVariant.PARAMETER;
    }
}
