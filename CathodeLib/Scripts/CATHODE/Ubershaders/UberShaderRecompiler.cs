using CATHODE;
using CATHODE.ShaderTypes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

#if !(UNITY_EDITOR || UNITY_STANDALONE_WIN || GODOT)
namespace CathodeLib.Ubershaders
{
    public static class UberShaderRecompiler
    {
        /* The families we can compile arbitrary permutations for. */
        public static bool HasMaster(SHADER_LIST family)
        {
            return UbershaderMasters.Has(family);
        }

        private static bool? _compilerAvailable = null;
        public static bool CompilerAvailable
        {
            get
            {
                if (_compilerAvailable == null)
                {
                    IntPtr module = LoadLibrary("d3dcompiler_43.dll");
                    _compilerAvailable = module != IntPtr.Zero;
                    if (module != IntPtr.Zero) FreeLibrary(module);
                }
                return _compilerAvailable.Value;
            }
        }

        public static bool CanCompile(SHADER_LIST family)
        {
            return HasMaster(family) && CompilerAvailable;
        }

        /// <summary>
        /// True when the permutation runs through the tessellator, so it needs a hull and a domain
        /// shader as well as the usual two. The vertex stage emits control points on these masks
        /// rather than clip positions, so both are mandatory: without them there is nothing to
        /// turn the patch back into triangles.
        /// </summary>
        public static bool RequiresTessellationStages(SHADER_LIST family, long mask)
        {
            if (family == SHADER_LIST.CA_ENVIRONMENT)
                return Bit(mask, (int)CA_ENVIRONMENT.FEATURES.TESSELLATION);
            return false;
        }

        #region FEATURE COVERAGE
        private static readonly Dictionary<string, string> _preprocessCache = new Dictionary<string, string>();

        private static string PreprocessedMaster(SHADER_LIST family, long mask, string stage)
        {
            string key = family + ":" + mask.ToString("X") + ":" + stage;
            string cached;
            if (_preprocessCache.TryGetValue(key, out cached))
                return cached;

            string error;
            string text = Preprocess43(Defines(family, mask) + Master(family, stage), out error);
            //store a digest, not the text - browsing a big family generates a lot of these
            if (text == null) cached = null;
            else using (SHA1 sha = SHA1.Create())
                cached = BitConverter.ToString(sha.ComputeHash(Encoding.ASCII.GetBytes(text)));
            _preprocessCache[key] = cached;
            return cached;
        }

        /// <summary>
        /// Would flipping this feature bit actually change the shader? False means the family recompiles but this particular feature was never reconstructed.
        /// </summary>
        public static bool ToggleAffectsMaster(SHADER_LIST family, long mask, int bit)
        {
            return MastersDiffer(family, mask, mask ^ (1L << bit));
        }

        /// <summary>
        /// Do these two masks compile to different shaders? False means the change is invisible to the reconstruction - every feature that differs is one it never got to see.
        /// </summary>
        public static bool MastersDiffer(SHADER_LIST family, long mask, long toggled)
        {
            if (!CanCompile(family)) return false;
            if (mask == toggled) return false;
            try
            {
                foreach (string stage in new string[] { "vs", "ps" })
                {
                    string a = PreprocessedMaster(family, mask, stage);
                    string b = PreprocessedMaster(family, toggled, stage);
                    //preprocessor unavailable or the master won't preprocess: don't block the user
                    if (a == null || b == null) return true;
                    if (a != b) return true;
                }
            }
            catch { return true; }
            return false;
        }
        #endregion

        /// <summary>
        /// Compile the vertex and pixel shader for an arbitrary feature mask. Returns false with an error message if the mask fails to compile (or the family has no entry in the database).
        /// </summary>
        public static bool Compile(SHADER_LIST family, long mask, out byte[] vertexShader, out byte[] pixelShader, out string error)
        {
            byte[] hullShader, domainShader;
            return Compile(family, mask, out vertexShader, out pixelShader, out hullShader, out domainShader, out error);
        }

        /// <summary>
        /// Compile every stage the permutation needs. Hull and domain come back non-null only for a tessellated mask - see <see cref="RequiresTessellationStages"/>.
        /// </summary>
        public static bool Compile(SHADER_LIST family, long mask, out byte[] vertexShader, out byte[] pixelShader,
            out byte[] hullShader, out byte[] domainShader, out string error)
        {
            vertexShader = null;
            pixelShader = null;
            hullShader = null;
            domainShader = null;
            if (!CanCompile(family))
            {
                error = "No shader master is available for " + family;
                return false;
            }

            //An entry can exist and still be missing a stage - a partially generated table, or a
            //family whose tessellation stages were never packed. Say so here rather than letting
            //the master lookup throw out of the middle of a compile.
            if (!StagesPresent(family, mask, out error))
                return false;

            string defines = Defines(family, mask);
            vertexShader = Compile43(defines + Master(family, "vs"), "main", "vs_5_0", out error);
            if (vertexShader == null)
                return false;
            pixelShader = Compile43(defines + Master(family, "ps"), "main", "ps_5_0", out error);
            if (pixelShader == null)
                return false;
            if (RequiresTessellationStages(family, mask))
            {
                //The vertex stage emits control points rather than clip positions on these masks,
                //so both of these are mandatory - a missing one leaves nothing to rasterise.
                hullShader = Compile43(defines + Master(family, "hs"), "main", "hs_5_0", out error);
                if (hullShader == null)
                    return false;
                domainShader = Compile43(defines + Master(family, "ds"), "main", "ds_5_0", out error);
                if (domainShader == null)
                    return false;
            }
            error = null;
            return true;
        }

        #region MASTERS
        private static readonly Dictionary<string, string> _masterCache = new Dictionary<string, string>();

        private static string Master(SHADER_LIST family, string stage)
        {
            string key = family.ToString().Substring("CA_".Length).ToLower() + "_master_" + stage;
            string cached;
            if (_masterCache.TryGetValue(key, out cached))
                return cached;

            if (!UbershaderMasters.TryGet(family, stage, out cached))
                throw new Exception("No database source for " + family + " (" + stage + ") on "
                    + UbershaderMasters.Platform + " in the ubershader table.");
            _masterCache[key] = cached;
            return cached;
        }
        #endregion

        #region DEFINES
        /* Every feature bit becomes an F_<NAME> 0/1 define, plus per-family macros for interpolant
         * packing and constant slots - the exact scheme the byte-parity scoring runs used */
        /// <summary>
        /// Does the shipped table carry every stage this permutation needs?
        /// </summary>
        private static bool StagesPresent(SHADER_LIST family, long mask, out string error)
        {
            List<string> needed = new List<string>() { "vs", "ps" };
            if (RequiresTessellationStages(family, mask)) { needed.Add("hs"); needed.Add("ds"); }
            foreach (string stage in needed)
            {
                string unused;
                if (UbershaderMasters.TryGet(family, stage, out unused)) continue;
                error = "The shipped ubershader table has no " + stage + " master for " + family
                      + " on " + UbershaderMasters.Platform + ".";
                return false;
            }
            error = null;
            return true;
        }

        public static string Defines(SHADER_LIST family, long mask)
        {
            Type featuresType = typeof(SHADER_LIST).Assembly.GetType("CATHODE.ShaderTypes." + family + "+FEATURES");
            StringBuilder sb = new StringBuilder();
            foreach (int b in Enum.GetValues(featuresType).Cast<int>().Distinct().OrderBy(x => x))
                sb.AppendLine("#define F_" + Enum.GetName(featuresType, b) + " " + (((mask >> b) & 1) != 0 ? 1 : 0));
            for (int b = 0; b < 64; b++)
                if (!Enum.IsDefined(featuresType, b) && ((mask >> b) & 1) != 0)
                    sb.AppendLine("#define F_BIT" + b + " 1");
            if (family == SHADER_LIST.CA_FOGSPHERE)
                sb.Append(FogsphereMacros(mask));
            if (family == SHADER_LIST.CA_FOGPLANE)
                sb.Append(FogplaneMacros(mask));
            if (family == SHADER_LIST.CA_SKIN)
                sb.Append(SkinMacros(mask));
            if (family == SHADER_LIST.CA_LOW_LOD_CHARACTER)
                sb.Append(LlcMacros(mask));
            if (family == SHADER_LIST.CA_LIGHTMAP_ENVIRONMENT)
                sb.Append(LmeMacros(mask));
            if (family == SHADER_LIST.CA_CHARACTER)
                sb.Append(CharacterMacros(mask));
            if (family == SHADER_LIST.CA_RIBBON)
                sb.Append(RibbonMacros(mask));
            if (family == SHADER_LIST.CA_DECAL)
                sb.Append(DecalMacros(mask));
            if (family == SHADER_LIST.CA_PARTICLE)
                sb.Append(ParticleMacros(mask));
            if (family == SHADER_LIST.CA_ENVIRONMENT)
                sb.Append(EnvironmentMacros(mask));
            if (family == SHADER_LIST.CA_DECAL_ENVIRONMENT)
                sb.Append(DecEnvMacros(mask));
            if (family == SHADER_LIST.CA_SKIN_OCCLUSION)
                sb.Append(SknOccMacros(mask));
            if (family == SHADER_LIST.CA_SURFACE_EFFECTS)
                sb.Append(SfxMacros(mask));
            if (family == SHADER_LIST.CA_PLANET)
                sb.Append(PlanetMacros(mask));
            if (family == SHADER_LIST.CA_EFFECT_OVERLAY)
                sb.Append(EovMacros(mask));
            if (family == SHADER_LIST.CA_TERRAIN)
                sb.Append(TerrMacros(mask));
            if (family == SHADER_LIST.CA_REFRACTION)
                sb.Append(RefrMacros(mask));
            if (family == SHADER_LIST.CA_HAIR)
                sb.Append(HairMacros(mask));
            if (family == SHADER_LIST.CA_SIMPLEWATER)
                sb.Append(SwatMacros(mask));
            if (family == SHADER_LIST.CA_EYE)
                sb.Append(EyeMacros(mask));
            if (family == SHADER_LIST.CA_DEFERRED)
                sb.Append(DeferMacros(mask));
            if (family == SHADER_LIST.CA_POST_PROCESSING)
                sb.Append(PpMacros(mask));
            if (family == SHADER_LIST.CA_FILTERS)
                sb.Append(FiltMacros(mask));
            if (family == SHADER_LIST.CA_SPACESUIT_VISOR)
                sb.Append(VisorMacros(mask));
            if (family == SHADER_LIST.CA_NONINTERACTIVE_WATER)
                sb.Append(WaterMacros(mask));
            return sb.ToString();
        }

        /// FOGPLANE interpolant registers: t0 (misc), t1 (fades) always; DIFFUSE_MAPPING_0 adds an
        /// anim-offset register; LINEAR_HEIGHT_DENSITY adds normal/ray/worldpos/t5; COLOR is always
        /// the last register before SV_Position.
        private static string FogplaneMacros(long mask)
        {
            bool diff0 = ((mask >> (int)CA_FOGPLANE.FEATURES.DIFFUSE_MAPPING_0) & 1) != 0;
            bool lh = ((mask >> (int)CA_FOGPLANE.FEATURES.LINEAR_HEIGHT_DENSITY) & 1) != 0;
            int reg = 2;
            StringBuilder sb = new StringBuilder();
            StringBuilder decl = new StringBuilder();
            int animReg = -1;
            if (diff0) { animReg = reg; sb.AppendLine("#define M_ANIM t" + reg); decl.Append("float4 t" + reg + " : TEXCOORD" + reg + "; "); reg++; }
            if (lh)
            {
                sb.AppendLine("#define M_NORMAL t" + reg); decl.Append("float4 t" + reg + " : TEXCOORD" + reg + "; "); reg++;
                sb.AppendLine("#define M_RAY t" + reg); decl.Append("float4 t" + reg + " : TEXCOORD" + reg + "; "); reg++;
                sb.AppendLine("#define M_WPOS t" + reg); decl.Append("float4 t" + reg + " : TEXCOORD" + reg + "; "); reg++;
                if (diff0)
                {
                    //density rides the anim register's w; no separate T5 register
                    sb.AppendLine("#define M_DENS t" + animReg + ".w");
                }
                else
                {
                    sb.AppendLine("#define M_DENS t" + reg + ".x");
                    sb.AppendLine("#define M_T5 t" + reg);
                    decl.Append("float4 t" + reg + " : TEXCOORD" + reg + "; "); reg++;
                }
            }
            decl.Append("float4 col : COLOR;");
            sb.AppendLine("#define XFIELDS " + decl);
            return sb.ToString();
        }

