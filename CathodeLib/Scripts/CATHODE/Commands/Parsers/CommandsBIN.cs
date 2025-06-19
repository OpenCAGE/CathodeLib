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
                //Create Composites
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

                //Create Entities
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
                                    FunctionEntity func = new FunctionEntity()
                                    {
                                        shortGUID = Utilities.Consume<ShortGuid>(reader, command_entries[i + 2].Item2),
                                        function = Utilities.Consume<ShortGuid>(reader, command_entries[i + 3].Item2)
                                    };
                                    string name = Utilities.ReadString(reader, command_entries[i + 4].Item2);
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
                                        alias = new EntityPath() { path = Utilities.ConsumeArray<ShortGuid>(reader, (int)(command_entries[i + 3].Item1 & (uint)CommandTypes.COMMAND_SIZE_MASK), command_entries[i + 3].Item2) }
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
                                        proxy = new EntityPath() { path = Utilities.ConsumeArray<ShortGuid>(reader, (int)(command_entries[i + 4].Item1 & (uint)CommandTypes.COMMAND_SIZE_MASK), command_entries[i + 4].Item2) }
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

                //Apply TriggerSequence and CAGEAnimation data
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
                                        if (ent is TriggerSequence)
                                            trig = (TriggerSequence)ent;
                                        else
                                        {
                                            cache.Item2.Remove(ent.shortGUID);
                                            cache.Item1.functions.Remove((FunctionEntity)ent);
                                            trig = new TriggerSequence(ent.shortGUID);
                                            cache.Item2.Add(ent.shortGUID, trig);
                                            cache.Item1.functions.Add(trig);
                                        }
                                    }
                                    else
                                    {
                                        trig = new TriggerSequence(entityID);
                                        cache.Item2.Add(ent.shortGUID, trig);
                                        cache.Item1.functions.Add(trig);
                                    }
                                    if (Utilities.Consume<ShortGuid>(reader, command_entries[i + 3].Item2).ToString() != "TriggerSequence")
                                    {
                                        string sdffsdff = "";
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
                                        if (ent is TriggerSequence)
                                            trig = (TriggerSequence)ent;
                                        else
                                        {
                                            cache.Item2.Remove(ent.shortGUID);
                                            cache.Item1.functions.Remove((FunctionEntity)ent);
                                            trig = new TriggerSequence(ent.shortGUID);
                                            cache.Item2.Add(ent.shortGUID, trig);
                                            cache.Item1.functions.Add(trig);
                                        }
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
                                        connectedEntity = new EntityPath() { path = Utilities.ConsumeArray<ShortGuid>(reader, (int)(command_entries[i + 4].Item1 & (uint)CommandTypes.COMMAND_SIZE_MASK), command_entries[i + 4].Item2) }
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
                                        if (ent is CAGEAnimation)
                                            cageAnim = (CAGEAnimation)ent;
                                        else
                                        {
                                            cache.Item2.Remove(ent.shortGUID);
                                            cache.Item1.functions.Remove((FunctionEntity)ent);
                                            cageAnim = new CAGEAnimation(ent.shortGUID);
                                            cache.Item2.Add(ent.shortGUID, cageAnim);
                                            cache.Item1.functions.Add(cageAnim);
                                        }
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
                                        //track_guid = 4
                                        target_param = Utilities.Consume<ShortGuid>(reader, command_entries[i + 5].Item2),
                                        target_param_type = (DataType)Utilities.Consume<uint>(reader, command_entries[i + 6].Item2),
                                        target_sub_param = Utilities.Consume<ShortGuid>(reader, command_entries[i + 7].Item2),
                                        //binding_type = 8
                                        connectedEntity = new EntityPath() { path = Utilities.ConsumeArray<ShortGuid>(reader, (int)(command_entries[i + 9].Item1 & (uint)CommandTypes.COMMAND_SIZE_MASK), command_entries[i + 9].Item2) }
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
                                        if (ent is CAGEAnimation)
                                            cageAnim = (CAGEAnimation)ent;
                                        else
                                        {
                                            cache.Item2.Remove(ent.shortGUID);
                                            cache.Item1.functions.Remove((FunctionEntity)ent);
                                            cageAnim = new CAGEAnimation(ent.shortGUID);
                                            cache.Item2.Add(ent.shortGUID, cageAnim);
                                            cache.Item1.functions.Add(cageAnim);
                                        }
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
                                    switch (trackType.enumIndex)
                                    {
                                        case 0: //FLOAT
                                            {
                                                CAGEAnimation.FloatTrack track = cageAnim.animations.FirstOrDefault(o => o.shortGUID == trackID);
                                                if (track == null)
                                                {
                                                    track = new CAGEAnimation.FloatTrack() { shortGUID = trackID };
                                                    cageAnim.animations.Add(track);
                                                }
                                                track.keyframes.Add(new CAGEAnimation.FloatTrack.Keyframe()
                                                {
                                                    mode = (CAGEAnimation.InterpolationMode)Utilities.Consume<int>(reader, command_entries[i + 5].Item2),
                                                    time = Utilities.Consume<float>(reader, command_entries[i + 5].Item2 + 4),
                                                    value = Utilities.Consume<Vector2>(reader, command_entries[i + 5].Item2 + 8),
                                                    tan_in = Utilities.Consume<Vector2>(reader, command_entries[i + 5].Item2 + 16),
                                                    tan_out = Utilities.Consume<Vector2>(reader, command_entries[i + 5].Item2 + 24),
                                                });
                                            }
                                            break;
                                        case 4: //GUID
                                        case 3: //STRING
                                        case 5: //MASTERING
                                            {
                                                CAGEAnimation.EventTrack track = cageAnim.events.FirstOrDefault(o => o.shortGUID == trackID);
                                                if (track == null)
                                                {
                                                    track = new CAGEAnimation.EventTrack() { shortGUID = trackID };
                                                    cageAnim.events.Add(track);
                                                }
                                                track.keyframes.Add(new CAGEAnimation.EventTrack.Keyframe()
                                                {
                                                    mode = (CAGEAnimation.InterpolationMode)Utilities.Consume<int>(reader, command_entries[i + 5].Item2),
                                                    time = Utilities.Consume<float>(reader, command_entries[i + 5].Item2 + 4),
                                                    forward = Utilities.Consume<ShortGuid>(reader, command_entries[i + 5].Item2 + 8),
                                                    reverse = Utilities.Consume<ShortGuid>(reader, command_entries[i + 5].Item2 + 12),
                                                    track_type = (CAGEAnimation.TrackType)Utilities.Consume<int>(reader, command_entries[i + 5].Item2 + 16),
                                                    duration = Utilities.Consume<float>(reader, command_entries[i + 5].Item2 + 20),
                                                });
                                            }
                                            break;
                                    }
                                }
                                break;
                        };
                    }
                }

                //Apply links and parameters
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
                                    ShortGuid entityID = Utilities.Consume<ShortGuid>(reader, command_entries[i + 2].Item2);
                                    if (cache.Item2.TryGetValue(entityID, out Entity ent))
                                    {
                                        ResourceReference resource = new ResourceReference();
                                        reader.BaseStream.Position = command_entries[i + 6].Item2;
                                        resource.position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                                        resource.rotation = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                                        //resource.resource_id
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
                                        if (ent.variant != EntityVariant.FUNCTION)
                                        {
                                            string gsdfgsdf = "";
                                        }
                                        ((FunctionEntity)ent).resources.Add(resource);
                                    }
                                }
                                break;
                            case (uint)CommandTypes.COMMAND_IDENTIFIER_MASK & (uint)CommandTypes.CONTEXT_PARAMETER:
                                {
                                    Tuple<Composite, Dictionary<ShortGuid, Entity>> cache = entityCache[Utilities.Consume<ShortGuid>(reader, command_entries[i + 1].Item2)];
                                    ShortGuid entityID = Utilities.Consume<ShortGuid>(reader, command_entries[i + 2].Item2);
                                    if (cache.Item2.TryGetValue(entityID, out Entity ent))
                                    {
                                        ShortGuid type = Utilities.Consume<ShortGuid>(reader, command_entries[i + 3].Item2); //FunctionType guid?
                                        ShortGuid paramName = Utilities.Consume<ShortGuid>(reader, command_entries[i + 4].Item2);
                                        ParameterData paramData = null;
                                        reader.BaseStream.Position = command_entries[i + 5].Item2;
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
                                            case (uint)CommandTypes.COMMAND_IDENTIFIER_MASK & (uint)CommandTypes.DATA_STRING:
                                                paramData = new cString(Utilities.ReadString(reader));
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
                                            case (uint)CommandTypes.COMMAND_IDENTIFIER_MASK & (uint)CommandTypes.DATA_FILE_PATH:
                                                {
                                                    //todo
                                                    break;
                                                }
                                            case (uint)CommandTypes.COMMAND_IDENTIFIER_MASK & (uint)CommandTypes.DATA_SPLINE:
                                                {
                                                    //toodo
                                                    break;
                                                }
                                        };
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
            }

            EntryPoints[1] = Entries.FirstOrDefault(o => o.name == "GLOBAL").shortGUID;
            EntryPoints[2] = Entries.FirstOrDefault(o => o.name == "PAUSEMENU").shortGUID;
        }

        public static void Write(ShortGuid[] EntryPoints, List<Composite> Entries, out byte[] content)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                using (BinaryWriter writer = new BinaryWriter(stream))
                {
                    writer.BaseStream.SetLength(0);


                }
                content = stream.ToArray();
            }
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