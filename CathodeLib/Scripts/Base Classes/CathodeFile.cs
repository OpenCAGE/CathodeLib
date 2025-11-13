using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CathodeLib
{
    public class CathodeFile
    {
        public Action<string> OnLoadBegin;
        public Action<string> OnLoadSuccess;

        public Action<string> OnSaveBegin;
        public Action<string> OnSaveSuccess;

        public string Filepath { get { return _filepath; } }
        protected string _filepath = "";

        public bool Loaded { get { return _loaded; } }
        protected bool _loaded = false;

        public static Implementation Implementation = Implementation.NONE;

        /// <summary>
        /// Override this property to return true if the derived class handles loading manually in its constructor
        /// </summary>
        protected virtual bool HandlesLoadingManually => false;

        public CathodeFile(string filepath)
        {
            _filepath = filepath;

            if (!HandlesLoadingManually)
                _loaded = Load();
        }

        public CathodeFile(MemoryStream stream, string virtualPath = "")
        {
            _filepath = virtualPath;
            if (!HandlesLoadingManually)
                _loaded = Load(stream);
        }

        public CathodeFile(byte[] data, string virtualPath = "")
        {
            _filepath = virtualPath;
            if (!HandlesLoadingManually)
            {
                using (var stream = new MemoryStream(data))
                {
                    _loaded = Load(stream);
                }
            }
        }

        #region EXTERNAL_FUNCS
        /// <summary>
        /// Try and load the file, if it exists
        /// </summary>
        protected bool Load()
        {
            return Load(File.Exists(_filepath) ? new MemoryStream(File.ReadAllBytes(_filepath)) : null);
        }

        /// <summary>
        /// Load from MemoryStream
        /// </summary>
        protected bool Load(MemoryStream stream)
        {
            OnLoadBegin?.Invoke(_filepath);
            if (stream == null || stream.Length == 0) return false;

#if !CATHODE_FAIL_HARD
            try
            {
#endif
                if (LoadInternal(stream))
                {
                    OnLoadSuccess?.Invoke(_filepath);
                    return true;
                }
                else return false;
#if !CATHODE_FAIL_HARD
            }
            catch
            {
                return false;
            }
#endif
        }

        /// <summary>
        /// Save the file back to its original filepath
        /// </summary>
        public bool Save()
        {
            OnSaveBegin?.Invoke(_filepath);
            if (_filepath == "") return false;

#if !CATHODE_FAIL_HARD
            try
            {
#endif
                if (SaveInternal())
                {
                    OnSaveSuccess?.Invoke(_filepath);
                    return true;
                }
                else return false;
#if !CATHODE_FAIL_HARD
            }
            catch
            {
               return false;
            }
#endif
        }

        /// <summary>
        /// Save the file to a new path, and optionally remember it for future saves
        /// </summary>
        public bool Save(string path = "", bool updatePath = true)
        {
            string origFilepath = updatePath && path != "" ? path : _filepath;
            if (path != "") _filepath = path;
            bool saved = Save();
            if (!updatePath) _filepath = origFilepath;
            return saved;
        }
        #endregion

        #region TO_OVERRIDE
        /// <summary>
        /// Virtual function to override in inherited classes for loading the file
        /// </summary>
        protected virtual bool LoadInternal(MemoryStream stream)
        {
            Console.WriteLine("WARNING: This class does not implement loading functionality!");
            return false;
        }

        /// <summary>
        /// Virtual function to override in inherited classes for saving the file
        /// </summary>
        protected virtual bool SaveInternal()
        {
            Console.WriteLine("WARNING: This class does not implement saving functionality!");
            return false;
        }
        #endregion
    }

    [Flags]
    public enum Implementation
    {
        NONE = 1,
        CREATE = 2,
        LOAD = 4,
        SAVE = 8,
    }
}
