using System;
using System.Collections.Generic;
using CathodeLib;

namespace CATHODE.Assets
{
    public class AssetPAK
    {
        protected string _filePathPAK = "";
        protected string _filePathBIN = "";

        virtual public PAKReturnType Load() { return PAKReturnType.FAIL_FEATURE_IS_COMING_SOON; }
        virtual public List<string> GetFileNames() { return null; }
        virtual public int GetFilesize(string FileName) { return -1; }
        virtual public int GetFileIndex(string FileName) { throw new Exception("Tried to locate file in uninitialised PAK!"); }
        virtual public PAKReturnType AddFile(string PathToNewFile, int TrimFromPath = 0) { return PAKReturnType.FAIL_FEATURE_IS_COMING_SOON; }
        virtual public PAKReturnType DeleteFile(string FileName) { return PAKReturnType.FAIL_FEATURE_IS_COMING_SOON; }
        virtual public PAKReturnType ReplaceFile(string PathToNewFile, string FileName) { return PAKReturnType.FAIL_FEATURE_IS_COMING_SOON; }
        virtual public PAKReturnType ExportFile(string PathToExport, string FileName) { return PAKReturnType.FAIL_FEATURE_IS_COMING_SOON; }
        virtual public PAKReturnType Save() { return PAKReturnType.FAIL_FEATURE_IS_COMING_SOON; }
    }

    public enum PAKType
    {
        PAK2,
        PAK_TEXTURES,
        PAK_MODELS,
        PAK_SCRIPTS,
        PAK_MATERIALMAPS,
        PAK_SHADERS,
        UNRECOGNISED
    };
    public enum PAKReturnType
    {
        FAIL_COULD_NOT_ACCESS_FILE,
        FAIL_TRIED_TO_LOAD_VIRTUAL_ARCHIVE,
        FAIL_GENERAL_LOGIC_ERROR,
        FAIL_ARCHIVE_IS_NOT_EXCPETED_TYPE,
        FAIL_REQUEST_IS_UNSUPPORTED,
        FAIL_FEATURE_IS_COMING_SOON,
        FAIL_UNKNOWN,
        SUCCESS,
        SUCCESS_WITH_WARNINGS
    };

    public enum FileIdentifiers
    {
        ASSET_FILE = 14,

        SHADER_DATA = 3,
        MODEL_DATA = 19,
        TEXTURE_DATA = 45,

        //From ABOUT.TXT (unsure where used)
        STRING_FILE_VERSION = 6,
        ENTITY_FILE_VERSION = 171,
    }
}
