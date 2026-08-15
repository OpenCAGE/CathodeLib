#if !(UNITY_EDITOR || UNITY_STANDALONE_WIN || GODOT)
namespace CathodeLib.Radiosity
{

    public static partial class RadiosityDebugExport
    {
        /// <summary>
        /// The viewer page. <c>/*__DATA__*/</c> is replaced with the level's payload on export.
        /// Deliberately dependency-free WebGL2 so the result opens from disk with no network.
        /// </summary>
        private const string Template = @"<!doctype html>
<html lang=""en"">
<head>
<meta charset=""utf-8"">
<title>Radiosity explorer</title>
<style>
  :root { --bg:#111318; --panel:#191d25; --line:#2b3240; --text:#dde3ee; --dim:#8d97a8; --accent:#6fb3ff; }
  * { box-sizing: border-box; }
  html, body { margin:0; height:100%; overflow:hidden; background:var(--bg); color:var(--text);
               font:13px/1.45 ui-sans-serif, system-ui, 'Segoe UI', sans-serif; }
  #gl { position:fixed; inset:0; display:block; cursor:grab; }
  #gl.dragging { cursor:grabbing; }
  #ui { position:fixed; top:0; left:0; width:290px; max-height:100%; overflow-y:auto;
        background:rgba(25,29,37,.94); border-right:1px solid var(--line); padding:14px; }
  h1 { font-size:14px; margin:0 0 2px; letter-spacing:.02em; }
  h2 { font-size:11px; text-transform:uppercase; letter-spacing:.09em; color:var(--dim);
       margin:16px 0 7px; font-weight:600; }
  .sub { color:var(--dim); font-size:11px; margin-bottom:4px; }
  label { display:flex; align-items:center; gap:7px; padding:2px 0; cursor:pointer; }
  label span.k { margin-left:auto; color:var(--dim); font-variant-numeric:tabular-nums; font-size:11px; }
  input[type=checkbox], input[type=radio] { accent-color:var(--accent); }
  input[type=range] { width:100%; accent-color:var(--accent); }
  select { width:100%; background:#0e1116; color:var(--text); border:1px solid var(--line);
           border-radius:4px; padding:4px; }
  .swatch { width:10px; height:10px; border-radius:2px; display:inline-block; }
  #pick { position:fixed; right:0; top:0; width:290px; max-height:100%; overflow-y:auto;
          background:rgba(25,29,37,.94); border-left:1px solid var(--line); padding:14px; display:none; }
  #pick table { width:100%; border-collapse:collapse; font-variant-numeric:tabular-nums; }
  #pick td { padding:2px 0; vertical-align:top; }
  #pick td:first-child { color:var(--dim); width:44%; }
  #hint { position:fixed; bottom:10px; left:50%; transform:translateX(-50%); color:var(--dim);
          font-size:11px; background:rgba(25,29,37,.85); padding:5px 12px; border-radius:5px; }
  kbd { background:#0e1116; border:1px solid var(--line); border-radius:3px; padding:0 4px; }
</style>
</head>
<body>
<canvas id=""gl""></canvas>
<div id=""ui""></div>
<div id=""pick""></div>
<div id=""hint"">drag orbit · right-drag / shift-drag pan · wheel zoom · <kbd>F</kbd> frame · click a probe to inspect</div>
<script>
const DATA = /*__DATA__*/;

// ---- payload decoding -------------------------------------------------------------------
function decode(s) {
  if (typeof s !== 'string') return s;
  const c = s.indexOf(':'), tag = s.slice(0, c);
  const bin = atob(s.slice(c + 1));
  const buf = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) buf[i] = bin.charCodeAt(i);
  if (tag === 'f32') return new Float32Array(buf.buffer);
  if (tag === 'u32') return new Uint32Array(buf.buffer);
  return buf;
}
for (const sl of DATA.slices) for (const k in sl) sl[k] = decode(sl[k]);
if (DATA.movers) for (const k in DATA.movers) DATA.movers[k] = decode(DATA.movers[k]);