        /// FOGSPHERE interpolant packing, measured from the shipped signatures: whole float4
        /// TEXCOORD registers packed by hand in a fixed feature order, unassigned components zeroed
        /// by the VS.
        private static string FogsphereMacros(long mask)
        {
            bool fres = ((mask >> (int)CA_FOGSPHERE.FEATURES.FRESNEL_TERM) & 1) != 0;
            bool soft = ((mask >> (int)CA_FOGSPHERE.FEATURES.SOFTNESS) & 1) != 0;
            bool al = ((mask >> (int)CA_FOGSPHERE.FEATURES.ALPHA_LIGHTING) & 1) != 0;
            bool blend = ((mask >> (int)CA_FOGSPHERE.FEATURES.BLEND_ALPHA_OVER_DISTANCE) & 1) != 0;
            bool sec = ((mask >> (int)CA_FOGSPHERE.FEATURES.SECONDARY_BLEND_ALPHA_OVER_DISTANCE) & 1) != 0;
            bool di = ((mask >> (int)CA_FOGSPHERE.FEATURES.DEPTH_INTERSECT_COLOUR) & 1) != 0;

            int reg = 3, comp = 0;
            Dictionary<int, bool[]> assigned = new Dictionary<int, bool[]>();
            string Alloc(int n, bool wholeReg = false)
            {
                if (wholeReg) { if (comp > 0) { reg++; comp = 0; } }
                else if (comp + n > 4 && n > 1) { reg++; comp = 0; }   //float3 normal must not straddle
                if (comp >= 4) { reg++; comp = 0; }
                if (!assigned.ContainsKey(reg)) assigned[reg] = new bool[4];
                string comps = "";
                for (int k = 0; k < n; k++)
                {
                    if (comp >= 4) { reg++; comp = 0; if (!assigned.ContainsKey(reg)) assigned[reg] = new bool[4]; }
                    assigned[reg][comp] = true;
                    comps += "xyzw"[comp];
                    comp++;
                }
                return "t" + reg + "." + comps;
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("#define M_DENSITY " + Alloc(1));
            if (fres)
            {
                sb.AppendLine("#define M_FRES_NORMAL " + Alloc(3));
                sb.AppendLine("#define M_FRES_POWER " + Alloc(1));
            }
            if (soft) sb.AppendLine("#define M_SOFT_DIST " + Alloc(1));
            if (al) sb.AppendLine("#define M_AL_UV " + Alloc(2));
            if (blend)
            {
                sb.AppendLine("#define M_BLEND_END " + Alloc(1));
                sb.AppendLine("#define M_BLEND_START " + Alloc(1));
            }
            if (sec)
            {
                sb.AppendLine("#define M_SEC_END " + Alloc(1));
                sb.AppendLine("#define M_SEC_START " + Alloc(1));
            }
            if (di)
            {
                sb.AppendLine("#define M_DI_PARAM " + Alloc(1));
                sb.AppendLine("#define M_DI_COLOUR " + Alloc(4, wholeReg: true));
            }

            StringBuilder decl = new StringBuilder();
            for (int r = 4; r <= reg; r++) decl.Append("float4 t" + r + " : TEXCOORD" + r + "; ");
            sb.AppendLine("#define XFIELDS " + decl);

            StringBuilder zero = new StringBuilder();
            foreach (KeyValuePair<int, bool[]> kv in assigned)
            {
                string comps = "";
                for (int k = 0; k < 4; k++) if (!kv.Value[k]) comps += "xyzw"[k];
                if (comps.Length > 0)
                    zero.Append("o.t" + kv.Key + "." + comps + " = " + (comps.Length == 1 ? "0" : "float" + comps.Length + "(" + string.Join(",", Enumerable.Repeat("0", comps.Length)) + ")") + "; ");
            }
            sb.AppendLine("#define ZERO_TAIL " + zero);
            return sb.ToString();
        }

        /// CA_SKIN vertex parameters: the two subsurface scales always, plus the alpha-blend noise
        /// power under DIRT_MAPPING.
        private static IEnumerable<int> SkinVSParams(long m)
        {
            bool B(int b) => ((m >> b) & 1) != 0;
            yield return 1; yield return 2;                                     // TRANSMITTANCE/SUBSURFACE_SCALE
            if (B(18)) yield return 51;                                         // ALPHABLEND_NOISE_POWER
        }

        /// CA_SKIN pixel parameters, measured across all 64 shipped masks.
        private static IEnumerable<int> SkinPSParams(long m)
        {
            bool B(int b) => ((m >> b) & 1) != 0;
            yield return 3; yield return 4; yield return 5;                     // BUMP_SCATTERING, DIFFUSE_UV_MULT/TINT
            if (B(7)) yield return 6;                                           // SECONDARY_DIFFUSE_UV_MULT
            if (B(8)) yield return 7;                                           // DIFFUSE_ROUGHNESS_FACTOR
            if (B(9)) { yield return 8; yield return 9; yield return 10; }      // NORMAL uv mult + two strengths
            if (B(10)) for (int p = 11; p <= 16; p++) yield return p;           // SECONDARY_NORMAL group
            if (B(12)) { yield return 41; yield return 42; yield return 43; }   // SPECULAR tint/power/uv
            if (B(13)) yield return 44;                                         // SECONDARY_SPECULAR_UV_MULT
            if (B(14)) yield return 45;                                         // ENVIRONMENT_MAP_MULT
            if (B(16)) yield return 46;                                         // SSR_AMOUNT
            if (B(18)) { yield return 47; yield return 48; yield return 49; yield return 50; }  // DIRT group
        }

        /// CA_SKIN samplers, in SAMPLERS enum order. Note the family binds from s0 with
        /// CONVOLVED_BRDF_MAX_HACK, which nothing samples but which still claims a register - so
        /// every real sampler sits one higher than a naive count of the used maps would give.
        private static List<int> SkinSamplerIds(long mask)
        {
            bool B(int b) => ((mask >> b) & 1) != 0;
            List<int> ids = new List<int>() { 0, 1 };                           // CONVOLVED_BRDF_MAX_HACK, DIFFUSE_MAP
            if (B(7)) ids.Add(2);
            if (B(9)) ids.Add(3);
            if (B(10)) ids.Add(4);
            if (B(11)) { ids.Add(5); ids.Add(6); }
            if (B(12)) ids.Add(7);
            if (B(13)) ids.Add(8);
            if (B(14)) ids.Add(9);
            if (B(17)) ids.Add(10);
            if (B(18)) { ids.Add(11); ids.Add(12); }
            return ids;
        }

        /// CA_SKIN constant slots, sampler registers and interpolants. The interpolant set is a
        /// fixed six - world normal, uv (+ damage uv), view vector and clip w, the skinned
        /// transmittance vector, binormal, tangent - plus the vertex colour under DIRT_MAPPING.
        /// Verified 64/64 against the shipped input signatures.
        private static string SkinMacros(long mask)
        {
            Type paramType = typeof(SHADER_LIST).Assembly.GetType("CATHODE.ShaderTypes." + SHADER_LIST.CA_SKIN + "+PARAMETERS");
            StringBuilder sb = new StringBuilder();
            Dictionary<int, int> vs = AllocSlots(SHADER_LIST.CA_SKIN, SkinVSParams(mask));
            Dictionary<int, int> ps = AllocSlots(SHADER_LIST.CA_SKIN, SkinPSParams(mask));
            foreach (KeyValuePair<int, int> kv in vs)
                sb.AppendLine("#define PV_" + Enum.GetName(paramType, kv.Key) + " " + SlotExpr("rp_parameter_vs", kv.Value, ParamWidth(SHADER_LIST.CA_SKIN, kv.Key)));
            foreach (KeyValuePair<int, int> kv in ps)
                sb.AppendLine("#define P_" + Enum.GetName(paramType, kv.Key) + " " + SlotExpr("rp_parameter_ps", kv.Value, ParamWidth(SHADER_LIST.CA_SKIN, kv.Key)));
            foreach (KeyValuePair<int, int> kv in ps)
                sb.AppendLine("#define PROW_" + Enum.GetName(paramType, kv.Key) + " " + (kv.Value / 4 + 1));

            Type samplerType = typeof(SHADER_LIST).Assembly.GetType("CATHODE.ShaderTypes." + SHADER_LIST.CA_SKIN + "+SAMPLERS");
            List<int> smps = SkinSamplerIds(mask);
            for (int i = 0; i < smps.Count; i++)
            {
                string name = Enum.GetName(samplerType, smps[i]);
                sb.AppendLine("#define SMP_" + name + " s" + i);
                sb.AppendLine("#define TEX_" + name + " t" + i);
            }

            bool dirt = ((mask >> 18) & 1) != 0;
            string fields = "float4 t0 : TEXCOORD0; float4 t1 : TEXCOORD1; float4 t2 : TEXCOORD2; "
                          + "float4 t3 : TEXCOORD3; float4 t4 : TEXCOORD4; float4 t5 : TEXCOORD5;"
                          + (dirt ? " float4 vcol : COLOR0;" : "");
            //SV_Position reaches the pixel shader on every mask, unread
            fields += " float4 pos : SV_Position;";
            sb.AppendLine("#define SKN_PS_FIELDS " + fields);
            //the VS output struct is exactly the PS input struct here
            sb.AppendLine("#define SKN_VS_FIELDS " + fields);
            return sb.ToString();
        }

        /// A parameter slot as an HLSL expression into the constant array.
        private static string SlotExpr(string arrayName, int slot, int width)
        {
            int reg = slot / 4, comp = slot % 4;
            string comps = "xyzw".Substring(comp, width);
            return width == 4 && comp == 0 ? arrayName + "[" + reg + "]" : arrayName + "[" + reg + "]." + comps;
        }

        /// CA_LOW_LOD_CHARACTER vertex parameters: the normal strength always, plus the three
        /// custom tint colours - which are VS constants only on the unskinned path.
        private static IEnumerable<int> LlcVSParams(long m)
        {
            bool B(int b) => ((m >> b) & 1) != 0;
            yield return 8;                                                     // NORMAL_MAP_STRENGTH
            if (B(7)) { yield return 14; yield return 15; yield return 16; }
        }

        /// CA_LOW_LOD_CHARACTER pixel parameters, measured across the shipped masks.
        private static IEnumerable<int> LlcPSParams(long m)
        {
            bool B(int b) => ((m >> b) & 1) != 0;
            yield return 4; yield return 5; yield return 6; yield return 7;      // FRESNEL, DIFFUSE uv/tint, NORMAL uv
            if (B(17)) { yield return 9; yield return 10; yield return 11; }     // SPECULAR
            if (B(19)) yield return 12;                                          // DIFFUSE_ROUGHNESS_FACTOR
            if (B(22)) yield return 13;                                          // IS_CUSTOM_CHARACTER_DECAL
            if (B(27)) yield return 17;                                          // SSR_AMOUNT
            if (B(29)) { yield return 18; yield return 19; }                     // ENVIRONMENT
        }

        /// CA_LOW_LOD_CHARACTER samplers, in SAMPLERS enum order.
        private static List<int> LlcSamplerIds(long mask)
        {
            bool B(int b) => ((mask >> b) & 1) != 0;
            List<int> ids = new List<int>() { 0 };                               // DIFFUSE_MAP
            if (B(15)) ids.Add(1);
            if (B(17)) ids.Add(2);
            if (B(24) || B(22) || (B(26) && B(17))) ids.Add(3);  // CUSTOM_CHARACTER and the plastic mask read it too
            if (B(28)) ids.Add(4);
            if (B(29)) ids.Add(5);
            return ids;
        }

        /// CA_LOW_LOD_CHARACTER constant slots, sampler registers and interpolants: world normal
        /// and uv, a clip copy under DEPTH_ONLY (the depth pass reads z/w from it), the tangent
        /// frame, the three custom tint colours, and the view vector.
        private static string LlcMacros(long mask)
        {
            Type paramType = typeof(SHADER_LIST).Assembly.GetType("CATHODE.ShaderTypes." + SHADER_LIST.CA_LOW_LOD_CHARACTER + "+PARAMETERS");
            StringBuilder sb = new StringBuilder();
            Dictionary<int, int> vs = AllocSlots(SHADER_LIST.CA_LOW_LOD_CHARACTER, LlcVSParams(mask));
            Dictionary<int, int> ps = AllocSlots(SHADER_LIST.CA_LOW_LOD_CHARACTER, LlcPSParams(mask));
            foreach (KeyValuePair<int, int> kv in vs)
                sb.AppendLine("#define PV_" + Enum.GetName(paramType, kv.Key) + " " + SlotExpr("rp_parameter_vs", kv.Value, ParamWidth(SHADER_LIST.CA_LOW_LOD_CHARACTER, kv.Key)));
            foreach (KeyValuePair<int, int> kv in ps)
                sb.AppendLine("#define P_" + Enum.GetName(paramType, kv.Key) + " " + SlotExpr("rp_parameter_ps", kv.Value, ParamWidth(SHADER_LIST.CA_LOW_LOD_CHARACTER, kv.Key)));

            Type samplerType = typeof(SHADER_LIST).Assembly.GetType("CATHODE.ShaderTypes." + SHADER_LIST.CA_LOW_LOD_CHARACTER + "+SAMPLERS");
            List<int> smps = LlcSamplerIds(mask);
            for (int i = 0; i < smps.Count; i++)
            {
                string name = Enum.GetName(samplerType, smps[i]);
                sb.AppendLine("#define SMP_" + name + " s" + i);
                sb.AppendLine("#define TEX_" + name + " t" + i);
            }

            bool Bit(int b) => ((mask >> b) & 1) != 0;
            string f = "float4 t0 : TEXCOORD0; float4 t1 : TEXCOORD1;";
            int ti = 2;
            if (Bit(1)) f += " float4 dep : TEXCOORD" + ti++ + ";";
            if (Bit(15) || Bit(7)) { f += " float4 binorm : TEXCOORD" + ti++ + ";"; f += " float4 tang : TEXCOORD" + ti++ + ";"; }
            if (Bit(22)) { f += " float4 tc1 : TEXCOORD" + ti++ + ";"; f += " float4 tc2 : TEXCOORD" + ti++ + ";"; f += " float4 tc3 : TEXCOORD" + ti++ + ";"; }
            f += " float4 view : TEXCOORD" + ti++ + ";";
            sb.AppendLine("#define LLC_PS_FIELDS " + f + " float4 pos : SV_Position;");
            sb.AppendLine("#define LLC_VS_FIELDS " + f + " float4 pos : SV_Position;");
            return sb.ToString();
        }


        /// CA_RIBBON vertex parameters.
        private static IEnumerable<int> RibbonVSParams(long m)
        {
            bool B(int b) => ((m >> b) & 1) != 0;
            yield return 0; yield return 1; yield return 2;                     // MASK_AMOUNT_MIN/MAX/MIDPOINT
            yield return 5;                                                     // LIFETIME
            yield return 11; yield return 12;                                   // UV_REPEAT, UV_SCROLLSPEED
            if (B(10)) { yield return 13; yield return 14; yield return 15; }    // U2_SCALE, V2_REPEAT, V2_SCROLLSPEED
            yield return 32; yield return 33; yield return 34;                   // WIDTH_START/MID/END
            yield return 35; yield return 36;                                    // WIDTH_IN/OUT
            if (B(22))                                                           // COLOUR_TINT
            {
                yield return 37; yield return 38; yield return 39;               // COLOUR_SCALE_START/MID/END
                yield return 40; yield return 41; yield return 42;               // COLOUR_TINT_START/MID/END
            }
            yield return 43; yield return 44;                                    // FADE_IN, FADE_OUT
            if (B(25)) { yield return 45; yield return 46; }                      // SIDE_FADE_START/END
            if (B(26)) yield return 47;                                          // DIST_SCALE
        }

        /// CA_RIBBON pixel parameters.
        private static IEnumerable<int> RibbonPSParams(long m)
        {
            bool B(int b) => ((m >> b) & 1) != 0;
            yield return 44;                                                     // FADE_OUT
            if (B(34)) { yield return 53; yield return 54; yield return 55; }     // SOFTNESS_*
            if (B(36)) yield return 56;                                          // AMBIENT_LIGHTING_COLOUR
        }

        /// CA_RIBBON samplers. TEXTURE_MAP2 is included by SECOND_TEXTURE (not by MULTI_TEXTURE,
        /// which is only what samples it), so a colour ramp beside an unsampled second texture
        /// still lands at t2.
        private static List<int> RibbonSamplerIds(long mask)
        {
            bool B(int b) => ((mask >> b) & 1) != 0;
            List<int> ids = new List<int>() { 0 };                               // TEXTURE_MAP
            if (B(16)) ids.Add(1);
            if (B(33)) ids.Add(2);
            return ids;
        }

        /// CA_RIBBON engine parameters. Unlike every other mastered family this table is NOT
        /// constant across the shipped masks - each emitter mode contributes its own block.
        /// Derived from, and reproduces exactly, all 126 shipped tables.
        private static List<int> RibbonEngineIds(long mask)
        {
            bool B(int b) => ((mask >> b) & 1) != 0;
            List<int> ids = new List<int>();
            for (int i = 3; i <= 9; i++) ids.Add(i);
            if (B(17)) { ids.Add(16); ids.Add(17); }                             // CONTINUOUS
            if (B(18)) { ids.Add(18); ids.Add(19); ids.Add(20); }                // TRAILING
            if (B(21)) { ids.Add(21); ids.Add(22); ids.Add(23); }                // POINT_TO_POINT
            for (int i = 24; i <= 31; i++) ids.Add(i);
            if (B(27)) { ids.Add(48); ids.Add(49); }                             // SPREAD_FEATURE
            ids.Add(50); ids.Add(51); ids.Add(52);
            return ids;
        }

        /// CA_RIBBON constant slots, samplers and interpolants: t0 = colour+alpha, t1 = uv, then a
        /// packed stream of the lit uv (2 lanes), the second uv (2) and the view depth (1), in
        /// that order, rounded up to whole float4s. SV_Position reaches the pixel shader only
        /// under SOFTNESS. Verified 126/126 against the shipped input signatures.
        private static string RibbonMacros(long mask)
        {
            Type paramType = typeof(SHADER_LIST).Assembly.GetType("CATHODE.ShaderTypes." + SHADER_LIST.CA_RIBBON + "+PARAMETERS");
            Type samplerType = typeof(SHADER_LIST).Assembly.GetType("CATHODE.ShaderTypes." + SHADER_LIST.CA_RIBBON + "+SAMPLERS");
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<int, int> kv in AllocSlots(SHADER_LIST.CA_RIBBON, RibbonVSParams(mask)))
                sb.AppendLine("#define PV_" + Enum.GetName(paramType, kv.Key) + " " + SlotExpr("rp_parameter_vs", kv.Value, ParamWidth(SHADER_LIST.CA_RIBBON, kv.Key)));
            foreach (KeyValuePair<int, int> kv in AllocSlots(SHADER_LIST.CA_RIBBON, RibbonPSParams(mask)))
                sb.AppendLine("#define P_" + Enum.GetName(paramType, kv.Key) + " " + SlotExpr("rp_parameter_ps", kv.Value, ParamWidth(SHADER_LIST.CA_RIBBON, kv.Key)));

            List<int> smps = RibbonSamplerIds(mask);
            for (int i = 0; i < smps.Count; i++)
            {
                string name = Enum.GetName(samplerType, smps[i]);
                sb.AppendLine("#define SMP_" + name + " s" + i);
                sb.AppendLine("#define TEX_" + name + " t" + i);
            }

            bool B(int b) => ((mask >> b) & 1) != 0;
            int lanes = (B(3) ? 2 : 0) + (B(10) ? 2 : 0) + (B(34) ? 1 : 0);
            int extra = (lanes + 3) / 4;
            List<string> fields = new List<string>() { "float4 t0 : TEXCOORD0;", "float4 t1 : TEXCOORD1;" };
            for (int i = 0; i < extra; i++)
                fields.Add("float4 t" + (2 + i) + " : TEXCOORD" + (2 + i) + ";");
            string body = string.Join(" ", fields.ToArray());
            sb.AppendLine("#define RIB_PS_FIELDS " + body + (B(34) ? " float4 pos : SV_Position;" : ""));
            sb.AppendLine("#define RIB_VS_FIELDS " + body + " float4 pos : SV_Position;");
            return sb.ToString();
        }

        /// CA_DECAL vertex parameters: just the fade duration.
        private static IEnumerable<int> DecalVSParams(long m)
        {
            yield return 0;                                                     // FADE_TOTALTIME
        }

        /// CA_DECAL pixel parameters, measured across all 36 shipped masks.
        private static IEnumerable<int> DecalPSParams(long m)
        {
            bool B(int b) => ((m >> b) & 1) != 0;
            yield return 1; yield return 2; yield return 3;                     // SPECULAR_POWER/LEVEL, GLOW_COLOUR
            if (B(9)) { yield return 5; yield return 6; yield return 7; }        // NORMAL_MAP_EASE/MULTIPLY_START/END
            if (B(14)) { yield return 8; yield return 9; }                       // PARALLAX_SCALE, PARALLAX_EASE_DURATION
            if (B(15)) { yield return 10; yield return 11; }                     // BURNTHROUGH_THRESHOLD/DEPTH
            if (B(16)) for (int p = 12; p <= 16; p++) yield return p;            // LIQUIFX
            if (B(19)) for (int p = 17; p <= 22; p++) yield return p;            // ALPHATHRESHOLD
            if (B(21)) yield return 23;                                          // ALPHATHRESHOLD_CLAMP_REFERENCE
            if (B(22)) yield return 24;                                          // LIQUIFX2_DURATION
            if (B(24)) yield return 25;                                          // ENVIRONMENT_MAP_MULT
            if (B(26)) { yield return 26; yield return 27; yield return 28; }     // MAX/MIN/POWER FRESNEL
            if (B(27)) { yield return 29; yield return 30; yield return 31; }     // COLOUR_START/END/LERP_POWER
        }

        /// CA_DECAL samplers, in SAMPLERS enum order. The trap is ALPHATHRESHOLD_MAP, which is
        /// included by ALPHATHRESHOLD_EXTRAALPHA (bit 20) and NOT by ALPHATHRESHOLD itself - a
        /// threshold without the extra alpha map still gets all six parameters but no sampler.
        private static List<int> DecalSamplerIds(long mask)
        {
            bool B(int b) => ((mask >> b) & 1) != 0;
            List<int> ids = new List<int>();
            if (B(5)) ids.Add(0);
            if (B(6)) ids.Add(1);
            if (B(9)) ids.Add(2);
            if (B(11)) ids.Add(3);
            if (B(12)) ids.Add(4);
            if (B(14)) ids.Add(5);
            if (B(15)) ids.Add(6);
            if (B(16)) ids.Add(7);
            if (B(20)) ids.Add(8);
            if (B(22)) ids.Add(9);
            if (B(24)) ids.Add(10);
            if (B(28)) ids.Add(11);
            return ids;
        }

        /// CA_DECAL constant slots, samplers and interpolants: t0/t1/t2 = the three decal axes
        /// (scaled) with DecalParams.x, the fade and 0 in their w; t3 = clip position; t4 = the
        /// depth-buffer uv times clip w (plus the liquifx flow direction in zw); t5..t8 = the
        /// transposed clip-to-decal matrix; t9 = the decal origin, shipped only when something
        /// needs a view vector. Verified 36/36 against the shipped input signatures.
        private static string DecalMacros(long mask)
        {
            Type paramType = typeof(SHADER_LIST).Assembly.GetType("CATHODE.ShaderTypes." + SHADER_LIST.CA_DECAL + "+PARAMETERS");
            Type samplerType = typeof(SHADER_LIST).Assembly.GetType("CATHODE.ShaderTypes." + SHADER_LIST.CA_DECAL + "+SAMPLERS");
            StringBuilder sb = new StringBuilder();
            Dictionary<int, int> ps = AllocSlots(SHADER_LIST.CA_DECAL, DecalPSParams(mask));
            foreach (KeyValuePair<int, int> kv in AllocSlots(SHADER_LIST.CA_DECAL, DecalVSParams(mask)))
                sb.AppendLine("#define PV_" + Enum.GetName(paramType, kv.Key) + " " + SlotExpr("rp_parameter_vs", kv.Value, ParamWidth(SHADER_LIST.CA_DECAL, kv.Key)));
            foreach (KeyValuePair<int, int> kv in ps)
                sb.AppendLine("#define P_" + Enum.GetName(paramType, kv.Key) + " " + SlotExpr("rp_parameter_ps", kv.Value, ParamWidth(SHADER_LIST.CA_DECAL, kv.Key)));
            foreach (KeyValuePair<int, int> kv in ps)
                sb.AppendLine("#define PROW_" + Enum.GetName(paramType, kv.Key) + " " + (kv.Value / 4 + 1));

            List<int> smps = DecalSamplerIds(mask);
            for (int i = 0; i < smps.Count; i++)
            {
                string name = Enum.GetName(samplerType, smps[i]);
                sb.AppendLine("#define SMP_" + name + " s" + i);
                sb.AppendLine("#define TEX_" + name + " t" + i);
            }

            bool B(int b) => ((mask >> b) & 1) != 0;
            List<string> fields = new List<string>();
            for (int i = 0; i <= 8; i++) fields.Add("float4 t" + i + " : TEXCOORD" + i + ";");
            if (B(14) || B(24) || B(26)) fields.Add("float4 t9 : TEXCOORD9;");
            sb.AppendLine("#define DEC_PS_FIELDS " + string.Join(" ", fields.ToArray()));
            sb.AppendLine("#define DEC_VS_FIELDS DEC_PS_FIELDS float4 pos : SV_Position;");
            return sb.ToString();
        }

        /// CA_PARTICLE vertex parameters. A CPU-simulated particle moves most of the simulation
        /// constants to the engine table, so the VS only carries them on the GPU path.
        private static IEnumerable<int> ParticleVSParams(long m)
        {
            bool B(int b) => ((m >> b) & 1) != 0;
            bool cpu = B(27);
            for (int p = 1; p <= 17; p++) yield return p;                        // ASPECT_RATIO..COLOUR_SCALE_MAX
            if (!cpu) { yield return 18; yield return 19; yield return 20; }      // WIND
            yield return 22; yield return 23; yield return 24;                    // CAMERA_RELATIVE_POS
            yield return 25;                                                      // SPHERE_PROJECTION_RADIUS
            yield return 27; yield return 28;                                     // PIVOT_X/Y
            if (!cpu && B(31)) for (int p = 39; p <= 44; p++) yield return p;      // SPEED
            if (!cpu && B(32)) for (int p = 45; p <= 47; p++) yield return p;      // LAUNCH
            if (!cpu && (B(33) || B(34) || B(35))) for (int p = 48; p <= 50; p++) yield return p;      // EMISSION_AREA
            if (!cpu && B(39)) { yield return 51; yield return 52; }               // GRAVITY
            if (B(40)) for (int p = 53; p <= 56; p++) yield return p;              // COLOUR_TINT + MIDPOINT
            if (!cpu && B(42)) { yield return 57; yield return 58; }               // SPREAD
            if (B(43)) for (int p = 59; p <= 65; p++) yield return p;              // ROTATION
            if (B(46)) { yield return 66; yield return 67; }                       // FADE_NEAR_CAMERA
            if (B(47)) for (int p = 68; p <= 70; p++) yield return p;              // TEXTURE_ANIMATION
            if (B(54)) for (int p = 75; p <= 80; p++) yield return p;              // PIVOT/TURBULENCE
            if (B(60)) yield return 93;                                           // PARALLAX_POSITION
        }

        /// CA_PARTICLE pixel parameters.
        private static IEnumerable<int> ParticlePSParams(long m)
        {
            bool B(int b) => ((m >> b) & 1) != 0;
            yield return 21;                                                      // ALPHA_REF_VALUE
            yield return 26;                                                      // DISTORTION_STRENGTH
            if (B(52)) { yield return 71; yield return 72; yield return 73; }      // SOFTNESS
            if (B(53)) yield return 74;                                           // REVERSE_SOFTNESS_EDGE
            if (B(55)) for (int p = 81; p <= 86; p++) yield return p;              // ALPHATHRESHOLD
            if (B(56)) { yield return 87; yield return 88; }                       // DEPTH_FADE
            if (B(59)) for (int p = 89; p <= 92; p++) yield return p;              // FLOW
            if (B(62)) yield return 94;                                           // AMBIENT_LIGHTING_COLOUR
        }

