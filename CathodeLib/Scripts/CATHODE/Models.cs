using CATHODE.Scripting;
using CathodeLib;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace CATHODE
{
    /* Handles Cathode LEVEL_MODELS.PAK/MODELS_LEVEL.BIN files */
    public class Models : CathodeFile
    {
        public Models(string path) : base(path) { }

        #region FILE_IO
        /* Load the file */
        override protected bool LoadInternal()
        {
            using (BinaryReader reader = new BinaryReader(File.OpenRead(_filepath)))
            {
                
            }
            return true;
        }

        /* Save the file */
        override protected bool SaveInternal()
        {
            using (BinaryWriter stream = new BinaryWriter(File.OpenWrite(_filepath)))
            {

            }
            return true;
        }
        #endregion

        #region ACCESSORS

        #endregion

        #region HELPERS

        #endregion

        #region STRUCTURES

        #endregion
    }
}