const NS = DATA.slices.length;
const SLICE_HUES = [[0.42,0.70,1],[1,0.62,0.34],[0.55,0.88,0.52],[0.92,0.55,0.90],
                    [0.98,0.85,0.40],[0.50,0.90,0.88],[0.80,0.45,0.45],[0.65,0.65,0.95]];

// ---- GL setup ---------------------------------------------------------------------------
const canvas = document.getElementById('gl');
const gl = canvas.getContext('webgl2', { antialias: true, alpha: false });
if (!gl) document.body.innerHTML = '<p style=""padding:20px"">WebGL2 is required.</p>';

function shader(type, src) {
  const s = gl.createShader(type);
  gl.shaderSource(s, src); gl.compileShader(s);
  if (!gl.getShaderParameter(s, gl.COMPILE_STATUS)) throw new Error(gl.getShaderInfoLog(s));
  return s;
}
function program(vs, fs) {
  const p = gl.createProgram();
  gl.attachShader(p, shader(gl.VERTEX_SHADER, vs));
  gl.attachShader(p, shader(gl.FRAGMENT_SHADER, fs));
  gl.linkProgram(p);
  if (!gl.getProgramParameter(p, gl.LINK_STATUS)) throw new Error(gl.getProgramInfoLog(p));
  return p;
}

const POINT_VS = `#version 300 es
in vec3 aPos; in vec3 aCol;
uniform mat4 uVP; uniform float uSize; uniform float uFade;
out vec3 vCol;
void main() {
  vec4 clip = uVP * vec4(aPos, 1.0);
  gl_Position = clip;
  gl_PointSize = max(1.0, uSize * (uFade / max(0.001, clip.w)));
  vCol = aCol;
}`;
const POINT_FS = `#version 300 es
precision highp float;
in vec3 vCol; out vec4 outColour; uniform float uAlpha;
void main() {
  vec2 d = gl_PointCoord - 0.5;
  if (dot(d, d) > 0.25) discard;
  outColour = vec4(vCol, uAlpha);
}`;
const LINE_VS = `#version 300 es
in vec3 aPos; in vec3 aCol;
uniform mat4 uVP; out vec3 vCol;
void main() { gl_Position = uVP * vec4(aPos, 1.0); vCol = aCol; }`;
const LINE_FS = `#version 300 es
precision highp float;
in vec3 vCol; out vec4 outColour; uniform float uAlpha;
void main() { outColour = vec4(vCol, uAlpha); }`;

const pointProg = program(POINT_VS, POINT_FS);
const lineProg = program(LINE_VS, LINE_FS);

function makeBuffers(pos, col) {
  const vao = gl.createVertexArray();
  gl.bindVertexArray(vao);
  const pb = gl.createBuffer();
  gl.bindBuffer(gl.ARRAY_BUFFER, pb);
  gl.bufferData(gl.ARRAY_BUFFER, pos, gl.STATIC_DRAW);
  gl.enableVertexAttribArray(0); gl.vertexAttribPointer(0, 3, gl.FLOAT, false, 0, 0);
  const cb = gl.createBuffer();
  gl.bindBuffer(gl.ARRAY_BUFFER, cb);
  gl.bufferData(gl.ARRAY_BUFFER, col, gl.DYNAMIC_DRAW);
  gl.enableVertexAttribArray(1); gl.vertexAttribPointer(1, 3, gl.FLOAT, false, 0, 0);
  gl.bindVertexArray(null);
  return { vao, colourBuffer: cb, count: pos.length / 3 };
}

// ---- build renderable layers ------------------------------------------------------------
// Every probe from every slice lives in one buffer; a per-vertex colour array is recomputed
// whenever the colour mode changes, and a slice filter is applied by writing black.
function concatSlices(key) {
  let n = 0;
  for (const s of DATA.slices) n += s[key].length;
  const out = new Float32Array(n);
  let o = 0, owner = [];
  for (let i = 0; i < NS; i++) {
    out.set(DATA.slices[i][key], o);
    owner.push([o / 3, DATA.slices[i][key].length / 3]);
    o += DATA.slices[i][key].length;
  }
  return { data: out, owner };
}

