using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using CATHODE;
using static CATHODE.HavokPackfile;

namespace CathodeLib.Havok
{
    /* Writing a NEW physics system - one hkpPhysicsSystem holding one dynamic hkpRigidBody over an
       hkpConvexVerticesShape built from a mesh's convex hull - into a 2012 packfile (PHYSICS.HKX / PHYSICS.HKX64).

       What retail ships, measured over every PC level (5,741 systems, 7,630 bodies; ReleaseSweep modes physcensus,
       physfields, physbody, physnames): 86% of systems hold one body and 71% of bodies are an hkpConvexVerticesShape
       (the rest boxes behind translate shapes, list shapes, cylinders, capsules and a few MOPPs). A dynamic prop is a
       BOX_INERTIA motion with quality MOVING, friction 0.5 and restitution 0.4 or 0.2. The convex shape stores its
       hull shrunk by the convex radius (hull + radius is the modelled surface), its AABB over the shrunk vertices, the
       vertices four to a FourVectors group with the last one repeated as padding, and plane equations one radius
       outside the vertices. The body's derived fields follow its shape: objectRadius is the farthest corner of the
       AABB grown by the radius as seen from the centre of mass (5,155 of 5,446 convex bodies), and
       allowedPenetrationDepth is a fifth of that AABB's smallest full extent (every dynamic body). The motion state
       holds the body where its model sits in the composite, with centreOfMass0/1 = translation + rotated COM.
       Retail names the system after its composite's path and the body after the ModelReference entity it drives
       (241 of 258 on Torrens). Nothing we know of reads either name; they are copied so a new system looks like a
       retail one. */
    /// <summary>Appending new physics systems to a <see cref="HavokPackfile"/> (extension methods), and the convex shape builder they use.</summary>
    public static class PhysicsSystemWriter
    {
        /// <summary>The hull is simplified to at most this many corners; shrinking by the convex radius can split corners, and up to twice
        /// this many are stored (retail keeps 97% of its convex shapes at or under 64 vertices and ships 151 between 65 and 128).</summary>
        public const int ConvexMaxVertices = 64;
        /// <summary>Havok's default convex radius, the largest retail uses.</summary>
        const float ConvexRadiusCap = 0.05f;
        /// <summary>Retail radii run from 1% to 5% of the largest half extent, most near 3%.</summary>
        const double ConvexRadiusRelative = 0.03;

        /// <summary>
        /// Build the convex shape for a physics body from a mesh's vertices: the convex hull, simplified to
        /// <paramref name="maxVertices"/> corners if it has more, shrunk by a convex radius so that hull + radius is
        /// the original surface, with its solid mass properties.
        /// </summary>
        /// <param name="points">The mesh's vertices, in the body's own space, in metres.</param>
        /// <param name="convexRadius">Leave null for the derived radius: 3% of the largest half extent, at most 5 cm,
        /// and never more than a quarter of the distance from the centre to the nearest face.</param>
        public static ConvexBody BuildConvexBody(IList<Vector3> points, int maxVertices = ConvexMaxVertices, float? convexRadius = null)
        {
            if (points == null)
                throw new ArgumentNullException(nameof(points));
            if (maxVertices < 4)
                throw new ArgumentOutOfRangeException(nameof(maxVertices), "A convex body needs at least four vertices.");
            return ConvexBodyBuilder.Build(points, maxVertices, convexRadius);
        }

