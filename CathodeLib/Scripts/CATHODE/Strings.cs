using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Linq;
using System.Windows.Media.Imaging;
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
using UnityEngine;
#else
using System.Numerics;
#endif

namespace CATHODE
{
    /* DATA/TEXT/**//**.TXT */
    public class Strings : CathodeFile
    {
        public List<Str> Entries = new List<Str>();
        public static new Implementation Implementation = Implementation.CREATE | Implementation.LOAD | Implementation.SAVE;
        public Strings(string path) : base(path) { }

        #region FILE_IO
        override protected bool LoadInternal()
        {
            CurrentReadState state = CurrentReadState.NONE;
            string content = File.ReadAllText(_filepath);
            Str current = new Str();
            for (int i = 0; i < content.Length; i++)
            {
                switch (state)
                {
                    case CurrentReadState.READING_ID:
                        current.id += content[i];
                        break;
                    case CurrentReadState.READING_VALUE:
                        current.value += content[i];
                        break;
                }

                switch (content[i])
                {
                    case '[':
                        state = CurrentReadState.READING_ID;
                        break;
                    case ']':
                        state = CurrentReadState.NONE;
                        break;
                    case '{':
                        state = CurrentReadState.READING_VALUE;
                        break;
                    case '}':
                        state = CurrentReadState.NONE;
                        current.id = current.id.Substring(0, current.id.Length - 1);
                        current.value = current.value.Substring(0, current.value.Length - 1);
                        Entries.Add(current);
                        current = new Str();
                        break;
                }
            }
            return true;
        }

        override protected bool SaveInternal()
        {
            string content = "";
            foreach (Str entry in Entries)
            {
                content += "[" + entry.id + "]\n\n{" + entry.value + "}\n\n";
            }
            File.WriteAllText(_filepath, content);
            return true;
        }
        #endregion

        #region STRUCTURES
        public class Str
        {
            public string id = "";
            public string value = "";
        }
        private enum CurrentReadState
        {
            NONE,
            READING_ID,
            READING_VALUE,
        }
        #endregion
    }
}