const inputProbes = concatSlices('inputPos');
const surfaceProbes = concatSlices('surfacePos');
const clusters = concatSlices('clusterPos');
const lights = concatSlices('lightPos');

// Influence links, expanded to line pairs (surface probe -> cluster).
let infPos, infWeightPerLine, infSlicePerLine;
(function buildInfluences() {
  let total = 0;
  for (const s of DATA.slices) total += s.infProbe.length;
  infPos = new Float32Array(total * 6);
  infWeightPerLine = new Uint8Array(total);
  infSlicePerLine = new Uint8Array(total);
  let l = 0;
  for (let si = 0; si < NS; si++) {
    const s = DATA.slices[si];
    for (let i = 0; i < s.infProbe.length; i++) {
      const a = s.infProbe[i] * 3, b = s.infCluster[i] * 3;
      infPos[l * 6 + 0] = s.surfacePos[a];     infPos[l * 6 + 1] = s.surfacePos[a + 1]; infPos[l * 6 + 2] = s.surfacePos[a + 2];
      infPos[l * 6 + 3] = s.clusterPos[b];     infPos[l * 6 + 4] = s.clusterPos[b + 1]; infPos[l * 6 + 5] = s.clusterPos[b + 2];
      infWeightPerLine[l] = s.infWeight[i];
      infSlicePerLine[l] = si;
      l++;
    }
  }
})();

const layers = {
  inputProbes:   { buf: makeBuffers(inputProbes.data,   new Float32Array(inputProbes.data.length)),   on: true,  kind: 'point', label: 'Input probes (emitters)' },
  surfaceProbes: { buf: makeBuffers(surfaceProbes.data, new Float32Array(surfaceProbes.data.length)), on: false, kind: 'point', label: 'Surface probes (receivers)' },
  clusters:      { buf: makeBuffers(clusters.data,      new Float32Array(clusters.data.length)),      on: false, kind: 'point', label: 'Clusters' },
  lights:        { buf: makeBuffers(lights.data,        new Float32Array(lights.data.length)),        on: true,  kind: 'point', label: 'Surface lights' },
  influences:    { buf: makeBuffers(infPos,             new Float32Array(infPos.length)),             on: false, kind: 'line',  label: 'Influence links' },
};
if (DATA.movers) {
  const mp = DATA.movers.pos;
  layers.movers = { buf: makeBuffers(mp, new Float32Array(mp.length)), on: false, kind: 'point', label: 'Model origins' };
}

// ---- colouring --------------------------------------------------------------------------
let colourMode = 'albedo';
let sliceFilter = -1;           // -1 = all
let weightMin = 0;

function sliceOf(owner, index) {
  for (let i = 0; i < owner.length; i++)
    if (index >= owner[i][0] && index < owner[i][0] + owner[i][1]) return i;
  return 0;
}

