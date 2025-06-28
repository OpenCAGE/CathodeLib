using System;
using System.Collections.Generic;
using System.Text;
using CathodeLib;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CATHODE.Scripting;
using System.Drawing;
using System.Windows.Media.Animation;
using System.ComponentModel.Design.Serialization;
using System.Reflection;
using System.Data;
using static CATHODE.Textures.TEX4;
using static CATHODE.Resources;

#if UNITY_EDITOR || UNITY_STANDALONE_WIN
using UnityEngine;
#else
using System.Numerics;
#endif

namespace CATHODE.Scripting.Internal.Parsers
{
    public static class CommandsBIN
    {
        private static ShortGuid NAME_GUID;
        private static ShortGuid ANIM_TRACK_TYPE_GUID;
        private static ShortGuid PHYSICS_SYSTEM_GUID;
        private static ShortGuid RESOURCE_GUID;

        static CommandsBIN()
        {
            NAME_GUID = ShortGuidUtils.Generate("name");
            ANIM_TRACK_TYPE_GUID = ShortGuidUtils.Generate("ANIM_TRACK_TYPE");
            PHYSICS_SYSTEM_GUID = ShortGuidUtils.Generate("PhysicsSystem");
            RESOURCE_GUID = ShortGuidUtils.Generate("resource");
        }

