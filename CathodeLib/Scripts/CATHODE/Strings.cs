using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Linq;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
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
        public Dictionary<string, string> Entries = new Dictionary<string, string>();
        public static new Implementation Implementation = Implementation.CREATE | Implementation.LOAD | Implementation.SAVE;
        public Strings(string path) : base(path) { }

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
                        break;
                    case CurrentReadState.READING_VALUE:
                        value += content[i];
                        if (content[i] == '{') isInInternalBracket = true;
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
                        if (isInInternalBracket) break;
                        state = CurrentReadState.READING_VALUE;
                        break;
                    case '}':
                        if (isInInternalBracket) break;
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

                switch (state)
                {
                    case CurrentReadState.READING_VALUE:
                        if (isInInternalBracket && content[i] == '}') isInInternalBracket = false;
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
                content += "[" + entry.Key + "]\n\n{" + entry.Value + "}\n\n";
            }
            File.WriteAllText(_filepath, content);
            return true;
        }
        #endregion

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