function recolour() {
  // Input probes.
  {
    const n = inputProbes.data.length / 3, c = new Float32Array(n * 3);
    for (let si = 0; si < NS; si++) {
      const s = DATA.slices[si], base = inputProbes.owner[si][0], cnt = inputProbes.owner[si][1];
      const hue = SLICE_HUES[si % SLICE_HUES.length];
      for (let i = 0; i < cnt; i++) {
        const o = (base + i) * 3;
        if (sliceFilter >= 0 && sliceFilter !== si) { c[o] = c[o+1] = c[o+2] = 0.06; continue; }
        if (colourMode === 'albedo') {
          c[o] = s.inputAlbedo[i*3] / 255; c[o+1] = s.inputAlbedo[i*3+1] / 255; c[o+2] = s.inputAlbedo[i*3+2] / 255;
        } else if (colourMode === 'normal') {
          c[o] = s.inputNormal[i*3] / 255; c[o+1] = s.inputNormal[i*3+1] / 255; c[o+2] = s.inputNormal[i*3+2] / 255;
        } else if (colourMode === 'slice') {
          c[o] = hue[0]; c[o+1] = hue[1]; c[o+2] = hue[2];
        } else if (colourMode === 'luminance') {
          const y = (0.299*s.inputAlbedo[i*3] + 0.587*s.inputAlbedo[i*3+1] + 0.114*s.inputAlbedo[i*3+2]) / 255;
          const h = heat(y); c[o] = h[0]; c[o+1] = h[1]; c[o+2] = h[2];
        } else { c[o] = c[o+1] = 0.75; c[o+2] = 0.8; }
      }
    }
    upload(layers.inputProbes.buf, c);
  }

  // Surface probes, optionally shaded by how much influence they receive.
  {
    const n = surfaceProbes.data.length / 3, c = new Float32Array(n * 3);
    const totals = new Float32Array(n);
    let l = 0;
    for (let si = 0; si < NS; si++) {
      const s = DATA.slices[si], base = surfaceProbes.owner[si][0];
      for (let i = 0; i < s.infProbe.length; i++) totals[base + s.infProbe[i]] += s.infWeight[i];
    }
    let peak = 1;
    for (let i = 0; i < n; i++) peak = Math.max(peak, totals[i]);
    for (let si = 0; si < NS; si++) {
      const base = surfaceProbes.owner[si][0], cnt = surfaceProbes.owner[si][1];
      const hue = SLICE_HUES[si % SLICE_HUES.length];
      for (let i = 0; i < cnt; i++) {
        const idx = base + i, o = idx * 3;
        if (sliceFilter >= 0 && sliceFilter !== si) { c[o] = c[o+1] = c[o+2] = 0.06; continue; }
        if (colourMode === 'slice') { c[o] = hue[0]; c[o+1] = hue[1]; c[o+2] = hue[2]; }
        else { const h = heat(totals[idx] / peak); c[o] = h[0]; c[o+1] = h[1]; c[o+2] = h[2]; }
      }
    }
    upload(layers.surfaceProbes.buf, c);
  }

  // Clusters.
  {
    const n = clusters.data.length / 3, c = new Float32Array(n * 3);
    for (let si = 0; si < NS; si++) {
      const base = clusters.owner[si][0], cnt = clusters.owner[si][1];
      const hue = SLICE_HUES[si % SLICE_HUES.length];
      for (let i = 0; i < cnt; i++) {
        const o = (base + i) * 3;
        if (sliceFilter >= 0 && sliceFilter !== si) { c[o] = c[o+1] = c[o+2] = 0.06; continue; }
        c[o] = hue[0] * 0.8; c[o+1] = hue[1] * 0.8; c[o+2] = hue[2] * 0.8;
      }
    }
    upload(layers.clusters.buf, c);
  }

  // Surface lights, by their own colour scaled by Scale.
  {
    const n = lights.data.length / 3, c = new Float32Array(n * 3);
    for (let si = 0; si < NS; si++) {
      const s = DATA.slices[si], base = lights.owner[si][0], cnt = lights.owner[si][1];
      for (let i = 0; i < cnt; i++) {
        const o = (base + i) * 3;
        if (sliceFilter >= 0 && sliceFilter !== si) { c[o] = c[o+1] = c[o+2] = 0.06; continue; }
        const k = Math.min(1, (s.lightScale[i] + 1) / 32);
        c[o] = (s.lightRgb[i*3] / 255) * (0.35 + 0.65*k);
        c[o+1] = (s.lightRgb[i*3+1] / 255) * (0.35 + 0.65*k);
        c[o+2] = (s.lightRgb[i*3+2] / 255) * (0.35 + 0.65*k);
      }
    }
    upload(layers.lights.buf, c);
  }

  // Influence links: two vertices per line, coloured by weight.
  {
    const c = new Float32Array(infPos.length);
    for (let l = 0; l < infWeightPerLine.length; l++) {
      const w = infWeightPerLine[l], si = infSlicePerLine[l];
      let r, g, b;
      if ((sliceFilter >= 0 && sliceFilter !== si) || w < weightMin) { r = g = b = 0; }
      else { const h = heat(w / 255); r = h[0]; g = h[1]; b = h[2]; }
      c[l*6] = r; c[l*6+1] = g; c[l*6+2] = b;
      c[l*6+3] = r; c[l*6+4] = g; c[l*6+5] = b;
    }
    upload(layers.influences.buf, c);
  }

  if (layers.movers) {
    const k = DATA.movers.kind, n = k.length, c = new Float32Array(n * 3);
    for (let i = 0; i < n; i++) {
      const o = i * 3;
      if (k[i] === 1) { c[o] = 1; c[o+1] = 0.85; c[o+2] = 0.3; }
      else if (k[i] === 2) { c[o] = 1; c[o+1] = 0.45; c[o+2] = 0.2; }
      else { c[o] = 0.28; c[o+1] = 0.32; c[o+2] = 0.4; }
    }
    upload(layers.movers.buf, c);
  }
}

