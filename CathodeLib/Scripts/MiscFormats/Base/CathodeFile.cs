using System;
using System.Collections.Generic;
using System.Text;

namespace CATHODE.Misc
{
    public class CathodeFile
    {
        public string FilePath { get { return _filepath; } }
        protected string _filepath = "";

        public CathodeFile(string filepath)
        {
            _filepath = filepath;
            Load();
        }

        protected virtual void Load()
        {
            Console.WriteLine("WARNING: This class does not implement loading functionality!");
        }

        public virtual void Save()
        {
            Console.WriteLine("WARNING: This class does not implement saving functionality!");
        }
    }
}
