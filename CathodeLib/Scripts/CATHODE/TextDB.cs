using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Linq;
using System.Text;
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
using UnityEngine;
#else
using System.Numerics;
#endif

namespace CATHODE
{
    /* DATA/TEXT/**//**.TXT */
    public class TextDB : CathodeFile
    {
        public Dictionary<string, string> Entries = new Dictionary<string, string>();
        public static new Implementation Implementation = Implementation.CREATE | Implementation.LOAD | Implementation.SAVE;
        public TextDB(string path) : base(path) { }

        #region FILE_IO
        override protected bool LoadInternal()
        {
            CurrentReadState state = CurrentReadState.NONE;
            string content = File.ReadAllText(_filepath);
            string id = "";
            string value = "";
            bool isInInternalBracket = false;
            for (int i = 0; i < content.Length; i++)
            {
                switch (state)
                {
                    case CurrentReadState.READING_ID:
                        id += content[i];
                        if (content[i] == ']') state = CurrentReadState.NONE;
                        break;
                    case CurrentReadState.READING_VALUE:
                        value += content[i];
                        if (content[i] == '{') isInInternalBracket = true;
                        break;
                    case CurrentReadState.NONE:
                        if (content[i] == '[') state = CurrentReadState.READING_ID;
                        break;
                }

                switch (content[i])
                {
                    case '{':
                        if (isInInternalBracket) break;
                        state = CurrentReadState.READING_VALUE;
                        break;
                    case '}':
                        if (isInInternalBracket)
                        {
                            isInInternalBracket = false;
                            break;
                        }
                        state = CurrentReadState.NONE;

                        id = id.Substring(0, id.Length - 1);
                        value = value.Substring(0, value.Length - 1);
                        if (!Entries.ContainsKey(id))
                            Entries.Add(id, value);

                        id = "";
                        value = "";
                        isInInternalBracket = false;
                        break;
                }
            }
            return true;
        }

        override protected bool SaveInternal()
        {
            string content = "";
            foreach (KeyValuePair<string, string> entry in Entries)
            {
                content += "[" + entry.Key + "]\n{" + entry.Value + "}\n\n";
            }
            File.WriteAllText(_filepath, content, Encoding.Unicode);
            return true;
        }
        #endregion

        public override string ToString()
        {
            return Path.GetFileNameWithoutExtension(_filepath);
        }

        #region STRUCTURES
        private enum CurrentReadState
        {
            NONE,
            READING_ID,
            READING_VALUE,
        }
        #endregion
    }
}