        /// <summary>
        /// Append a new physics system holding one dynamic rigid body over <paramref name="shape"/>, registered last
        /// in <c>hkpPhysicsData.systems[]</c> so its <see cref="HavokPackfile.PhysicsSystem.SystemIndex"/> is the new highest.
        /// The system, body and shape are modelled on a one-body convex system already in this file.
        /// </summary>
        /// <remarks>
        /// Both packfiles of a level (32 and 64-bit) need the system at the same index; call this on each with the same
        /// shape and settings and compare the results, restoring a <see cref="CollisionProxyWriter.CreateCheckpoint"/> on either side if they
        /// disagree. Not available on the mobile/Switch tagfiles.
        /// </remarks>
        public static PhysicsSystem AddConvexPhysicsSystem(this HavokPackfile packfile, string systemName, ConvexBody shape, PhysicsBodySettings body)
        {
            if (packfile.Tagfile != null)
                throw new NotSupportedException("New physics systems can only be written to the PC packfiles, not a mobile/Switch tagfile.");
            if (shape == null || body == null)
                throw new ArgumentNullException(shape == null ? nameof(shape) : nameof(body));
            if (shape.Vertices.Count < 4 || shape.Planes.Count < 4)
                throw new ArgumentException("The convex shape has no volume.", nameof(shape));
            if (!(body.Mass > 0f) || float.IsInfinity(body.Mass))
                throw new ArgumentException("A dynamic body needs a positive mass.", nameof(body));

            PackfileObject physicsData = null;
            for (int i = 0; i < packfile.Objects.Count; i++)
                if (packfile.Objects[i].Class == ObjectClass.PhysicsData) { physicsData = packfile.Objects[i]; break; }
            if (physicsData == null)
                throw new InvalidOperationException("This packfile has no hkpPhysicsData to register a physics system in.");

            if (!FindConvexBodyTemplate(packfile, out PackfileObject systemTemplate, out PackfileObject bodyTemplate, out PackfileObject shapeTemplate))
                throw new InvalidOperationException("This packfile holds no one-body convex physics system to model the new one on.");

            int ptr = packfile.Header.PointerSize;
            bool is64 = ptr == 8;
            PhysicsLayout L = PhysicsLayout.For(is64);

            byte[] systemNameBytes = AsciiZ(systemName);
            byte[] bodyNameBytes = AsciiZ(body.Name);
            int groups = (shape.Vertices.Count + 3) / 4;

            //Retail's order: the system, its body pointer array and name, the body and its name, the shape and its arrays
            int sysOff = AlignPayload(packfile.DataPayload.Length, 16);
            int sysArray = sysOff + L.SystemSize;
            int sysName = AlignPayload(sysArray + ptr, 16);
            int bodyOff = AlignPayload(sysName + systemNameBytes.Length, 16);
            int bodyName = bodyOff + L.BodySize;
            int shapeOff = AlignPayload(bodyName + bodyNameBytes.Length, 16);
            int verticesOff = shapeOff + L.ShapeSize;
            int planesOff = verticesOff + groups * 48;
            int end = AlignPayload(planesOff + shape.Planes.Count * 16, 16);

            byte[] grown = new byte[end];
            Buffer.BlockCopy(packfile.DataPayload, 0, grown, 0, packfile.DataPayload.Length);
            packfile.DataPayload = grown;
            byte[] d = packfile.DataPayload;

            // -- hkpPhysicsSystem: one body, no constraints, actions or phantoms, active
            Buffer.BlockCopy(d, (int)systemTemplate.DataOffset, d, sysOff, L.SystemSize);
            CollisionProxyWriter.WriteArrayHeader(d, sysOff + L.SystemRigidBodies, ptr, 1);
            WriteEmptyArray(d, sysOff + L.SystemConstraints, ptr);
            WriteEmptyArray(d, sysOff + L.SystemActions, ptr);
            WriteEmptyArray(d, sysOff + L.SystemPhantoms, ptr);
            Array.Clear(d, sysOff + L.SystemName, ptr);
            Array.Clear(d, sysOff + L.SystemUserData, ptr);
            d[sysOff + L.SystemActive] = 1;
            Array.Clear(d, sysArray, sysName - sysArray);
            Buffer.BlockCopy(systemNameBytes, 0, d, sysName, systemNameBytes.Length);
            packfile.LocalFixups.Add(new LocalFixup { Src = (uint)(sysOff + L.SystemRigidBodies), Dst = (uint)sysArray });
            packfile.LocalFixups.Add(new LocalFixup { Src = (uint)(sysOff + L.SystemName), Dst = (uint)sysName });
            packfile.GlobalFixups.Add(new GlobalFixup { Src = (uint)sysArray, DstSectionIndex = 2, Dst = (uint)bodyOff });

            // -- hkpRigidBody: the template's bytes, then everything that follows from this shape, mass and placement
            Buffer.BlockCopy(d, (int)bodyTemplate.DataOffset, d, bodyOff, L.BodySize);
            Buffer.BlockCopy(bodyNameBytes, 0, d, bodyName, bodyNameBytes.Length);
            Array.Clear(d, bodyOff + L.BodyShape, ptr);
            Array.Clear(d, bodyOff + L.BodyName, ptr);
            packfile.GlobalFixups.Add(new GlobalFixup { Src = (uint)(bodyOff + L.BodyShape), DstSectionIndex = 2, Dst = (uint)shapeOff });
            packfile.LocalFixups.Add(new LocalFixup { Src = (uint)(bodyOff + L.BodyName), Dst = (uint)bodyName });

            float r = shape.ConvexRadius;
            Vector3 grownHalf = shape.AabbHalfExtents + new Vector3(r);
            float minFull = 2f * Math.Min(grownHalf.X, Math.Min(grownHalf.Y, grownHalf.Z));
            WriteSingle(d, bodyOff + L.BodyAllowedPenetration, 0.2f * minFull);
            WriteSingle(d, bodyOff + L.BodyFriction, body.Friction);
            WriteSingle(d, bodyOff + L.BodyRestitution, body.Restitution);

            Quaternion rotation = Quaternion.Normalize(body.Rotation);
            Matrix4x4 basis = Matrix4x4.CreateFromQuaternion(rotation);
            Vector3 com = shape.CenterOfMass;
            Vector3 comWorld = body.Position + Vector3.Transform(com, rotation);
            int R = bodyOff + L.MotionState;
            //hkRotation is three columns, each the image of an axis: System.Numerics keeps those images in its rows
            WriteVector4(d, R, new Vector4(basis.M11, basis.M12, basis.M13, 0f));
            WriteVector4(d, R + 16, new Vector4(basis.M21, basis.M22, basis.M23, 0f));
            WriteVector4(d, R + 32, new Vector4(basis.M31, basis.M32, basis.M33, 0f));
            WriteSingle(d, R + 48, body.Position.X);
            WriteSingle(d, R + 52, body.Position.Y);
            WriteSingle(d, R + 56, body.Position.Z);
            WriteVector4(d, R + 64, new Vector4(comWorld, 0f));    //centerOfMass0
            WriteVector4(d, R + 80, new Vector4(comWorld, 0f));    //centerOfMass1
            WriteVector4(d, R + 96, new Vector4(rotation.X, rotation.Y, rotation.Z, rotation.W));   //rotation0
            WriteVector4(d, R + 112, new Vector4(rotation.X, rotation.Y, rotation.Z, rotation.W));  //rotation1
            WriteVector4(d, R + 128, new Vector4(com, 0f));        //centerOfMassLocal
            WriteVector4(d, R + 144, Vector4.Zero);                //deltaAngle
            Vector3 comFromCentre = com - shape.AabbCenter;
            Vector3 farCorner = new Vector3(Math.Abs(comFromCentre.X), Math.Abs(comFromCentre.Y), Math.Abs(comFromCentre.Z)) + grownHalf;
            WriteSingle(d, R + 160, farCorner.Length());           //objectRadius
            Vector3 inertia = shape.InertiaPerKg * body.Mass;
            WriteVector4(d, R + 176, new Vector4(1f / inertia.X, 1f / inertia.Y, 1f / inertia.Z, 1f / body.Mass));
            WriteVector4(d, R + 192, Vector4.Zero);                //linearVelocity
            WriteVector4(d, R + 208, Vector4.Zero);                //angularVelocity

            // -- hkpConvexVerticesShape: no connectivity, as 3,579 of retail's 5,446
            Buffer.BlockCopy(d, (int)shapeTemplate.DataOffset, d, shapeOff, L.ShapeSize);
            WriteUInt32(d, shapeOff + L.ShapeUserData, 1024);      //every retail shape without connectivity carries 0x400
            if (is64) WriteUInt32(d, shapeOff + L.ShapeUserData + 4, 0);
            WriteSingle(d, shapeOff + L.ShapeRadius, r);
            WriteVector4(d, shapeOff + L.ShapeAabbHalf, new Vector4(shape.AabbHalfExtents, 0f));
            WriteVector4(d, shapeOff + L.ShapeAabbCenter, new Vector4(shape.AabbCenter, 0f));
            CollisionProxyWriter.WriteArrayHeader(d, shapeOff + L.ShapeRotatedVertices, ptr, groups);
            WriteUInt32(d, shapeOff + L.ShapeNumVertices, (uint)shape.Vertices.Count);
            d[shapeOff + L.ShapeUseSpuBuffer] = 0;
            CollisionProxyWriter.WriteArrayHeader(d, shapeOff + L.ShapePlaneEquations, ptr, shape.Planes.Count);
            Array.Clear(d, shapeOff + L.ShapeConnectivity, ptr);
            packfile.LocalFixups.Add(new LocalFixup { Src = (uint)(shapeOff + L.ShapeRotatedVertices), Dst = (uint)verticesOff });
            packfile.LocalFixups.Add(new LocalFixup { Src = (uint)(shapeOff + L.ShapePlaneEquations), Dst = (uint)planesOff });
            for (int g = 0; g < groups; g++)
            {
                int at = verticesOff + g * 48;
                for (int k = 0; k < 4; k++)
                {
                    Vector3 v = shape.Vertices[Math.Min(g * 4 + k, shape.Vertices.Count - 1)];   //padding repeats the last, as retail
                    WriteSingle(d, at + k * 4, v.X);
                    WriteSingle(d, at + 16 + k * 4, v.Y);
                    WriteSingle(d, at + 32 + k * 4, v.Z);
                }
            }
            for (int p = 0; p < shape.Planes.Count; p++)
                WriteVector4(d, planesOff + p * 16, shape.Planes[p]);

            // -- register: objects in payload order, then the system in hkpPhysicsData.systems[]
            AddObject(packfile, sysOff, systemTemplate, ObjectClass.PhysicsSystem);
            AddObject(packfile, bodyOff, bodyTemplate, ObjectClass.RigidBody);
            AddObject(packfile, shapeOff, shapeTemplate, shapeTemplate.Class);
            packfile.AppendPhysicsSystemToPhysicsData((uint)sysOff);

            //Only the new view is made: re-parsing would replace every PhysicsSystem object, and the resources and
            //PHYSICS.MAP rows holding the old ones would be left pointing at copies no longer in the list
            PhysicsSystem created = new PhysicsSystem
            {
                SystemIndex = packfile.PhysicsSystems.Count,
                DataOffset = (uint)sysOff,
                Name = packfile.ReadStringPtr((uint)(sysOff + L.SystemName)),
                Object = packfile.Objects[packfile.Objects.Count - 3],
            };
            created.Object.ProxyIndex = created.SystemIndex;
            packfile.PhysicsSystems.Add(created);

            //The readers are the first oracle: the system must list at its index with the body that went in
            if (!packfile.TryReadPointerArray((uint)(physicsData.DataOffset + packfile.ObjectHeaderSize + (uint)ptr), out List<uint> systems)
                || systems.Count != packfile.PhysicsSystems.Count || systems[systems.Count - 1] != (uint)sysOff)
                throw new InvalidOperationException("The new physics system did not land last in hkpPhysicsData.systems[].");
            List<RigidBodyInfo> bodies = packfile.GetRigidBodies(created);
            if (bodies.Count != 1 || bodies[0].ShapeClassName != "hkpConvexVerticesShape" || bodies[0].Name != (body.Name ?? "")
                || Math.Abs(bodies[0].Mass - body.Mass) > 1e-3f * body.Mass)
                throw new InvalidOperationException("The new physics system reads back wrongly.");
            return created;
        }