function upload(buf, colours) {
  gl.bindBuffer(gl.ARRAY_BUFFER, buf.colourBuffer);
  gl.bufferData(gl.ARRAY_BUFFER, colours, gl.DYNAMIC_DRAW);
}

function heat(t) {
  t = Math.max(0, Math.min(1, t));
  // dark blue -> cyan -> yellow -> white
  if (t < 0.33) { const u = t / 0.33; return [0.05 + 0.05*u, 0.10 + 0.55*u, 0.35 + 0.60*u]; }
  if (t < 0.66) { const u = (t - 0.33) / 0.33; return [0.10 + 0.85*u, 0.65 + 0.30*u, 0.95 - 0.75*u]; }
  const u = (t - 0.66) / 0.34; return [0.95 + 0.05*u, 0.95 + 0.05*u, 0.20 + 0.80*u];
}

// ---- camera -----------------------------------------------------------------------------
const bounds = (() => {
  const lo = [1e30, 1e30, 1e30], hi = [-1e30, -1e30, -1e30];
  const p = inputProbes.data.length ? inputProbes.data : surfaceProbes.data;
  for (let i = 0; i < p.length; i += 3)
    for (let k = 0; k < 3; k++) { lo[k] = Math.min(lo[k], p[i+k]); hi[k] = Math.max(hi[k], p[i+k]); }
  return { lo, hi };
})();
const centre = [0,1,2].map(k => (bounds.lo[k] + bounds.hi[k]) / 2);
const span = Math.max(1, Math.max(bounds.hi[0]-bounds.lo[0], Math.max(bounds.hi[1]-bounds.lo[1], bounds.hi[2]-bounds.lo[2])));

const cam = { target: centre.slice(), dist: span * 1.1, yaw: 0.7, pitch: 0.5 };

function eye() {
  const cp = Math.cos(cam.pitch);
  return [cam.target[0] + cam.dist * cp * Math.sin(cam.yaw),
          cam.target[1] + cam.dist * Math.sin(cam.pitch),
          cam.target[2] + cam.dist * cp * Math.cos(cam.yaw)];
}
function viewProj() {
  const e = eye(), up = [0, 1, 0];
  const f = norm(sub(cam.target, e)), s = norm(cross(f, up)), u = cross(s, f);
  const view = [ s[0], u[0], -f[0], 0,  s[1], u[1], -f[1], 0,  s[2], u[2], -f[2], 0,
                 -dot(s, e), -dot(u, e), dot(f, e), 1 ];
  const aspect = canvas.width / canvas.height;
  const near = Math.max(0.01, cam.dist * 0.001), far = cam.dist * 8 + span * 4;
  const t = 1 / Math.tan(0.5 * 1.05);
  const proj = [ t/aspect,0,0,0, 0,t,0,0, 0,0,(far+near)/(near-far),-1, 0,0,(2*far*near)/(near-far),0 ];
  return mul(proj, view);
}
const sub=(a,b)=>[a[0]-b[0],a[1]-b[1],a[2]-b[2]];
const dot=(a,b)=>a[0]*b[0]+a[1]*b[1]+a[2]*b[2];
const cross=(a,b)=>[a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0]];
const norm=a=>{const l=Math.hypot(a[0],a[1],a[2])||1;return[a[0]/l,a[1]/l,a[2]/l];};
function mul(a,b){const o=new Array(16);for(let c=0;c<4;c++)for(let r=0;r<4;r++){let v=0;for(let k=0;k<4;k++)v+=a[k*4+r]*b[c*4+k];o[c*4+r]=v;}return o;}

