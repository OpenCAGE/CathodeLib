using System;
using System.Collections.Generic;
using System.Text;

namespace CATHODE.Commands
{
    /* A parameter which consists of a name and data */
    [Serializable]
    public class Parameter
    {
        public Parameter(string name, ParameterData data)
        {
            shortGUID = ShortGuidUtils.Generate(name);
            content = data;
        }
        public Parameter(ShortGuid id, ParameterData data)
        {
            shortGUID = id;
            content = data;
        }

        public ShortGuid shortGUID; //The ID of the param in the entity
        public ParameterData content = null;
    }
}