        static void AddObject(HavokPackfile packfile, int offset, PackfileObject template, ObjectClass cls)
        {
            packfile.VirtualFixups.Add(new VirtualFixup { Src = (uint)offset, SectionIndex = 0, NameOffset = template.ClassNameOffset });
            packfile.Objects.Add(new PackfileObject
            {
                DataOffset = (uint)offset,
                ClassNameOffset = template.ClassNameOffset,
                ClassName = template.ClassName,
                Class = cls,
                ProxyIndex = -1,
            });
        }

        static void WriteEmptyArray(byte[] d, int field, int ptr)
        {
            Array.Clear(d, field, ptr + 4);
            WriteUInt32(d, field + ptr + 4, 0x80000000u);
        }

        static byte[] AsciiZ(string s)
        {
            s = s ?? "";
            byte[] text = Encoding.ASCII.GetBytes(s);
            byte[] z = new byte[text.Length + 1];
            Buffer.BlockCopy(text, 0, z, 0, text.Length);
            return z;
        }

        /// <summary>
        /// A retail one-body system in this file whose body is a BOX_INERTIA dynamic of quality MOVING over an
        /// hkpConvexVerticesShape without connectivity - the commonest retail prop, and the layout every offset in
        /// <see cref="PhysicsLayout"/> was read from. Its body and shape must hold no fixups but the ones we replace.
        /// </summary>
        static bool FindConvexBodyTemplate(HavokPackfile packfile, out PackfileObject systemObject, out PackfileObject bodyObject, out PackfileObject shapeObject)
        {
            systemObject = bodyObject = shapeObject = null;
            PhysicsLayout L = PhysicsLayout.For(packfile.Header.PointerSize == 8);
            var byOffset = new Dictionary<uint, PackfileObject>(packfile.Objects.Count);
            foreach (PackfileObject o in packfile.Objects) byOffset[o.DataOffset] = o;
            var local = new Dictionary<uint, uint>(packfile.LocalFixups.Count);
            foreach (LocalFixup f in packfile.LocalFixups) local[f.Src] = f.Dst;
            var global = new Dictionary<uint, uint>(packfile.GlobalFixups.Count);
            foreach (GlobalFixup f in packfile.GlobalFixups) global[f.Src] = f.Dst;
            var fixupSources = new List<uint>(local.Keys.Concat(global.Keys));
            fixupSources.Sort();

            bool OnlyFixupsAt(uint start, int length, params int[] allowed)
            {
                int i = fixupSources.BinarySearch(start);
                if (i < 0) i = ~i;
                for (; i < fixupSources.Count && fixupSources[i] < start + (uint)length; i++)
                    if (Array.IndexOf(allowed, (int)(fixupSources[i] - start)) < 0) return false;
                return true;
            }

            foreach (PhysicsSystem system in packfile.PhysicsSystems)
            {
                if (!byOffset.TryGetValue(system.DataOffset, out PackfileObject sys)) continue;
                if (!packfile.TryGetRigidBodyOffsets(system, out List<uint> bodies) || bodies.Count != 1) continue;
                uint b = bodies[0];
                if (!byOffset.TryGetValue(b, out PackfileObject bo) || bo.ClassName != "hkpRigidBody") continue;
                if (b + (uint)L.BodySize > (uint)packfile.DataPayload.Length) continue;
                if (packfile.DataPayload[b + (uint)L.MotionType] != 3 || (sbyte)packfile.DataPayload[b + (uint)L.BodyQuality] != 4) continue;
                if (!global.TryGetValue(b + (uint)L.BodyShape, out uint s) || !byOffset.TryGetValue(s, out PackfileObject so)) continue;
                if (so.ClassName != "hkpConvexVerticesShape" || global.ContainsKey(s + (uint)L.ShapeConnectivity)) continue;
                if (!local.TryGetValue(b + (uint)L.BodyName, out uint nameAt) || nameAt != b + (uint)L.BodySize) continue;
                if (!OnlyFixupsAt(b, L.BodySize, L.BodyShape, L.BodyName)) continue;
                if (!OnlyFixupsAt(s, L.ShapeSize, L.ShapeRotatedVertices, L.ShapePlaneEquations)) continue;
                if (!OnlyFixupsAt(system.DataOffset, L.SystemSize, L.SystemRigidBodies, L.SystemName)) continue;
                systemObject = sys; bodyObject = bo; shapeObject = so;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Field offsets in the 2012 packfile layouts of hkpPhysicsSystem, hkpRigidBody and hkpConvexVerticesShape,
        /// read off every retail file (the variance maps of physcensus --vary, checked field by field by physfields).
        /// </summary>
        sealed class PhysicsLayout
        {
            public int SystemSize, SystemRigidBodies, SystemConstraints, SystemActions, SystemPhantoms, SystemName, SystemUserData, SystemActive;
            public int BodySize, BodyShape, BodyQuality, BodyFilterInfo, BodyAllowedPenetration, BodyName, BodyFriction, BodyRestitution, MotionState;
            /// <summary>hkpMotion::m_type, just after the inline motion's hkReferencedObject (8 bytes on 32-bit, 16 on 64-bit).</summary>
            public int MotionType;
            public int ShapeSize, ShapeUserData, ShapeRadius, ShapeAabbHalf, ShapeAabbCenter, ShapeRotatedVertices, ShapeNumVertices, ShapeUseSpuBuffer, ShapePlaneEquations, ShapeConnectivity;

            static readonly PhysicsLayout Packfile32 = new PhysicsLayout
            {
                SystemSize = 80, SystemRigidBodies = 8, SystemConstraints = 20, SystemActions = 32, SystemPhantoms = 44, SystemName = 56, SystemUserData = 60, SystemActive = 64,
                BodySize = 544, BodyShape = 16, BodyQuality = 42, BodyFilterInfo = 44, BodyAllowedPenetration = 92, BodyName = 120, BodyFriction = 140, BodyRestitution = 144, MotionState = 240, MotionType = 232,
                ShapeSize = 112, ShapeUserData = 8, ShapeRadius = 16, ShapeAabbHalf = 32, ShapeAabbCenter = 48, ShapeRotatedVertices = 64, ShapeNumVertices = 76, ShapeUseSpuBuffer = 80, ShapePlaneEquations = 84, ShapeConnectivity = 96,
            };
            static readonly PhysicsLayout Packfile64 = new PhysicsLayout
            {
                SystemSize = 112, SystemRigidBodies = 16, SystemConstraints = 32, SystemActions = 48, SystemPhantoms = 64, SystemName = 80, SystemUserData = 88, SystemActive = 96,
                BodySize = 720, BodyShape = 32, BodyQuality = 74, BodyFilterInfo = 76, BodyAllowedPenetration = 136, BodyName = 176, BodyFriction = 204, BodyRestitution = 208, MotionState = 368, MotionType = 352,
                ShapeSize = 128, ShapeUserData = 16, ShapeRadius = 32, ShapeAabbHalf = 48, ShapeAabbCenter = 64, ShapeRotatedVertices = 80, ShapeNumVertices = 96, ShapeUseSpuBuffer = 100, ShapePlaneEquations = 104, ShapeConnectivity = 120,
            };
            public static PhysicsLayout For(bool is64) => is64 ? Packfile64 : Packfile32;
        }

        // ------------------------------------------------------------------ the convex body

        static class ConvexBodyBuilder
        {
            /// <summary>Above this many welded points the hull is taken over support points in fixed directions rather than every point.</summary>
            const int ExactHullPointLimit = 4000;
            const int SupportDirections = 1024;

            public static ConvexBody Build(IList<Vector3> input, int maxVertices, float? requestedRadius)
            {
                List<D3> points = Weld(input);
                if (points.Count < 4)
                    throw new ArgumentException("A physics body needs a mesh with at least four distinct vertices.");
                double scale = Scale(points);
                if (!(scale > 1e-6))
                    throw new ArgumentException("The mesh has no size.");

                List<D3> directions = Directions(points, SupportDirections);
                List<D3> candidates = points.Count > ExactHullPointLimit ? SupportPoints(points, directions) : points;
                Hull full;
                try { full = Hull.Build(candidates, scale); }
                catch (ArgumentException) when (candidates == points)
                {
                    //Near-coplanar noise the exact hull cannot settle: the hull over support points is within the
                    //angular spacing of the directions, far inside the convex radius
                    full = Hull.Build(SupportPoints(points, directions), scale);
                }
                int fullVertices = full.VertexCount;

                //Shrinking splits every corner where more than three faces meet, so the vertex limit is checked on the
                //shrunk shape: the surface is simplified to fewer and fewer corners until what is stored fits in twice the budget
                List<D3> fullCorners = full.Vertices();
                List<D3> surface = null, shrunk = null;
                List<int> triangles = null;
                List<Plane> shrunkPlanes = null;
                double volume = 0, radius = 0;
                D3 centre = default, inertiaPerKg = default;
                List<D3> firstSurface = null; List<int> firstTriangles = null; List<Plane> firstPlanes = null;
                double firstVolume = 0; D3 firstCentre = default, firstInertia = default;
                for (int target = maxVertices; ; target = Math.Max(4, (int)(target * 0.85)))
                {
                    Hull hull = fullCorners.Count <= target ? Hull.Build(fullCorners, scale) : Simplify(fullCorners, target, scale);
                    surface = hull.Vertices();
                    triangles = hull.Triangles(surface);
                    MassProperties(surface, triangles, out volume, out centre, out inertiaPerKg);
                    if (!(volume > scale * scale * scale * 1e-9))
                        throw new ArgumentException("The mesh is flat: a physics body needs volume.");

                    //Radius: 3% of the largest half extent, capped at Havok's default, and small against the hull's thinnest part
                    List<Plane> planes = MergedPlanes(surface, triangles, scale);
                    D3 min = surface[0], max = surface[0];
                    foreach (D3 p in surface) { min = D3.Min(min, p); max = D3.Max(max, p); }
                    D3 half = (max - min) * 0.5;
                    double inradius = double.MaxValue;
                    foreach (Plane pl in planes) inradius = Math.Min(inradius, -(pl.N.Dot(centre) + pl.D));
                    radius = requestedRadius ?? Math.Min(ConvexRadiusCap, ConvexRadiusRelative * Math.Max(half.X, Math.Max(half.Y, half.Z)));
                    radius = Math.Max(0.0, Math.Min(radius, 0.25 * inradius));

                    //Shrinking moves a corner in by the radius over the sine of its half angle, so a knife edge moves much
                    //further than the radius: halve the radius until hull + radius keeps the surface's box to within 1%
                    //of its size (the role Havok's maxShrinkingVerticesDisplacement plays)
                    shrunk = surface;
                    shrunkPlanes = planes;
                    for (int halving = 0; ; halving++)
                    {
                        if (!(radius > scale * 1e-6)) { radius = 0; shrunk = surface; shrunkPlanes = planes; break; }
                        List<D3> s = Shrink(planes, centre, radius, scale);
                        //A corner where many faces meet splits into a tight cluster when they all move in; keep one of each
                        //cluster (the shape moves by under a quarter of the radius) so the stored vertices stay distinct in
                        //single precision and their count down
                        if (s != null) s = Hull.Merge(s, Math.Max(0.25 * radius, scale * 1e-6));
                        List<D3> candidate = null;
                        List<Plane> candidatePlanes = null;
                        if (s != null && s.Count >= 4)
                        {
                            try
                            {
                                Hull sh = Hull.Build(s, scale);
                                candidate = sh.Vertices();
                                candidatePlanes = MergedPlanes(candidate, sh.Triangles(candidate), scale);
                            }
                            catch (ArgumentException) { candidate = null; }
                        }
                        if (candidate != null)
                        {
                            D3 smin = candidate[0], smax = candidate[0];
                            foreach (D3 p in candidate) { smin = D3.Min(smin, p); smax = D3.Max(smax, p); }
                            D3 lost = (half - (smax - smin) * 0.5) - new D3(radius, radius, radius);
                            if (Math.Max(lost.X, Math.Max(lost.Y, lost.Z)) <= 0.01 * scale)
                            {
                                shrunk = candidate;
                                shrunkPlanes = candidatePlanes;
                                break;
                            }
                        }
                        if (requestedRadius != null && candidate != null) { shrunk = candidate; shrunkPlanes = candidatePlanes; break; }
                        radius = halving < 6 ? radius * 0.5 : 0;
                    }

                    if (firstSurface == null)
                    {
                        firstSurface = surface; firstTriangles = triangles; firstPlanes = planes;
                        firstVolume = volume; firstCentre = centre; firstInertia = inertiaPerKg;
                    }
                    if (shrunk.Count <= 2 * maxVertices)
                        break;
                    if (target <= 4)
                    {
                        //nothing left to simplify: store the surface itself, unshrunk
                        radius = 0; shrunk = surface; shrunkPlanes = planes;
                        break;
                    }
                }

                //Shrinking a round, finely triangulated mesh splits so many corners that keeping a radius can cost the
                //surface most of its detail. Better the full surface with no radius than a radius on a crude one.
                if (requestedRadius == null && radius > 0 && volume < 0.98 * firstVolume)
                {
                    surface = firstSurface; triangles = firstTriangles; shrunk = firstSurface; shrunkPlanes = firstPlanes;
                    volume = firstVolume; centre = firstCentre; inertiaPerKg = firstInertia; radius = 0;
                }

                ConvexBody body = new ConvexBody
                {
                    ConvexRadius = (float)radius,
                    Volume = (float)volume,
                    CenterOfMass = centre.ToVector3(),
                    InertiaPerKg = inertiaPerKg.ToVector3(),
                    InputPoints = points.Count,
                    FullHullVertices = fullVertices,
                };
                foreach (D3 p in surface) body.SurfaceVertices.Add(p.ToVector3());
                body.SurfaceTriangles.AddRange(triangles);
                foreach (D3 p in shrunk) body.Vertices.Add(p.ToVector3());
                foreach (Plane pl in shrunkPlanes) body.Planes.Add(new Vector4(pl.N.ToVector3(), (float)(pl.D - radius)));
                Vector3 vmin = body.Vertices[0], vmax = body.Vertices[0];
                foreach (Vector3 v in body.Vertices) { vmin = Vector3.Min(vmin, v); vmax = Vector3.Max(vmax, v); }
                body.AabbCenter = (vmin + vmax) * 0.5f;
                body.AabbHalfExtents = (vmax - vmin) * 0.5f;
                return body;
            }

            static List<D3> Weld(IList<Vector3> input)
            {
                var seen = new HashSet<(float, float, float)>();
                var result = new List<D3>(input.Count);
                foreach (Vector3 v in input)
                {
                    if (float.IsNaN(v.X) || float.IsNaN(v.Y) || float.IsNaN(v.Z) || float.IsInfinity(v.X) || float.IsInfinity(v.Y) || float.IsInfinity(v.Z))
                        continue;
                    if (seen.Add((v.X, v.Y, v.Z)))
                        result.Add(new D3(v.X, v.Y, v.Z));
                }
                return result;
            }

            static double Scale(List<D3> points)
            {
                D3 min = points[0], max = points[0];
                foreach (D3 p in points) { min = D3.Min(min, p); max = D3.Max(max, p); }
                D3 e = max - min;
                return Math.Max(e.X, Math.Max(e.Y, e.Z));
            }

            static List<D3> Fibonacci(int n)
            {
                var dirs = new List<D3>(n + 6) { new D3(1, 0, 0), new D3(-1, 0, 0), new D3(0, 1, 0), new D3(0, -1, 0), new D3(0, 0, 1), new D3(0, 0, -1) };
                double golden = Math.PI * (3.0 - Math.Sqrt(5.0));
                for (int i = 0; i < n; i++)
                {
                    double y = 1.0 - (i + 0.5) * 2.0 / n;
                    double rad = Math.Sqrt(Math.Max(0.0, 1.0 - y * y));
                    double t = golden * i;
                    dirs.Add(new D3(Math.Cos(t) * rad, y, Math.Sin(t) * rad));
                }
                return dirs;
            }

            /// <summary>The hull over support points in fewer and fewer directions, always keeping the six axis extremes
            /// so the box the shape occupies does not change, until it has at most <paramref name="target"/> corners.</summary>
            static Hull Simplify(List<D3> corners, int target, double scale)
            {
                //Sparse directions miss corners whose normal cone is narrow - the apex of a shallow tilted cone - and
                //can flatten a body; the principal axes always find them, and of the hulls that fit the budget the
                //one keeping the most volume wins
                Hull best = null; double bestVolume = -1;
                for (int dirs = target * 4; ; dirs = Math.Max(1, (int)(dirs * 0.85)))
                {
                    try
                    {
                        Hull simplified = Hull.Build(SupportPoints(corners, Directions(corners, Math.Max(0, dirs - 12))), scale);
                        if (simplified.VertexCount <= target)
                        {
                            List<D3> v = simplified.Vertices();
                            MassProperties(v, simplified.Triangles(v), out double vol, out _, out _);
                            if (vol > bestVolume) { best = simplified; bestVolume = vol; }
                        }
                    }
                    catch (ArgumentException) { }
                    if (dirs <= 1) break;
                }
                if (best == null)
                    throw new ArgumentException("The mesh's hull could not be simplified to " + target + " corners.");
                //Vertices() must be the last call before the caller's Triangles(): rebuild the order on the winner
                best.Vertices();
                return best;
            }

            /// <summary>Fibonacci directions plus the six axis directions and the six along the points' principal axes.</summary>
            static List<D3> Directions(List<D3> points, int count)
            {
                List<D3> dirs = Fibonacci(count);
                foreach (D3 axis in PrincipalAxes(points)) { dirs.Add(axis); dirs.Add(axis * -1); }
                return dirs;
            }

            /// <summary>Eigenvectors of the points' covariance (Jacobi rotations on the 3x3 symmetric matrix).</summary>
            static List<D3> PrincipalAxes(List<D3> points)
            {
                D3 mean = new D3(0, 0, 0);
                foreach (D3 p in points) mean = mean + p;
                mean = mean * (1.0 / Math.Max(1, points.Count));
                double[,] a = new double[3, 3];
                foreach (D3 p in points)
                {
                    D3 q = p - mean;
                    double[] v = { q.X, q.Y, q.Z };
                    for (int i = 0; i < 3; i++) for (int j = 0; j < 3; j++) a[i, j] += v[i] * v[j];
                }
                double[,] e = { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
                for (int sweep = 0; sweep < 32; sweep++)
                {
                    double off = Math.Abs(a[0, 1]) + Math.Abs(a[0, 2]) + Math.Abs(a[1, 2]);
                    double diag = Math.Abs(a[0, 0]) + Math.Abs(a[1, 1]) + Math.Abs(a[2, 2]);
                    if (off <= 1e-15 * Math.Max(diag, 1e-300)) break;
                    for (int p = 0; p < 2; p++)
                        for (int q = p + 1; q < 3; q++)
                        {
                            if (Math.Abs(a[p, q]) <= 1e-300) continue;
                            double theta = (a[q, q] - a[p, p]) / (2 * a[p, q]);
                            double t = theta == 0 ? 1 : Math.Sign(theta) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1));
                            double c = 1 / Math.Sqrt(t * t + 1), s = t * c;
                            for (int k = 0; k < 3; k++)
                            {
                                double akp = a[k, p], akq = a[k, q];
                                a[k, p] = c * akp - s * akq; a[k, q] = s * akp + c * akq;
                            }
                            for (int k = 0; k < 3; k++)
                            {
                                double apk = a[p, k], aqk = a[q, k];
                                a[p, k] = c * apk - s * aqk; a[q, k] = s * apk + c * aqk;
                            }
                            for (int k = 0; k < 3; k++)
                            {
                                double ekp = e[k, p], ekq = e[k, q];
                                e[k, p] = c * ekp - s * ekq; e[k, q] = s * ekp + c * ekq;
                            }
                        }
                }
                var axes = new List<D3>();
                for (int k = 0; k < 3; k++)
                {
                    D3 axis = new D3(e[0, k], e[1, k], e[2, k]);
                    double len = axis.Length();
                    if (len > 1e-12) axes.Add(axis * (1.0 / len));
                }
                return axes;
            }

            static List<D3> SupportPoints(List<D3> points, List<D3> dirs)
            {
                var chosen = new HashSet<int>();
                foreach (D3 dir in dirs)
                {
                    int best = 0; double bestDot = double.MinValue;
                    for (int i = 0; i < points.Count; i++)
                    {
                        double dot = points[i].Dot(dir);
                        if (dot > bestDot) { bestDot = dot; best = i; }
                    }
                    chosen.Add(best);
                }
                List<int> ordered = chosen.ToList();
                ordered.Sort();
                return ordered.Select(i => points[i]).ToList();
            }

            /// <summary>
            /// Move every face inward by <paramref name="radius"/> and find the corners of what is left, through the
            /// dual: with the centre at the origin, the planes n.x &lt;= h become points n/h, and each face of their hull
            /// is a corner of the shrunk solid. Faces that vanish in the shrinking fall inside the dual hull.
            /// </summary>
            static List<D3> Shrink(List<Plane> planes, D3 centre, double radius, double scale)
            {
                var dual = new List<D3>(planes.Count);
                foreach (Plane pl in planes)
                {
                    double h = -(pl.N.Dot(centre) + pl.D) - radius;
                    if (!(h > scale * 1e-9)) return null;
                    dual.Add(pl.N * (1.0 / h));
                }
                Hull hull;
                try { hull = Hull.Build(dual, Scale(dual)); }
                catch (ArgumentException) { return null; }
                var corners = new List<D3>();
                double weld = scale * 1e-6;
                foreach (Hull.Face f in hull.AliveFaces())
                {
                    //Face plane N.y + D = 0 with the origin inside (D < 0): the corner is N / -D
                    if (!(-f.D > 1e-12)) return null;
                    D3 corner = centre + f.N * (1.0 / -f.D);
                    bool dup = false;
                    foreach (D3 c in corners) if ((c - corner).Length() < weld) { dup = true; break; }
                    if (!dup) corners.Add(corner);
                }
                return corners;
            }

            /// <summary>Coplanar triangles merged into one plane each: outward unit normal, offset through the farthest vertex.</summary>
            static List<Plane> MergedPlanes(List<D3> vertices, List<int> triangles, double scale)
            {
                int n = triangles.Count / 3;
                var normals = new D3[n];
                var areas = new double[n];
                for (int t = 0; t < n; t++)
                {
                    D3 a = vertices[triangles[t * 3]], b = vertices[triangles[t * 3 + 1]], c = vertices[triangles[t * 3 + 2]];
                    D3 cross = (b - a).Cross(c - a);
                    areas[t] = cross.Length();
                    normals[t] = areas[t] > 0 ? cross * (1.0 / areas[t]) : new D3(0, 0, 0);
                }
                //Merge triangles that share an edge and whose planes agree (the far vertex of one on the other's plane)
                int[] parent = Enumerable.Range(0, n).ToArray();
                int Find(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
                var edgeOwner = new Dictionary<(int, int), int>();
                for (int t = 0; t < n; t++)
                    for (int k = 0; k < 3; k++)
                        edgeOwner[(triangles[t * 3 + k], triangles[t * 3 + (k + 1) % 3])] = t;
                double tol = scale * 1e-5;
                for (int t = 0; t < n; t++)
                    for (int k = 0; k < 3; k++)
                    {
                        int a = triangles[t * 3 + k], b = triangles[t * 3 + (k + 1) % 3];
                        if (!edgeOwner.TryGetValue((b, a), out int u) || u < t) continue;
                        int far = triangles[u * 3] != a && triangles[u * 3] != b ? triangles[u * 3] : triangles[u * 3 + 1] != a && triangles[u * 3 + 1] != b ? triangles[u * 3 + 1] : triangles[u * 3 + 2];
                        D3 origin = vertices[triangles[t * 3]];
                        if (areas[t] > 0 && areas[u] > 0 && Math.Abs(normals[t].Dot(vertices[far] - origin)) < tol && normals[t].Dot(normals[u]) > 0.9999)
                            parent[Find(u)] = Find(t);
                    }
                var groups = new Dictionary<int, D3>();
                for (int t = 0; t < n; t++)
                {
                    int g = Find(t);
                    groups.TryGetValue(g, out D3 sum);
                    groups[g] = sum + normals[t] * areas[t];
                }
                var planes = new List<Plane>(groups.Count);
                foreach (D3 sum in groups.Values)
                {
                    double len = sum.Length();
                    if (!(len > 0)) continue;
                    D3 normal = sum * (1.0 / len);
                    double far = double.MinValue;
                    foreach (D3 v in vertices) far = Math.Max(far, normal.Dot(v));
                    planes.Add(new Plane { N = normal, D = -far });
                }
                return planes;
            }

            /// <summary>Volume, centroid and inertia (diagonal about the centroid, per kg) of the solid a closed outward mesh bounds.</summary>
            static void MassProperties(List<D3> v, List<int> tris, out double volume, out D3 centroid, out D3 inertiaPerKg)
            {
                //Tetrahedra from a point inside, then the canonical second moments (Blow & Binstock / Eberly)
                D3 o = new D3(0, 0, 0);
                foreach (D3 p in v) o = o + p;
                o = o * (1.0 / v.Count);
                double vol6 = 0;
                D3 c = new D3(0, 0, 0);
                double xx = 0, yy = 0, zz = 0, xy = 0, xz = 0, yz = 0;
                for (int t = 0; t < tris.Count; t += 3)
                {
                    D3 a = v[tris[t]] - o, b = v[tris[t + 1]] - o, d = v[tris[t + 2]] - o;
                    double det = a.Dot(b.Cross(d));
                    vol6 += det;
                    c = c + (a + b + d) * det;
                    xx += det * (a.X * a.X + b.X * b.X + d.X * d.X + a.X * b.X + a.X * d.X + b.X * d.X);
                    yy += det * (a.Y * a.Y + b.Y * b.Y + d.Y * d.Y + a.Y * b.Y + a.Y * d.Y + b.Y * d.Y);
                    zz += det * (a.Z * a.Z + b.Z * b.Z + d.Z * d.Z + a.Z * b.Z + a.Z * d.Z + b.Z * d.Z);
                    xy += det * (2 * a.X * a.Y + 2 * b.X * b.Y + 2 * d.X * d.Y + a.X * b.Y + a.Y * b.X + a.X * d.Y + a.Y * d.X + b.X * d.Y + b.Y * d.X);
                    xz += det * (2 * a.X * a.Z + 2 * b.X * b.Z + 2 * d.X * d.Z + a.X * b.Z + a.Z * b.X + a.X * d.Z + a.Z * d.X + b.X * d.Z + b.Z * d.X);
                    yz += det * (2 * a.Y * a.Z + 2 * b.Y * b.Z + 2 * d.Y * d.Z + a.Y * b.Z + a.Z * b.Y + a.Y * d.Z + a.Z * d.Y + b.Y * d.Z + b.Z * d.Y);
                }
                volume = vol6 / 6.0;
                if (!(volume > 0)) { centroid = o; inertiaPerKg = new D3(0, 0, 0); return; }
                D3 cm = c * (1.0 / (4.0 * vol6));
                //Second moments about o, per unit volume: integral x^2 dV = xx / 60 (with det = 6 V_tet), etc.
                double ixx = xx / 60.0, iyy = yy / 60.0, izz = zz / 60.0;
                //About the centroid, per kg (divide by the volume, then the parallel axis shift)
                double sxx = ixx / volume - cm.X * cm.X, syy = iyy / volume - cm.Y * cm.Y, szz = izz / volume - cm.Z * cm.Z;
                inertiaPerKg = new D3(syy + szz, sxx + szz, sxx + syy);
                centroid = o + cm;
            }

            internal struct Plane { public D3 N; public double D; }
        }
    }

    /// <summary>The settings of the one rigid body a new physics system holds.</summary>
    public sealed class PhysicsBodySettings
    {
        /// <summary>Retail names a body after the ModelReference entity it drives.</summary>
        public string Name = "Body";
        /// <summary>Kilograms. Retail props run from 0.1 (debris) to about 50.</summary>
        public float Mass = 1f;
        public float Friction = 0.5f;
        public float Restitution = 0.4f;
        /// <summary>Where the body sits in the system's space: retail uses the position of the model it drives in its composite.</summary>
        public Vector3 Position = Vector3.Zero;
        public Quaternion Rotation = Quaternion.Identity;
    }

    /// <summary>
    /// A convex collision shape built from a mesh, with its solid mass properties: what
    /// <see cref="PhysicsSystemWriter.AddConvexPhysicsSystem"/> writes, and what a preview can show before anything is written.
    /// </summary>
    public sealed class ConvexBody
    {
        /// <summary>The hull's corners before shrinking: the modelled surface (up to the rounding the radius puts on its edges).</summary>
        public List<Vector3> SurfaceVertices = new List<Vector3>();
        /// <summary>Outward triangles over <see cref="SurfaceVertices"/>.</summary>
        public List<int> SurfaceTriangles = new List<int>();
        /// <summary>What the shape stores: the hull shrunk by <see cref="ConvexRadius"/>.</summary>
        public List<Vector3> Vertices = new List<Vector3>();
        /// <summary>Stored plane equations (xyz outward normal, w offset), one radius outside <see cref="Vertices"/>.</summary>
        public List<Vector4> Planes = new List<Vector4>();
        public float ConvexRadius;
        /// <summary>AABB over <see cref="Vertices"/> without the radius, as retail stores it.</summary>
        public Vector3 AabbCenter;
        public Vector3 AabbHalfExtents;
        public float Volume;
        /// <summary>Centre of the solid hull, in the shape's space.</summary>
        public Vector3 CenterOfMass;
        /// <summary>Diagonal of the solid hull's inertia tensor about its centre of mass, for a mass of 1 kg.</summary>
        public Vector3 InertiaPerKg;
        /// <summary>Vertices in the mesh the hull was built from, after welding.</summary>
        public int InputPoints;
        /// <summary>Corners of the full hull before it was simplified to the vertex limit.</summary>
        public int FullHullVertices;
    }

    /// <summary>A double-precision vector for the hull maths; the shape itself is stored in single precision.</summary>
    internal struct D3
    {
        public double X, Y, Z;
        public D3(double x, double y, double z) { X = x; Y = y; Z = z; }
        public static D3 operator +(D3 a, D3 b) => new D3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static D3 operator -(D3 a, D3 b) => new D3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static D3 operator *(D3 a, double s) => new D3(a.X * s, a.Y * s, a.Z * s);
        public double Dot(D3 b) => X * b.X + Y * b.Y + Z * b.Z;
        public D3 Cross(D3 b) => new D3(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);
        public double Length() => Math.Sqrt(X * X + Y * Y + Z * Z);
        public static D3 Min(D3 a, D3 b) => new D3(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));
        public static D3 Max(D3 a, D3 b) => new D3(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));
        public Vector3 ToVector3() => new Vector3((float)X, (float)Y, (float)Z);
    }