let dragging = null, lastX = 0, lastY = 0;
canvas.addEventListener('mousedown', e => {
  dragging = (e.button === 2 || e.shiftKey) ? 'pan' : 'orbit';
  lastX = e.clientX; lastY = e.clientY; canvas.classList.add('dragging');
});
addEventListener('mouseup', () => { dragging = null; canvas.classList.remove('dragging'); });
addEventListener('mousemove', e => {
  if (!dragging) return;
  const dx = e.clientX - lastX, dy = e.clientY - lastY;
  lastX = e.clientX; lastY = e.clientY;
  if (dragging === 'orbit') {
    cam.yaw -= dx * 0.006;
    cam.pitch = Math.max(-1.55, Math.min(1.55, cam.pitch + dy * 0.006));
  } else {
    const e0 = eye(), f = norm(sub(cam.target, e0)), s = norm(cross(f, [0,1,0])), u = cross(s, f);
    const k = cam.dist * 0.0016;
    for (let i = 0; i < 3; i++) cam.target[i] += (-s[i] * dx + u[i] * dy) * k;
  }
  draw();
});
canvas.addEventListener('contextmenu', e => e.preventDefault());
canvas.addEventListener('wheel', e => {
  e.preventDefault();
  cam.dist = Math.max(span * 0.002, cam.dist * Math.exp(e.deltaY * 0.0011));
  draw();
}, { passive: false });
addEventListener('keydown', e => {
  if (e.key === 'f' || e.key === 'F') { cam.target = centre.slice(); cam.dist = span * 1.1; draw(); }
});

// ---- picking ----------------------------------------------------------------------------
canvas.addEventListener('click', e => {
  if (!layers.inputProbes.on && !layers.surfaceProbes.on) return;
  const vp = viewProj(), rect = canvas.getBoundingClientRect();
  const mx = (e.clientX - rect.left) / rect.width * 2 - 1;
  const my = -((e.clientY - rect.top) / rect.height * 2 - 1);
  let best = -1, bestD = 0.02, bestSet = null;

  const test = (data, owner, setName) => {
    for (let i = 0; i < data.length; i += 3) {
      const x = data[i], y = data[i+1], z = data[i+2];
      const cw = vp[3]*x + vp[7]*y + vp[11]*z + vp[15];
      if (cw <= 0) continue;
      const cx = (vp[0]*x + vp[4]*y + vp[8]*z + vp[12]) / cw;
      const cy = (vp[1]*x + vp[5]*y + vp[9]*z + vp[13]) / cw;
      const d = Math.hypot(cx - mx, cy - my);
      if (d < bestD) { bestD = d; best = i / 3; bestSet = setName; }
    }
  };
  if (layers.inputProbes.on) test(inputProbes.data, inputProbes.owner, 'input');
  if (layers.surfaceProbes.on) test(surfaceProbes.data, surfaceProbes.owner, 'surface');
  if (best < 0) { document.getElementById('pick').style.display = 'none'; return; }
  showPick(bestSet, best);
});