        /// CA_PARTICLE samplers: TEXTURE_MAP always, the colour ramp on bit 56 and the flow pair
        /// on FLOW_UV_ANIMATION (59).
        private static List<int> ParticleSamplerIds(long mask)
        {
            bool B(int b) => ((mask >> b) & 1) != 0;
            List<int> ids = new List<int>() { 0 };
            if (B(56)) ids.Add(1);
            if (B(59)) { ids.Add(2); ids.Add(3); }
            return ids;
        }

        /// CA_PARTICLE engine parameters. CPU (bit 27) is the master gate: a CPU-simulated
        /// particle ships the whole simulation constant block and each simulation feature adds its
        /// own sub-block, while a GPU particle ships none of it. Reproduces all 2,353 shipped
        /// tables exactly.
        private static List<int> ParticleEngineIds(long mask)
        {
            bool B(int b) => ((mask >> b) & 1) != 0;
            bool cpu = B(27);
            List<int> ids = new List<int>() { 0, 1, 4, 6, 8 };
            if (cpu)
            {
                ids.Add(18); ids.Add(19); ids.Add(20);
                for (int i = 29; i <= 37; i++) ids.Add(i);
            }
            if (B(29)) ids.Add(38);                                               // CUSTOM_SEED_CPU
            if (cpu && B(31)) for (int i = 39; i <= 44; i++) ids.Add(i);           // START_MID_END_SPEED
            if (cpu && B(32)) { ids.Add(45); ids.Add(46); ids.Add(47); }            // LAUNCH_DECELERATE_SPEED
            if (cpu && (B(33) || B(34) || B(35))) { ids.Add(48); ids.Add(49); ids.Add(50); }            // EMISSION_AREA
            if (cpu && B(39)) { ids.Add(51); ids.Add(52); }                        // GRAVITY
            if (cpu && B(42)) { ids.Add(57); ids.Add(58); }                        // SPREAD_FEATURE
            ids.Sort();
            return ids;
        }

        /// CA_PARTICLE interpolant packing. BLENDING_DISTORTION reads none of these - its pixel
        /// shader is a six-instruction short-circuit - but the vertex shader still SHIPS the light
        /// and texture-animation interpolants, and each still costs a 24-byte signature entry.
        private static string ParticleInterpolants(long mask)
        {
            bool B(int b) => ((mask >> b) & 1) != 0;
            List<KeyValuePair<string, int>> fields = new List<KeyValuePair<string, int>>();
            if (B(25)) fields.Add(new KeyValuePair<string, int>("LIGHT", 3));
            else if (B(26)) fields.Add(new KeyValuePair<string, int>("LIGHT", 2));
            //ALPHA_TEST ships the world position and a per-vertex SPRITE NORMAL so the pixel
            //shader can shade the cutout - two whole registers, each with its w flushed to zero
            if (B(23)) { fields.Add(new KeyValuePair<string, int>("WORLDPOS", 3)); fields.Add(new KeyValuePair<string, int>("SPRITENRM", 3)); }
            //FRAMEB, then the occlusion pair, THEN the blend scalar: retail puts OWNZW immediately
            //after FRAMEB and lets BLEND backfill after it
            if (B(47)) fields.Add(new KeyValuePair<string, int>("FRAMEB", 2));
            if (B(22) && B(61)) fields.Add(new KeyValuePair<string, int>("OWNZW", 2));
            //distortion does not ship the blend scalar at all - its pixel shader never samples the
            //sheet, and retail zeroes or reuses the lane instead
            if (B(47) && !B(22)) fields.Add(new KeyValuePair<string, int>("BLEND", 1));
            //under distortion only ONE of BLEND/AGE can ride the free t1.w lane. When
            //TEXTURE_ANIMATION takes it for the blend scalar the age still needs a real field -
            //retail ships a fourth TEXCOORD the pixel shader never reads, visible only in the ISGN
            if (B(55) && (!B(22) || (B(47) && !B(61)))) fields.Add(new KeyValuePair<string, int>("AGE", 1));

            StringBuilder sb = new StringBuilder();
            bool[] free = new bool[64];
            for (int i = 0; i < 64; i++) free[i] = true;
            int maxReg = 1;
            foreach (KeyValuePair<string, int> f in fields)
            {
                int slot = -1;
                for (int s = 0; s < 60 && slot < 0; s++)
                {
                    if (s / 4 != (s + f.Value - 1) / 4) continue;
                    bool fits = true;
                    for (int k = 0; k < f.Value; k++) if (!free[s + k]) { fits = false; break; }
                    if (fits) slot = s;
                }
                for (int k = 0; k < f.Value; k++) free[slot + k] = false;
                int reg = 2 + slot / 4;
                if (reg > maxReg) maxReg = reg;
                sb.AppendLine("#define M_" + f.Key + " t" + reg + "." + "xyzw".Substring(slot % 4, f.Value));
            }
            StringBuilder xf = new StringBuilder();
            for (int r = 2; r <= maxReg; r++) xf.Append("float4 t" + r + " : TEXCOORD" + r + "; ");
            //under BLENDING_DISTORTION the blend scalar rides the t1.w lane the distortion path
            //leaves free, so it never reaches the t2+ field list at all
            if (B(47) && B(22)) sb.AppendLine("#define M_BLEND t1.w");
            //and the ALPHATHRESHOLD age takes the same lane when no sheet competes for it
            if (B(55) && B(22) && !B(47)) sb.AppendLine("#define M_AGE t1.w");
            sb.AppendLine("#define PS_XFIELDS " + xf);

            //VS side: zero any unassigned components of the extra registers
            StringBuilder zt = new StringBuilder();
            for (int r = 2; r <= maxReg; r++)
            {
                string comps = "";
                for (int k = 0; k < 4; k++)
                {
                    int slot = (r - 2) * 4 + k;
                    if (free[slot]) comps += "xyzw"[k];
                }
                if (comps.Length > 0)
                    zt.Append("o.t" + r + "." + comps + " = " + (comps.Length == 1 ? "0.0" : "float" + comps.Length + "(" + string.Join(",", Enumerable.Repeat("0.0", comps.Length).ToArray()) + ")") + "; ");
            }
            sb.AppendLine("#define VS_ZERO_TAIL " + zt);

            int smp = 1;
            if (B(56)) { sb.AppendLine("#define SMP_RAMP s" + smp); sb.AppendLine("#define TEX_RAMP t" + smp); smp++; }
            if (B(59))
            {
                sb.AppendLine("#define SMP_FLOW s" + smp); sb.AppendLine("#define TEX_FLOW t" + smp); smp++;
                sb.AppendLine("#define SMP_FLOWTEX s" + smp); sb.AppendLine("#define TEX_FLOWTEX t" + smp); smp++;
            }
            return sb.ToString();
        }

        /// CA_PARTICLE constant slots plus the interpolant packing.
        private static string ParticleMacros(long mask)
        {
            Type paramType = typeof(SHADER_LIST).Assembly.GetType("CATHODE.ShaderTypes." + SHADER_LIST.CA_PARTICLE + "+PARAMETERS");
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<int, int> kv in AllocSlots(SHADER_LIST.CA_PARTICLE, ParticleVSParams(mask)))
                sb.AppendLine("#define P_" + Enum.GetName(paramType, kv.Key) + " " + SlotExpr("rp_parameter_vs", kv.Value, ParamWidth(SHADER_LIST.CA_PARTICLE, kv.Key)));
            foreach (KeyValuePair<int, int> kv in AllocSlots(SHADER_LIST.CA_PARTICLE, ParticlePSParams(mask)))
                sb.AppendLine("#define P_" + Enum.GetName(paramType, kv.Key) + " " + SlotExpr("rp_parameter_ps", kv.Value, ParamWidth(SHADER_LIST.CA_PARTICLE, kv.Key)));
            sb.Append(ParticleInterpolants(mask));
            return sb.ToString();
        }

        /// CA_ENVIRONMENT vertex parameters.
        private static IEnumerable<int> EnvVSParams(long m)
        {
            bool B(int b) => ((m >> b) & 1) != 0;
            if (B(18)) yield return 11;  // NORMAL_MAP_STRENGTH
            if (B(34)) yield return 28;  // VERT_AO_TINT
            if (B(48)) yield return 40;  // ALPHABLEND_NOISE_POWER
            if (B(55)) yield return 51;  // WETNESS_UV_MULT
        }

        /// CA_ENVIRONMENT tessellation constants. The hull stage always takes the same three, at
        /// slots 0/1/2 on all ten shipped tessellated masks. The domain stage takes what its own
        /// work needs: the tangent frame's scale, the vertex-AO tint it passes through, and
        /// Phong's shape factor.
        private static IEnumerable<int> EnvHSParams(long m)
        {
            if (!Bit(m, 58)) yield break;                                        // TESSELLATION
            yield return 53;                                                     // TESSELLATION_FACTOR
            yield return 54;                                                     // MIN_TESSELLATION_DISTANCE
            yield return 55;                                                     // TESSELLATION_RANGE
        }

        private static IEnumerable<int> EnvDSParams(long m)
        {
            if (!Bit(m, 58)) yield break;                                        // TESSELLATION
            if (Bit(m, 18)) yield return 11;                                     // NORMAL_MAP_STRENGTH
            if (Bit(m, 34)) yield return 28;                                     // VERT_AO_TINT
            if (Bit(m, 60)) yield return 56;                                     // SHAPE_FACTOR (PHONG)
        }

        /// CA_ENVIRONMENT pixel parameters, measured across all 1,226 shipped masks.
        private static IEnumerable<int> EnvPSParams(long m)
        {
            bool B(int b) => ((m >> b) & 1) != 0;
            yield return 3;                                    // FRESNEL_INTENSITY
            if (B(11)) yield return 4;                         // PLANAR_REFLECTIVE_OVERBRIGHT_SCALAR
            if (B(12)) yield return 5;                         // SEPARATE_ALPHA_UV_MULT
            yield return 6; yield return 7;                    // DIFFUSE_UV_MULT, DIFFUSE_TINT
            if (B(16)) { yield return 8; yield return 9; }      // SECONDARY_DIFFUSE
            if (B(18)) yield return 10;                        // NORMAL_UV_MULT
            if (B(20)) { yield return 12; yield return 13; }    // SECONDARY_NORMAL
            if (B(22)) { yield return 14; yield return 15; yield return 16; } // SPECULAR
            if (B(24)) { yield return 17; yield return 18; yield return 19; } // SECONDARY_SPECULAR
            if (B(27)) { yield return 20; yield return 21; yield return 22; } // GLASS
            if (B(28) || B(29) || B(30)) yield return 23;                        // DIFFUSE_ROUGHNESS_FACTOR
            if (B(31)) { yield return 24; yield return 25; }    // ENVIRONMENT_MAPPING
            if (B(32)) { yield return 26; yield return 27; }    // AMBIENT_OCCLUSION
            if (B(35)) { yield return 29; yield return 30; }    // EMISSIVE
            if (B(36)) { yield return 31; yield return 32; }    // DUST
            if (B(38)) yield return 33;                        // SSR_AMOUNT
            if (B(41)) yield return 34;                        // FUR_RIM_LIGHTING_FACTOR
            if (B(42)) { yield return 35; yield return 36; yield return 37; } // PARALLAX
            if (B(43)) yield return 38;                        // OPACITY_MODIFIER_VALUE
            if (B(48)) yield return 39;                        // ALPHABLEND_NOISE_UV_MULT (POWER is VS-only)
            if (B(50)) for (int p = 41; p <= 46; p++) yield return p;          // SPARKLE
            if (B(52)) { yield return 47; yield return 48; yield return 49; }  // DIRT
            if (B(55)) { yield return 50; yield return 51; }    // WETNESS
            if (B(56)) yield return 52;                        // CUSTOM_TINT_COLOUR
        }

        /// CA_ENVIRONMENT samplers, in SAMPLERS enum order.
        private static List<int> EnvSamplerIds(long mask)
        {
            bool B(int b) => ((mask >> b) & 1) != 0;
            List<int> ids = new List<int>();
            if (B(12)) ids.Add(0);
            ids.Add(1);                                                        // DIFFUSE_MAP
            if (B(16)) ids.Add(2);
            if (B(18)) ids.Add(3);
            if (B(20)) ids.Add(4);
            if (B(22)) ids.Add(5);
            if (B(24)) ids.Add(6);
            if (B(31)) ids.Add(7);
            if (B(32)) ids.Add(8);
            if (B(36)) ids.Add(9);
            if (B(39)) ids.Add(10);
            if (B(42)) ids.Add(11);
            if (B(48)) ids.Add(12);
            if (B(50)) ids.Add(13);
            if (B(52)) ids.Add(14);
            if (B(55)) ids.Add(15);
            if (B(61)) ids.Add(16);
            return ids;
        }

        /// CA_ENVIRONMENT engine parameters: a fixed four, plus one for SEPARATE_ALPHA.
        /// Reproduces all 1,226 shipped tables exactly.
        private static List<int> EnvEngineIds(long mask)
        {
            List<int> ids = new List<int>() { 0, 1, 2 };
            if (((mask >> 12) & 1) != 0) ids.Add(5);                            // SEPARATE_ALPHA
            ids.Add(6);
            return ids;
        }

        /* The master names CA_ENVIRONMENT samplers in short form, indexed by SAMPLERS enum id */
        private static readonly string[] _envSamplerNames =
        {
            "SEPALPHA", "DIFFUSE", "SECDIFFUSE", "NORMAL", "SECNORMAL", "SPECULAR", "SECSPECULAR",
            "ENVMAP", "AOMAP", "DUSTMAP", "IRRCUBE", "PARALLAXMAP", "NOISEMAP", "SPARKLEMAP",
            "DIRTMAP", "WETNOISE", "DISPMAP"
        };

        /// CA_ENVIRONMENT constant slots, samplers and interpolants. The PS input struct is a
        /// fixed trio then feature extras in observed retail order, with sequential TEXCOORD
        /// indices (the ISGN bytes depend on them). Verified 1,226/1,226 against the shipped
        /// input signatures.
        private static string EnvironmentMacros(long mask)
        {
            Type paramType = typeof(SHADER_LIST).Assembly.GetType("CATHODE.ShaderTypes." + SHADER_LIST.CA_ENVIRONMENT + "+PARAMETERS");
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<int, int> kv in AllocSlots(SHADER_LIST.CA_ENVIRONMENT, EnvVSParams(mask)))
                sb.AppendLine("#define PV_" + Enum.GetName(paramType, kv.Key) + " " + SlotExpr("rp_parameter_vs", kv.Value, ParamWidth(SHADER_LIST.CA_ENVIRONMENT, kv.Key)));
            foreach (KeyValuePair<int, int> kv in AllocSlots(SHADER_LIST.CA_ENVIRONMENT, EnvPSParams(mask)))
                sb.AppendLine("#define P_" + Enum.GetName(paramType, kv.Key) + " " + SlotExpr("rp_parameter_ps", kv.Value, ParamWidth(SHADER_LIST.CA_ENVIRONMENT, kv.Key)));
            foreach (KeyValuePair<int, int> kv in AllocSlots(SHADER_LIST.CA_ENVIRONMENT, EnvHSParams(mask)))
                sb.AppendLine("#define PH_" + Enum.GetName(paramType, kv.Key) + " " + SlotExpr("rp_parameter_hs", kv.Value, ParamWidth(SHADER_LIST.CA_ENVIRONMENT, kv.Key)));
            foreach (KeyValuePair<int, int> kv in AllocSlots(SHADER_LIST.CA_ENVIRONMENT, EnvDSParams(mask)))
                sb.AppendLine("#define PD_" + Enum.GetName(paramType, kv.Key) + " " + SlotExpr("rp_parameter_ds", kv.Value, ParamWidth(SHADER_LIST.CA_ENVIRONMENT, kv.Key)));

            //The master pins several samplers to fixed registers outside this sequence: the planar
            //reflection buffer (s7), the alpha-light position map (s9), the fresnel LUT (s11, always)
            //and the radiosity pair (s12/s13). No shipped mask allocates enough sequence samplers to
            //reach any of them - the longest shipped run is 9, ending at s8 - but a never-shipped
            //feature combination can, and the collision is a hard compile error. Step over whichever
            //fixed registers this mask actually declares.
            HashSet<int> reservedRegisters = new HashSet<int>() { 11 };
            if (((mask >> 11) & 1) != 0) reservedRegisters.Add(7);
            if (((mask >> 6) & 1) != 0 && ((mask >> 49) & 1) != 0)
            {
                reservedRegisters.Add(9);
                reservedRegisters.Add(12);
                reservedRegisters.Add(13);
            }

            List<int> smps = EnvSamplerIds(mask);
            int smpReg = 0;
            for (int i = 0; i < smps.Count; i++)
            {
                while (reservedRegisters.Contains(smpReg)) smpReg++;
                sb.AppendLine("#define SMP_" + _envSamplerNames[smps[i]] + " s" + smpReg);
                sb.AppendLine("#define TEX_" + _envSamplerNames[smps[i]] + " t" + smpReg);
                smpReg++;
            }

            bool B(int b) => ((mask >> b) & 1) != 0;
            bool lit = B(40) || B(51);
            List<string> fields = new List<string>() { "float4 t0 : TEXCOORD0;", "float4 t1 : TEXCOORD1;", "float4 t2 : TEXCOORD2;" };
            int ti = 3;
            //FOG_ALPHA vertex fog term rides straight after the prelight
            if (B(1)) fields.Add("float4 fog : TEXCOORD" + ti++ + ";");
            //planar reflection puts the view vector ahead of the tangent frame
            if (lit && B(11)) fields.Add("float4 view : TEXCOORD" + ti++ + ";");
            //the tangent frame ships for a SECONDARY normal map too, even with no primary one
            if (B(18) || B(20)) { fields.Add("float4 tanA : TEXCOORD" + ti++ + ";"); fields.Add("float4 tanB : TEXCOORD" + ti++ + ";"); }
            //view precedes vao when SPEC/ENV/DIFFUSE_ROUGHNESS, or a normal map outside decals/dirt
            bool vaoLate = B(22) || B(31) || B(28) || ((B(18) || B(20)) && !B(43) && !B(52));
            if (B(34) && !vaoLate) fields.Add("float4 vao : TEXCOORD" + ti++ + ";");
            if (lit && !B(11)) fields.Add("float4 view : TEXCOORD" + ti++ + ";");
            if (B(34) && vaoLate) fields.Add("float4 vao : TEXCOORD" + ti++ + ";");
            if (B(49)) fields.Add("float4 alit : TEXCOORD" + ti++ + ";");
            //AO_UV probe coord displaced off t1.zw - folds into alit.zw when ALPHA_LIGHTING
            if (B(33) && B(51) && !B(49)) fields.Add("float4 auvp : TEXCOORD" + ti++ + ";");
            //WETNESS triplanar uv displaced off t1.zw - folds into alit.zw the same way
            if (B(55) && B(51) && !B(49)) fields.Add("float4 wet : TEXCOORD" + ti++ + ";");
            if (B(0)) fields.Add("float4 vcol : COLOR0;");
            sb.AppendLine("#define ENV_PS_FIELDS " + string.Join(" ", fields.ToArray()));