    /// <summary>
    /// An incremental 3D convex hull (quickhull): a tetrahedron, then repeatedly the farthest outside point of a
    /// face is joined to the horizon of the faces it sees. Triangles only; coplanar triangles are merged into
    /// planes afterwards by the caller.
    /// </summary>
    internal sealed class Hull
    {
        internal sealed class Face
        {
            public int A, B, C;
            public D3 N;
            public double D;
            public List<int> Outside;
            public bool Alive = true;
        }

        readonly List<D3> _p;
        readonly double _eps;
        readonly double _scale;
        readonly List<Face> _faces = new List<Face>();
        readonly Dictionary<long, int> _edges = new Dictionary<long, int>();

        Hull(List<D3> points, double eps, double scale) { _p = points; _eps = eps; _scale = scale; }

        public static Hull Build(List<D3> points, double scale)
        {
            //A hull that trips over its own tolerance is retried with a looser one (up to 1e-5 of its size, so
            //the containment check it must pass stays meaningful), then on its points merged within a small
            //fraction of its size - near-duplicate corners, three planes meeting at one vertex a float apart,
            //are what leave a horizon ambiguous
            scale = Math.Max(scale, 1e-12);
            Exception last = null;
            //(merge, joggle): then, as qhull does, the points nudged by a tiny fixed pseudo-random amount, which
            //breaks the exact near-coplanarities a float mesh is full of
            foreach ((double merge, double joggle) in new[] { (0.0, 0.0), (1e-6, 0.0), (1e-5, 0.0), (1e-4, 0.0), (1e-6, 1e-6), (1e-5, 1e-5) })
            {
                List<D3> pts = merge == 0 ? points : Merge(points, scale * merge);
                if (joggle > 0)
                {
                    var random = new Random(696);
                    double amount = scale * joggle;
                    pts = pts.Select(p => new D3(p.X + (random.NextDouble() * 2 - 1) * amount, p.Y + (random.NextDouble() * 2 - 1) * amount, p.Z + (random.NextDouble() * 2 - 1) * amount)).ToList();
                }
                if (pts.Count < 4) break;
                double eps = scale * 1e-9;
                for (int attempt = 0; attempt < 3; attempt++, eps *= 100)
                {
                    try
                    {
                        Hull hull = new Hull(pts, eps, scale);
                        hull.Run();
                        return hull;
                    }
                    catch (InvalidOperationException e) { last = e; }
                }
            }
            throw new ArgumentException("The convex hull of this mesh could not be built: " + (last?.Message ?? "too few distinct points"));
        }