function showPick(set, index) {
  const el = document.getElementById('pick');
  const owner = set === 'input' ? inputProbes.owner : surfaceProbes.owner;
  const data = set === 'input' ? inputProbes.data : surfaceProbes.data;
  const si = sliceOf(owner, index), local = index - owner[si][0];
  const s = DATA.slices[si];
  let rows = '';
  const row = (k, v) => { rows += '<tr><td>' + k + '</td><td>' + v + '</td></tr>'; };
  row('set', set === 'input' ? 'input probe' : 'surface probe');
  row('slice', si);
  row('index in slice', local);
  row('position', [0,1,2].map(k => data[index*3+k].toFixed(2)).join(', '));

  if (set === 'input') {
    row('atlas texel', s.inputTexel[local]);
    const a = [s.inputAlbedo[local*3], s.inputAlbedo[local*3+1], s.inputAlbedo[local*3+2]];
    row('albedo', a.join(', ') + ' <span class=""swatch"" style=""background:rgb(' + a.join(',') + ')""></span>');
    row('albedo luminance', (0.299*a[0] + 0.587*a[1] + 0.114*a[2]).toFixed(1));
    const n = [s.inputNormal[local*3], s.inputNormal[local*3+1], s.inputNormal[local*3+2]];
    row('normal (encoded)', n.join(', '));
  } else {
    row('probe slot', s.surfaceSlot[local]);
    let count = 0, sum = 0, list = [];
    for (let i = 0; i < s.infProbe.length; i++) {
      if (s.infProbe[i] !== local) continue;
      count++; sum += s.infWeight[i];
      if (list.length < 32) {
        const c = s.infCluster[i] * 3;
        const d = Math.hypot(s.clusterPos[c] - data[index*3], s.clusterPos[c+1] - data[index*3+1], s.clusterPos[c+2] - data[index*3+2]);
        list.push({ w: s.infWeight[i], d: d, atlas: s.clusterAtlas[s.infCluster[i]] });
      }
    }
    row('influences', count + ' of 32');
    row('weight sum', sum);
    row('weight mean', count ? (sum / count).toFixed(1) : '-');
    list.sort((a, b) => b.w - a.w);
    rows += '<tr><td colspan=2 style=""padding-top:8px;color:var(--dim)"">weight &middot; distance &middot; cluster</td></tr>';
    for (const it of list)
      rows += '<tr><td>' + it.w + '</td><td>' + it.d.toFixed(2) + ' m &middot; ' + it.atlas + '</td></tr>';
  }
  el.innerHTML = '<h1>Probe</h1><table>' + rows + '</table>';
  el.style.display = 'block';
}

// ---- UI ---------------------------------------------------------------------------------
(function buildUi() {
  const el = document.getElementById('ui');
  let stats = '';
  for (let i = 0; i < NS; i++) {
    const s = DATA.slices[i];
    stats += '<div class=""sub"">slice ' + i + ': ' + (s.inputPos.length/3) + ' input, ' +
             (s.surfacePos.length/3) + ' surface, ' + s.infProbe.length + ' influences, ' +
             (s.lightPos.length/3) + ' lights</div>';
  }
  let html = '<h1>' + DATA.level + '</h1><div class=""sub"">' + NS + ' slice' + (NS===1?'':'s') + '</div>' + stats;

  html += '<h2>Layers</h2>';
  for (const key in layers)
    html += '<label><input type=checkbox data-layer=""' + key + '""' + (layers[key].on ? ' checked' : '') +
            '> ' + layers[key].label + '<span class=""k"">' + layers[key].buf.count.toLocaleString() + '</span></label>';

  html += '<h2>Colour probes by</h2>';
  for (const m of ['albedo', 'luminance', 'normal', 'slice'])
    html += '<label><input type=radio name=cm value=""' + m + '""' + (m === colourMode ? ' checked' : '') + '> ' + m + '</label>';

  html += '<h2>Slice filter</h2><select id=sf><option value=-1>all slices</option>';
  for (let i = 0; i < NS; i++) html += '<option value=' + i + '>slice ' + i + '</option>';
  html += '</select>';

  html += '<h2>Influence weight &ge; <span id=wv>0</span></h2><input type=range id=wm min=0 max=255 value=0>';
  html += '<h2>Point size</h2><input type=range id=ps min=1 max=24 value=6>';
  html += '<h2>Opacity</h2><input type=range id=op min=5 max=100 value=95>';

  // Colour key. The heat ramp is used for influence weight, for surface probes' received weight
  // sum, and for albedo luminance, so it is worth spelling out which end is which.
  html += '<h2>Key</h2><div id=key></div>';
  el.innerHTML = html;

  const key = document.getElementById('key');
  let bar = '';
  for (let i = 0; i <= 40; i++) {
    const h = heat(i / 40);
    bar += '<span style=""display:inline-block;width:calc(100%/41);height:12px;vertical-align:top;' +
           'background:rgb(' + h.map(v => Math.round(v * 255)).join(',') + ')""></span>';
  }
  key.innerHTML =
    '<div class=""sub"">influence weight, and surface-probe received weight</div>' +
    '<div style=""white-space:nowrap;line-height:0"">' + bar + '</div>' +
    '<div class=""sub"" style=""display:flex;justify-content:space-between""><span>0 weak</span><span>255 strong</span></div>' +
    '<div class=""sub"" style=""margin-top:8px"">points</div>' +
    '<label style=""cursor:default""><span class=""swatch"" style=""background:rgb(179,217,255)""></span> input probe (emitter)</label>' +
    '<label style=""cursor:default""><span class=""swatch"" style=""background:rgb(255,242,204)""></span> surface light</label>' +
    '<label style=""cursor:default""><span class=""swatch"" style=""background:rgb(255,217,77)""></span> model origin: light</label>' +
    '<label style=""cursor:default""><span class=""swatch"" style=""background:rgb(255,115,51)""></span> model origin: emissive</label>' +
    '<label style=""cursor:default""><span class=""swatch"" style=""background:rgb(71,82,102)""></span> model origin: other</label>' +
    '<div class=""sub"" style=""margin-top:8px"">a probe dimmed to near black is filtered out by the ' +
    'slice or weight controls above, not unlit</div>';

  el.addEventListener('change', e => {
    const t = e.target;
    if (t.dataset.layer) layers[t.dataset.layer].on = t.checked;
    if (t.name === 'cm') { colourMode = t.value; recolour(); }
    if (t.id === 'sf') { sliceFilter = parseInt(t.value, 10); recolour(); }
    draw();
  });
  el.addEventListener('input', e => {
    const t = e.target;
    if (t.id === 'ps') pointSize = parseFloat(t.value);
    if (t.id === 'op') opacity = parseFloat(t.value) / 100;
    if (t.id === 'wm') { weightMin = parseFloat(t.value); document.getElementById('wv').textContent = t.value; recolour(); }
    draw();
  });
})();