        // see void EntityManager::apply_commands
        public static void Read(byte[] content, out ShortGuid[] EntryPoints, out List<Composite> Entries)
        {
            EntryPoints = new ShortGuid[3];
            Entries = new List<Composite>();

            Tuple<uint, int>[] command_entries;
            byte[] data_buffer;
            using (BinaryReader reader = new BinaryReader(new MemoryStream(content)))
            {
                int filesize = reader.ReadInt32();
                command_entries = new Tuple<uint, int>[reader.ReadInt32()]; // data_id / offset
                int data_buffer_size = reader.ReadInt32();
                for (int i = 0; i < command_entries.Length; i++)
                    command_entries[i] = new Tuple<uint, int>(reader.ReadUInt32(), reader.ReadInt32());
                data_buffer = reader.ReadBytes(data_buffer_size);
            }

            Dictionary<ShortGuid, Tuple<Composite, Dictionary<ShortGuid, Entity>>> entityCache = new Dictionary<ShortGuid, Tuple<Composite, Dictionary<ShortGuid, Entity>>>();

            using (BinaryReader reader = new BinaryReader(new MemoryStream(data_buffer)))
            {
                /*
                int y = 0;
                while (y < command_entries.Length)
                {
                    uint id = command_entries[y].Item1;
                    uint count = (id & (uint)CommandTypes.COMMAND_SIZE_MASK);
                    if ((id & (uint)CommandTypes.COMMAND_ADD) != 0)
                    {
                        Console.WriteLine("[" + y + "] ADD " + count);
                        switch (id & (uint)CommandTypes.COMMAND_IDENTIFIER_MASK)
                        {
                            case (uint)CommandTypes.COMMAND_IDENTIFIER_MASK & (uint)CommandTypes.CONTEXT_TEMPLATE:
                                Console.WriteLine("\t CONTEXT_TEMPLATE");
                                break;
                            case (uint)CommandTypes.COMMAND_IDENTIFIER_MASK & (uint)CommandTypes.CONTEXT_ROOT:
                                Console.WriteLine("\t CONTEXT_ROOT");
                                break;
                            case (uint)CommandTypes.COMMAND_IDENTIFIER_MASK & (uint)CommandTypes.CONTEXT_ENTITY:
                                Console.WriteLine("\t CONTEXT_ENTITY");
                                break;
                            case (uint)CommandTypes.COMMAND_IDENTIFIER_MASK & (uint)CommandTypes.CONTEXT_ALIAS:
                                Console.WriteLine("\t CONTEXT_ALIAS");
                                break;
                            case (uint)CommandTypes.COMMAND_IDENTIFIER_MASK & (uint)CommandTypes.CONTEXT_PROXY:
                                Console.WriteLine("\t CONTEXT_PROXY");
                                break;
                            case (uint)CommandTypes.COMMAND_IDENTIFIER_MASK & (uint)CommandTypes.CONTEXT_CONNECTOR:
                                Console.WriteLine("\t CONTEXT_CONNECTOR");
                                break;
                            case (uint)CommandTypes.COMMAND_IDENTIFIER_MASK & (uint)CommandTypes.CONTEXT_METHOD:
                                Console.WriteLine("\t CONTEXT_METHOD");
                                break;
                            case (uint)CommandTypes.COMMAND_IDENTIFIER_MASK & (uint)CommandTypes.CONTEXT_SEQUENCE:
                                Console.WriteLine("\t CONTEXT_SEQUENCE");
                                break;
                            case (uint)CommandTypes.COMMAND_IDENTIFIER_MASK & (uint)CommandTypes.CONTEXT_BINDING:
                                Console.WriteLine("\t CONTEXT_BINDING");
                                break;
                            case (uint)CommandTypes.COMMAND_IDENTIFIER_MASK & (uint)CommandTypes.CONTEXT_TRACK:
                                Console.WriteLine("\t CONTEXT_TRACK");
                                break;
                            case (uint)CommandTypes.COMMAND_IDENTIFIER_MASK & (uint)CommandTypes.CONTEXT_RESOURCE:
                                Console.WriteLine("\t CONTEXT_RESOURCE");
                                break;
                            case (uint)CommandTypes.COMMAND_IDENTIFIER_MASK & (uint)CommandTypes.CONTEXT_PARAMETER:
                                Console.WriteLine("\t CONTEXT_PARAMETER");
                                break;
                            case (uint)CommandTypes.COMMAND_IDENTIFIER_MASK & (uint)CommandTypes.CONTEXT_LINK:
                                Console.WriteLine("\t CONTEXT_LINK");
                                break;
                        };
                    }
                    else if ((id & (uint)CommandTypes.COMMAND_REMOVE) != 0)
                    {
                        Console.WriteLine("[" + y + "] REMOVE " + count);
                        switch (id & (uint)CommandTypes.COMMAND_IDENTIFIER_MASK)
                        {
                            case (uint)CommandTypes.COMMAND_IDENTIFIER_MASK & (uint)CommandTypes.CONTEXT_TEMPLATE:
                                Console.WriteLine("\t CONTEXT_TEMPLATE");
                                break;
                            case (uint)CommandTypes.COMMAND_IDENTIFIER_MASK & (uint)CommandTypes.CONTEXT_ROOT:
                                Console.WriteLine("\t CONTEXT_ROOT");
                                break;
                            case (uint)CommandTypes.COMMAND_IDENTIFIER_MASK & (uint)CommandTypes.CONTEXT_ENTITY:
                                Console.WriteLine("\t CONTEXT_ENTITY");
                                break;
                            case (uint)CommandTypes.COMMAND_IDENTIFIER_MASK & (uint)CommandTypes.CONTEXT_ALIAS:
                                Console.WriteLine("\t CONTEXT_ALIAS");
                                break;
                            case (uint)CommandTypes.COMMAND_IDENTIFIER_MASK & (uint)CommandTypes.CONTEXT_PROXY:
                                Console.WriteLine("\t CONTEXT_PROXY");
                                break;
                            case (uint)CommandTypes.COMMAND_IDENTIFIER_MASK & (uint)CommandTypes.CONTEXT_CONNECTOR:
                                Console.WriteLine("\t CONTEXT_CONNECTOR");
                                break;
                            case (uint)CommandTypes.COMMAND_IDENTIFIER_MASK & (uint)CommandTypes.CONTEXT_METHOD:
                                Console.WriteLine("\t CONTEXT_METHOD");
                                break;
                            case (uint)CommandTypes.COMMAND_IDENTIFIER_MASK & (uint)CommandTypes.CONTEXT_SEQUENCE:
                                Console.WriteLine("\t CONTEXT_SEQUENCE");
                                break;
                            case (uint)CommandTypes.COMMAND_IDENTIFIER_MASK & (uint)CommandTypes.CONTEXT_BINDING:
                                Console.WriteLine("\t CONTEXT_BINDING");
                                break;
                            case (uint)CommandTypes.COMMAND_IDENTIFIER_MASK & (uint)CommandTypes.CONTEXT_TRACK:
                                Console.WriteLine("\t CONTEXT_TRACK");
                                break;
                            case (uint)CommandTypes.COMMAND_IDENTIFIER_MASK & (uint)CommandTypes.CONTEXT_RESOURCE:
                                Console.WriteLine("\t CONTEXT_RESOURCE");
                                break;
                            case (uint)CommandTypes.COMMAND_IDENTIFIER_MASK & (uint)CommandTypes.CONTEXT_PARAMETER:
                                Console.WriteLine("\t CONTEXT_PARAMETER");
                                break;
                            case (uint)CommandTypes.COMMAND_IDENTIFIER_MASK & (uint)CommandTypes.CONTEXT_LINK:
                                Console.WriteLine("\t CONTEXT_LINK");
                                break;
                        };
                    }
                    else
                    {
                        string sdfdf = "";
                    }
                    y += (int)count;
                }
                */


                for (int i = 0; i < command_entries.Length; i++)
                {
                    uint id = command_entries[i].Item1;
                    uint count = (id & (uint)CommandTypes.COMMAND_SIZE_MASK);
                    if ((id & (uint)CommandTypes.COMMAND_ADD) != 0)
                    {
                        switch (id & (uint)CommandTypes.COMMAND_IDENTIFIER_MASK)
                        {
                            case (uint)CommandTypes.COMMAND_IDENTIFIER_MASK & (uint)CommandTypes.CONTEXT_TEMPLATE:
                                {
                                    Composite composite = new Composite()
                                    {
                                        shortGUID = Utilities.Consume<ShortGuid>(reader, command_entries[i + 1].Item2),
                                        name = Utilities.ReadString(reader, command_entries[i + 2].Item2)
                                    };
                                    if (!entityCache.ContainsKey(composite.shortGUID))
                                    {
                                        Entries.Add(composite);
                                        entityCache.Add(composite.shortGUID, new Tuple<Composite, Dictionary<ShortGuid, Entity>>(composite, new Dictionary<ShortGuid, Entity>()));
                                    }
                                }
                                break;
                            case (uint)CommandTypes.COMMAND_IDENTIFIER_MASK & (uint)CommandTypes.CONTEXT_ROOT:
                                EntryPoints[0] = Utilities.Consume<ShortGuid>(reader, command_entries[i + 1].Item2);
                                break;
                        };
                    }
                }

                for (int i = 0; i < command_entries.Length; i++)
                {
                    uint id = command_entries[i].Item1;
                    uint count = (id & (uint)CommandTypes.COMMAND_SIZE_MASK);
                    if ((id & (uint)CommandTypes.COMMAND_ADD) != 0)
                    {
                        switch (id & (uint)CommandTypes.COMMAND_IDENTIFIER_MASK)
                        {
                            case (uint)CommandTypes.COMMAND_IDENTIFIER_MASK & (uint)CommandTypes.CONTEXT_ENTITY:
                                {
                                    Tuple<Composite, Dictionary<ShortGuid, Entity>> cache = entityCache[Utilities.Consume<ShortGuid>(reader, command_entries[i + 1].Item2)];
                                    ShortGuid guid = Utilities.Consume<ShortGuid>(reader, command_entries[i + 2].Item2);
                                    ShortGuid function = Utilities.Consume<ShortGuid>(reader, command_entries[i + 3].Item2);
                                    FunctionEntity func = function.AsFunctionType == FunctionType.TriggerSequence ? new TriggerSequence() : function.AsFunctionType == FunctionType.CAGEAnimation ? new CAGEAnimation() : new FunctionEntity();
                                    func.shortGUID = guid;
                                    func.function = function;
                                    string name = Utilities.ReadString(reader, command_entries[i + 4].Item2);
                                    //EntityUtils.SetName(cache.Item1, func, name);
                                    if (!cache.Item2.ContainsKey(func.shortGUID))
                                    {
                                        cache.Item1.functions.Add(func);
                                        cache.Item2.Add(func.shortGUID, func);
                                    }
                                }
                                break;
                            case (uint)CommandTypes.COMMAND_IDENTIFIER_MASK & (uint)CommandTypes.CONTEXT_ALIAS:
                                {
                                    Tuple<Composite, Dictionary<ShortGuid, Entity>> cache = entityCache[Utilities.Consume<ShortGuid>(reader, command_entries[i + 1].Item2)];
                                    AliasEntity alias = new AliasEntity()
                                    {
                                        shortGUID = Utilities.Consume<ShortGuid>(reader, command_entries[i + 2].Item2),
                                        alias = new EntityPath() { path = Utilities.ConsumeArray<ShortGuid>(reader, (int)(command_entries[i + 3].Item1 & (uint)CommandTypes.COMMAND_SIZE_MASK) / 4, command_entries[i + 3].Item2) }
                                    };
                                    if (!cache.Item2.ContainsKey(alias.shortGUID))
                                    {
                                        cache.Item1.aliases.Add(alias);
                                        cache.Item2.Add(alias.shortGUID, alias);
                                    }
                                }
                                break;
                            case (uint)CommandTypes.COMMAND_IDENTIFIER_MASK & (uint)CommandTypes.CONTEXT_PROXY:
                                {
                                    Tuple<Composite, Dictionary<ShortGuid, Entity>> cache = entityCache[Utilities.Consume<ShortGuid>(reader, command_entries[i + 1].Item2)];
                                    ProxyEntity prox = new ProxyEntity()
                                    {
                                        shortGUID = Utilities.Consume<ShortGuid>(reader, command_entries[i + 2].Item2),
                                        function = Utilities.Consume<ShortGuid>(reader, command_entries[i + 3].Item2),
                                        proxy = new EntityPath() { path = Utilities.ConsumeArray<ShortGuid>(reader, (int)(command_entries[i + 4].Item1 & (uint)CommandTypes.COMMAND_SIZE_MASK) / 4, command_entries[i + 4].Item2) }
                                    };
                                    if (!cache.Item2.ContainsKey(prox.shortGUID))
                                    {
                                        cache.Item1.proxies.Add(prox);
                                        cache.Item2.Add(prox.shortGUID, prox);
                                    }
                                }
                                break;
                            case (uint)CommandTypes.COMMAND_IDENTIFIER_MASK & (uint)CommandTypes.CONTEXT_CONNECTOR:
                                {
                                    Tuple<Composite, Dictionary<ShortGuid, Entity>> cache = entityCache[Utilities.Consume<ShortGuid>(reader, command_entries[i + 1].Item2)];
                                    VariableEntity var = new VariableEntity()
                                    {
                                        shortGUID = Utilities.Consume<ShortGuid>(reader, command_entries[i + 2].Item2),
                                        type = (DataType)Utilities.Consume<uint>(reader, command_entries[i + 3].Item2),
                                        name = Utilities.Consume<ShortGuid>(reader, command_entries[i + 4].Item2)
                                    };
                                    string name = Utilities.ReadString(reader, command_entries[i + 5].Item2);
                                    //ShortGuidUtils.Generate(name); //Keep track of variable names
                                    if (!cache.Item2.ContainsKey(var.shortGUID))
                                    {
                                        cache.Item1.variables.Add(var);
                                        cache.Item2.Add(var.shortGUID, var);
                                    }
                                }
                                break;
                        };
                    }
                }

                for (int i = 0; i < command_entries.Length; i++)
                {
                    uint id = command_entries[i].Item1;
                    uint count = (id & (uint)CommandTypes.COMMAND_SIZE_MASK);
                    if ((id & (uint)CommandTypes.COMMAND_ADD) != 0)
                    {
                        switch (id & (uint)CommandTypes.COMMAND_IDENTIFIER_MASK)
                        {
                            case (uint)CommandTypes.COMMAND_IDENTIFIER_MASK & (uint)CommandTypes.CONTEXT_METHOD:
                                {
                                    Tuple<Composite, Dictionary<ShortGuid, Entity>> cache = entityCache[Utilities.Consume<ShortGuid>(reader, command_entries[i + 1].Item2)];
                                    ShortGuid entityID = Utilities.Consume<ShortGuid>(reader, command_entries[i + 2].Item2);
                                    TriggerSequence trig = null;
                                    if (cache.Item2.TryGetValue(entityID, out Entity ent))
                                    {
                                        trig = (TriggerSequence)ent;
                                    }
                                    else
                                    {
                                        trig = new TriggerSequence(entityID);
                                        cache.Item2.Add(ent.shortGUID, trig);
                                        cache.Item1.functions.Add(trig);
                                    }
                                    string name = Utilities.ReadString(reader, command_entries[i + 4].Item2);
                                    trig.methods.Add(new TriggerSequence.MethodEntry(name));
                                }
                                break;
                            case (uint)CommandTypes.COMMAND_IDENTIFIER_MASK & (uint)CommandTypes.CONTEXT_SEQUENCE:
                                {
                                    Tuple<Composite, Dictionary<ShortGuid, Entity>> cache = entityCache[Utilities.Consume<ShortGuid>(reader, command_entries[i + 1].Item2)];
                                    ShortGuid entityID = Utilities.Consume<ShortGuid>(reader, command_entries[i + 2].Item2);
                                    TriggerSequence trig = null;
                                    if (cache.Item2.TryGetValue(entityID, out Entity ent))
                                    {
                                        trig = (TriggerSequence)ent;
                                    }
                                    else
                                    {
                                        trig = new TriggerSequence(entityID);
                                        cache.Item2.Add(ent.shortGUID, trig);
                                        cache.Item1.functions.Add(trig);
                                    }
                                    trig.sequence.Add(new TriggerSequence.SequenceEntry()
                                    {
                                        timing = Utilities.Consume<float>(reader, command_entries[i + 3].Item2),
                                        connectedEntity = new EntityPath() { path = Utilities.ConsumeArray<ShortGuid>(reader, (int)(command_entries[i + 4].Item1 & (uint)CommandTypes.COMMAND_SIZE_MASK) / 4, command_entries[i + 4].Item2) }
                                    });
                                }
                                break;
                            case (uint)CommandTypes.COMMAND_IDENTIFIER_MASK & (uint)CommandTypes.CONTEXT_BINDING:
                                {
                                    Tuple<Composite, Dictionary<ShortGuid, Entity>> cache = entityCache[Utilities.Consume<ShortGuid>(reader, command_entries[i + 1].Item2)];
                                    ShortGuid entityID = Utilities.Consume<ShortGuid>(reader, command_entries[i + 2].Item2);
                                    CAGEAnimation cageAnim = null;
                                    if (cache.Item2.TryGetValue(entityID, out Entity ent))
                                    {
                                        cageAnim = (CAGEAnimation)ent;
                                    }
                                    else
                                    {
                                        cageAnim = new CAGEAnimation(entityID);
                                        cache.Item2.Add(ent.shortGUID, cageAnim);
                                        cache.Item1.functions.Add(cageAnim);
                                    }
                                    cageAnim.connections.Add(new CAGEAnimation.Connection()
                                    {
                                        binding_guid = Utilities.Consume<ShortGuid>(reader, command_entries[i + 3].Item2),
                                        target_track = Utilities.Consume<ShortGuid>(reader, command_entries[i + 4].Item2),
                                        target_param = Utilities.Consume<ShortGuid>(reader, command_entries[i + 5].Item2),
                                        target_param_type = (DataType)Utilities.Consume<uint>(reader, command_entries[i + 6].Item2),
                                        target_sub_param = Utilities.Consume<ShortGuid>(reader, command_entries[i + 7].Item2),
                                        binding_type = (ObjectType)Utilities.Consume<uint>(reader, command_entries[i + 8].Item2),
                                        connectedEntity = new EntityPath() { path = Utilities.ConsumeArray<ShortGuid>(reader, (int)(command_entries[i + 9].Item1 & (uint)CommandTypes.COMMAND_SIZE_MASK) / 4, command_entries[i + 9].Item2) }
                                    });
                                }
                                break;
                            case (uint)CommandTypes.COMMAND_IDENTIFIER_MASK & (uint)CommandTypes.CONTEXT_TRACK:
                                {
                                    Tuple<Composite, Dictionary<ShortGuid, Entity>> cache = entityCache[Utilities.Consume<ShortGuid>(reader, command_entries[i + 1].Item2)];
                                    ShortGuid entityID = Utilities.Consume<ShortGuid>(reader, command_entries[i + 2].Item2);
                                    CAGEAnimation cageAnim = null;
                                    if (cache.Item2.TryGetValue(entityID, out Entity ent))
                                    {
                                        cageAnim = (CAGEAnimation)ent;
                                    }
                                    else
                                    {
                                        cageAnim = new CAGEAnimation(entityID);
                                        cache.Item2.Add(ent.shortGUID, cageAnim);
                                        cache.Item1.functions.Add(cageAnim);
                                    }
                                    ShortGuid trackID = Utilities.Consume<ShortGuid>(reader, command_entries[i + 3].Item2);
                                    reader.BaseStream.Position = command_entries[i + 4].Item2;
                                    cEnum trackType = new cEnum(new ShortGuid(reader), reader.ReadInt32());
                                    uint keyframes = (command_entries[i + 5].Item1 & (uint)CommandTypes.COMMAND_SIZE_MASK);
                                    switch (trackType.enumIndex)
                                    {
                                        case (int)CAGEAnimation.TrackType.FLOAT:
                                            {
                                                CAGEAnimation.FloatTrack track = cageAnim.animations.FirstOrDefault(o => o.shortGUID == trackID);
                                                if (track == null)
                                                {
                                                    track = new CAGEAnimation.FloatTrack() { shortGUID = trackID };
                                                    cageAnim.animations.Add(track);
                                                }
                                                keyframes /= 32;
                                                for (int x = 0; x < keyframes - 1; x++)
                                                {
                                                    track.keyframes.Add(new CAGEAnimation.FloatTrack.Keyframe()
                                                    {
                                                        mode = (CAGEAnimation.InterpolationMode)Utilities.Consume<int>(reader, command_entries[i + 5].Item2 + (32 * x)),
                                                        time = Utilities.Consume<float>(reader, command_entries[i + 5].Item2 + 4 + (32 * x)),
                                                        value = Utilities.Consume<Vector2>(reader, command_entries[i + 5].Item2 + 8 + (32 * x)),
                                                        tan_in = Utilities.Consume<Vector2>(reader, command_entries[i + 5].Item2 + 16 + (32 * x)),
                                                        tan_out = Utilities.Consume<Vector2>(reader, command_entries[i + 5].Item2 + 24 + (32 * x)),
                                                    });
                                                }
                                            }
                                            break;
                                        case (int)CAGEAnimation.TrackType.GUID: 
                                        case (int)CAGEAnimation.TrackType.STRING: 
                                        case (int)CAGEAnimation.TrackType.MASTERING:
                                            {
                                                CAGEAnimation.EventTrack track = cageAnim.events.FirstOrDefault(o => o.shortGUID == trackID);
                                                if (track == null)
                                                {
                                                    track = new CAGEAnimation.EventTrack() { shortGUID = trackID };
                                                    cageAnim.events.Add(track);
                                                }
                                                keyframes /= 24;
                                                for (int x = 0; x < keyframes - 1; x++)
                                                {
                                                    track.keyframes.Add(new CAGEAnimation.EventTrack.Keyframe()
                                                    {
                                                        mode = (CAGEAnimation.InterpolationMode)Utilities.Consume<int>(reader, command_entries[i + 5].Item2 + (24 * x)),
                                                        time = Utilities.Consume<float>(reader, command_entries[i + 5].Item2 + 4 + (24 * x)),
                                                        forward = Utilities.Consume<ShortGuid>(reader, command_entries[i + 5].Item2 + 8 + (24 * x)),
                                                        reverse = Utilities.Consume<ShortGuid>(reader, command_entries[i + 5].Item2 + 12 + (24 * x)),
                                                        track_type = (CAGEAnimation.TrackType)Utilities.Consume<int>(reader, command_entries[i + 5].Item2 + 16 + (24 * x)),
                                                        duration = Utilities.Consume<float>(reader, command_entries[i + 5].Item2 + 20 + (24 * x)),
                                                    });
                                                }
                                            }
                                            break;
                                    }
                                }
                                break;
                        };
                    }
                }

                for (int i = 0; i < command_entries.Length; i++)
                {
                    uint id = command_entries[i].Item1;
                    uint count = (id & (uint)CommandTypes.COMMAND_SIZE_MASK);
                    if ((id & (uint)CommandTypes.COMMAND_ADD) != 0)
                    {
                        switch (id & (uint)CommandTypes.COMMAND_IDENTIFIER_MASK)
                        {
                            case (uint)CommandTypes.COMMAND_IDENTIFIER_MASK & (uint)CommandTypes.CONTEXT_PARAMETER:
                                {
                                    Tuple<Composite, Dictionary<ShortGuid, Entity>> cache = entityCache[Utilities.Consume<ShortGuid>(reader, command_entries[i + 1].Item2)];
                                    ShortGuid entityID = Utilities.Consume<ShortGuid>(reader, command_entries[i + 2].Item2);
                                    ShortGuid type = Utilities.Consume<ShortGuid>(reader, command_entries[i + 3].Item2); //FunctionType guid?
                                    ShortGuid paramName = Utilities.Consume<ShortGuid>(reader, command_entries[i + 4].Item2);
                                    ParameterData paramData = null;
                                    reader.BaseStream.Position = command_entries[i + 5].Item2;
                                    uint length = (uint)CommandTypes.COMMAND_SIZE_MASK & command_entries[i + 5].Item1;
                                    switch ((uint)CommandTypes.COMMAND_IDENTIFIER_MASK & command_entries[i + 5].Item1)
                                    {
                                        case (uint)CommandTypes.COMMAND_IDENTIFIER_MASK & (uint)CommandTypes.DATA_POSITION:
                                            paramData = new cTransform();
                                            ((cTransform)paramData).position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                                            float _x, _y, _z; _y = reader.ReadSingle(); _x = reader.ReadSingle(); _z = reader.ReadSingle(); //This is Y/X/Z as it's stored as Yaw/Pitch/Roll
                                            ((cTransform)paramData).rotation = new Vector3(_x, _y, _z);
                                            break;
                                        case (uint)CommandTypes.COMMAND_IDENTIFIER_MASK & (uint)CommandTypes.DATA_INT:
                                            paramData = new cInteger(reader.ReadInt32());
                                            break;
                                        case (uint)CommandTypes.COMMAND_IDENTIFIER_MASK & (uint)CommandTypes.DATA_FILE_PATH:
                                        case (uint)CommandTypes.COMMAND_IDENTIFIER_MASK & (uint)CommandTypes.DATA_STRING:
                                            paramData = new cString(Utilities.ReadString(reader)); // ((cString)paramData).value.Length + 1 == length
                                            break;
                                        case (uint)CommandTypes.COMMAND_IDENTIFIER_MASK & (uint)CommandTypes.DATA_BOOL:
                                            paramData = new cBool(reader.ReadBoolean());
                                            break;
                                        case (uint)CommandTypes.COMMAND_IDENTIFIER_MASK & (uint)CommandTypes.DATA_FLOAT:
                                            paramData = new cFloat(reader.ReadSingle());
                                            break;
                                        case (uint)CommandTypes.COMMAND_IDENTIFIER_MASK & (uint)CommandTypes.DATA_GUID:
                                            paramData = new cResource(new ShortGuid(reader));
                                            break;
                                        case (uint)CommandTypes.COMMAND_IDENTIFIER_MASK & (uint)CommandTypes.DATA_VECTOR:
                                            paramData = new cVector3(new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()));
                                            break;
                                        case (uint)CommandTypes.COMMAND_IDENTIFIER_MASK & (uint)CommandTypes.DATA_ENUM:
                                            paramData = new cEnum(new ShortGuid(reader), reader.ReadInt32());
                                            break;
                                        case (uint)CommandTypes.COMMAND_IDENTIFIER_MASK & (uint)CommandTypes.DATA_PATH:
                                        case (uint)CommandTypes.COMMAND_IDENTIFIER_MASK & (uint)CommandTypes.DATA_FLOAT_TRACK:
                                        case (uint)CommandTypes.COMMAND_IDENTIFIER_MASK & (uint)CommandTypes.DATA_EVENT_TRACK:
                                        case (uint)CommandTypes.COMMAND_IDENTIFIER_MASK & (uint)CommandTypes.DATA_SPLINE:
                                            //todo - check PATH/FLOAT_TRACK/EVENT_TRACK
                                            length /= 24;
                                            List<cTransform> points = new List<cTransform>((int)length - 1);
                                            for (int x = 0; x < length - 1; x++)
                                            {
                                                cTransform spline_point = new cTransform();
                                                spline_point.position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                                                float __x, __y, __z; __y = reader.ReadSingle(); __x = reader.ReadSingle(); __z = reader.ReadSingle(); //This is Y/X/Z as it's stored as Yaw/Pitch/Roll
                                                spline_point.rotation = new Vector3(__x, __y, __z);
                                                points.Add(spline_point);
                                            }
                                            paramData = new cSpline(points);
                                            break;
                                    };
                                    if (cache.Item2.TryGetValue(entityID, out Entity ent))
                                    {
                                        if (paramData != null) //Skipping nulls: links seem to get added as null parameters
                                            if (paramName != NAME_GUID || (ent.variant == EntityVariant.FUNCTION && ((FunctionEntity)ent).function.AsFunctionType == FunctionType.Zone)) //Skipping "name" parameter as this is handled by our name table
                                                ent.AddParameter(paramName, paramData);
                                    }
                                }
                                break;
                            case (uint)CommandTypes.COMMAND_IDENTIFIER_MASK & (uint)CommandTypes.CONTEXT_LINK:
                                {
                                    Tuple<Composite, Dictionary<ShortGuid, Entity>> cache = entityCache[Utilities.Consume<ShortGuid>(reader, command_entries[i + 1].Item2)];
                                    ShortGuid parentEntID = Utilities.Consume<ShortGuid>(reader, command_entries[i + 3].Item2);
                                    if (cache.Item2.TryGetValue(parentEntID, out Entity parent))
                                    {
                                        //TODO: is there a count here?
                                        parent.childLinks.Add(new EntityConnector()
                                        {
                                            ID = Utilities.Consume<ShortGuid>(reader, command_entries[i + 2].Item2),
                                            //i + 4 = type
                                            thisParamID = Utilities.Consume<ShortGuid>(reader, command_entries[i + 5].Item2),
                                            linkedEntityID = Utilities.Consume<ShortGuid>(reader, command_entries[i + 6].Item2),
                                            //i + 7 = type
                                            linkedParamID = Utilities.Consume<ShortGuid>(reader, command_entries[i + 8].Item2),
                                        });
                                    }
                                }
                                break;
                        };
                    }
                }

                for (int i = 0; i < command_entries.Length; i++)
                {
                    uint id = command_entries[i].Item1;
                    uint count = (id & (uint)CommandTypes.COMMAND_SIZE_MASK);
                    if ((id & (uint)CommandTypes.COMMAND_ADD) != 0)
                    {
                        switch (id & (uint)CommandTypes.COMMAND_IDENTIFIER_MASK)
                        {
                            case (uint)CommandTypes.COMMAND_IDENTIFIER_MASK & (uint)CommandTypes.CONTEXT_RESOURCE:
                                {
                                    Tuple<Composite, Dictionary<ShortGuid, Entity>> cache = entityCache[Utilities.Consume<ShortGuid>(reader, command_entries[i + 1].Item2)];
                                    ResourceReference resource = new ResourceReference();
                                    reader.BaseStream.Position = command_entries[i + 6].Item2;
                                    resource.position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                                    resource.rotation = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                                    resource.resource_id = Utilities.Consume<ShortGuid>(reader, command_entries[i + 2].Item2);
                                    resource.resource_type = (ResourceType)Utilities.Consume<uint>(reader, command_entries[i + 3].Item2);
                                    switch (resource.resource_type)
                                    {
                                        case ResourceType.RENDERABLE_INSTANCE:
                                            resource.index = Utilities.Consume<int>(reader, command_entries[i + 4].Item2);
                                            resource.count = Utilities.Consume<int>(reader, command_entries[i + 5].Item2);
                                            break;
                                        case ResourceType.COLLISION_MAPPING:
                                            resource.index = Utilities.Consume<int>(reader, command_entries[i + 4].Item2);
                                            resource.entityID = Utilities.Consume<ShortGuid>(reader, command_entries[i + 5].Item2);
                                            break;
                                        case ResourceType.ANIMATED_MODEL:
                                        case ResourceType.DYNAMIC_PHYSICS_SYSTEM:
                                            resource.index = Utilities.Consume<int>(reader, command_entries[i + 4].Item2);
                                            break;
                                    }
                                    if (cache.Item2.TryGetValue(resource.resource_id, out Entity ent))
                                    {
                                        ((FunctionEntity)ent).resources.Add(resource);
                                    }
                                    else
                                    {
                                        bool addedAsParam = false;
                                        foreach (Entity entity in cache.Item1.GetEntities())
                                        {
                                            foreach (Parameter parameter in entity.parameters)
                                            {
                                                if (parameter.name != RESOURCE_GUID) continue;
                                                if (((cResource)parameter.content).shortGUID == resource.resource_id)
                                                {
                                                    ((cResource)parameter.content).value.Add(resource);
                                                    addedAsParam = true;
                                                    break;
                                                }
                                            }
                                        }
                                        if (!addedAsParam)
                                        {
                                            if (resource.resource_type == ResourceType.DYNAMIC_PHYSICS_SYSTEM)
                                            {
                                                FunctionEntity physEnt = cache.Item1.functions.FirstOrDefault(o => o.function == PHYSICS_SYSTEM_GUID);
                                                if (physEnt != null)
                                                {
                                                    physEnt.resources.Add(resource);
                                                    addedAsParam = true;
                                                }
                                            }
                                            if (!addedAsParam)
                                            {
                                                Console.WriteLine("Failed to find entity or parameter [" + resource.resource_id.ToByteString() + "] in composite: " + cache.Item1.name);
                                                Console.WriteLine("\t Type - " + resource.resource_type);
                                            }
                                        }
                                    }
                                }
                                break;
                        };
                    }
                }
            }