        /// <summary>Points closer than <paramref name="tolerance"/> to one already kept are dropped (a grid of that pitch).</summary>
        internal static List<D3> Merge(List<D3> points, double tolerance)
        {
            var cells = new Dictionary<(long, long, long), List<D3>>();
            var kept = new List<D3>(points.Count);
            foreach (D3 p in points)
            {
                long cx = (long)Math.Floor(p.X / tolerance), cy = (long)Math.Floor(p.Y / tolerance), cz = (long)Math.Floor(p.Z / tolerance);
                bool near = false;
                for (long dx = -1; dx <= 1 && !near; dx++)
                    for (long dy = -1; dy <= 1 && !near; dy++)
                        for (long dz = -1; dz <= 1 && !near; dz++)
                            if (cells.TryGetValue((cx + dx, cy + dy, cz + dz), out List<D3> cell))
                                foreach (D3 q in cell)
                                    if ((q - p).Length() < tolerance) { near = true; break; }
                if (near) continue;
                if (!cells.TryGetValue((cx, cy, cz), out List<D3> own)) cells[(cx, cy, cz)] = own = new List<D3>();
                own.Add(p);
                kept.Add(p);
            }
            return kept;
        }

        public IEnumerable<Face> AliveFaces() => _faces.Where(f => f.Alive);

