using System;
using System.Collections.Generic;

namespace CATHODE
{
    public class CathodePAK
    {
        protected string FilePathPAK = "";
        protected string FilePathBIN = "";

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
}