            EntryPoints[1] = Entries.FirstOrDefault(o => o.name == "GLOBAL").shortGUID;
            EntryPoints[2] = Entries.FirstOrDefault(o => o.name == "PAUSEMENU").shortGUID;
        }

        public static void Write(ShortGuid[] EntryPoints, List<Composite> Entries, out byte[] content)
        {
            List<Tuple<uint, int>> _commandEntries = new List<Tuple<uint, int>>();
            using (MemoryStream _dataBufferStream = new MemoryStream())
            using (BinaryWriter _dataWriter = new BinaryWriter(_dataBufferStream))
            {
                foreach (var composite in Entries)
                {
                    int offset = (int)_dataBufferStream.Position;
                    _commandEntries.Add(new Tuple<uint, int>((uint)(CommandTypes.CONTEXT_TEMPLATE | CommandTypes.COMMAND_ADD), offset));

                    Utilities.Write<ShortGuid>(_dataWriter, composite.shortGUID);
                    _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_GUID | 4, offset));

                    offset = (int)_dataBufferStream.Position;
                    Utilities.WriteString(composite.name, _dataWriter, true);
                    _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_STRING | (uint)(composite.name.Length + 1), offset));
                }

                {
                    int offset = (int)_dataBufferStream.Position;
                    _commandEntries.Add(new Tuple<uint, int>((uint)(CommandTypes.CONTEXT_ROOT | CommandTypes.COMMAND_ADD), offset));
                    Utilities.Write<ShortGuid>(_dataWriter, EntryPoints[0]);
                    _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_GUID | 4, offset));
                }

                foreach (var composite in Entries)
                {
                    foreach (var func in composite.functions)
                    {
                        int offset = (int)_dataBufferStream.Position;
                        _commandEntries.Add(new Tuple<uint, int>((uint)(CommandTypes.CONTEXT_ENTITY | CommandTypes.COMMAND_ADD), offset));

                        Utilities.Write<ShortGuid>(_dataWriter, composite.shortGUID);
                        _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_GUID | 4, offset));

                        offset = (int)_dataBufferStream.Position;
                        Utilities.Write<ShortGuid>(_dataWriter, func.shortGUID);
                        _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_GUID | 4, offset));

                        offset = (int)_dataBufferStream.Position;
                        Utilities.Write<ShortGuid>(_dataWriter, func.function);
                        _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_GUID | 4, offset));

                        offset = (int)_dataBufferStream.Position;
                        string name = EntityUtils.GetName(composite, func);
                        Utilities.WriteString(name, _dataWriter, true);
                        _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_STRING | (uint)(name.Length + 1), offset));
                    }

                    foreach (var alias in composite.aliases)
                    {
                        int offset = (int)_dataBufferStream.Position;
                        _commandEntries.Add(new Tuple<uint, int>((uint)(CommandTypes.CONTEXT_ALIAS | CommandTypes.COMMAND_ADD), offset));

                        Utilities.Write<ShortGuid>(_dataWriter, composite.shortGUID);
                        _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_GUID | 4, offset));

                        offset = (int)_dataBufferStream.Position;
                        Utilities.Write<ShortGuid>(_dataWriter, alias.shortGUID);
                        _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_GUID | 4, offset));

                        offset = (int)_dataBufferStream.Position;
                        Utilities.Write<ShortGuid>(_dataWriter, alias.alias.path);
                        _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_PATH | (uint)(alias.alias.path.Length * 4), offset));
                    }

                    foreach (var proxy in composite.proxies)
                    {
                        int offset = (int)_dataBufferStream.Position;
                        _commandEntries.Add(new Tuple<uint, int>((uint)(CommandTypes.CONTEXT_PROXY | CommandTypes.COMMAND_ADD), offset));

                        Utilities.Write<ShortGuid>(_dataWriter, composite.shortGUID);
                        _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_GUID | 4, offset));

                        offset = (int)_dataBufferStream.Position;
                        Utilities.Write<ShortGuid>(_dataWriter, proxy.shortGUID);
                        _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_GUID | 4, offset));

                        offset = (int)_dataBufferStream.Position;
                        Utilities.Write<ShortGuid>(_dataWriter, proxy.function);
                        _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_GUID | 4, offset));

                        offset = (int)_dataBufferStream.Position;
                        Utilities.Write<ShortGuid>(_dataWriter, proxy.proxy.path);
                        _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_PATH | (uint)(proxy.proxy.path.Length * 4), offset));
                    }

                    foreach (var variable in composite.variables)
                    {
                        int offset = (int)_dataBufferStream.Position;
                        _commandEntries.Add(new Tuple<uint, int>((uint)(CommandTypes.CONTEXT_CONNECTOR | CommandTypes.COMMAND_ADD), offset));

                        Utilities.Write<ShortGuid>(_dataWriter, composite.shortGUID);
                        _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_GUID | 4, offset));

                        offset = (int)_dataBufferStream.Position;
                        Utilities.Write<ShortGuid>(_dataWriter, variable.shortGUID);
                        _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_GUID | 4, offset));

                        offset = (int)_dataBufferStream.Position;
                        _dataWriter.Write((uint)variable.type);
                        _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_GUID | 4, offset));

                        offset = (int)_dataBufferStream.Position;
                        Utilities.Write<ShortGuid>(_dataWriter, variable.name);
                        _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_GUID | 4, offset));

                        offset = (int)_dataBufferStream.Position;
                        string name = variable.name.ToString();
                        Utilities.WriteString(name, _dataWriter, true);
                        _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_STRING | (uint)(name.Length + 1), offset));
                    }
                }

                foreach (var composite in Entries)
                {
                    foreach (var func in composite.functions)
                    {
                        if (func is TriggerSequence trig)
                        {
                            foreach (var method in trig.methods)
                            {
                                int offset = (int)_dataBufferStream.Position;
                                _commandEntries.Add(new Tuple<uint, int>((uint)(CommandTypes.CONTEXT_METHOD | CommandTypes.COMMAND_ADD), offset));

                                Utilities.Write<ShortGuid>(_dataWriter, composite.shortGUID);
                                _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_GUID | 4, offset));

                                offset = (int)_dataBufferStream.Position;
                                Utilities.Write<ShortGuid>(_dataWriter, trig.shortGUID);
                                _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_GUID | 4, offset));

                                offset = (int)_dataBufferStream.Position;
                                Utilities.Write<ShortGuid>(_dataWriter, trig.function);
                                _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_GUID | 4, offset));

                                offset = (int)_dataBufferStream.Position;
                                string name = method.method.ToString();
                                Utilities.WriteString(name, _dataWriter, true);
                                _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_STRING | (uint)(name.Length + 1), offset));
                            }

                            foreach (var sequenceEntry in trig.sequence)
                            {
                                int offset = (int)_dataBufferStream.Position;
                                _commandEntries.Add(new Tuple<uint, int>((uint)(CommandTypes.CONTEXT_SEQUENCE | CommandTypes.COMMAND_ADD), offset));

                                Utilities.Write<ShortGuid>(_dataWriter, composite.shortGUID);
                                _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_GUID | 4, offset));

                                offset = (int)_dataBufferStream.Position;
                                Utilities.Write<ShortGuid>(_dataWriter, trig.shortGUID);
                                _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_GUID | 4, offset));

                                offset = (int)_dataBufferStream.Position;
                                _dataWriter.Write(sequenceEntry.timing);
                                _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_FLOAT | 4, offset));

                                offset = (int)_dataBufferStream.Position;
                                Utilities.Write<ShortGuid>(_dataWriter, sequenceEntry.connectedEntity.path);
                                _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_PATH | (uint)(sequenceEntry.connectedEntity.path.Length * 4), offset));
                            }
                        }
                        else if (func is CAGEAnimation cageAnim)
                        {
                            foreach (var connection in cageAnim.connections)
                            {
                                int offset = (int)_dataBufferStream.Position;
                                _commandEntries.Add(new Tuple<uint, int>((uint)(CommandTypes.CONTEXT_BINDING | CommandTypes.COMMAND_ADD), offset));

                                Utilities.Write<ShortGuid>(_dataWriter, composite.shortGUID);
                                _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_GUID | 4, offset));

                                offset = (int)_dataBufferStream.Position;
                                Utilities.Write<ShortGuid>(_dataWriter, cageAnim.shortGUID);
                                _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_GUID | 4, offset));

                                offset = (int)_dataBufferStream.Position;
                                Utilities.Write<ShortGuid>(_dataWriter, connection.binding_guid);
                                _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_GUID | 4, offset));

                                offset = (int)_dataBufferStream.Position;
                                Utilities.Write<ShortGuid>(_dataWriter, connection.target_track);
                                _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_GUID | 4, offset));

                                offset = (int)_dataBufferStream.Position;
                                Utilities.Write<ShortGuid>(_dataWriter, connection.target_param);
                                _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_GUID | 4, offset));

                                offset = (int)_dataBufferStream.Position;
                                _dataWriter.Write((uint)connection.target_param_type);
                                _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_GUID | 4, offset));

                                offset = (int)_dataBufferStream.Position;
                                Utilities.Write<ShortGuid>(_dataWriter, connection.target_sub_param);
                                _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_GUID | 4, offset));

                                offset = (int)_dataBufferStream.Position;
                                _dataWriter.Write((uint)connection.binding_type);
                                _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_GUID | 4, offset));

                                offset = (int)_dataBufferStream.Position;
                                Utilities.Write<ShortGuid>(_dataWriter, connection.connectedEntity.path);
                                _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_PATH | (uint)(connection.connectedEntity.path.Length * 4), offset));
                            }

                            foreach (var floatTrack in cageAnim.animations)
                            {
                                int offset = (int)_dataBufferStream.Position;
                                _commandEntries.Add(new Tuple<uint, int>((uint)(CommandTypes.CONTEXT_TRACK | CommandTypes.COMMAND_ADD), offset));

                                Utilities.Write<ShortGuid>(_dataWriter, composite.shortGUID);
                                _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_GUID | 4, offset));

                                offset = (int)_dataBufferStream.Position;
                                Utilities.Write<ShortGuid>(_dataWriter, cageAnim.shortGUID);
                                _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_GUID | 4, offset));

                                offset = (int)_dataBufferStream.Position;
                                Utilities.Write<ShortGuid>(_dataWriter, floatTrack.shortGUID);
                                _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_GUID | 4, offset));

                                offset = (int)_dataBufferStream.Position;
                                Utilities.Write<ShortGuid>(_dataWriter, ANIM_TRACK_TYPE_GUID);
                                _dataWriter.Write((int)CAGEAnimation.TrackType.FLOAT);
                                _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_ENUM | 8, offset));

                                offset = (int)_dataBufferStream.Position;
                                foreach (var keyframe in floatTrack.keyframes)
                                {
                                    _dataWriter.Write((int)keyframe.mode);
                                    _dataWriter.Write(keyframe.time);
                                    _dataWriter.Write(keyframe.value.X);
                                    _dataWriter.Write(keyframe.value.Y);
                                    _dataWriter.Write(keyframe.tan_in.X);
                                    _dataWriter.Write(keyframe.tan_in.Y);
                                    _dataWriter.Write(keyframe.tan_out.X);
                                    _dataWriter.Write(keyframe.tan_out.Y);
                                }
                                _dataWriter.Write(new byte[32]);
                                _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_FLOAT_TRACK | (uint)(32 * (floatTrack.keyframes.Count + 1)), offset));
                            }

                            foreach (var eventTrack in cageAnim.events)
                            {
                                int offset = (int)_dataBufferStream.Position;
                                _commandEntries.Add(new Tuple<uint, int>((uint)(CommandTypes.CONTEXT_TRACK | CommandTypes.COMMAND_ADD), offset));

                                Utilities.Write<ShortGuid>(_dataWriter, composite.shortGUID);
                                _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_GUID | 4, offset));

                                offset = (int)_dataBufferStream.Position;
                                Utilities.Write<ShortGuid>(_dataWriter, cageAnim.shortGUID);
                                _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_GUID | 4, offset));

                                offset = (int)_dataBufferStream.Position;
                                Utilities.Write<ShortGuid>(_dataWriter, eventTrack.shortGUID);
                                _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_GUID | 4, offset));

                                offset = (int)_dataBufferStream.Position;
                                Utilities.Write<ShortGuid>(_dataWriter, ANIM_TRACK_TYPE_GUID);
                                _dataWriter.Write((int)(eventTrack.keyframes.Count == 0 ? CAGEAnimation.TrackType.INVALID : eventTrack.keyframes[0].track_type));
                                _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_ENUM | 8, offset));

                                offset = (int)_dataBufferStream.Position;
                                foreach (var keyframe in eventTrack.keyframes)
                                {
                                    _dataWriter.Write((int)keyframe.mode);
                                    _dataWriter.Write(keyframe.time);
                                    Utilities.Write<ShortGuid>(_dataWriter, keyframe.forward);
                                    Utilities.Write<ShortGuid>(_dataWriter, keyframe.reverse);
                                    _dataWriter.Write((int)keyframe.track_type);
                                    _dataWriter.Write(keyframe.duration);
                                }
                                _dataWriter.Write(new byte[24]);
                                _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_EVENT_TRACK | (uint)(24 * (eventTrack.keyframes.Count + 1)), offset));
                            }
                        }
                    }
                }

                foreach (var composite in Entries)
                {
                    foreach (var func in composite.functions)
                    {
                        foreach (var resource in func.resources)
                        {
                            AddResourceCommand(_commandEntries, _dataBufferStream, _dataWriter, composite, resource);
                        }
                    }

                    foreach (var entity in composite.GetEntities())
                    {
                        foreach (var paramEntry in entity.parameters)
                        {
                            int offset = (int)_dataBufferStream.Position;
                            _commandEntries.Add(new Tuple<uint, int>((uint)(CommandTypes.CONTEXT_PARAMETER | CommandTypes.COMMAND_ADD), offset));

                            Utilities.Write<ShortGuid>(_dataWriter, composite.shortGUID);
                            _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_GUID | 4, offset));

                            offset = (int)_dataBufferStream.Position;
                            Utilities.Write<ShortGuid>(_dataWriter, entity.shortGUID);
                            _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_GUID | 4, offset));

                            //TODO: type guid for entity
                            offset = (int)_dataBufferStream.Position;
                            Utilities.Write<ShortGuid>(_dataWriter, new ShortGuid(0));
                            _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_GUID | 4, offset));

                            offset = (int)_dataBufferStream.Position;
                            Utilities.Write<ShortGuid>(_dataWriter, paramEntry.name);
                            _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_GUID | 4, offset));

                            offset = (int)_dataBufferStream.Position;
                            switch (paramEntry.content.dataType)
                            {
                                case DataType.TRANSFORM:
                                    cTransform t = (cTransform)paramEntry.content;
                                    _dataWriter.Write(t.position.X);
                                    _dataWriter.Write(t.position.Y);
                                    _dataWriter.Write(t.position.Z);
                                    _dataWriter.Write(t.rotation.Y);
                                    _dataWriter.Write(t.rotation.X);
                                    _dataWriter.Write(t.rotation.Z);
                                    _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_POSITION | 24, offset));
                                    break;
                                case DataType.INTEGER:
                                    _dataWriter.Write(((cInteger)paramEntry.content).value);
                                    _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_INT | 4, offset));
                                    break;
                                case DataType.ENUM_STRING:
                                case DataType.STRING:
                                    cString st = (cString)paramEntry.content;
                                    Utilities.WriteString(st.value, _dataWriter, true);
                                    _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_STRING | (uint)(st.value.Length + 1), offset));
                                    break;
                                case DataType.BOOL:
                                    _dataWriter.Write(((cBool)paramEntry.content).value);
                                    _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_BOOL | 1, offset));
                                    break;
                                case DataType.FLOAT:
                                    _dataWriter.Write(((cFloat)paramEntry.content).value);
                                    _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_FLOAT | 4, offset));
                                    break;
                                case DataType.RESOURCE:
                                    cResource r = (cResource)paramEntry.content;
                                    Utilities.Write<ShortGuid>(_dataWriter, r.shortGUID);
                                    _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_GUID | 4, offset));
                                    for (int x = 0; x < r.value.Count; x++)
                                        AddResourceCommand(_commandEntries, _dataBufferStream, _dataWriter, composite, r.value[x]);
                                    break;
                                case DataType.VECTOR:
                                    cVector3 v = (cVector3)paramEntry.content;
                                    _dataWriter.Write(v.value.X);
                                    _dataWriter.Write(v.value.Y);
                                    _dataWriter.Write(v.value.Z);
                                    _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_VECTOR | 12, offset));
                                    break;
                                case DataType.ENUM:
                                    cEnum e = (cEnum)paramEntry.content;
                                    Utilities.Write<ShortGuid>(_dataWriter, e.enumID);
                                    _dataWriter.Write(e.enumIndex);
                                    _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_ENUM | 8, offset));
                                    break;
                                case DataType.SPLINE:
                                    cSpline s = (cSpline)paramEntry.content;
                                    foreach (var point in s.splinePoints)
                                    {
                                        _dataWriter.Write(point.position.X);
                                        _dataWriter.Write(point.position.Y);
                                        _dataWriter.Write(point.position.Z);
                                        _dataWriter.Write(point.rotation.Y);
                                        _dataWriter.Write(point.rotation.X);
                                        _dataWriter.Write(point.rotation.Z);
                                    }
                                    _dataWriter.Write(-1.0f); _dataWriter.Write(-1.0f); _dataWriter.Write(-1.0f);
                                    _dataWriter.Write(-1.0f); _dataWriter.Write(-1.0f); _dataWriter.Write(-1.0f);
                                    _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_SPLINE | (uint)((s.splinePoints.Count + 1) * 24), offset));
                                    break;
                            }
                        }
                    }

                    foreach (var entity in composite.GetEntities())
                    {
                        foreach (var link in entity.childLinks)
                        {
                            int offset = (int)_dataBufferStream.Position;
                            _commandEntries.Add(new Tuple<uint, int>((uint)(CommandTypes.CONTEXT_LINK | CommandTypes.COMMAND_ADD), offset));

                            Utilities.Write<ShortGuid>(_dataWriter, composite.shortGUID);
                            _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_GUID | 4, offset));

                            offset = (int)_dataBufferStream.Position;
                            Utilities.Write<ShortGuid>(_dataWriter, link.ID);
                            _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_GUID | 4, offset));

                            offset = (int)_dataBufferStream.Position;
                            Utilities.Write<ShortGuid>(_dataWriter, entity.shortGUID);
                            _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_GUID | 4, offset));

                            //TODO: get this entity type (required?)
                            offset = (int)_dataBufferStream.Position;
                            Utilities.Write<ShortGuid>(_dataWriter, new ShortGuid(0));
                            _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_GUID | 4, offset));

                            offset = (int)_dataBufferStream.Position;
                            Utilities.Write<ShortGuid>(_dataWriter, link.thisParamID);
                            _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_GUID | 4, offset));

                            offset = (int)_dataBufferStream.Position;
                            Utilities.Write<ShortGuid>(_dataWriter, link.linkedEntityID);
                            _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_GUID | 4, offset));

                            //TODO: get linked entity type (required?)
                            offset = (int)_dataBufferStream.Position;
                            Utilities.Write<ShortGuid>(_dataWriter, new ShortGuid(0));
                            _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_GUID | 4, offset));

                            offset = (int)_dataBufferStream.Position;
                            Utilities.Write<ShortGuid>(_dataWriter, link.linkedParamID);
                            _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_GUID | 4, offset));
                        }
                    }
                }

                using (MemoryStream stream = new MemoryStream())
                {
                    using (BinaryWriter writer = new BinaryWriter(stream))
                    {
                        writer.Write(12 + (_commandEntries.Count * 8) + (int)_dataBufferStream.Length);
                        writer.Write(_commandEntries.Count);
                        writer.Write((int)_dataBufferStream.Length);
                        foreach (var entry in _commandEntries)
                        {
                            writer.Write(entry.Item1);
                            writer.Write(entry.Item2);
                        }
                        writer.Write(_dataBufferStream.ToArray());
                    }
                    content = stream.ToArray();
                }
                File.WriteAllBytes("output.bin", _dataBufferStream.ToArray());
            }
        }

        private static void AddResourceCommand(List<Tuple<uint, int>> _commandEntries, MemoryStream _dataBufferStream, BinaryWriter _dataWriter, Composite composite, ResourceReference resource)
        {
            int offset = (int)_dataBufferStream.Position;
            _commandEntries.Add(new Tuple<uint, int>((uint)(CommandTypes.CONTEXT_RESOURCE | CommandTypes.COMMAND_ADD), offset));

            Utilities.Write<ShortGuid>(_dataWriter, composite.shortGUID);
            _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_GUID | 4, offset));

            offset = (int)_dataBufferStream.Position;
            Utilities.Write<ShortGuid>(_dataWriter, resource.resource_id);
            _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_GUID | 4, offset));

            offset = (int)_dataBufferStream.Position;
            _dataWriter.Write((uint)resource.resource_type);
            _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_GUID | 4, offset));

            switch (resource.resource_type)
            {
                case ResourceType.RENDERABLE_INSTANCE:
                    offset = (int)_dataBufferStream.Position;
                    _dataWriter.Write(resource.index);
                    _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_INT | 4, offset));

                    offset = (int)_dataBufferStream.Position;
                    _dataWriter.Write(resource.count);
                    _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_INT | 4, offset));
                    break;
                case ResourceType.COLLISION_MAPPING:
                    offset = (int)_dataBufferStream.Position;
                    _dataWriter.Write(resource.index);
                    _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_INT | 4, offset));

                    offset = (int)_dataBufferStream.Position;
                    Utilities.Write<ShortGuid>(_dataWriter, resource.entityID);
                    _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_INT | 4, offset));
                    break;
                case ResourceType.ANIMATED_MODEL:
                case ResourceType.DYNAMIC_PHYSICS_SYSTEM:
                    offset = (int)_dataBufferStream.Position;
                    _dataWriter.Write(resource.index);
                    _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_INT | 4, offset));

                    offset = (int)_dataBufferStream.Position;
                    _dataWriter.Write(0);
                    _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_INT | 4, offset));
                    break;
                default:
                    offset = (int)_dataBufferStream.Position;
                    _dataWriter.Write(0);
                    _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_INT | 4, offset));

                    offset = (int)_dataBufferStream.Position;
                    _dataWriter.Write(0);
                    _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_INT | 4, offset));
                    break;
            }

            offset = (int)_dataBufferStream.Position;
            _dataWriter.Write(resource.position.X);
            _dataWriter.Write(resource.position.Y);
            _dataWriter.Write(resource.position.Z);
            _dataWriter.Write(resource.rotation.X);
            _dataWriter.Write(resource.rotation.Y);
            _dataWriter.Write(resource.rotation.Z);
            _commandEntries.Add(new Tuple<uint, int>((uint)CommandTypes.DATA_POSITION | 24, offset));
        }

        private enum CommandTypes : uint
        {
            COMMAND_APPLY_MASK = 0x30000000,
            COMMAND_ADD = 0x10000000,
            COMMAND_REMOVE = 0x20000000,

            COMMAND_ALIGNMENT_MASK = 0xC0000000,
            COMMAND_ALIGNED4_FLAG = 0x40000000,
            COMMAND_ALIGNED16_FLAG = 0x80000000,

            COMMAND_TYPE_MASK = 0x0F000000,
            COMMAND_CONTEXT = 0x01000000,
            COMMAND_DATA = 0x02000000,

            COMMAND_IDENTIFIER_MASK = 0x00FF0000,
            COMMAND_SIZE_MASK = 0x0000FFFF,

            COMMAND_CONTEXT_MASK = COMMAND_CONTEXT | COMMAND_APPLY_MASK | COMMAND_ALIGNMENT_MASK | COMMAND_IDENTIFIER_MASK | COMMAND_SIZE_MASK,
            COMMAND_DATA_MASK = COMMAND_DATA | COMMAND_APPLY_MASK | COMMAND_ALIGNMENT_MASK | COMMAND_IDENTIFIER_MASK | COMMAND_SIZE_MASK,

            CONTEXT_TEMPLATE = COMMAND_CONTEXT | (COMMAND_IDENTIFIER_MASK & (1 << 16)) | (COMMAND_SIZE_MASK & 3),
            CONTEXT_ENTITY = COMMAND_CONTEXT | (COMMAND_IDENTIFIER_MASK & (2 << 16)) | (COMMAND_SIZE_MASK & 5),
            CONTEXT_TYPE = COMMAND_CONTEXT | (COMMAND_IDENTIFIER_MASK & (3 << 16)) | (COMMAND_SIZE_MASK & 3),
            CONTEXT_ALIAS = COMMAND_CONTEXT | (COMMAND_IDENTIFIER_MASK & (4 << 16)) | (COMMAND_SIZE_MASK & 4),
            CONTEXT_PARAMETER = COMMAND_CONTEXT | (COMMAND_IDENTIFIER_MASK & (5 << 16)) | (COMMAND_SIZE_MASK & 6),
            CONTEXT_SEQUENCE = COMMAND_CONTEXT | (COMMAND_IDENTIFIER_MASK & (6 << 16)) | (COMMAND_SIZE_MASK & 5),
            CONTEXT_LINK = COMMAND_CONTEXT | (COMMAND_IDENTIFIER_MASK & (7 << 16)) | (COMMAND_SIZE_MASK & 9),
            CONTEXT_CONNECTOR = COMMAND_CONTEXT | (COMMAND_IDENTIFIER_MASK & (8 << 16)) | (COMMAND_SIZE_MASK & 6),
            CONTEXT_BINDING = COMMAND_CONTEXT | (COMMAND_IDENTIFIER_MASK & (9 << 16)) | (COMMAND_SIZE_MASK & 10),
            CONTEXT_ROOT = COMMAND_CONTEXT | (COMMAND_IDENTIFIER_MASK & (10 << 16)) | (COMMAND_SIZE_MASK & 2),
            CONTEXT_RESOURCE = COMMAND_CONTEXT | (COMMAND_IDENTIFIER_MASK & (11 << 16)) | (COMMAND_SIZE_MASK & 7),
            CONTEXT_METHOD = COMMAND_CONTEXT | (COMMAND_IDENTIFIER_MASK & (12 << 16)) | (COMMAND_SIZE_MASK & 5),
            CONTEXT_PROXY = COMMAND_CONTEXT | (COMMAND_IDENTIFIER_MASK & (13 << 16)) | (COMMAND_SIZE_MASK & 5),
            CONTEXT_TRACK = COMMAND_CONTEXT | (COMMAND_IDENTIFIER_MASK & (14 << 16)) | (COMMAND_SIZE_MASK & 6),
            CONTEXT_BREAKPOINT = COMMAND_CONTEXT | (COMMAND_IDENTIFIER_MASK & (15 << 16)) | (COMMAND_SIZE_MASK & 4),
            CONTEXT_REMOVED = COMMAND_CONTEXT | (COMMAND_IDENTIFIER_MASK & (16 << 16)) | (COMMAND_SIZE_MASK & 6),
            CONTEXT_INVALID = COMMAND_CONTEXT | (COMMAND_IDENTIFIER_MASK & (225 << 16)) | (COMMAND_SIZE_MASK & 0),

            DATA_BOOL = COMMAND_DATA | (COMMAND_IDENTIFIER_MASK & (1 << 16)) | (COMMAND_SIZE_MASK & (1)) | COMMAND_ALIGNED4_FLAG,
            DATA_STRING = COMMAND_DATA | (COMMAND_IDENTIFIER_MASK & (2 << 16)) | (COMMAND_SIZE_MASK & (0)) | COMMAND_ALIGNED4_FLAG,
            DATA_INT = COMMAND_DATA | (COMMAND_IDENTIFIER_MASK & (3 << 16)) | (COMMAND_SIZE_MASK & (4)) | COMMAND_ALIGNED4_FLAG,
            DATA_FLOAT = COMMAND_DATA | (COMMAND_IDENTIFIER_MASK & (4 << 16)) | (COMMAND_SIZE_MASK & (4)) | COMMAND_ALIGNED4_FLAG,
            DATA_PATH = COMMAND_DATA | (COMMAND_IDENTIFIER_MASK & (5 << 16)) | (COMMAND_SIZE_MASK & (0)) | COMMAND_ALIGNED4_FLAG,
            DATA_GUID = COMMAND_DATA | (COMMAND_IDENTIFIER_MASK & (6 << 16)) | (COMMAND_SIZE_MASK & (4)) | COMMAND_ALIGNED4_FLAG,
            DATA_VECTOR = COMMAND_DATA | (COMMAND_IDENTIFIER_MASK & (7 << 16)) | (COMMAND_SIZE_MASK & (12)) | COMMAND_ALIGNED4_FLAG,
            DATA_POSITION = COMMAND_DATA | (COMMAND_IDENTIFIER_MASK & (8 << 16)) | (COMMAND_SIZE_MASK & (24)) | COMMAND_ALIGNED4_FLAG,
            DATA_ENUM = COMMAND_DATA | (COMMAND_IDENTIFIER_MASK & (9 << 16)) | (COMMAND_SIZE_MASK & (8)) | COMMAND_ALIGNED4_FLAG,
            DATA_FLOAT_TRACK = COMMAND_DATA | (COMMAND_IDENTIFIER_MASK & (10 << 16)) | (COMMAND_SIZE_MASK & (0)) | COMMAND_ALIGNED4_FLAG,
            DATA_EVENT_TRACK = COMMAND_DATA | (COMMAND_IDENTIFIER_MASK & (11 << 16)) | (COMMAND_SIZE_MASK & (0)) | COMMAND_ALIGNED4_FLAG,
            DATA_SPLINE = COMMAND_DATA | (COMMAND_IDENTIFIER_MASK & (12 << 16)) | (COMMAND_SIZE_MASK & (0)) | COMMAND_ALIGNED4_FLAG,
            DATA_FILE_PATH = COMMAND_DATA | (COMMAND_IDENTIFIER_MASK & (13 << 16)) | (COMMAND_SIZE_MASK & (0)) | COMMAND_ALIGNED4_FLAG,
            DATA_INVALID = COMMAND_DATA | (COMMAND_IDENTIFIER_MASK & (225 << 16)) | (COMMAND_SIZE_MASK & (0)) | COMMAND_ALIGNED4_FLAG,
        };
    }
}