        public int VertexCount
        {
            get
            {
                var used = new HashSet<int>();
                foreach (Face f in AliveFaces()) { used.Add(f.A); used.Add(f.B); used.Add(f.C); }
                return used.Count;
            }
        }

        List<int> _vertexOrder;
        public List<D3> Vertices()
        {
            _vertexOrder = new List<int>();
            var seen = new HashSet<int>();
            foreach (Face f in AliveFaces())
                foreach (int i in new[] { f.A, f.B, f.C })
                    if (seen.Add(i)) _vertexOrder.Add(i);
            return _vertexOrder.Select(i => _p[i]).ToList();
        }

        /// <summary>Triangles indexing the list the last <see cref="Vertices"/> call returned.</summary>
        public List<int> Triangles(List<D3> vertices)
        {
            var remap = new Dictionary<int, int>();
            for (int i = 0; i < _vertexOrder.Count; i++) remap[_vertexOrder[i]] = i;
            var tris = new List<int>();
            foreach (Face f in AliveFaces()) { tris.Add(remap[f.A]); tris.Add(remap[f.B]); tris.Add(remap[f.C]); }
            return tris;
        }

        static long Key(int a, int b) => ((long)a << 32) | (uint)b;

        double Dist(Face f, int p) => f.N.Dot(_p[p]) + f.D;

