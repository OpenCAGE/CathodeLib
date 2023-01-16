using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CathodeLib
{
    public class CathodeFile
    {
        public string Filepath { get { return _filepath; } }
        protected string _filepath = "";

        public bool Loaded { get { return _loaded; } }
        protected bool _loaded = false;

        public CathodeFile(string filepath)
        {
            _filepath = filepath;
            _loaded = Load();
        }

        #region EXTERNAL_FUNCS
        /* Try and load the file, if it exists */
        private bool Load()
        {
            if (!File.Exists(_filepath))
            {
                return false;
            }

#if !CATHODE_FAIL_HARD
            try
            {
#endif
            return LoadInternal();
#if !CATHODE_FAIL_HARD
            }
            catch
            {
                return false;
            }
#endif
        }

        /* Save the file back to its original filepath */
        public bool Save()
        {
#if !CATHODE_FAIL_HARD
            try
            {
#endif
            return SaveInternal();
#if !CATHODE_FAIL_HARD
            }
            catch
            {
               return false;
            }
#endif
        }

        /* Save the file to a new path, and optionally remember it for future saves */
        public bool Save(string path = "", bool updatePath = true)
        {
            if (path != "" && updatePath)
                _filepath = path;

            return Save();
        }
        #endregion

        #region TO_OVERRIDE
        /* Virtual function to override in inherited classes for loading the file */
        protected virtual bool LoadInternal()
        {
            Console.WriteLine("WARNING: This class does not implement loading functionality!");
            return false;
        }

        /* Virtual function to override in inherited classes for saving the file */
        protected virtual bool SaveInternal()
        {
            Console.WriteLine("WARNING: This class does not implement saving functionality!");
            return false;
        }
        #endregion
    }
}
