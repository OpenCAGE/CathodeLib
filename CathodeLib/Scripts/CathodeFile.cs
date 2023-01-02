using System;
using System.Collections.Generic;
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

        protected virtual bool Load()
        {
            Console.WriteLine("WARNING: This class does not implement loading functionality!");
            return false;
        }

        public virtual bool Save()
        {
            Console.WriteLine("WARNING: This class does not implement saving functionality!");
            return false;
        }
    }
}