        int AddFace(int a, int b, int c)
        {
            D3 n = (_p[b] - _p[a]).Cross(_p[c] - _p[a]);
            double len = n.Length();
            if (!(len > 0))
                throw new InvalidOperationException("degenerate face");
            n = n * (1.0 / len);
            Face f = new Face { A = a, B = b, C = c, N = n, D = -n.Dot(_p[a]), Outside = new List<int>() };
            int id = _faces.Count;
            _faces.Add(f);
            foreach (long k in new[] { Key(a, b), Key(b, c), Key(c, a) })
            {
                if (_edges.ContainsKey(k))
                    throw new InvalidOperationException("non-manifold horizon");
                _edges[k] = id;
            }
            return id;
        }

        void RemoveFace(int id)
        {
            Face f = _faces[id];
            f.Alive = false;
            _edges.Remove(Key(f.A, f.B));
            _edges.Remove(Key(f.B, f.C));
            _edges.Remove(Key(f.C, f.A));
        }

        void Run()
        {
            int n = _p.Count;
            if (n < 4) throw new ArgumentException("A convex hull needs at least four points.");

            //Initial tetrahedron from the widest pair, the point farthest from their line, and from their plane
            int[] ext = new int[6];
            for (int i = 0; i < n; i++)
            {
                if (_p[i].X < _p[ext[0]].X) ext[0] = i; if (_p[i].X > _p[ext[1]].X) ext[1] = i;
                if (_p[i].Y < _p[ext[2]].Y) ext[2] = i; if (_p[i].Y > _p[ext[3]].Y) ext[3] = i;
                if (_p[i].Z < _p[ext[4]].Z) ext[4] = i; if (_p[i].Z > _p[ext[5]].Z) ext[5] = i;
            }
            int i0 = ext[0], i1 = ext[1];
            double best = -1;
            for (int a = 0; a < 6; a++)
                for (int b = a + 1; b < 6; b++)
                {
                    double len = (_p[ext[a]] - _p[ext[b]]).Length();
                    if (len > best) { best = len; i0 = ext[a]; i1 = ext[b]; }
                }
            if (!(best > _eps)) throw new ArgumentException("The mesh's vertices all coincide.");
            D3 line = (_p[i1] - _p[i0]) * (1.0 / best);
            int i2 = -1; best = -1;
            for (int i = 0; i < n; i++)
            {
                D3 rel = _p[i] - _p[i0];
                double dist = (rel - line * rel.Dot(line)).Length();
                if (dist > best) { best = dist; i2 = i; }
            }
            if (!(best > _eps)) throw new ArgumentException("The mesh is a line: a physics body needs volume.");
            D3 normal = (_p[i1] - _p[i0]).Cross(_p[i2] - _p[i0]);
            normal = normal * (1.0 / normal.Length());
            int i3 = -1; best = -1;
            for (int i = 0; i < n; i++)
            {
                double dist = Math.Abs(normal.Dot(_p[i] - _p[i0]));
                if (dist > best) { best = dist; i3 = i; }
            }
            if (!(best > _eps)) throw new ArgumentException("The mesh is flat: a physics body needs volume.");

            if (normal.Dot(_p[i3] - _p[i0]) > 0) { int t = i1; i1 = i2; i2 = t; }   //i3 behind (i0,i1,i2)
            AddFace(i0, i1, i2);
            AddFace(i0, i3, i1);
            AddFace(i1, i3, i2);
            AddFace(i2, i3, i0);

            for (int i = 0; i < n; i++)
            {
                if (i == i0 || i == i1 || i == i2 || i == i3) continue;
                Assign(i, 0, 4);
            }

            var pending = new Stack<int>(Enumerable.Range(0, 4));
            int guard = 0;
            while (pending.Count > 0)
            {
                if (++guard > 4 * n + 100) throw new InvalidOperationException("no progress");
                int fid = pending.Pop();
                Face face = _faces[fid];
                if (!face.Alive || face.Outside.Count == 0) continue;

                int eye = face.Outside[0]; double eyeDist = Dist(face, eye);
                foreach (int p in face.Outside) { double dd = Dist(face, p); if (dd > eyeDist) { eyeDist = dd; eye = p; } }

                //Faces the eye sees, found across shared edges from this one; their boundary is the horizon
                var visible = new List<int> { fid };
                var isVisible = new HashSet<int> { fid };
                var horizon = new List<(int, int)>();
                for (int q = 0; q < visible.Count; q++)
                {
                    Face vf = _faces[visible[q]];
                    foreach ((int a, int b) in new[] { (vf.A, vf.B), (vf.B, vf.C), (vf.C, vf.A) })
                    {
                        if (!_edges.TryGetValue(Key(b, a), out int nb)) throw new InvalidOperationException("open hull");
                        if (isVisible.Contains(nb)) continue;
                        if (Dist(_faces[nb], eye) > _eps) { isVisible.Add(nb); visible.Add(nb); }
                        else horizon.Add((a, b));
                    }
                }
                //Horizon edges found from faces seen later can belong to faces that turned out visible: drop those
                horizon.RemoveAll(e => isVisible.Contains(_edges.TryGetValue(Key(e.Item2, e.Item1), out int nb) ? nb : -1));

                var orphans = new List<int>();
                foreach (int v in visible)
                {
                    foreach (int p in _faces[v].Outside) if (p != eye) orphans.Add(p);
                    RemoveFace(v);
                }
                int first = _faces.Count;
                foreach ((int a, int b) in horizon)
                    AddFace(a, b, eye);
                int last = _faces.Count;
                foreach (int p in orphans) Assign(p, first, last);
                for (int f = first; f < last; f++) if (_faces[f].Outside.Count > 0) pending.Push(f);
            }

            //Closed and convex, or the tolerance was too tight
            foreach (Face f in AliveFaces())
                foreach ((int a, int b) in new[] { (f.A, f.B), (f.B, f.C), (f.C, f.A) })
                    if (!_edges.ContainsKey(Key(b, a))) throw new InvalidOperationException("open hull");

            //And it holds every point: on near-flat input (a wall a quarter of a millimetre thick) a tolerance
            //finer than the float noise lets the horizon come out wrong and whole corners be dropped
            //(1e-4 of the size: a sliver face's ill-defined normal alone puts true hull points up to ~7e-5 of the size
            //outside it on real models; the tolerance no longer grows with eps, so a genuinely wrong hull still fails)
            double hold = _scale * 1e-4;
            var faces = AliveFaces().ToList();
            for (int i = 0; i < n; i++)
                foreach (Face f in faces)
                    if (Dist(f, i) > hold) throw new InvalidOperationException("a point was left outside");
        }

        void Assign(int p, int from, int to)
        {
            int bestFace = -1; double best = _eps;
            for (int f = from; f < to; f++)
            {
                if (!_faces[f].Alive) continue;
                double dist = Dist(_faces[f], p);
                if (dist > best) { best = dist; bestFace = f; }
            }
            if (bestFace >= 0) _faces[bestFace].Outside.Add(p);
        }
    }
}
