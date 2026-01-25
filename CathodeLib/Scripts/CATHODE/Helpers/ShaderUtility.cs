using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace CATHODE.ShaderTypes
{
    /// <summary>
    /// Utility class for accessing shader features via reflection.
    /// </summary>
    public static class ShaderUtility
    {
        private static readonly Assembly Assembly = typeof(ShaderUtility).Assembly;

        /// <summary>
        /// Gets all enum values from the specified enum type for a shader type class.
        /// </summary>
        public static List<string> GetShaderFunctionality(SHADER_LIST shaderType, ShaderIndexType indexType)
        {
            string shaderTypeName = shaderType.ToString();

            try
            {
                Type shaderTypeClass = Assembly.GetType($"CATHODE.ShaderTypes.{shaderTypeName}");
                
                if (shaderTypeClass == null)
                    shaderTypeClass = Assembly.GetTypes().FirstOrDefault(t => t.Namespace == "CATHODE.ShaderTypes" && t.Name == shaderTypeName);

                if (shaderTypeClass == null)
                    return new List<string>();

                Type enumType = shaderTypeClass.GetNestedType(indexType.ToString(), BindingFlags.Public | BindingFlags.Static);
                if (enumType == null || !enumType.IsEnum)
                    return new List<string>();

                return Enum.GetNames(enumType).ToList();
            }
            catch
            {
                return new List<string>();
            }
        }

        /// <summary>
        /// Gets all samplers for the specified shader type.
        /// </summary>
        public static List<string> GetSamplers(SHADER_LIST shaderType)
        {
            return GetShaderFunctionality(shaderType, ShaderIndexType.SAMPLERS);
        }

        /// <summary>
        /// Gets all features for the specified shader type.
        /// </summary>
        public static List<string> GetFeatures(SHADER_LIST shaderType)
        {
            return GetShaderFunctionality(shaderType, ShaderIndexType.FEATURES);
        }

        /// <summary>
        /// Gets all parameters for the specified shader type.
        /// </summary>
        public static List<string> GetParameters(SHADER_LIST shaderType)
        {
            return GetShaderFunctionality(shaderType, ShaderIndexType.PARAMETERS);
        }

        /// <summary>
        /// Gets the integer index value of a specific enum member for a shader type.
        /// </summary>
        public static int? GetShaderFunctionalityIndex(SHADER_LIST shaderType, ShaderIndexType indexType, string enumMemberName)
        {
            string shaderTypeName = shaderType.ToString();

            try
            {
                Type shaderTypeClass = Assembly.GetType($"CATHODE.ShaderTypes.{shaderTypeName}");
                
                if (shaderTypeClass == null)
                    shaderTypeClass = Assembly.GetTypes().FirstOrDefault(t => t.Namespace == "CATHODE.ShaderTypes" && t.Name == shaderTypeName);

                if (shaderTypeClass == null)
                    return null;

                Type enumType = shaderTypeClass.GetNestedType(indexType.ToString(), BindingFlags.Public | BindingFlags.Static);
                if (enumType == null || !enumType.IsEnum)
                    return null;

                if (!Enum.IsDefined(enumType, enumMemberName))
                    return null;

                object enumValue = Enum.Parse(enumType, enumMemberName);
                return Convert.ToInt32(enumValue);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Gets the parameter type for a specific shader parameter using the GetParameterType method on the shader class.
        /// </summary>
        public static UberShaderParameterType? GetParameterType(SHADER_LIST shaderType, string parameterName)
        {
            string shaderTypeName = shaderType.ToString();

            try
            {
                Type shaderTypeClass = Assembly.GetType($"CATHODE.ShaderTypes.{shaderTypeName}");
                
                if (shaderTypeClass == null)
                    shaderTypeClass = Assembly.GetTypes().FirstOrDefault(t => t.Namespace == "CATHODE.ShaderTypes" && t.Name == shaderTypeName);

                if (shaderTypeClass == null)
                    return null;

                Type parametersEnumType = shaderTypeClass.GetNestedType("PARAMETERS", BindingFlags.Public | BindingFlags.Static);
                if (parametersEnumType == null || !parametersEnumType.IsEnum)
                    return null;

                if (!Enum.IsDefined(parametersEnumType, parameterName))
                    return null;

                object parameterEnumValue = Enum.Parse(parametersEnumType, parameterName);

                MethodInfo getParameterTypeMethod = shaderTypeClass.GetMethod("GetParameterType", BindingFlags.Public | BindingFlags.Static);
                if (getParameterTypeMethod == null)
                    return null;

                ParameterInfo[] parameters = getParameterTypeMethod.GetParameters();
                if (parameters.Length != 1 || parameters[0].ParameterType != parametersEnumType)
                    return null;

                object result = getParameterTypeMethod.Invoke(null, new[] { parameterEnumValue });
                if (result is UberShaderParameterType parameterType)
                    return parameterType;

                return null;
            }
            catch
            {
                return null;
            }
        }
    }

    public enum ShaderIndexType
    {
        FEATURES,
        SAMPLERS,
        PARAMETERS
    }
}

