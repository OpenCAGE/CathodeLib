using CathodeLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Linq;
using System.Text;
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
using UnityEngine;
#elif GODOT
using Godot;
using System.Numerics;
using Matrix4x4 = System.Numerics.Matrix4x4;
using Quaternion = System.Numerics.Quaternion;
using Vector2 = Godot.Vector2;
using Vector3 = Godot.Vector3;
using Vector4 = Godot.Vector4;
using Color = Godot.Color;
#else
using System.Numerics;
#endif

namespace CATHODE
{
    /// <summary>
    /// DATA/TEXT/*/*.TXT
    /// </summary>
    public class TextDB : CathodeFile
    {
        public Dictionary<string, string> Entries = new Dictionary<string, string>();
        public static new Implementation Implementation = Implementation.CREATE | Implementation.LOAD | Implementation.SAVE;

        public TextDB(string path) : base(path) { }

        #region FILE_IO
        override protected bool LoadInternal(MemoryStream stream)
        {
            //NOTE: Loading via byte[] or MemoryStream is not currently supported. Must be loaded via disk from a filepath!
            if (_filepath == "")
                return false;

            CurrentReadState state = CurrentReadState.NONE;
            string content = File.ReadAllText(_filepath);
            //Built up a character at a time: as strings, each character copied everything read so far
            StringBuilder id = new StringBuilder();
            StringBuilder value = new StringBuilder();
            bool isInInternalBracket = false;
            for (int i = 0; i < content.Length; i++)
            {
                switch (state)
                {
                    case CurrentReadState.READING_ID:
                        id.Append(content[i]);
                        if (content[i] == ']') state = CurrentReadState.NONE;
                        break;
                    case CurrentReadState.READING_VALUE:
                        value.Append(content[i]);
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

                        string entryId = id.ToString();
                        entryId = entryId.Substring(0, entryId.Length - 1);
                        string entryValue = value.ToString();
                        entryValue = entryValue.Substring(0, entryValue.Length - 1);
                        if (!Entries.ContainsKey(entryId))
                            Entries.Add(entryId, entryValue);

                        id.Clear();
                        value.Clear();
                        isInInternalBracket = false;
                        break;
                }
            }
            return true;
        }

        override protected bool SaveInternal()
        {
            StringBuilder content = new StringBuilder();
            foreach (KeyValuePair<string, string> entry in Entries)
            {
                content.Append("[").Append(entry.Key).Append("]\n{").Append(entry.Value).Append("}\n\n");
            }
            File.WriteAllText(_filepath, content.ToString(), Encoding.Unicode);
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