let pointSize = 6, opacity = 0.95;

// ---- draw -------------------------------------------------------------------------------
function resize() {
  const dpr = Math.min(2, devicePixelRatio || 1);
  canvas.width = Math.max(1, Math.floor(innerWidth * dpr));
  canvas.height = Math.max(1, Math.floor(innerHeight * dpr));
  gl.viewport(0, 0, canvas.width, canvas.height);
}
function draw() {
  if (!canvas.width || !canvas.height) return;
  gl.clearColor(0.043, 0.05, 0.063, 1);
  gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);
  gl.enable(gl.DEPTH_TEST);
  gl.enable(gl.BLEND);
  gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);
  const vp = viewProj();

  // Lines first so points read on top of them.
  gl.useProgram(lineProg);
  gl.uniformMatrix4fv(gl.getUniformLocation(lineProg, 'uVP'), false, new Float32Array(vp));
  gl.uniform1f(gl.getUniformLocation(lineProg, 'uAlpha'), opacity * 0.4);
  for (const k in layers) {
    const L = layers[k];
    if (!L.on || L.kind !== 'line') continue;
    gl.bindVertexArray(L.buf.vao);
    gl.drawArrays(gl.LINES, 0, L.buf.count);
  }

  gl.useProgram(pointProg);
  gl.uniformMatrix4fv(gl.getUniformLocation(pointProg, 'uVP'), false, new Float32Array(vp));
  gl.uniform1f(gl.getUniformLocation(pointProg, 'uAlpha'), opacity);
  gl.uniform1f(gl.getUniformLocation(pointProg, 'uFade'), span * 0.06);
  for (const k in layers) {
    const L = layers[k];
    if (!L.on || L.kind !== 'point') continue;
    gl.uniform1f(gl.getUniformLocation(pointProg, 'uSize'), k === 'lights' ? pointSize * 1.8 : pointSize);
    gl.bindVertexArray(L.buf.vao);
    gl.drawArrays(gl.POINTS, 0, L.buf.count);
  }
  gl.bindVertexArray(null);
}

addEventListener('resize', () => { resize(); draw(); });
resize();
recolour();
draw();
</script>
</body>
</html>";
    }
}
#endif