            //TESSELLATION feeds a hull shader, not the rasterizer: the VS output is a control
            //point, so there is no SV_Position and the layout is its own - the vertex colour sits
            //ahead of every TEXCOORD instead of trailing them
            if (B(58))
            {
                List<string> tf = new List<string>() { "float4 cp : CP_Position;" };
                if (B(0)) tf.Add("float4 vcol : COLOR0;");
                int tt = 0;
                tf.Add("float4 nrm : TEXCOORD" + tt++ + ";");
                tf.Add("float4 uv : TEXCOORD" + tt++ + ";");
                tf.Add("float4 pre : TEXCOORD" + tt++ + ";");
                if (B(18) || B(20)) { tf.Add("float4 tanA : TEXCOORD" + tt++ + ";"); tf.Add("float4 tanB : TEXCOORD" + tt++ + ";"); }
                tf.Add("float4 vw : TEXCOORD" + tt++ + ";");
                if (B(34)) tf.Add("float4 vao : TEXCOORD" + tt++ + ";");
                sb.AppendLine("#define ENV_VS_FIELDS " + string.Join(" ", tf.ToArray()));
            }
            else sb.AppendLine("#define ENV_VS_FIELDS ENV_PS_FIELDS float4 pos : SV_Position;");
            return sb.ToString();
        }

        /// CA_DECAL_ENVIRONMENT is CA_ENVIRONMENT with its feature bits renumbered and its own
        /// tail - a projected decal that fades on a timer and burns away through an alpha
        /// threshold. Only three masks ship, so the inclusion rule is the plain per-feature one.
        public static IEnumerable<int> DecEnvVSParams(long m)
        {
            if (Bit(m, 14)) yield return 8;    //ALPHABLEND_NOISE_POWER
            if (Bit(m, 21)) yield return 15;   //NORMAL_MAP_STRENGTH
            if (Bit(m, 37)) yield return 32;   //VERT_AO_TINT
            yield return 49;                   //FADE_TOTALTIME - every shipped mask carries it
        }

        public static IEnumerable<int> DecEnvPSParams(long m)
        {
            yield return 3;                                                   //FRESNEL_INTENSITY
            if (Bit(m, 11)) { yield return 4; yield return 5; yield return 6; }   //DIRT
            if (Bit(m, 14)) yield return 7;                                   //ALPHABLEND_NOISE_UV_MULT
            if (Bit(m, 15)) yield return 9;                                   //SEPARATE_ALPHA_UV_MULT
            yield return 10; yield return 11;                                 //DIFFUSE_UV_MULT, DIFFUSE_TINT
            if (Bit(m, 19)) { yield return 12; yield return 13; }             //SECONDARY_DIFFUSE
            if (Bit(m, 21)) yield return 14;                                  //NORMAL_UV_MULT
            if (Bit(m, 23)) { yield return 16; yield return 17; }             //SECONDARY_NORMAL
            if (Bit(m, 25)) { yield return 18; yield return 19; yield return 20; }   //SPECULAR
            if (Bit(m, 27)) { yield return 21; yield return 22; yield return 23; }   //SECONDARY_SPECULAR
            if (Bit(m, 30)) { yield return 24; yield return 25; yield return 26; }   //GLASS
            if (Bit(m, 31)) yield return 27;                                  //DIFFUSE_ROUGHNESS_FACTOR
            if (Bit(m, 34)) { yield return 28; yield return 29; }             //ENVIRONMENT_MAPPING
            if (Bit(m, 35)) { yield return 30; yield return 31; }             //AMBIENT_OCCLUSION
            if (Bit(m, 38)) { yield return 33; yield return 34; }             //EMISSIVE
            if (Bit(m, 39)) { yield return 35; yield return 36; }             //DUST
            if (Bit(m, 41)) yield return 37;                                  //SSR_AMOUNT
            if (Bit(m, 44)) yield return 38;                                  //FUR_RIM_LIGHTING_FACTOR
            if (Bit(m, 45)) { yield return 39; yield return 40; yield return 41; }   //PARALLAX
            if (Bit(m, 46)) yield return 42;                                  //OPACITY_MODIFIER_VALUE
            if (Bit(m, 57)) for (int p = 50; p <= 55; p++) yield return p;     //ALPHATHRESHOLD
            if (Bit(m, 59)) { yield return 56; yield return 57; yield return 58; }   //COLOUR_LERP
        }

        /// CA_DECAL_ENVIRONMENT samplers, in SAMPLERS enum order.
        private static List<int> DecEnvSamplerIds(long m)
        {
            List<int> ids = new List<int>();
            if (Bit(m, 10)) ids.Add(0);        //BEST_FIT_NORMAL_LOOKUP
            if (Bit(m, 11)) ids.Add(1);        //DIRT_MAP
            if (Bit(m, 14)) ids.Add(2);        //ALPHABLEND_NOISE_MAP
            if (Bit(m, 15)) ids.Add(3);        //SEPARATE_ALPHA_MAP
            ids.Add(4);                        //DIFFUSE_MAP
            if (Bit(m, 19)) ids.Add(5);
            if (Bit(m, 21)) ids.Add(6);        //NORMAL_MAP
            if (Bit(m, 23)) ids.Add(7);
            if (Bit(m, 25)) ids.Add(8);
            if (Bit(m, 27)) ids.Add(9);
            if (Bit(m, 34)) ids.Add(10);
            if (Bit(m, 35)) ids.Add(11);
            if (Bit(m, 39)) ids.Add(12);
            if (Bit(m, 42)) ids.Add(13);
            if (Bit(m, 45)) ids.Add(14);       //PARALLAX_MAP
            if (Bit(m, 55)) ids.Add(15);
            if (Bit(m, 58)) ids.Add(16);       //ALPHATHRESHOLD_MAP - only with EXTRAALPHA
            if (Bit(m, 60)) ids.Add(17);
            return ids;
        }

        /// CA_DECAL_ENVIRONMENT engine parameters: the same fixed set on all three shipped masks.
        private static List<int> DecEnvEngineIds(long mask)
        {
            return new List<int>() { 0, 1, 2, 10 };
        }

        /* The master names CA_DECAL_ENVIRONMENT samplers in short form, indexed by SAMPLERS enum id */
        private static readonly string[] _decEnvSamplerNames =
        {
            "BFN", "DIRTMAP", "NOISEMAP", "SEPALPHA", "DIFFUSE", "SECDIFFUSE", "NORMAL",
            "SECNORMAL", "SPECULAR", "SECSPECULAR", "ENVMAP", "AOMAP", "DUSTMAP", "IRRCUBE",
            "PARALLAXMAP", "DISPMAP", "ATMAP", "RAMPMAP"
        };

        /// CA_DECAL_ENVIRONMENT constant slots, samplers and interpolants.
        private static string DecEnvMacros(long mask)
        {
            Type paramType = typeof(SHADER_LIST).Assembly.GetType("CATHODE.ShaderTypes." + SHADER_LIST.CA_DECAL_ENVIRONMENT + "+PARAMETERS");
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<int, int> kv in AllocSlots(SHADER_LIST.CA_DECAL_ENVIRONMENT, DecEnvVSParams(mask)))
                sb.AppendLine("#define PV_" + Enum.GetName(paramType, kv.Key) + " " + SlotExpr("rp_parameter_vs", kv.Value, ParamWidth(SHADER_LIST.CA_DECAL_ENVIRONMENT, kv.Key)));
            foreach (KeyValuePair<int, int> kv in AllocSlots(SHADER_LIST.CA_DECAL_ENVIRONMENT, DecEnvPSParams(mask)))
                sb.AppendLine("#define P_" + Enum.GetName(paramType, kv.Key) + " " + SlotExpr("rp_parameter_ps", kv.Value, ParamWidth(SHADER_LIST.CA_DECAL_ENVIRONMENT, kv.Key)));
            List<int> smps = DecEnvSamplerIds(mask);
            for (int i = 0; i < smps.Count; i++)
            {
                sb.AppendLine("#define SMP_" + _decEnvSamplerNames[smps[i]] + " s" + i);
                sb.AppendLine("#define TEX_" + _decEnvSamplerNames[smps[i]] + " t" + i);
            }

            //Interpolants: normal+fade, uv pair, tangent frame, view, alpha-light coord. The
            //decal's own age rides whichever pair has a spare lane - t1.zw unless a lightmap
            //took it, and then the alpha-light coord's.
            List<string> fields = new List<string>() { "float4 t0 : TEXCOORD0;", "float4 t1 : TEXCOORD1;" };
            int ti = 2;
            if (Bit(mask, 21) || Bit(mask, 23)) { fields.Add("float4 tanA : TEXCOORD" + ti++ + ";"); fields.Add("float4 tanB : TEXCOORD" + ti++ + ";"); }
            fields.Add("float4 view : TEXCOORD" + ti++ + ";");
            if (Bit(mask, 51)) fields.Add("float4 alit : TEXCOORD" + ti++ + ";");
            if (Bit(mask, 0)) fields.Add("float4 vcol : COLOR0;");
            sb.AppendLine("#define DE_PS_FIELDS " + string.Join(" ", fields.ToArray()));
            sb.AppendLine("#define DE_VS_FIELDS DE_PS_FIELDS float4 pos : SV_Position;");
            return sb.ToString();
        }

        /// CA_SKIN_OCCLUSION: DRAW_PASS is the engine parameter, the depth bias is the vertex
        /// stage's only one, and the diffuse pair follows the decal feature that samples it.
        public static IEnumerable<int> SknOccVSParams(long m)
        {
            yield return 1;                                             //DEPTH_BIAS
        }

        public static IEnumerable<int> SknOccPSParams(long m)
        {
            if (Bit(m, 2)) { yield return 2; yield return 3; }          //DECAL_DIFFUSE
        }

        private static List<int> SknOccSamplerIds(long m)
        {
            List<int> ids = new List<int>();
            if (Bit(m, 2)) ids.Add(0);                                  //DIFFUSE_MAP
            return ids;
        }

        private static string SknOccMacros(long mask)
        {
            Type paramType = typeof(SHADER_LIST).Assembly.GetType("CATHODE.ShaderTypes." + SHADER_LIST.CA_SKIN_OCCLUSION + "+PARAMETERS");
            Type smpType = typeof(SHADER_LIST).Assembly.GetType("CATHODE.ShaderTypes." + SHADER_LIST.CA_SKIN_OCCLUSION + "+SAMPLERS");
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<int, int> kv in AllocSlots(SHADER_LIST.CA_SKIN_OCCLUSION, SknOccVSParams(mask)))
                sb.AppendLine("#define PV_" + Enum.GetName(paramType, kv.Key) + " " + SlotExpr("rp_parameter_vs", kv.Value, ParamWidth(SHADER_LIST.CA_SKIN_OCCLUSION, kv.Key)));
            foreach (KeyValuePair<int, int> kv in AllocSlots(SHADER_LIST.CA_SKIN_OCCLUSION, SknOccPSParams(mask)))
                sb.AppendLine("#define P_" + Enum.GetName(paramType, kv.Key) + " " + SlotExpr("rp_parameter_ps", kv.Value, ParamWidth(SHADER_LIST.CA_SKIN_OCCLUSION, kv.Key)));
            List<int> smps = SknOccSamplerIds(mask);
            for (int i = 0; i < smps.Count; i++)
            {
                string n = Enum.GetName(smpType, smps[i]);
                sb.AppendLine("#define SMP_" + n + " s" + i);
                sb.AppendLine("#define TEX_" + n + " t" + i);
            }
            return sb.ToString();
        }

        /// CA_SURFACE_EFFECTS: a frost/sparkle/glass overlay on the environment model. One mask
        /// ships, so these are the plain per-feature rules; the eleven features it always sets
        /// can be switched off, and the nineteen it never sets have no shipped code to recover.
        public static IEnumerable<int> SfxVSParams(long m)
        {
            if (Bit(m, 10)) yield return 7;                             //NORMAL_MAP_STRENGTH
            if (Bit(m, 27)) { yield return 26; yield return 27; yield return 28; yield return 29; }   //SPARKLE
        }

        public static IEnumerable<int> SfxPSParams(long m)
        {
            yield return 3;                                             //FRESNEL_INTENSITY
            yield return 4; yield return 5;                             //DIFFUSE_UV_MULT, DIFFUSE_TINT
            if (Bit(m, 10)) yield return 6;                             //NORMAL_UV_MULT
            if (Bit(m, 12)) { yield return 8; yield return 9; yield return 10; }   //SPECULAR
            if (Bit(m, 16)) { yield return 11; yield return 12; }       //EMISSIVE
            if (Bit(m, 17)) { yield return 13; yield return 14; }       //PARALLAX
            if (Bit(m, 18)) { yield return 15; yield return 16; }       //FROST
            if (Bit(m, 20)) yield return 17;                            //ENVIRONMENT_MAP_MULT
            if (Bit(m, 22)) { yield return 18; yield return 19; yield return 20; }   //GLASS fresnel range
            if (Bit(m, 23)) yield return 21;                            //DEPTH_COLOUR
            if (Bit(m, 22)) yield return 22;                            //DIFFUSE_ROUGHNESS_FACTOR
            if (Bit(m, 25)) { yield return 23; yield return 24; }       //RIM_LIGHTING
            if (Bit(m, 26)) yield return 25;                            //WRAP_NORMALS_FACTOR
            if (Bit(m, 27)) { yield return 30; yield return 31; }       //SPARKLE
        }

        private static List<int> SfxSamplerIds(long m)
        {
            List<int> ids = new List<int>();
            ids.Add(0);                                                 //DIFFUSE_MAP
            if (Bit(m, 10)) ids.Add(1);                                 //NORMAL_MAP
            if (Bit(m, 12)) ids.Add(2);                                 //SPECULAR_MAP
            if (Bit(m, 17)) ids.Add(3);                                 //PARALLAX_MAP
            if (Bit(m, 18)) ids.Add(4);                                 //FROST_MAP
            if (Bit(m, 20)) ids.Add(5);                                 //ENVIRONMENT_MAP
            if (Bit(m, 27)) ids.Add(6);                                 //SPARKLE_MAP
            return ids;
        }

        private static string SfxMacros(long mask)
        {
            Type paramType = typeof(SHADER_LIST).Assembly.GetType("CATHODE.ShaderTypes." + SHADER_LIST.CA_SURFACE_EFFECTS + "+PARAMETERS");
            Type smpType = typeof(SHADER_LIST).Assembly.GetType("CATHODE.ShaderTypes." + SHADER_LIST.CA_SURFACE_EFFECTS + "+SAMPLERS");
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<int, int> kv in AllocSlots(SHADER_LIST.CA_SURFACE_EFFECTS, SfxVSParams(mask)))
                sb.AppendLine("#define PV_" + Enum.GetName(paramType, kv.Key) + " " + SlotExpr("rp_parameter_vs", kv.Value, ParamWidth(SHADER_LIST.CA_SURFACE_EFFECTS, kv.Key)));
            foreach (KeyValuePair<int, int> kv in AllocSlots(SHADER_LIST.CA_SURFACE_EFFECTS, SfxPSParams(mask)))
                sb.AppendLine("#define P_" + Enum.GetName(paramType, kv.Key) + " " + SlotExpr("rp_parameter_ps", kv.Value, ParamWidth(SHADER_LIST.CA_SURFACE_EFFECTS, kv.Key)));
            List<int> smps = SfxSamplerIds(mask);
            for (int i = 0; i < smps.Count; i++)
            {
                string n = Enum.GetName(smpType, smps[i]);
                sb.AppendLine("#define SMP_" + n + " s" + i);
                sb.AppendLine("#define TEX_" + n + " t" + i);
            }
            return sb.ToString();
        }
        /// CA_LIGHTMAP_ENVIRONMENT vertex parameters.
        private static IEnumerable<int> LmeVSParams(long m)
        {
            bool B(int b) => ((m >> b) & 1) != 0;
            if (B(0)) yield return 9;                                            // ALPHABLEND_NOISE_POWER
            if (B(22) || B(24)) yield return 16;                                          // NORMAL_MAP_STRENGTH
            if (B(38)) yield return 33;                                          // VERT_AO_TINT
        }

        /// CA_LIGHTMAP_ENVIRONMENT pixel parameters. The dirt group follows VERTEX_COLOUR, not
        /// DIRT_MAPPING - every shipped dirt mask carries the vertex colour driving its coverage.
        private static IEnumerable<int> LmePSParams(long m)
        {
            bool B(int b) => ((m >> b) & 1) != 0;
            yield return 3; yield return 4;                                      // FRESNEL, LIGHTMAP_INTENSITY_SCALE
            if (B(0) || B(12)) { yield return 5; yield return 6; yield return 7; }  // DIRT group - DIRT_MAPPING reads it without VERTEX_COLOUR
            if (B(0) || B(15)) yield return 8;                                    // ALPHABLEND_NOISE_UV_MULT - ALPHABLEND_NOISE reads it too
            yield return 11; yield return 12;                                    // DIFFUSE_UV_MULT, DIFFUSE_TINT
            if (B(22)) yield return 15;                                          // NORMAL_UV_MULT
            if (B(19) || B(46)) { yield return 40; yield return 41; yield return 42; } // PARALLAX (PARALLAX_MAPPING reads them too)
            if (B(24)) { yield return 17; yield return 18; }                      // SECONDARY_NORMAL
            if (B(26)) { yield return 19; yield return 20; yield return 21; }     // SPECULAR
            if (B(28)) { yield return 22; yield return 23; yield return 24; }     // SECONDARY_SPECULAR
            if (B(31)) { yield return 25; yield return 26; yield return 27; }     // GLASS
            if (B(32) || B(33)) yield return 28;                                          // DIFFUSE_ROUGHNESS_FACTOR (FRONT_ROUGHNESS reads it too)
            if (B(35)) { yield return 29; yield return 30; }                      // ENVIRONMENT
            if (B(36)) { yield return 31; yield return 32; }                      // AMBIENT_OCCLUSION
            if (B(39)) { yield return 34; yield return 35; }                      // EMISSIVE
        }

        /// CA_LIGHTMAP_ENVIRONMENT samplers, in SAMPLERS enum order. LIGHTMAP_MAP and DIFFUSE_MAP
        /// are unconditional; the fresnel LUT sits on a fixed s11/t11 outside this sequence.
        private static List<int> LmeSamplerIds(long mask)
        {
            bool B(int b) => ((mask >> b) & 1) != 0;
            List<int> ids = new List<int>() { 0 };                               // LIGHTMAP_MAP
            if (B(11)) ids.Add(1);
            if (B(12)) ids.Add(2);
            if (B(15)) ids.Add(3);
            if (B(16)) ids.Add(4);
            ids.Add(5);                                                          // DIFFUSE_MAP
            if (B(20)) ids.Add(6);
            if (B(22)) ids.Add(7);
            if (B(24)) ids.Add(8);
            if (B(26)) ids.Add(9);
            if (B(28)) ids.Add(10);
            if (B(35)) ids.Add(11);
            if (B(36)) ids.Add(12);
            if (B(40)) ids.Add(13);
            if (B(43)) ids.Add(14);
            if (B(46)) ids.Add(15);
            return ids;
        }

        /// CA_LIGHTMAP_ENVIRONMENT constant slots, samplers and interpolants: world normal, the uv
        /// pair (diffuse in xy, lightmap in zw), the tangent frame, the view vector (which ships
        /// iff SPECULAR or ENVIRONMENT needs it), the vertex AO tint and the vertex colour.
        private static string LmeMacros(long mask)
        {
            Type paramType = typeof(SHADER_LIST).Assembly.GetType("CATHODE.ShaderTypes." + SHADER_LIST.CA_LIGHTMAP_ENVIRONMENT + "+PARAMETERS");
            Type samplerType = typeof(SHADER_LIST).Assembly.GetType("CATHODE.ShaderTypes." + SHADER_LIST.CA_LIGHTMAP_ENVIRONMENT + "+SAMPLERS");
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<int, int> kv in AllocSlots(SHADER_LIST.CA_LIGHTMAP_ENVIRONMENT, LmeVSParams(mask)))
                sb.AppendLine("#define PV_" + Enum.GetName(paramType, kv.Key) + " " + SlotExpr("rp_parameter_vs", kv.Value, ParamWidth(SHADER_LIST.CA_LIGHTMAP_ENVIRONMENT, kv.Key)));
            foreach (KeyValuePair<int, int> kv in AllocSlots(SHADER_LIST.CA_LIGHTMAP_ENVIRONMENT, LmePSParams(mask)))
                sb.AppendLine("#define P_" + Enum.GetName(paramType, kv.Key) + " " + SlotExpr("rp_parameter_ps", kv.Value, ParamWidth(SHADER_LIST.CA_LIGHTMAP_ENVIRONMENT, kv.Key)));

            List<int> smps = LmeSamplerIds(mask);
            for (int i = 0; i < smps.Count; i++)
            {
                string name = Enum.GetName(samplerType, smps[i]);
                sb.AppendLine("#define SMP_" + name + " s" + i);
                sb.AppendLine("#define TEX_" + name + " t" + i);
            }

            bool Bit(int b) => ((mask >> b) & 1) != 0;
            string fields = "float4 t0 : TEXCOORD0; float4 t1 : TEXCOORD1;";
            int ti = 2;
            if (Bit(22) || Bit(24)) { fields += " float4 binorm : TEXCOORD" + ti++ + ";"; fields += " float4 tang : TEXCOORD" + ti++ + ";"; }
            if (Bit(26) || Bit(35)) fields += " float4 view : TEXCOORD" + ti++ + ";";
            if (Bit(38)) fields += " float4 vao : TEXCOORD" + ti++ + ";";
            if (Bit(0)) fields += " float4 vcol : COLOR0;";
            sb.AppendLine("#define LME_PS_FIELDS " + fields + " float4 pos : SV_Position;");
            sb.AppendLine("#define LME_VS_FIELDS " + fields + " float4 pos : SV_Position;");
            return sb.ToString();
        }

        /// CA_CHARACTER vertex parameters.
        private static IEnumerable<int> CharVSParams(long m)
        {
            bool B(int b) => ((m >> b) & 1) != 0;
            if (B(16)) yield return 8;   // CUSTOM_CHARACTER_TINT_PRIORITY
            if (B(21)) yield return 13;  // ALPHABLEND_NOISE_POWER
            if (B(29)) yield return 20;  // NORMAL_MAP_STRENGTH
        }

        /// CA_CHARACTER pixel parameters, measured across all 427 shipped masks.
        private static IEnumerable<int> CharPSParams(long m)
        {
            bool B(int b) => ((m >> b) & 1) != 0;
            yield return 4;                                                    // FRESNEL_INTENSITY
            if (B(16)) yield return 7;                                         // IS_CUSTOM_CHARACTER_DECAL
            if (B(18)) { yield return 9; yield return 10; yield return 11; }   // DIRT
            if (B(21)) yield return 12;                                        // ALPHABLEND_NOISE_UV_MULT
            if (B(23)) yield return 14;                                        // SEPARATE_ALPHA_UV_MULT
            yield return 15; yield return 16;                                  // DIFFUSE_UV_MULT, DIFFUSE_TINT
            if (B(27)) { yield return 17; yield return 18; }                   // SECONDARY_DIFFUSE
            if (B(29)) yield return 19;                                        // NORMAL_UV_MULT
            if (B(31)) { yield return 21; yield return 22; }                   // SECONDARY_NORMAL
            if (B(33)) { yield return 23; yield return 24; yield return 25; }  // SPECULAR
            if (B(35)) { yield return 26; yield return 27; yield return 28; }  // SECONDARY_SPECULAR
            if (B(38)) { yield return 29; yield return 30; yield return 31; }  // GLASS
            if (B(39) || B(40)) yield return 32;                                        // DIFFUSE_ROUGHNESS_FACTOR
            if (B(42)) { yield return 33; yield return 34; }                   // ENVIRONMENT_MAPPING
            if (B(43)) { yield return 35; yield return 36; }                   // AMBIENT_OCCLUSION
            if (B(46)) { yield return 38; yield return 39; }                   // EMISSIVE
            if (B(47)) { yield return 40; yield return 41; }                   // DUST
            if (B(49)) yield return 42;                                        // SSR_AMOUNT
            if (B(52)) yield return 43;                                        // FUR_RIM_LIGHTING_FACTOR
            if (B(53)) { yield return 44; yield return 45; yield return 46; }  // PARALLAX
            if (B(54)) yield return 47;                                        // OPACITY_MODIFIER_VALUE
            if (B(61)) { yield return 48; yield return 49; }                   // ANGULAR_OPACITY_RAMP (bit 61, NOT 59/DECAL_SOLID - the two are perfectly correlated in shipped data)
        }

        /// CA_CHARACTER samplers, in SAMPLERS enum order.
        private static List<int> CharSamplerIds(long mask)
        {
            bool B(int b) => ((mask >> b) & 1) != 0;
            List<int> ids = new List<int>();
            if (B(18)) ids.Add(0);
            if (B(21)) ids.Add(1);
            if (B(23)) ids.Add(2);
            ids.Add(3);                                                        // DIFFUSE_MAP
            if (B(27)) ids.Add(4);
            if (B(29)) ids.Add(5);
            if (B(31)) ids.Add(6);
            if (B(33)) ids.Add(7);
            if (B(35)) ids.Add(8);
            if (B(42)) ids.Add(9);
            if (B(43)) ids.Add(10);
            if (B(47)) ids.Add(11);
            if (B(50)) ids.Add(12);
            if (B(53)) ids.Add(13);
            return ids;
        }

        /* The master names CA_CHARACTER samplers in short form, indexed by SAMPLERS enum id */
        private static readonly string[] _charSamplerNames =
        {
            "DIRTMAP", "NOISEMAP", "SEPALPHA", "DIFFUSE", "SECDIFFUSE", "NORMAL", "SECNORMAL",
            "SPECULAR", "SECSPECULAR", "ENVMAP", "AOMAP", "DUSTMAP", "IRRCUBE", "PARALLAXMAP"
        };

        /// CA_CHARACTER constant slots, samplers and interpolants: normal, uv, then the
        /// collision-skinning prelight, the custom-character tint, the tangent frame, the view
        /// vector, and the vertex colour last. Verified 427/427 against the shipped signatures.
        private static string CharacterMacros(long mask)
        {
            Type paramType = typeof(SHADER_LIST).Assembly.GetType("CATHODE.ShaderTypes." + SHADER_LIST.CA_CHARACTER + "+PARAMETERS");
            StringBuilder sb = new StringBuilder();
            Dictionary<int, int> ps = AllocSlots(SHADER_LIST.CA_CHARACTER, CharPSParams(mask));
            foreach (KeyValuePair<int, int> kv in AllocSlots(SHADER_LIST.CA_CHARACTER, CharVSParams(mask)))
                sb.AppendLine("#define PV_" + Enum.GetName(paramType, kv.Key) + " " + SlotExpr("rp_parameter_vs", kv.Value, ParamWidth(SHADER_LIST.CA_CHARACTER, kv.Key)));
            foreach (KeyValuePair<int, int> kv in ps)
                sb.AppendLine("#define P_" + Enum.GetName(paramType, kv.Key) + " " + SlotExpr("rp_parameter_ps", kv.Value, ParamWidth(SHADER_LIST.CA_CHARACTER, kv.Key)));
            //the constant-buffer ROW each PS parameter lands in: fxc only fuses two uv muls into
            //one four-lane op when both scalars swizzle out of a single row, so the master has to
            //be able to ask whether a pair shares one. Rows are offset by one so an absent
            //parameter (which the preprocessor reads as 0) never compares equal to a present one.
            foreach (KeyValuePair<int, int> kv in ps)
                sb.AppendLine("#define PROW_" + Enum.GetName(paramType, kv.Key) + " " + (kv.Value / 4 + 1));

            List<int> smps = CharSamplerIds(mask);
            for (int i = 0; i < smps.Count; i++)
            {
                sb.AppendLine("#define SMP_" + _charSamplerNames[smps[i]] + " s" + i);
                sb.AppendLine("#define TEX_" + _charSamplerNames[smps[i]] + " t" + i);
            }

            bool B(int b) => ((mask >> b) & 1) != 0;
            List<string> fields = new List<string>();
            int ti = 0;
            if (B(1))
            {
                //depth-only: one interpolant carrying clip z/w (collision skinning still ships its
                //extra one, unread)
                fields.Add("float4 t0 : TEXCOORD" + ti++ + ";");
                if (B(7)) fields.Add("float4 t1 : TEXCOORD" + ti++ + ";");
            }
            else
            {
                fields.Add("float4 t0 : TEXCOORD" + ti++ + ";");
                fields.Add("float4 t1 : TEXCOORD" + ti++ + ";");
                if (B(7)) fields.Add("float4 t2 : TEXCOORD" + ti++ + ";");
                if (B(16)) fields.Add("float4 cc : TEXCOORD" + ti++ + ";");
                if (B(29) || B(31)) { fields.Add("float4 tanA : TEXCOORD" + ti++ + ";"); fields.Add("float4 tanB : TEXCOORD" + ti++ + ";"); }
                fields.Add("float4 view : TEXCOORD" + ti++ + ";");
            }
            if (B(5)) fields.Add("float4 vcol : COLOR0;");
            sb.AppendLine("#define CHR_PS_FIELDS " + string.Join(" ", fields.ToArray()));
            sb.AppendLine("#define CHR_VS_FIELDS CHR_PS_FIELDS float4 pos : SV_Position;");
            return sb.ToString();
        }
        #endregion

        #region CONSTANT_TABLES
        /// <summary>
        /// Scalar slot width of a parameter, from the family's GetParameterType.
        /// </summary>
        public static int ParamWidth(SHADER_LIST family, int parameterId)
        {
            Assembly asm = typeof(SHADER_LIST).Assembly;
            Type familyClass = asm.GetType("CATHODE.ShaderTypes." + family);
            Type parametersType = asm.GetType("CATHODE.ShaderTypes." + family + "+PARAMETERS");
            try
            {
                object type = familyClass.GetMethod("GetParameterType").Invoke(null, new object[] { Enum.ToObject(parametersType, parameterId) });
                switch (type.ToString())
                {
                    case "Float": case "Half": case "Int": return 1;
                    case "Float2": case "Half2": return 2;
                    case "Float3": case "Half3": return 3;
                    case "Float4": case "Half4": return 4;
                    default: return 1;
                }
            }
            catch { return 1; }
        }

        /// <summary>
        /// The universal ufx constant allocator (verified 100% against 4,248 retail tables across
        /// 7 families): process included parameters in enum order; each takes the lowest free slot
        /// run of its width that does not straddle a 4-slot register boundary.
        /// </summary>
        public static Dictionary<int, int> AllocSlots(SHADER_LIST family, IEnumerable<int> included)
        {
            Dictionary<int, int> result = new Dictionary<int, int>();
            bool[] free = new bool[256];
            for (int i = 0; i < 256; i++) free[i] = true;
            foreach (int parameterId in included.OrderBy(x => x))
            {
                int width = ParamWidth(family, parameterId);
                int slot = -1;
                for (int s = 0; s < 250 && slot < 0; s++)
                {
                    if (s / 4 != (s + width - 1) / 4) continue;
                    bool fits = true;
                    for (int k = 0; k < width; k++) if (!free[s + k]) { fits = false; break; }
                    if (fits) slot = s;
                }
                for (int k = 0; k < width; k++) free[slot + k] = false;
                result[parameterId] = slot;
            }
            return result;
        }

        /// CA_PLANET vertex parameters.
        private static IEnumerable<int> PlanetVSParams(long m)
        {
            bool B(int b) => ((m >> b) & 1) != 0;
            if (B(6)) yield return 9;    // SCROLL_SPEED
            if (B(7)) yield return 10;   // DETAIL_SCROLL_SPEED
            if (B(10)) yield return 21;  // LIGHT_WRAP_ANGLE
        }

        /// CA_PLANET pixel parameters, verified across all 15 shipped masks.
        private static IEnumerable<int> PlanetPSParams(long m)
        {
            bool B(int b) => ((m >> b) & 1) != 0;
            yield return 0; yield return 1;                                     // RIM_TRANSPARENCY, OVERBRIGHT_SCALAR
            if (B(2)) yield return 2;                                           // DETAIL_TEX_SCALAR
            if (B(3)) { yield return 3; yield return 4; }                        // ATMOSPHERE_NORMAL
            if (B(4)) { yield return 5; yield return 6; yield return 7; }        // TERRAIN
            if (B(5)) yield return 8;                                           // TERRAIN_NORMAL_MAP_STRENGTH
            if (B(8)) { yield return 11; yield return 12; yield return 13; yield return 14; }  // FLOW
            if (B(9)) for (int p = 15; p <= 20; p++) yield return p;             // ATMOSPHERE_RIM
            if (B(11)) yield return 22;                                         // PENUMBRA_FALLOFF_POWER
            if (B(12)) yield return 23;                                         // SHADOW_HUE
            if (B(13)) yield return 24;                                         // GLOBAL_TINT_VALUE
        }

        /// CA_PLANET samplers, in SAMPLERS enum order. The engine remap table is empty on every mask.
        private static List<int> PlanetSamplerIds(long mask)
        {
            bool B(int b) => ((mask >> b) & 1) != 0;
            List<int> ids = new List<int>() { 0 };                              // ATMOSPHERE_MAP always
            if (B(2)) ids.Add(1);
            if (B(3)) ids.Add(2);
            if (B(4)) ids.Add(3);
            if (B(5)) ids.Add(4);
            if (B(8)) ids.Add(5);
            return ids;
        }

        /// CA_PLANET constant slots, samplers and interpolants.
        ///
        /// The interpolant layout is the expensive part of this family. Vector slots are fixed -
        /// t0.xy base uv, t1.xyz normal, t2.xyz view, t3.xyz bitangent, t4.xyz tangent, t5.xyz light
        /// direction - and every parameter carrying a per-instance multiplier claims one scalar
        /// LANE, allocated in feature-bit order over the components those vectors leave free.
        /// SCROLLING_UV and DETAIL_SCROLLING_UV each claim half of t6 for an extra uv set, which
        /// pushes the lanes past it. SHADOW_COLOURISATION looks like it should take a lane and does
        /// not. The texcoord count follows from where the last lane lands (6..9, never declared).
        ///
        /// Lane VALUES come from a fixed layout in the instance constants, except the vertex-colour
        /// lane (COLOR.w) and the light wrap, which the vertex shader converts from an angle to its
        /// cosine so the pixel shader can compare it against N.L directly.
        private static string PlanetMacros(long mask)
        {
            Type paramType = typeof(SHADER_LIST).Assembly.GetType("CATHODE.ShaderTypes." + SHADER_LIST.CA_PLANET + "+PARAMETERS");
            Type smpType = typeof(SHADER_LIST).Assembly.GetType("CATHODE.ShaderTypes." + SHADER_LIST.CA_PLANET + "+SAMPLERS");
            StringBuilder sb = new StringBuilder();
            bool B(int b) => ((mask >> b) & 1) != 0;
            foreach (KeyValuePair<int, int> kv in AllocSlots(SHADER_LIST.CA_PLANET, PlanetVSParams(mask)))
                sb.AppendLine("#define PV_" + Enum.GetName(paramType, kv.Key) + " " + SlotExpr("rp_parameter_vs", kv.Value, ParamWidth(SHADER_LIST.CA_PLANET, kv.Key)));
            foreach (KeyValuePair<int, int> kv in AllocSlots(SHADER_LIST.CA_PLANET, PlanetPSParams(mask)))
                sb.AppendLine("#define P_" + Enum.GetName(paramType, kv.Key) + " " + SlotExpr("rp_parameter_ps", kv.Value, ParamWidth(SHADER_LIST.CA_PLANET, kv.Key)));

            List<string> lanes = new List<string>();
            if (B(0)) lanes.Add("VCOL");
            if (B(1)) lanes.Add("OVERBRIGHT");
            if (B(2)) lanes.Add("DETAIL_TEX_SCALE");
            if (B(3)) { lanes.Add("ANM_TEX_SCALE"); lanes.Add("ANM_STRENGTH"); }
            if (B(4)) lanes.Add("TERRAIN_TEX_SCALE");
            if (B(5)) lanes.Add("TERRAIN_NORMAL_STRENGTH");
            if (B(8)) { lanes.Add("CYCLE_TIME"); lanes.Add("FLOW_SPEED"); lanes.Add("FLOW_TEX_SCALE"); lanes.Add("FLOW_WARP_STRENGTH"); }
            if (B(9)) { lanes.Add("RIM_TRANS"); lanes.Add("RIM_FRESNEL"); }
            if (B(10)) lanes.Add("LIGHT_WRAP");
            if (B(11)) lanes.Add("PENUMBRA");

            List<string> slotName = new List<string>() { "t0.z", "t0.w", "t1.w", "t2.w", "t3.w", "t4.w", "t5.w" };
            if (!B(6)) { slotName.Add("t6.x"); slotName.Add("t6.y"); }
            if (!B(7)) { slotName.Add("t6.z"); slotName.Add("t6.w"); }
            foreach (string r in new string[] { "t7", "t8" })
                foreach (string c in new string[] { ".x", ".y", ".z", ".w" })
                    slotName.Add(r + c);

            int hi = 5;
            if (B(6) || B(7)) hi = 6;
            for (int i = 0; i < lanes.Count && i < slotName.Count; i++)
            {
                sb.AppendLine("#define M_" + lanes[i] + " " + slotName[i]);
                int reg = slotName[i][1] - '0';
                if (reg > hi) hi = reg;
            }
            sb.AppendLine("#define M_NTEX " + (hi + 1));

            Dictionary<string, string> laneVal = new Dictionary<string, string>()
            {
                { "VCOL", "v.col.w" },
                { "OVERBRIGHT", "RInstConstants[1].w" },
                { "DETAIL_TEX_SCALE", "RInstConstants[4].z" },
                { "ANM_TEX_SCALE", "RInstConstants[4].w" },
                { "ANM_STRENGTH", "RInstConstants[5].y" },
                { "TERRAIN_TEX_SCALE", "RInstConstants[5].x" },
                { "TERRAIN_NORMAL_STRENGTH", "RInstConstants[5].z" },
                { "CYCLE_TIME", "RInstConstants[3].x" },
                { "FLOW_SPEED", "RInstConstants[3].y" },
                { "FLOW_TEX_SCALE", "RInstConstants[3].z" },
                { "FLOW_WARP_STRENGTH", "RInstConstants[3].w" },
                { "RIM_TRANS", "RInstConstants[2].z" },
                { "RIM_FRESNEL", "RInstConstants[2].w" },
                { "LIGHT_WRAP", "cos(PV_LIGHT_WRAP_ANGLE * RInstConstants[2].x + 1.57079633)" },
                { "PENUMBRA", "RInstConstants[2].y" },
            };
            Dictionary<string, string> slotVal = new Dictionary<string, string>();
            for (int i = 0; i < lanes.Count && i < slotName.Count; i++)
                slotVal[slotName[i]] = laneVal[lanes[i]];
            if (B(6)) { slotVal["t6.x"] = "v.uv.x * 16.0"; slotVal["t6.y"] = "v.uv.y * 16.0"; }
            if (B(7))
            {
                slotVal["t6.z"] = "v.uv.x * 16.0 + (Time.x * PV_DETAIL_SCROLL_SPEED) * RInstConstants[4].x";
                slotVal["t6.w"] = "v.uv.y * 16.0";
            }
            foreach (string r in new string[] { "t0", "t1", "t2", "t3", "t4", "t5", "t6", "t7", "t8" })
                foreach (string c in new string[] { "x", "y", "z", "w" })
                {
                    if (r == "t0" && (c == "x" || c == "y")) continue;
                    if (r != "t0" && r != "t6" && r != "t7" && r != "t8" && c != "w") continue;
                    string key = r + "." + c;
                    sb.AppendLine("#define LANE_" + r.ToUpper() + c.ToUpper() + " " + (slotVal.ContainsKey(key) ? slotVal[key] : "0.0"));
                }

            List<int> smps = PlanetSamplerIds(mask);
            for (int i = 0; i < smps.Count; i++)
            {
                string n = Enum.GetName(smpType, smps[i]);
                sb.AppendLine("#define SMP_" + n + " s" + i);
                sb.AppendLine("#define TEX_" + n + " t" + i);
            }
            return sb.ToString();
        }


        /// CA_EFFECT_OVERLAY vertex parameters: the fade time, on every mask.
        private static IEnumerable<int> EovVSParams(long m)
        {
            yield return 9;                                                     // FADE_TOTALTIME
        }

        /// CA_EFFECT_OVERLAY pixel parameters. Only ENVMAP moves anything, and it moves nothing in
        /// the shader body - the two ENVMAP masks compile to code byte-identical to their
        /// counterparts, and differ only by the extra parameter and sampler slot.
        private static IEnumerable<int> EovPSParams(long m)
        {
            for (int p = 0; p <= 8; p++) yield return p;                        // everything but FADE_TOTALTIME
            for (int p = 10; p <= 15; p++) yield return p;
            if (((m >> (int)CA_EFFECT_OVERLAY.FEATURES.ENVMAP) & 1) != 0) yield return 16;
        }

        /// CA_EFFECT_OVERLAY samplers. The deferred depth and normal buffers the shader also reads
        /// sit at engine-fixed registers and are outside this table.
        private static List<int> EovSamplerIds(long mask)
        {
            List<int> ids = new List<int>() { 0, 1 };                           // TEXTURE_MAP, SPARKLE_MAP
            if (((mask >> (int)CA_EFFECT_OVERLAY.FEATURES.ENVMAP) & 1) != 0) ids.Add(2);
            return ids;
        }

        /// CA_EFFECT_OVERLAY constant slots and samplers. There is no interpolant packing to derive
        /// here: the register layout is fixed per shape feature and lives in the master itself.
        /// CA_SPACESUIT_VISOR: DRAW_PASS is the family's only engine parameter; every other one is a
        /// pixel parameter that ships exactly when the feature that reads it does.
        public static IEnumerable<int> VisorPsParams(long m)
        {
            List<int> ids = new List<int>();
            ids.Add(1);                                                 //GLASS_SPEC_POWER
            if (Bit(m, 3)) ids.Add(2);                                  //ENVIRONMENT_MAP_MULT
            if (Bit(m, 4)) { ids.Add(3); ids.Add(4); ids.Add(5); ids.Add(6); }
            if (Bit(m, 6)) { ids.Add(7); ids.Add(8); }                  //NORMAL_MAP_MULT/STRENGTH
            if (Bit(m, 7)) ids.Add(9);                                  //MASKING_MAP_MULT
            if (Bit(m, 8)) ids.Add(10);                                 //FACE_INTENSITY_MULT
            if (Bit(m, 9)) { ids.Add(11); ids.Add(12); }                //BREATH
            if (Bit(m, 10)) { ids.Add(13); ids.Add(14); }               //DIRT
            if (Bit(m, 12)) { ids.Add(15); ids.Add(16); }               //VISOR_DISTORTION
            return ids;
        }

        private static List<int> VisorSamplerIds(long m)
        {
            List<int> ids = new List<int>();
            if (Bit(m, 3)) ids.Add(0);                                  //ENVIRONMENT_MAP
            if (Bit(m, 6)) ids.Add(1);                                  //NORMAL_MAP
            if (Bit(m, 7)) ids.Add(2);                                  //MASKING_MAP
            if (Bit(m, 8)) ids.Add(3);                                  //FACE_MAP
            if (Bit(m, 9)) ids.Add(4);                                  //BREATH_GRADIENT_MAP
            if (Bit(m, 10)) { ids.Add(5); ids.Add(6); }                 //UNSCALED_DIRT_MAP, DIRT_MAP
            return ids;
        }

        private static string VisorMacros(long mask)
        {
            Type paramType = typeof(SHADER_LIST).Assembly.GetType("CATHODE.ShaderTypes." + SHADER_LIST.CA_SPACESUIT_VISOR + "+PARAMETERS");
            Type smpType = typeof(SHADER_LIST).Assembly.GetType("CATHODE.ShaderTypes." + SHADER_LIST.CA_SPACESUIT_VISOR + "+SAMPLERS");
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<int, int> kv in AllocSlots(SHADER_LIST.CA_SPACESUIT_VISOR, VisorPsParams(mask)))
                sb.AppendLine("#define P_" + Enum.GetName(paramType, kv.Key) + " " + SlotExpr("rp_parameter_ps", kv.Value, ParamWidth(SHADER_LIST.CA_SPACESUIT_VISOR, kv.Key)));
            List<int> smps = VisorSamplerIds(mask);
            for (int i = 0; i < smps.Count; i++)
            {
                string n = Enum.GetName(smpType, smps[i]);
                sb.AppendLine("#define SMP_" + n + " s" + i);
                sb.AppendLine("#define TEX_" + n + " t" + i);
            }
            return sb.ToString();
        }

        /// CA_NONINTERACTIVE_WATER: the scroll speed and scale go to the vertex stage, everything
        /// else to the pixel stage. The depth-fog ramp and the fresnel range always ship.
        public static IEnumerable<int> WaterVsParams(long m)
        {
            List<int> ids = new List<int> { 9, 10 };                    //SPEED, SCALE
            if (Bit(m, 1)) { ids.Add(12); ids.Add(13); }                //SECONDARY_SPEED/SCALE
            return ids;
        }

        public static IEnumerable<int> WaterPsParams(long m)
        {
            List<int> ids = new List<int>();
            for (int p = 0; p <= 9; p++) ids.Add(p);                    //SHININESS, depth fog, SPEED
            ids.Add(11);                                                //NORMAL_MAP_STRENGTH
            if (Bit(m, 1)) { ids.Add(12); ids.Add(14); }                //SECONDARY_*
            if (Bit(m, 3)) { ids.Add(15); ids.Add(16); ids.Add(17); ids.Add(18); }
            ids.Add(19); ids.Add(20); ids.Add(21);                      //FRESNEL_POWER, MIN, MAX
            if (Bit(m, 4) || Bit(m, 5)) ids.Add(22);                    //ENVIRONMENT_MAP_MULT
            if (Bit(m, 5)) ids.Add(23);                                 //ENVMAP_SIZE
            if (Bit(m, 6)) { ids.Add(24); ids.Add(25); ids.Add(26); }
            if (Bit(m, 7)) ids.Add(27);                                 //REFLECTION_PERTURBATION_STRENGTH
            if (Bit(m, 8)) ids.Add(29);                                 //ALPHALIGHT_MULT
            return ids;
        }

        private static List<int> WaterSamplerIds(long m)
        {
            List<int> ids = new List<int> { 0 };                        //NORMAL_MAP
            if (Bit(m, 1)) ids.Add(1);                                  //SECONDARY_NORMAL_MAP
            if (Bit(m, 2)) ids.Add(2);                                  //ALPHA_MASK
            if (Bit(m, 3)) ids.Add(3);                                  //FLOW_MAP
            if (Bit(m, 4) || Bit(m, 5)) ids.Add(4);                     //ENVIRONMENT_MAP
            return ids;
        }

        private static string WaterMacros(long mask)
        {
            Type paramType = typeof(SHADER_LIST).Assembly.GetType("CATHODE.ShaderTypes." + SHADER_LIST.CA_NONINTERACTIVE_WATER + "+PARAMETERS");
            Type smpType = typeof(SHADER_LIST).Assembly.GetType("CATHODE.ShaderTypes." + SHADER_LIST.CA_NONINTERACTIVE_WATER + "+SAMPLERS");
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<int, int> kv in AllocSlots(SHADER_LIST.CA_NONINTERACTIVE_WATER, WaterVsParams(mask)))
                sb.AppendLine("#define PV_" + Enum.GetName(paramType, kv.Key) + " " + SlotExpr("rp_parameter_vs", kv.Value, ParamWidth(SHADER_LIST.CA_NONINTERACTIVE_WATER, kv.Key)));
            foreach (KeyValuePair<int, int> kv in AllocSlots(SHADER_LIST.CA_NONINTERACTIVE_WATER, WaterPsParams(mask)))
                sb.AppendLine("#define P_" + Enum.GetName(paramType, kv.Key) + " " + SlotExpr("rp_parameter_ps", kv.Value, ParamWidth(SHADER_LIST.CA_NONINTERACTIVE_WATER, kv.Key)));
            List<int> smps = WaterSamplerIds(mask);
            for (int i = 0; i < smps.Count; i++)
            {
                string n = Enum.GetName(smpType, smps[i]);
                sb.AppendLine("#define SMP_" + n + " s" + i);
                sb.AppendLine("#define TEX_" + n + " t" + i);
            }
            return sb.ToString();
        }

        /// CA_LIQUID_ENVIRONMENT: the flow constants always ship; only the environment multiplier is
        /// conditional. The master hard-codes the slots, so this is only needed for the remap table.
        public static IEnumerable<int> LiquidPsParams(long m)
        {
            List<int> ids = new List<int>();
            for (int p = 0; p <= 14; p++) ids.Add(p);
            if (Bit(m, 2)) ids.Add(15);                                 //ENVIRONMENT_MAP_MULT
            return ids;
        }

        private static List<int> LiquidSamplerIds(long m)
        {
            List<int> ids = new List<int> { 0, 1, 2, 3, 4 };            //the five flow maps always ship
            if (Bit(m, 1)) { ids.Add(5); ids.Add(6); }                  //NORMAL_MAP, NORMAL_ALPHA_MAP
            if (Bit(m, 2)) ids.Add(7);                                  //ENVIRONMENT_MAP
            return ids;
        }

        private static bool Bit(long mask, int b)
        {
            return ((mask >> b) & 1) != 0;
        }

        private static string EovMacros(long mask)
        {
            Type paramType = typeof(SHADER_LIST).Assembly.GetType("CATHODE.ShaderTypes." + SHADER_LIST.CA_EFFECT_OVERLAY + "+PARAMETERS");
            Type smpType = typeof(SHADER_LIST).Assembly.GetType("CATHODE.ShaderTypes." + SHADER_LIST.CA_EFFECT_OVERLAY + "+SAMPLERS");
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<int, int> kv in AllocSlots(SHADER_LIST.CA_EFFECT_OVERLAY, EovVSParams(mask)))
                sb.AppendLine("#define PV_" + Enum.GetName(paramType, kv.Key) + " " + SlotExpr("rp_parameter_vs", kv.Value, ParamWidth(SHADER_LIST.CA_EFFECT_OVERLAY, kv.Key)));
            foreach (KeyValuePair<int, int> kv in AllocSlots(SHADER_LIST.CA_EFFECT_OVERLAY, EovPSParams(mask)))
                sb.AppendLine("#define P_" + Enum.GetName(paramType, kv.Key) + " " + SlotExpr("rp_parameter_ps", kv.Value, ParamWidth(SHADER_LIST.CA_EFFECT_OVERLAY, kv.Key)));
            List<int> smps = EovSamplerIds(mask);
            for (int i = 0; i < smps.Count; i++)
            {
                string n = Enum.GetName(smpType, smps[i]);
                sb.AppendLine("#define SMP_" + n + " s" + i);
                sb.AppendLine("#define TEX_" + n + " t" + i);
            }
            return sb.ToString();
        }


        /// CA_TERRAIN pixel parameters. Ids 0 and 1 (the priority levels) are never included, and
        /// the vertex table is empty on every mask.
        private static IEnumerable<int> TerrPSParams(long m)
        {
            bool B(int b) => ((m >> b) & 1) != 0;
            yield return 2; yield return 3; yield return 4;                      // FRESNEL, DIFFUSE_UV, DIFFUSE_TINT
            if (B(2)) { yield return 5; yield return 6; }                        // SECONDARY_DIFFUSE
            if (B(3)) yield return 7;                                            // NORMAL_UV
            if (B(4)) yield return 8;                                            // SECONDARY_NORMAL_UV
            if (B(5)) { yield return 9; yield return 10; yield return 11; }      // SPECULAR
            if (B(6)) { yield return 12; yield return 13; yield return 14; }     // SECONDARY_SPECULAR
            if (B(7)) { yield return 15; yield return 16; yield return 17; }     // PARALLAX
            if (B(8)) yield return 18;                                           // ALPHABLEND_NOISE_UV
            if (B(9)) { yield return 19; yield return 20; }                      // ENVIRONMENT
            if (B(10)) yield return 21;                                          // DIFFUSE_AMBIENT
            if (B(11)) { yield return 22; yield return 23; }                     // LIGHTMAP
        }

        /// CA_TERRAIN samplers, in SAMPLERS enum order. The fresnel lookup the shader also reads is
        /// a 3D engine texture at a fixed register and is outside this table.
        private static List<int> TerrSamplerIds(long mask)
        {
            bool B(int b) => ((mask >> b) & 1) != 0;
            List<int> ids = new List<int>() { 0 };                               // DIFFUSE_MAP
            if (B(2)) ids.Add(1);
            if (B(3)) ids.Add(2);
            if (B(4)) ids.Add(3);
            if (B(5)) ids.Add(4);
            if (B(6)) ids.Add(5);
            if (B(7)) ids.Add(6);
            if (B(8)) ids.Add(7);
            if (B(9)) ids.Add(8);
            if (B(11)) ids.Add(9);
            if (B(12)) ids.Add(10);
            return ids;
        }

        /// CA_TERRAIN constant slots and samplers.
        private static string TerrMacros(long mask)
        {
            Type paramType = typeof(SHADER_LIST).Assembly.GetType("CATHODE.ShaderTypes." + SHADER_LIST.CA_TERRAIN + "+PARAMETERS");
            Type smpType = typeof(SHADER_LIST).Assembly.GetType("CATHODE.ShaderTypes." + SHADER_LIST.CA_TERRAIN + "+SAMPLERS");
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<int, int> kv in AllocSlots(SHADER_LIST.CA_TERRAIN, TerrPSParams(mask)))
                sb.AppendLine("#define P_" + Enum.GetName(paramType, kv.Key) + " " + SlotExpr("rp_parameter_ps", kv.Value, ParamWidth(SHADER_LIST.CA_TERRAIN, kv.Key)));
            List<int> smps = TerrSamplerIds(mask);
            for (int i = 0; i < smps.Count; i++)
            {
                string n = Enum.GetName(smpType, smps[i]);
                sb.AppendLine("#define SMP_" + n + " s" + i);
                sb.AppendLine("#define TEX_" + n + " t" + i);
            }
            return sb.ToString();
        }


        /// CA_REFRACTION vertex parameters.
        private static IEnumerable<int> RefrVSParams(long m)
        {
            bool B(int b) => ((m >> b) & 1) != 0;
            if (B(1)) { yield return 4; yield return 5; }                        // SECONDARY_SPEED/SCALE
            if (B(5)) yield return 13;                                           // FADE_TOTALTIME
        }

        /// CA_REFRACTION pixel parameters. SECONDARY_SCALE (5) is vertex-only and SECONDARY_SPEED
        /// (4) appears in both stages.
        private static IEnumerable<int> RefrPSParams(long m)
        {
            bool B(int b) => ((m >> b) & 1) != 0;
            yield return 0; yield return 1; yield return 2; yield return 3;
            if (B(1)) { yield return 4; yield return 6; }                        // SECONDARY_NORMAL
            if (B(3)) { yield return 7; yield return 8; }                        // DISTORTION_OCCLUSION
            if (B(4)) { yield return 9; yield return 10; yield return 11; yield return 12; }  // FLOW
            if (B(6)) for (int p = 14; p <= 19; p++) yield return p;              // ALPHATHRESHOLD
            if (B(8)) { yield return 20; yield return 21; yield return 22; }      // COLOUR_LERP (unshipped)
        }

        /// CA_REFRACTION samplers. The deferred depth buffer DISTORTION_OCCLUSION reads is at a
        /// fixed engine register and is outside this table. The engine table is empty on every mask.
        private static List<int> RefrSamplerIds(long mask)
        {
            bool B(int b) => ((mask >> b) & 1) != 0;
            List<int> ids = new List<int>() { 0 };                                // NORMAL_MAP
            if (B(1)) ids.Add(1);
            if (B(2)) ids.Add(2);
            if (B(4)) ids.Add(3);
            if (B(6)) ids.Add(4);
            if (B(9)) ids.Add(5);
            return ids;
        }

        /// CA_REFRACTION constant slots and samplers.
        private static string RefrMacros(long mask)
        {
            Type paramType = typeof(SHADER_LIST).Assembly.GetType("CATHODE.ShaderTypes." + SHADER_LIST.CA_REFRACTION + "+PARAMETERS");
            Type smpType = typeof(SHADER_LIST).Assembly.GetType("CATHODE.ShaderTypes." + SHADER_LIST.CA_REFRACTION + "+SAMPLERS");
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<int, int> kv in AllocSlots(SHADER_LIST.CA_REFRACTION, RefrVSParams(mask)))
                sb.AppendLine("#define PV_" + Enum.GetName(paramType, kv.Key) + " " + SlotExpr("rp_parameter_vs", kv.Value, ParamWidth(SHADER_LIST.CA_REFRACTION, kv.Key)));
            foreach (KeyValuePair<int, int> kv in AllocSlots(SHADER_LIST.CA_REFRACTION, RefrPSParams(mask)))
                sb.AppendLine("#define P_" + Enum.GetName(paramType, kv.Key) + " " + SlotExpr("rp_parameter_ps", kv.Value, ParamWidth(SHADER_LIST.CA_REFRACTION, kv.Key)));
            List<int> smps = RefrSamplerIds(mask);
            for (int i = 0; i < smps.Count; i++)
            {
                string n = Enum.GetName(smpType, smps[i]);
                sb.AppendLine("#define SMP_" + n + " s" + i);
                sb.AppendLine("#define TEX_" + n + " t" + i);
            }
            return sb.ToString();
        }

        /// CA_HAIR pixel parameters. DRAW_PASS never participates, and ENVIRONMENT_MAP_MULT follows
        /// ENVIRONMENT_MAPPING, which no shipped mask sets. There are no vertex parameters at all -
        /// the vertex stage reads nothing but engine constants.
        private static IEnumerable<int> HairPSParams(long m)
        {
            bool B(int b) => ((m >> b) & 1) != 0;
            yield return 1; yield return 2; yield return 3;                       // DIFFUSE UV/TINT/CONTRAST
            if (B(7)) yield return 4;                                             // ENVIRONMENT_MAP_MULT
            if (B(10)) { yield return 5; yield return 6; yield return 7; }        // SPECULAR
            if (B(11)) { yield return 8; yield return 9; }                        // NORMAL
        }

        /// CA_HAIR samplers. FLOW_MAP and DIFFUSE_MAP are on every mask - the depth-only pre-pass
        /// still binds the flow map even though its compiled shader never reads it.
        private static List<int> HairSamplerIds(long mask)
        {
            bool B(int b) => ((mask >> b) & 1) != 0;
            List<int> ids = new List<int>() { 0, 1 };                             // FLOW_MAP, DIFFUSE_MAP
            if (B(7)) ids.Add(2);                                                 // ENVIRONMENT_MAP
            if (B(9)) ids.Add(3);                                                 // IRRADIANCE_CUBE_MAP
            if (B(10)) ids.Add(4);                                                // SPECULAR_MAP
            if (B(11)) ids.Add(5);                                                // NORMAL_MAP
            return ids;
        }

        /// CA_HAIR constant slots and samplers.
        private static string HairMacros(long mask)
        {
            Type paramType = typeof(SHADER_LIST).Assembly.GetType("CATHODE.ShaderTypes." + SHADER_LIST.CA_HAIR + "+PARAMETERS");
            Type smpType = typeof(SHADER_LIST).Assembly.GetType("CATHODE.ShaderTypes." + SHADER_LIST.CA_HAIR + "+SAMPLERS");
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<int, int> kv in AllocSlots(SHADER_LIST.CA_HAIR, HairPSParams(mask)))
                sb.AppendLine("#define P_" + Enum.GetName(paramType, kv.Key) + " " + SlotExpr("rp_parameter_ps", kv.Value, ParamWidth(SHADER_LIST.CA_HAIR, kv.Key)));
            List<int> smps = HairSamplerIds(mask);
            for (int i = 0; i < smps.Count; i++)
            {
                string n = Enum.GetName(smpType, smps[i]);
                sb.AppendLine("#define SMP_" + n + " s" + i);
                sb.AppendLine("#define TEX_" + n + " t" + i);
            }
            return sb.ToString();
        }

        /// CA_SIMPLEWATER has NO parameter gating at all: every shipped mask carries the identical
        /// vertex and pixel table and all four samplers, whatever features are set.  SCALE and
        /// SECONDARY_SCALE are vertex-only; SPEED and SECONDARY_SPEED appear in both stages.
        private static IEnumerable<int> SwatVSParams(long m)
        {
            yield return 10; yield return 11; yield return 13; yield return 14;
        }

        private static IEnumerable<int> SwatPSParams(long m)
        {
            for (int p = 0; p <= 27; p++)
                if (p != 11 && p != 14) yield return p;
        }

        private static List<int> SwatSamplerIds(long mask)
        {
            return new List<int>() { 0, 1, 2, 3 };
        }

        /// CA_SIMPLEWATER constant slots and samplers.
        private static string SwatMacros(long mask)
        {
            Type paramType = typeof(SHADER_LIST).Assembly.GetType("CATHODE.ShaderTypes." + SHADER_LIST.CA_SIMPLEWATER + "+PARAMETERS");
            Type smpType = typeof(SHADER_LIST).Assembly.GetType("CATHODE.ShaderTypes." + SHADER_LIST.CA_SIMPLEWATER + "+SAMPLERS");
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<int, int> kv in AllocSlots(SHADER_LIST.CA_SIMPLEWATER, SwatVSParams(mask)))
                sb.AppendLine("#define PV_" + Enum.GetName(paramType, kv.Key) + " " + SlotExpr("rp_parameter_vs", kv.Value, ParamWidth(SHADER_LIST.CA_SIMPLEWATER, kv.Key)));
            foreach (KeyValuePair<int, int> kv in AllocSlots(SHADER_LIST.CA_SIMPLEWATER, SwatPSParams(mask)))
                sb.AppendLine("#define P_" + Enum.GetName(paramType, kv.Key) + " " + SlotExpr("rp_parameter_ps", kv.Value, ParamWidth(SHADER_LIST.CA_SIMPLEWATER, kv.Key)));
            List<int> smps = SwatSamplerIds(mask);
            for (int i = 0; i < smps.Count; i++)
            {
                string n = Enum.GetName(smpType, smps[i]);
                sb.AppendLine("#define SMP_" + n + " s" + i);
                sb.AppendLine("#define TEX_" + n + " t" + i);
            }
            return sb.ToString();
        }

        /// CA_EYE pixel parameters: everything but DRAW_PASS is unconditional and SSR_AMOUNT follows
        /// the SSR bit.  There are no vertex parameters, and all seven samplers and all seven engine
        /// parameters appear on every mask - including CONVOLVED_BRDF_MAX_HACK, whose sampler the
        /// compiled shader never reads.
        private static IEnumerable<int> EyePSParams(long m)
        {
            bool B(int b) => ((m >> b) & 1) != 0;
            for (int p = 1; p <= 12; p++) yield return p;
            if (B(6)) yield return 13;                                        // SSR_AMOUNT
        }

        private static List<int> EyeSamplerIds(long mask)
        {
            return new List<int>() { 0, 1, 2, 3, 4, 5, 6 };
        }

        /// CA_EYE constant slots and samplers.
        private static string EyeMacros(long mask)
        {
            Type paramType = typeof(SHADER_LIST).Assembly.GetType("CATHODE.ShaderTypes." + SHADER_LIST.CA_EYE + "+PARAMETERS");
            Type smpType = typeof(SHADER_LIST).Assembly.GetType("CATHODE.ShaderTypes." + SHADER_LIST.CA_EYE + "+SAMPLERS");
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<int, int> kv in AllocSlots(SHADER_LIST.CA_EYE, EyePSParams(mask)))
                sb.AppendLine("#define P_" + Enum.GetName(paramType, kv.Key) + " " + SlotExpr("rp_parameter_ps", kv.Value, ParamWidth(SHADER_LIST.CA_EYE, kv.Key)));
            List<int> smps = EyeSamplerIds(mask);
            for (int i = 0; i < smps.Count; i++)
            {
                string n = Enum.GetName(smpType, smps[i]);
                sb.AppendLine("#define SMP_" + n + " s" + i);
                sb.AppendLine("#define TEX_" + n + " t" + i);
            }
            return sb.ToString();
        }




        /// CA_FILTERS constant slots.  Like CA_POST_PROCESSING, the same full parameter set on
        /// every mask: FLARE_OFFSETS in the VS, the rest in the PS.  No ubershader samplers - the
        /// textures each filter reads sit at fixed engine registers.
        private static string FiltMacros(long mask)
        {
            Type paramType = typeof(SHADER_LIST).Assembly.GetType("CATHODE.ShaderTypes." + SHADER_LIST.CA_FILTERS + "+PARAMETERS");
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<int, int> kv in AllocSlots(SHADER_LIST.CA_FILTERS, FiltVsParams()))
                sb.AppendLine("#define PV_" + Enum.GetName(paramType, kv.Key) + " " + SlotExpr("rp_parameter_vs", kv.Value, ParamWidth(SHADER_LIST.CA_FILTERS, kv.Key)));
            foreach (KeyValuePair<int, int> kv in AllocSlots(SHADER_LIST.CA_FILTERS, FiltPsParams()))
                sb.AppendLine("#define P_" + Enum.GetName(paramType, kv.Key) + " " + SlotExpr("rp_parameter_ps", kv.Value, ParamWidth(SHADER_LIST.CA_FILTERS, kv.Key)));
            return sb.ToString();
        }

        private static List<int> FiltVsParams()
        {
            return new List<int>() { 0 };
        }

        private static List<int> FiltPsParams()
        {
            List<int> ids = new List<int>();
            for (int p = 1; p <= 7; p++) ids.Add(p);
            return ids;
        }
        /// CA_POST_PROCESSING constant slots.  Every shipped mask carries the SAME full parameter
        /// set - the feature bits gate code, not constants - so the tables never vary.  The family
        /// has no ubershader samplers either; its five textures sit at fixed engine registers.
        private static string PpMacros(long mask)
        {
            Type paramType = typeof(SHADER_LIST).Assembly.GetType("CATHODE.ShaderTypes." + SHADER_LIST.CA_POST_PROCESSING + "+PARAMETERS");
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<int, int> kv in AllocSlots(SHADER_LIST.CA_POST_PROCESSING, PpVsParams()))
                sb.AppendLine("#define PV_" + Enum.GetName(paramType, kv.Key) + " " + SlotExpr("rp_parameter_vs", kv.Value, ParamWidth(SHADER_LIST.CA_POST_PROCESSING, kv.Key)));
            foreach (KeyValuePair<int, int> kv in AllocSlots(SHADER_LIST.CA_POST_PROCESSING, PpPsParams()))
                sb.AppendLine("#define P_" + Enum.GetName(paramType, kv.Key) + " " + SlotExpr("rp_parameter_ps", kv.Value, ParamWidth(SHADER_LIST.CA_POST_PROCESSING, kv.Key)));
            return sb.ToString();
        }

        private static List<int> PpVsParams()
        {
            return new List<int>() { 11, 12, 14, 15, 16 };
        }

        private static List<int> PpPsParams()
        {
            List<int> ids = new List<int>();
            for (int p = 0; p <= 10; p++) ids.Add(p);
            ids.Add(12); ids.Add(13);
            for (int p = 17; p <= 22; p++) ids.Add(p);
            return ids;
        }
        /// CA_DEFERRED interpolant lanes.  The light's own shape payload takes a different amount
        /// of room in each of the three cases (spot, strip, point), and the feature items pack
        /// into whatever lanes are left: a vector takes the first run of three contiguous free
        /// lanes inside ONE register, a scalar the first free lane.  Everything the shape does not
        /// use has to be written as zero, or the VS output signature stops matching.
        private static string DeferMacros(long mask)
        {
            bool B(int b) => ((mask >> b) & 1) != 0;
            var sb = new StringBuilder();

            // free lanes after the shape's own payload, as absolute lane indices (reg*4 + comp)
            var free = new List<int>();
            if (B(1))                       // SPOT: t4.zw = cone params, t5 = axis + outer radius
                for (int i = 24; i < 64; i++) free.Add(i);
            else if (B(3))                  // STRIP: t4.z = half length, t5 = axis + radius
            { free.Add(19); for (int i = 24; i < 64; i++) free.Add(i); }
            else                            // POINT: nothing past the uv
                for (int i = 18; i < 64; i++) free.Add(i);

            var items = new List<(string name, int width)>();
            if (B(4)) { items.Add(("VIEWVEC", 3)); items.Add(("SPEC0", 1)); items.Add(("SPEC1", 1)); }
            if (B(6)) { items.Add(("SHAD0", 1)); items.Add(("SHAD1", 1)); }
            if (B(8)) items.Add(("SOFT0", 1));
            if (B(9)) items.Add(("BIAS0", 1));
            if (B(10)) { items.Add(("AREA0", 1)); items.Add(("AREA1", 1)); }
            if (B(11) && !B(4)) items.Add(("VIEWVEC", 3));

            var taken = new HashSet<int>();
            string Lane(int abs) => "t" + (abs / 4) + "." + "xyzw"[abs % 4];
            foreach (var it in items)
            {
                if (it.width == 3)
                {
                    int at = -1;
                    foreach (int f in free)
                    {
                        // three contiguous lanes in ONE register - not necessarily at .x
                        if (f % 4 > 1 || taken.Contains(f)) continue;
                        if (!taken.Contains(f + 1) && !taken.Contains(f + 2) &&
                            free.Contains(f + 1) && free.Contains(f + 2)) { at = f; break; }
                    }
                    for (int k = 0; k < 3; k++) taken.Add(at + k);
                    sb.AppendLine("#define L_" + it.name + " t" + (at / 4) + "." + "xyzw".Substring(at % 4, 3));
                    sb.AppendLine("#define HAS_" + it.name + " 1");
                }
                else
                {
                    int at = free.First(f => !taken.Contains(f));
                    taken.Add(at);
                    sb.AppendLine("#define L_" + it.name + " " + Lane(at));
                    sb.AppendLine("#define HAS_" + it.name + " 1");
                }
            }

            // how many TEXCOORD registers the struct needs, and which trailing lanes stay zero
            int last = taken.Count == 0 ? (B(1) ? 23 : B(3) ? 19 : 17) : taken.Max();
            int nreg = last / 4 + 1;
            if (nreg < (B(1) || B(3) ? 6 : 5)) nreg = B(1) || B(3) ? 6 : 5;
            var fields = new List<string>();
            for (int r = 0; r < nreg; r++) fields.Add("float4 t" + r + " : TEXCOORD" + r + ";");
            sb.AppendLine("#define DEF_VS_FIELDS " + string.Join(" ", fields) + " float4 pos : SV_Position;");
            sb.AppendLine("#define DEF_PS_FIELDS " + string.Join(" ", fields));

            var zeros = new List<string>();
            for (int r = 0; r < nreg; r++)
            {
                var comps = "";
                for (int c = 0; c < 4; c++)
                {
                    int abs = r * 4 + c;
                    if (free.Contains(abs) && !taken.Contains(abs)) comps += "xyzw"[c];
                }
                if (comps.Length == 1) zeros.Add("o.t" + r + "." + comps + " = 0.0;");
                else if (comps.Length > 1) zeros.Add("o.t" + r + "." + comps + " = (float" + comps.Length + ")0.0;");
            }
            sb.AppendLine("#define DEF_VS_ZEROS " + (zeros.Count == 0 ? "" : string.Join(" ", zeros)));
            return sb.ToString();
        }

        /// <summary>
        /// Synthesize the parameter remap tables for a mastered family's mask, in place on the
        /// shader. Only fills tables for families whose inclusion rules are known; leaves donor
        /// tables intact otherwise.
        /// </summary>
        public static void SynthesizeRemaps(SHADER_LIST family, long mask, Shaders.Shader shader)
        {
            if (family == SHADER_LIST.CA_RADIOSITY_INDIRECT || family == SHADER_LIST.CA_RADIOSITY_DIRECT_SPOT)
            {
                //Offline solver passes: no parameters and no ubershader samplers - the atlas
                //textures and the light's own constants all sit at fixed registers.
                shader.VertexShaderParameterRemaps.Clear();
                shader.PixelShaderParameterRemaps.Clear();
                shader.EngineParameterRemaps.Clear();
                return;
            }
            if (family == SHADER_LIST.CA_LIQUID_ENVIRONMENT)
            {
                //Every flow constant ships on every mask; only the environment multiplier follows
                //its feature, and it lands in the gap the two four-wide colours leave behind.
                shader.VertexShaderParameterRemaps.Clear();
                shader.PixelShaderParameterRemaps.Clear();
                shader.PixelShaderParameterRemaps.AddRange(BuildRemapTable(family, new List<int>(LiquidPsParams(mask))));
                shader.EngineParameterRemaps.Clear();
                return;
            }
            if (family == SHADER_LIST.CA_SPACESUIT_VISOR)
            {
                shader.VertexShaderParameterRemaps.Clear();
                shader.PixelShaderParameterRemaps.Clear();
                shader.PixelShaderParameterRemaps.AddRange(BuildRemapTable(family, new List<int>(VisorPsParams(mask))));
                //DRAW_PASS is the only engine parameter, and it always ships
                shader.EngineParameterRemaps.Clear();
                shader.EngineParameterRemaps.Add(0);
                return;
            }
            if (family == SHADER_LIST.CA_NONINTERACTIVE_WATER)
            {
                shader.VertexShaderParameterRemaps.Clear();
                shader.VertexShaderParameterRemaps.AddRange(BuildRemapTable(family, new List<int>(WaterVsParams(mask))));
                shader.PixelShaderParameterRemaps.Clear();
                shader.PixelShaderParameterRemaps.AddRange(BuildRemapTable(family, new List<int>(WaterPsParams(mask))));
                shader.EngineParameterRemaps.Clear();
                return;
            }
            if (family == SHADER_LIST.CA_GALAXY)
            {
                //The skybox stars declare no parameters and no samplers at all - the erf table the
                //pixel stage reads sits at a fixed engine register.
                shader.VertexShaderParameterRemaps.Clear();
                shader.PixelShaderParameterRemaps.Clear();
                shader.EngineParameterRemaps.Clear();
                return;
            }
            if (family == SHADER_LIST.CA_DAMAGE_DILATE_LOCATIONS || family == SHADER_LIST.CA_DAMAGE_RENDER_DAMAGE)
            {
                //The damage atlas passes have no parameters: every constant they use is borrowed
                //from the shared pixel cbuffer, and their samplers are the same on every mask.
                shader.VertexShaderParameterRemaps.Clear();
                shader.PixelShaderParameterRemaps.Clear();
                shader.EngineParameterRemaps.Clear();
                return;
            }
            if (family == SHADER_LIST.CA_DISTORTION_OVERLAY)
            {
                //All eight parameters ship on both masks; the feature only gates code.
                shader.VertexShaderParameterRemaps.Clear();
                shader.PixelShaderParameterRemaps.Clear();
                shader.PixelShaderParameterRemaps.AddRange(BuildRemapTable(family, new List<int> { 0, 1, 2, 3, 4, 5, 6, 7 }));
                shader.EngineParameterRemaps.Clear();
                return;
            }
            if (family == SHADER_LIST.CA_CAMERA_MAP)
            {
                //The projection rows are vertex parameters and the brightness is a pixel one, on
                //every mask. SHIFT_PRIORITY_LEVEL is the family's only engine parameter.
                shader.VertexShaderParameterRemaps.Clear();
                shader.VertexShaderParameterRemaps.AddRange(BuildRemapTable(family, new List<int> { 1, 2, 3, 4 }));
                shader.PixelShaderParameterRemaps.Clear();
                shader.PixelShaderParameterRemaps.AddRange(BuildRemapTable(family, new List<int> { 0 }));
                shader.EngineParameterRemaps.Clear();
                for (int id = 0; id < 10; id++)
                    shader.EngineParameterRemaps.Add(id == 9 ? 0 : 255);
                return;
            }
            if (family == SHADER_LIST.CA_SIMPLE_REFRACTION)
            {
                //Identical tables on both shipped masks. SECONDARY_SCALE is the only parameter the
                //pixel stage never takes - it is applied to the uv in the vertex stage instead.
                shader.VertexShaderParameterRemaps.Clear();
                shader.VertexShaderParameterRemaps.AddRange(BuildRemapTable(family, new List<int> { 4, 5 }));
                shader.PixelShaderParameterRemaps.Clear();
                shader.PixelShaderParameterRemaps.AddRange(BuildRemapTable(family, new List<int> { 0, 1, 2, 3, 4, 6, 7, 8, 9, 10, 11, 12 }));
                shader.EngineParameterRemaps.Clear();
                return;
            }
            if (family == SHADER_LIST.CA_FILTERS)
            {
                //Identical tables on all 21 masks; no ubershader samplers.
                shader.VertexShaderParameterRemaps.Clear();
                shader.VertexShaderParameterRemaps.AddRange(BuildRemapTable(family, FiltVsParams()));
                shader.PixelShaderParameterRemaps.Clear();
                shader.PixelShaderParameterRemaps.AddRange(BuildRemapTable(family, FiltPsParams()));
                shader.EngineParameterRemaps.Clear();
                return;
            }
            if (family == SHADER_LIST.CA_POST_PROCESSING)
            {
                //Identical tables on all 73 masks - the features gate code, not constants.  No
                //ubershader samplers: the five textures sit at fixed engine registers.
                shader.VertexShaderParameterRemaps.Clear();
                shader.VertexShaderParameterRemaps.AddRange(BuildRemapTable(family, PpVsParams()));
                shader.PixelShaderParameterRemaps.Clear();
                shader.PixelShaderParameterRemaps.AddRange(BuildRemapTable(family, PpPsParams()));
                shader.EngineParameterRemaps.Clear();
                return;
            }
            if (family == SHADER_LIST.CA_ALPHALIGHT_CLEAR || family == SHADER_LIST.CA_ALPHALIGHT_POSITION)
            {
                //The two alpha-light setup passes: no parameters, no ubershader samplers.
                shader.VertexShaderParameterRemaps.Clear();
                shader.PixelShaderParameterRemaps.Clear();
                shader.EngineParameterRemaps.Clear();
                return;
            }
            if (family == SHADER_LIST.CA_DEFERRED_DEPTH || family == SHADER_LIST.CA_DEFERRED_CONST)
            {
                //The light-volume prepasses: no parameters, no samplers, nothing handed to the PS.
                shader.VertexShaderParameterRemaps.Clear();
                shader.PixelShaderParameterRemaps.Clear();
                shader.EngineParameterRemaps.Clear();
                return;
            }
            if (family == SHADER_LIST.CA_VELOCITY)
            {
                //No ubershader parameters or samplers; the bone buffers are engine resources.
                shader.VertexShaderParameterRemaps.Clear();
                shader.PixelShaderParameterRemaps.Clear();
                shader.EngineParameterRemaps.Clear();
                return;
            }
            if (family == SHADER_LIST.CA_ALPHALIGHT_LIGHT)
            {
                //No ubershader parameters or samplers - every light property rides the interpolants.
                shader.VertexShaderParameterRemaps.Clear();
                shader.PixelShaderParameterRemaps.Clear();
                shader.EngineParameterRemaps.Clear();
                return;
            }
            if (family == SHADER_LIST.CA_RADIOSITY_RENDER)
            {
                //No ubershader parameters and no ubershader samplers: the G-buffer, lightmap and
                //cube array all sit at fixed engine registers.
                shader.VertexShaderParameterRemaps.Clear();
                shader.PixelShaderParameterRemaps.Clear();
                shader.EngineParameterRemaps.Clear();
                return;
            }
            if (family == SHADER_LIST.CA_DEFERRED)
            {
                //Like FOGSPHERE: no ubershader parameters and no ubershader samplers.  Every light
                //property rides down the interpolants out of RInstConstants instead, so all 196
                //shipped masks carry empty tables.
                shader.VertexShaderParameterRemaps.Clear();
                shader.PixelShaderParameterRemaps.Clear();
                shader.EngineParameterRemaps.Clear();
                return;
            }
            if (family == SHADER_LIST.CA_FOGSPHERE)
            {
                //No ubershader parameters at all - every shipped mask has empty tables
                shader.VertexShaderParameterRemaps.Clear();
                shader.PixelShaderParameterRemaps.Clear();
                shader.EngineParameterRemaps.Clear();
                return;
            }
            if (family == SHADER_LIST.CA_FOGPLANE)
            {
                //VS: ids 0-3 always; PS: ids 4-15 always, SPEED_0/SCALE_0 with DIFFUSE_MAPPING_0,
                //SPEED_1/SCALE_1 with DIFFUSE_MAPPING_1 (measured across all 39 shipped masks)
                bool diff0 = ((mask >> (int)CA_FOGPLANE.FEATURES.DIFFUSE_MAPPING_0) & 1) != 0;
                bool diff1 = ((mask >> (int)CA_FOGPLANE.FEATURES.DIFFUSE_MAPPING_1) & 1) != 0;

                List<int> vsIds = new List<int>() { 0, 1, 2, 3 };
                List<int> psIds = new List<int>();
                for (int id = 4; id <= 15; id++) psIds.Add(id);
                if (diff0) { psIds.Add(16); psIds.Add(17); }
                if (diff1) { psIds.Add(18); psIds.Add(19); }

                shader.VertexShaderParameterRemaps.Clear();
                shader.VertexShaderParameterRemaps.AddRange(BuildRemapTable(family, vsIds));
                shader.PixelShaderParameterRemaps.Clear();
                shader.PixelShaderParameterRemaps.AddRange(BuildRemapTable(family, psIds));
                shader.EngineParameterRemaps.Clear();
                return;
            }
            if (family == SHADER_LIST.CA_SKIN)
            {
                //Engine table is the same five entries on all 64 shipped masks. Samplers follow the
                //SAMPLERS enum, and the table spans 0..maxIncluded like the parameter ones.
                shader.VertexShaderParameterRemaps.Clear();
                shader.VertexShaderParameterRemaps.AddRange(BuildRemapTable(family, SkinVSParams(mask).ToList()));
                shader.PixelShaderParameterRemaps.Clear();
                shader.PixelShaderParameterRemaps.AddRange(BuildRemapTable(family, SkinPSParams(mask).ToList()));
                shader.EngineParameterRemaps.Clear();
                shader.EngineParameterRemaps.AddRange(new int[] { 0, 255, 255, 255, 1 });
                shader.SamplerRemaps.Clear();
                shader.SamplerRemaps.AddRange(BuildSequentialRemapTable(SkinSamplerIds(mask)));
                return;
            }
            if (family == SHADER_LIST.CA_LOW_LOD_CHARACTER)
            {
                //Engine table is the same six entries on all 24 shipped masks.
                shader.VertexShaderParameterRemaps.Clear();
                shader.VertexShaderParameterRemaps.AddRange(BuildRemapTable(family, LlcVSParams(mask).ToList()));
                shader.PixelShaderParameterRemaps.Clear();
                shader.PixelShaderParameterRemaps.AddRange(BuildRemapTable(family, LlcPSParams(mask).ToList()));
                shader.EngineParameterRemaps.Clear();
                shader.EngineParameterRemaps.AddRange(new int[] { 0, 1, 2, 3, 255, 4 });
                shader.SamplerRemaps.Clear();
                shader.SamplerRemaps.AddRange(BuildSequentialRemapTable(LlcSamplerIds(mask)));
                return;
            }
            if (family == SHADER_LIST.CA_LIGHTMAP_ENVIRONMENT)
            {
                //Engine table is the same twelve entries on all 40 shipped masks.
                shader.VertexShaderParameterRemaps.Clear();
                shader.VertexShaderParameterRemaps.AddRange(BuildRemapTable(family, LmeVSParams(mask).ToList()));
                shader.PixelShaderParameterRemaps.Clear();
                shader.PixelShaderParameterRemaps.AddRange(BuildRemapTable(family, LmePSParams(mask).ToList()));
                shader.EngineParameterRemaps.Clear();
                shader.EngineParameterRemaps.AddRange(new int[] { 0, 1, 2, 255, 255, 255, 255, 255, 255, 255, 255, 3 });
                shader.SamplerRemaps.Clear();
                shader.SamplerRemaps.AddRange(BuildSequentialRemapTable(LmeSamplerIds(mask)));
                return;
            }
            if (family == SHADER_LIST.CA_CHARACTER)
            {
                //Engine table is the same sixteen entries on all 421 shipped masks.
                shader.VertexShaderParameterRemaps.Clear();
                shader.VertexShaderParameterRemaps.AddRange(BuildRemapTable(family, CharVSParams(mask).ToList()));
                shader.PixelShaderParameterRemaps.Clear();
                shader.PixelShaderParameterRemaps.AddRange(BuildRemapTable(family, CharPSParams(mask).ToList()));
                shader.EngineParameterRemaps.Clear();
                shader.EngineParameterRemaps.AddRange(new int[] { 0, 1, 2, 3, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 4 });
                shader.SamplerRemaps.Clear();
                shader.SamplerRemaps.AddRange(BuildSequentialRemapTable(CharSamplerIds(mask)));
                return;
            }
            if (family == SHADER_LIST.CA_RIBBON)
            {
                //the only mastered family whose engine table varies with the mask - see RibbonEngineIds
                shader.VertexShaderParameterRemaps.Clear();
                shader.VertexShaderParameterRemaps.AddRange(BuildRemapTable(family, RibbonVSParams(mask).ToList()));
                shader.PixelShaderParameterRemaps.Clear();
                shader.PixelShaderParameterRemaps.AddRange(BuildRemapTable(family, RibbonPSParams(mask).ToList()));
                shader.EngineParameterRemaps.Clear();
                shader.EngineParameterRemaps.AddRange(BuildSequentialRemapTable(RibbonEngineIds(mask)));
                shader.SamplerRemaps.Clear();
                shader.SamplerRemaps.AddRange(BuildSequentialRemapTable(RibbonSamplerIds(mask)));
                return;
            }
            if (family == SHADER_LIST.CA_DECAL)
            {
                //Engine table is the same five entries on all 36 shipped masks.
                shader.VertexShaderParameterRemaps.Clear();
                shader.VertexShaderParameterRemaps.AddRange(BuildRemapTable(family, DecalVSParams(mask).ToList()));
                shader.PixelShaderParameterRemaps.Clear();
                shader.PixelShaderParameterRemaps.AddRange(BuildRemapTable(family, DecalPSParams(mask).ToList()));
                shader.EngineParameterRemaps.Clear();
                shader.EngineParameterRemaps.AddRange(new int[] { 255, 255, 255, 255, 0 });
                shader.SamplerRemaps.Clear();
                shader.SamplerRemaps.AddRange(BuildSequentialRemapTable(DecalSamplerIds(mask)));
                return;
            }
            if (family == SHADER_LIST.CA_PARTICLE)
            {
                //engine table varies with the mask - see ParticleEngineIds
                shader.VertexShaderParameterRemaps.Clear();
                shader.VertexShaderParameterRemaps.AddRange(BuildRemapTable(family, ParticleVSParams(mask).ToList()));
                shader.PixelShaderParameterRemaps.Clear();
                shader.PixelShaderParameterRemaps.AddRange(BuildRemapTable(family, ParticlePSParams(mask).ToList()));
                shader.EngineParameterRemaps.Clear();
                shader.EngineParameterRemaps.AddRange(BuildSequentialRemapTable(ParticleEngineIds(mask)));
                shader.SamplerRemaps.Clear();
                shader.SamplerRemaps.AddRange(BuildSequentialRemapTable(ParticleSamplerIds(mask)));
                return;
            }
            if (family == SHADER_LIST.CA_ENVIRONMENT)
            {
                shader.VertexShaderParameterRemaps.Clear();
                shader.VertexShaderParameterRemaps.AddRange(BuildRemapTable(family, EnvVSParams(mask).ToList()));
                shader.PixelShaderParameterRemaps.Clear();
                shader.PixelShaderParameterRemaps.AddRange(BuildRemapTable(family, EnvPSParams(mask).ToList()));
                //Only a tessellated mask carries these; every other one leaves both tables empty,
                //which is what retail does too.
                shader.HullShaderParameterRemaps.Clear();
                shader.DomainShaderParameterRemaps.Clear();
                if (RequiresTessellationStages(family, mask))
                {
                    shader.HullShaderParameterRemaps.AddRange(BuildRemapTable(family, EnvHSParams(mask).ToList()));
                    shader.DomainShaderParameterRemaps.AddRange(BuildRemapTable(family, EnvDSParams(mask).ToList()));
                }
                shader.EngineParameterRemaps.Clear();
                shader.EngineParameterRemaps.AddRange(BuildSequentialRemapTable(EnvEngineIds(mask)));
                shader.SamplerRemaps.Clear();
                shader.SamplerRemaps.AddRange(BuildSequentialRemapTable(EnvSamplerIds(mask)));
                return;
            }
            if (family == SHADER_LIST.CA_DECAL_ENVIRONMENT)
            {
                shader.VertexShaderParameterRemaps.Clear();
                shader.VertexShaderParameterRemaps.AddRange(BuildRemapTable(family, DecEnvVSParams(mask).ToList()));
                shader.PixelShaderParameterRemaps.Clear();
                shader.PixelShaderParameterRemaps.AddRange(BuildRemapTable(family, DecEnvPSParams(mask).ToList()));
                shader.EngineParameterRemaps.Clear();
                shader.EngineParameterRemaps.AddRange(BuildSequentialRemapTable(DecEnvEngineIds(mask)));
                shader.SamplerRemaps.Clear();
                shader.SamplerRemaps.AddRange(BuildSequentialRemapTable(DecEnvSamplerIds(mask)));
                return;
            }
            if (family == SHADER_LIST.CA_OCCLUSION_CULLING)
            {
                //The occlusion pass declares no parameters and no samplers at all.
                shader.VertexShaderParameterRemaps.Clear();
                shader.PixelShaderParameterRemaps.Clear();
                shader.EngineParameterRemaps.Clear();
                shader.SamplerRemaps.Clear();
                return;
            }
            if (family == SHADER_LIST.CA_LIGHT_DECAL)
            {
                //No parameters; the intensity map is its only sampler and always ships.
                shader.VertexShaderParameterRemaps.Clear();
                shader.PixelShaderParameterRemaps.Clear();
                shader.EngineParameterRemaps.Clear();
                shader.SamplerRemaps.Clear();
                shader.SamplerRemaps.Add(0);
                return;
            }
            if (family == SHADER_LIST.CA_SKIN_OCCLUSION)
            {
                shader.VertexShaderParameterRemaps.Clear();
                shader.VertexShaderParameterRemaps.AddRange(BuildRemapTable(family, SknOccVSParams(mask).ToList()));
                shader.PixelShaderParameterRemaps.Clear();
                shader.PixelShaderParameterRemaps.AddRange(BuildRemapTable(family, SknOccPSParams(mask).ToList()));
                shader.EngineParameterRemaps.Clear();
                shader.EngineParameterRemaps.AddRange(BuildSequentialRemapTable(new List<int>() { 0, 1, 2 }));
                shader.SamplerRemaps.Clear();
                shader.SamplerRemaps.AddRange(BuildSequentialRemapTable(SknOccSamplerIds(mask)));
                return;
            }
            if (family == SHADER_LIST.CA_SURFACE_EFFECTS)
            {
                shader.VertexShaderParameterRemaps.Clear();
                shader.VertexShaderParameterRemaps.AddRange(BuildRemapTable(family, SfxVSParams(mask).ToList()));
                shader.PixelShaderParameterRemaps.Clear();
                shader.PixelShaderParameterRemaps.AddRange(BuildRemapTable(family, SfxPSParams(mask).ToList()));
                shader.EngineParameterRemaps.Clear();
                shader.EngineParameterRemaps.AddRange(BuildSequentialRemapTable(new List<int>() { 0, 1, 2 }));
                shader.SamplerRemaps.Clear();
                shader.SamplerRemaps.AddRange(BuildSequentialRemapTable(SfxSamplerIds(mask)));
                return;
            }
            if (family == SHADER_LIST.CA_PLANET)
            {
                shader.VertexShaderParameterRemaps.Clear();
                shader.VertexShaderParameterRemaps.AddRange(BuildRemapTable(family, PlanetVSParams(mask).ToList()));
                shader.PixelShaderParameterRemaps.Clear();
                shader.PixelShaderParameterRemaps.AddRange(BuildRemapTable(family, PlanetPSParams(mask).ToList()));
                shader.EngineParameterRemaps.Clear();
                shader.SamplerRemaps.Clear();
                shader.SamplerRemaps.AddRange(BuildSequentialRemapTable(PlanetSamplerIds(mask)));
                return;
            }
            if (family == SHADER_LIST.CA_EFFECT_OVERLAY)
            {
                shader.VertexShaderParameterRemaps.Clear();
                shader.VertexShaderParameterRemaps.AddRange(BuildRemapTable(family, EovVSParams(mask).ToList()));
                shader.PixelShaderParameterRemaps.Clear();
                shader.PixelShaderParameterRemaps.AddRange(BuildRemapTable(family, EovPSParams(mask).ToList()));
                shader.EngineParameterRemaps.Clear();
                shader.SamplerRemaps.Clear();
                shader.SamplerRemaps.AddRange(BuildSequentialRemapTable(EovSamplerIds(mask)));
                return;
            }
            if (family == SHADER_LIST.CA_VOLUME_LIGHT)
            {
                /* This family has no ubershader parameters and no remapped samplers at all - it
                 * reads only engine constants and the fixed depth/shadow/gobo registers - so every
                 * table is empty on every one of its masks. */
                shader.VertexShaderParameterRemaps.Clear();
                shader.PixelShaderParameterRemaps.Clear();
                shader.EngineParameterRemaps.Clear();
                shader.SamplerRemaps.Clear();
                return;
            }
            if (family == SHADER_LIST.CA_TERRAIN)
            {
                shader.VertexShaderParameterRemaps.Clear();
                shader.PixelShaderParameterRemaps.Clear();
                shader.PixelShaderParameterRemaps.AddRange(BuildRemapTable(family, TerrPSParams(mask).ToList()));
                shader.EngineParameterRemaps.Clear();
                shader.EngineParameterRemaps.AddRange(BuildSequentialRemapTable(new List<int>() { 0, 1 }));
                shader.SamplerRemaps.Clear();
                shader.SamplerRemaps.AddRange(BuildSequentialRemapTable(TerrSamplerIds(mask)));
                return;
            }
            if (family == SHADER_LIST.CA_REFRACTION)
            {
                shader.VertexShaderParameterRemaps.Clear();
                shader.VertexShaderParameterRemaps.AddRange(BuildRemapTable(family, RefrVSParams(mask).ToList()));
                shader.PixelShaderParameterRemaps.Clear();
                shader.PixelShaderParameterRemaps.AddRange(BuildRemapTable(family, RefrPSParams(mask).ToList()));
                shader.EngineParameterRemaps.Clear();
                shader.SamplerRemaps.Clear();
                shader.SamplerRemaps.AddRange(BuildSequentialRemapTable(RefrSamplerIds(mask)));
                return;
            }
            if (family == SHADER_LIST.CA_HAIR)
            {
                /* No vertex parameters on any mask; the engine table is a constant 0,1. */
                shader.VertexShaderParameterRemaps.Clear();
                shader.PixelShaderParameterRemaps.Clear();
                shader.PixelShaderParameterRemaps.AddRange(BuildRemapTable(family, HairPSParams(mask).ToList()));
                shader.EngineParameterRemaps.Clear();
                shader.EngineParameterRemaps.AddRange(BuildSequentialRemapTable(new List<int>() { 0, 1 }));
                shader.SamplerRemaps.Clear();
                shader.SamplerRemaps.AddRange(BuildSequentialRemapTable(HairSamplerIds(mask)));
                return;
            }
            if (family == SHADER_LIST.CA_SIMPLEWATER)
            {
                /* The engine table is empty on every mask. */
                shader.VertexShaderParameterRemaps.Clear();
                shader.VertexShaderParameterRemaps.AddRange(BuildRemapTable(family, SwatVSParams(mask).ToList()));
                shader.PixelShaderParameterRemaps.Clear();
                shader.PixelShaderParameterRemaps.AddRange(BuildRemapTable(family, SwatPSParams(mask).ToList()));
                shader.EngineParameterRemaps.Clear();
                shader.SamplerRemaps.Clear();
                shader.SamplerRemaps.AddRange(BuildSequentialRemapTable(SwatSamplerIds(mask)));
                return;
            }
            if (family == SHADER_LIST.CA_EYE)
            {
                /* No vertex parameters; the engine table is a constant 0..6. */
                shader.VertexShaderParameterRemaps.Clear();
                shader.PixelShaderParameterRemaps.Clear();
                shader.PixelShaderParameterRemaps.AddRange(BuildRemapTable(family, EyePSParams(mask).ToList()));
                shader.EngineParameterRemaps.Clear();
                shader.EngineParameterRemaps.AddRange(BuildSequentialRemapTable(new List<int>() { 0, 1, 2, 3, 4, 5, 6 }));
                shader.SamplerRemaps.Clear();
                shader.SamplerRemaps.AddRange(BuildSequentialRemapTable(EyeSamplerIds(mask)));
                return;
            }
        }

        /* A remap table spans parameter ids 0..maxIncluded, marking non-participating ids 255 -
         * exactly the retail layout */

        /* A sampler (or engine) remap table also spans ids 0..maxIncluded with 255 for absent, but
         * the included ids take SEQUENTIAL bind indices - unlike the parameter tables, whose
         * values are width-allocated constant slots. Using the parameter builder here silently
         * produces wrong bind indices wherever a parameter sharing that id is wider than one
         * scalar (measured: 8 CA_SKIN masks and 7 CA_LIGHTMAP_ENVIRONMENT masks). */
        private static List<int> BuildSequentialRemapTable(List<int> includedIds)
        {
            List<int> table = new List<int>();
            if (includedIds.Count == 0)
                return table;
            int maxId = includedIds.Max();
            int next = 0;
            for (int id = 0; id <= maxId; id++)
                table.Add(includedIds.Contains(id) ? next++ : 255);
            return table;
        }
        private static List<int> BuildRemapTable(SHADER_LIST family, List<int> includedIds)
        {
            List<int> table = new List<int>();
            if (includedIds.Count == 0)
                return table;
            Dictionary<int, int> slots = AllocSlots(family, includedIds);
            int maxId = includedIds.Max();
            for (int id = 0; id <= maxId; id++)
                table.Add(slots.ContainsKey(id) ? slots[id] : 255);
            return table;
        }
        #endregion

        #region COMPILER
        [ComImport, Guid("8BA5FB08-5195-40e2-AC58-0D989C3A0102"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ID3DBlob
        {
            [PreserveSig] IntPtr GetBufferPointer();
            [PreserveSig] IntPtr GetBufferSize();
        }

        [DllImport("d3dcompiler_43.dll", EntryPoint = "D3DCompile", CharSet = CharSet.Ansi)]
        private static extern int D3DCompile43(byte[] srcData, IntPtr srcDataSize, string sourceName, IntPtr defines, IntPtr include,
            string entrypoint, string target, uint flags1, uint flags2, out ID3DBlob code, out ID3DBlob errors);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string fileName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr module);

        /* Default flags, matching how the retail pipeline invoked fxc */
        public static byte[] Compile43(string hlsl, string entry, string target, out string error)
        {
            byte[] src = Encoding.ASCII.GetBytes(hlsl);
            ID3DBlob code, errs;
            int hr = D3DCompile43(src, (IntPtr)src.Length, "opencage", IntPtr.Zero, IntPtr.Zero, entry, target, 0, 0, out code, out errs);
            error = null;
            if (errs != null)
            {
                IntPtr errPtr = errs.GetBufferPointer();
                int errLen = (int)errs.GetBufferSize();
                byte[] errBytes = new byte[errLen];
                Marshal.Copy(errPtr, errBytes, 0, errLen);
                error = Encoding.ASCII.GetString(errBytes).TrimEnd('\0');
                Marshal.ReleaseComObject(errs);
            }
            if (hr != 0 || code == null) return null;
            IntPtr ptr = code.GetBufferPointer();
            int len = (int)code.GetBufferSize();
            byte[] result = new byte[len];
            Marshal.Copy(ptr, result, 0, len);
            Marshal.ReleaseComObject(code);
            return result;
        }

        [DllImport("d3dcompiler_43.dll", EntryPoint = "D3DPreprocess", CharSet = CharSet.Ansi)]
        private static extern int D3DPreprocess43(byte[] srcData, IntPtr srcDataSize, string sourceName, IntPtr defines, IntPtr include,
            out ID3DBlob codeText, out ID3DBlob errors);

        /* Run the master through the preprocessor only - which #if arms survive, without paying for
         * a compile. Returns null if the source doesn't preprocess. */
        public static string Preprocess43(string hlsl, out string error)
        {
            byte[] src = Encoding.ASCII.GetBytes(hlsl);
            ID3DBlob code, errs;
            int hr = D3DPreprocess43(src, (IntPtr)src.Length, "opencage", IntPtr.Zero, IntPtr.Zero, out code, out errs);
            error = null;
            if (errs != null)
            {
                int errLen = (int)errs.GetBufferSize();
                byte[] errBytes = new byte[errLen];
                Marshal.Copy(errs.GetBufferPointer(), errBytes, 0, errLen);
                error = Encoding.ASCII.GetString(errBytes).TrimEnd('\0');
                Marshal.ReleaseComObject(errs);
            }
            if (hr != 0 || code == null) return null;
            int len = (int)code.GetBufferSize();
            byte[] result = new byte[len];
            Marshal.Copy(code.GetBufferPointer(), result, 0, len);
            Marshal.ReleaseComObject(code);
            return Encoding.ASCII.GetString(result);
        }

        #endregion
    }
}
#endif
