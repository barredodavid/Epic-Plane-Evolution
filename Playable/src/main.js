// Epic Plane Evolution playable ad: a port of the Unity prototype (Player.cs, AdSceneBuilder.cs) to three.js,
// shipped as one self-contained HTML file. World: the track runs along -Z, x = right, y = up.
// Logic works in "track z" (distance travelled, positive); meshes sit at -z.
import * as THREE from 'three';
import { GLTFLoader } from 'three/examples/jsm/loaders/GLTFLoader.js';

const A = window.ASSETS; // { models: {name: base64}, audio: {name: base64} }, inlined by build.mjs
const STORE_URL = 'https://davidbarredo.netlify.app/en.html#planes';
const AUTO = new URLSearchParams(location.search).get('auto');

// ---------- Level (same numbers as the Unity scene) ----------
const RUN_SPEED = 10, LANE = 4, HOVER = 1.8, SPACING = 2.3, SQUAD_SCALE = 0.65, MAX_PLANES = 12, BONUS = 0.25;
const RAMP_Z = 150, RAMP_LEN = 15, RAMP_H = 3, GROUND_Y = 0.3;
const START_YEAR = 200;
// rot: turns each model so its nose points along -Z (checked with ?test).
const ERAS = [
  { name: 'Paper Plane', year: 0, model: 'PaperPlane', size: 3.2, rot: [0, Math.PI, 0], dist: 12, secs: 1.3, crashes: true },
  { name: 'Biplane', year: 1900, model: 'Biplane', size: 3.4, rot: [0, -Math.PI / 2, 0], dist: 360, secs: 2.5 },
  { name: 'Propeller', year: 1930, model: 'Propeller', size: 3.4, rot: [0, 0, 0], dist: 900, secs: 3 },
  { name: 'Jet', year: 1950, model: 'Jet', size: 3.4, rot: [0, Math.PI, 0], dist: 3400, secs: 3.2 },
  { name: 'Rocket', year: 1969, model: 'Rocket', size: 3, rot: [-Math.PI / 2, 0, 0], secs: 3.5, space: true },
];
// [z, left op, left value, right op, right value]. Ops: '+' years, '/' years, 'x' planes.
const GATES = [
  [20, '+', -100, '+', 1700],
  [45, 'x', 3, '/', 2],
  [70, '+', -500, '+', 35],
  [95, '+', 20, '/', 3],
  [120, '+', -900, 'x', 2],
  [140, '+', 14, '/', 2],
];
const isGood = (op, v) => op === '+' ? v >= 0 : op === '/' ? v <= 1 : v >= 1;
const gateText = (op, v) => op === '+' ? `${v >= 0 ? '+' : ''}${v}\nYEARS` : op === '/' ? `÷${v}\nYEARS` : `x${v}\nPLANES`;
const eraFor = year => ERAS.reduce((best, e, i) => year >= e.year ? i : best, 0);
const clamp = (v, a, b) => Math.min(b, Math.max(a, v));
const invLerp = (a, b, v) => clamp((v - a) / (b - a), 0, 1);
const lerp = (a, b, t) => a + (b - a) * t;

// ---------- Renderer, scene, camera ----------
const renderer = new THREE.WebGLRenderer({ antialias: true });
renderer.setPixelRatio(Math.min(devicePixelRatio, 2));
document.body.prepend(renderer.domElement);
const scene = new THREE.Scene();
const SKY = new THREE.Color(0.62, 0.8, 1), SPACE = new THREE.Color(0.01, 0.01, 0.04);
scene.background = SKY.clone();
scene.fog = new THREE.Fog(new THREE.Color(0.78, 0.88, 1), 80, 600);
const camera = new THREE.PerspectiveCamera(60, 1, 0.3, 5000);
scene.add(new THREE.HemisphereLight(0xd8e6ff, 0x6f7f5f, 1.1));
const sun = new THREE.DirectionalLight(0xfff2d9, 1.6);
sun.position.set(-3, 6, 4);
scene.add(sun);

function resize() {
  renderer.setSize(innerWidth, innerHeight);
  camera.aspect = innerWidth / innerHeight;
  // 60° vertical on a 9:16 screen (the Unity framing); taller phones keep that width instead of cropping it.
  const halfWidth = Math.tan(THREE.MathUtils.degToRad(30)) * 9 / 16;
  camera.fov = camera.aspect < 9 / 16 ? THREE.MathUtils.radToDeg(2 * Math.atan(halfWidth / camera.aspect)) : 60;
  camera.updateProjectionMatrix();
}
addEventListener('resize', resize);
resize();

const mat = color => new THREE.MeshLambertMaterial({ color });
function block(x, y, z, sx, sy, sz, color) {
  const m = new THREE.Mesh(new THREE.BoxGeometry(sx, sy, sz), mat(color));
  m.position.set(x, y, -z);
  scene.add(m);
  return m;
}

// Text drawn on a canvas, chunky with a dark outline (the Unity TextMesh look). Redrawn once the font has
// loaded: a canvas drawn earlier silently falls back to a default font.
const redraws = [];
function textTexture(text, w, h, px) {
  const c = document.createElement('canvas');
  c.width = w; c.height = h;
  const t = new THREE.CanvasTexture(c);
  t.colorSpace = THREE.SRGBColorSpace;
  const draw = () => {
    const g = c.getContext('2d');
    g.clearRect(0, 0, w, h);
    g.font = `${px}px Lilita`;
    g.textAlign = 'center';
    g.textBaseline = 'middle';
    g.lineJoin = 'round';
    const lines = text.split('\n');
    lines.forEach((line, i) => {
      const y = h / 2 + (i - (lines.length - 1) / 2) * px * 1.05;
      g.lineWidth = px * 0.22;
      g.strokeStyle = '#0d1438';
      g.strokeText(line, w / 2, y);
      g.fillStyle = '#fff';
      g.fillText(line, w / 2, y);
    });
    t.needsUpdate = true;
  };
  draw();
  redraws.push(draw);
  return t;
}

// ---------- Track ----------
block(0, -0.55, 5800, 600, 1, 12000, 0x5ca34d);           // grass
block(0, -0.05, 75, 10, 0.1, 160, 0x599ef2);               // runway
for (let z = 0; z < RAMP_Z; z += 10) block(0, 0.01, z, 10, 0.02, 3, 0x3366cc);
for (const x of [-5.25, 5.25]) block(x, 0.3, 75, 0.5, 0.6, 160, 0xffd933);
const rampLen = Math.hypot(RAMP_LEN, RAMP_H);
block(0, RAMP_H / 2 - 0.15, RAMP_Z + RAMP_LEN / 2, 10, 0.3, rampLen, 0xff8c33).rotation.x = Math.atan2(RAMP_H, RAMP_LEN);

// Distance signs after the ramp: they fly past during the flight and sell the speed.
const signStart = RAMP_Z + RAMP_LEN;
for (let m = 100; m <= 4000; m += m < 1000 ? 100 : 500) {
  block(9, 2, signStart + m, 0.3, 4, 0.3, 0xffffff);
  const s = new THREE.Sprite(new THREE.SpriteMaterial({ map: textTexture(`${m} m`, 256, 96, 64), fog: true }));
  s.scale.set(4, 1.5, 1);
  s.position.set(9, 4.8, -(signStart + m));
  scene.add(s);
}

// Low-poly trees and clouds, instanced (a handful of draw calls on phones).
let seed = 7;
const rnd = (a = 0, b = 1) => a + (b - a) * ((seed = (seed * 16807) % 2147483647) / 2147483647);
const side = () => (rnd() < 0.5 ? -1 : 1);
const dummy = new THREE.Object3D();
function instanced(geometry, material, count, place) {
  const mesh = new THREE.InstancedMesh(geometry, material, count);
  for (let i = 0; i < count; i++) { place(i); dummy.updateMatrix(); mesh.setMatrixAt(i, dummy.matrix); }
  scene.add(mesh);
  return mesh;
}
const TREES = 180;
const treeSpots = Array.from({ length: TREES }, (_, i) => ({
  x: rnd(8, 60) * side(), z: i < 90 ? rnd(-20, 220) : rnd(220, 4200), s: rnd(3, 6), pine: rnd() < 0.35,
}));
instanced(new THREE.CylinderGeometry(0.12, 0.18, 1, 6), mat(0x7a5233), TREES, i => {
  const t = treeSpots[i];
  dummy.position.set(t.x, t.s * 0.25, -t.z); dummy.scale.set(t.s, t.s * 0.5, t.s); dummy.rotation.set(0, 0, 0);
});
instanced(new THREE.IcosahedronGeometry(0.5, 0), mat(0x3f8f3a), TREES, i => {
  const t = treeSpots[i];
  dummy.position.set(t.x, t.s * (t.pine ? 0.9 : 0.75), -t.z);
  dummy.scale.set(t.s * (t.pine ? 0.7 : 1), t.s * (t.pine ? 1.4 : 0.9), t.s * (t.pine ? 0.7 : 1));
  dummy.rotation.set(0, rnd(0, 6), 0);
});
const cloudMat = new THREE.MeshLambertMaterial({ color: 0xffffff, emissive: 0x59595f });
const cloudSpots = [];
for (let i = 0; i < 45; i++) cloudSpots.push([rnd(15, 70) * side(), rnd(12, 45), rnd(0, 4000), rnd(4, 9)]);
for (let i = 0; i < 30; i++) { // around the rocket's climb, height = (d / 1500)^2 * 1200
  const d = rnd(200, 1500);
  cloudSpots.push([rnd(-35, 35), (d / 1500) ** 2 * 1200 + 3 + rnd(-15, 15), signStart + d, rnd(6, 12)]);
}
instanced(new THREE.SphereGeometry(1, 10, 8), cloudMat, cloudSpots.length * 4, i => {
  const [x, y, z, s] = cloudSpots[i >> 2], k = i & 3;
  dummy.position.set(x + (k - 1.5) * s * 0.45, y + rnd(-0.1, 0.25) * s, -z + rnd(-0.2, 0.2) * s);
  dummy.scale.set(1, 0.7, 0.8).multiplyScalar(s * rnd(0.7, 1.1) * 0.5);
  dummy.rotation.set(0, 0, 0);
});
const starGeo = new THREE.BufferGeometry();
const starPos = [];
for (let i = 0; i < 400; i++) {
  const d = new THREE.Vector3(rnd(-1, 1), rnd(0, 1), rnd(-1, 1)).normalize().multiplyScalar(rnd(900, 1600));
  starPos.push(d.x, d.y, d.z);
}
starGeo.setAttribute('position', new THREE.Float32BufferAttribute(starPos, 3));
const stars = new THREE.Points(starGeo, new THREE.PointsMaterial({ color: 0xffffff, size: 6, fog: false }));
stars.visible = false;
scene.add(stars);

// ---------- Gates and coins ----------
const gateMat = good => new THREE.MeshBasicMaterial({ color: good ? 0x338cff : 0xff4d59, transparent: true, opacity: 0.5, depthWrite: false });
const gates = GATES.map(([z, lop, lv, rop, rv]) => {
  const group = new THREE.Group();
  group.position.z = -z;
  for (const [x, op, v] of [[-2.5, lop, lv], [2.5, rop, rv]]) {
    const panel = new THREE.Mesh(new THREE.PlaneGeometry(4.8, 3), gateMat(isGood(op, v)));
    panel.position.set(x, 1.5, 0);
    const label = new THREE.Mesh(new THREE.PlaneGeometry(4.8, 3), new THREE.MeshBasicMaterial({ map: textTexture(gateText(op, v), 320, 200, 74), transparent: true }));
    label.position.set(x, 1.6, 0.05);
    group.add(panel, label);
    for (const px of [-2.45, 2.45]) {
      const post = new THREE.Mesh(new THREE.BoxGeometry(0.18, 3.4, 0.18), mat(0x26337f));
      post.position.set(x + px, 1.7, 0);
      group.add(post);
    }
  }
  scene.add(group);
  return { z, left: [lop, lv], right: [rop, rv], group, used: false };
});

const coinGeo = new THREE.CylinderGeometry(0.4, 0.4, 0.06, 20).rotateX(Math.PI / 2);
const coinMat = new THREE.MeshLambertMaterial({ color: 0xffc726, emissive: 0x594004 });
const coins = [];
for (const start of [26, 51, 76, 101, 125]) for (const x of [-2.5, 2.5]) for (let i = 0; i < 4; i++) {
  const m = new THREE.Mesh(coinGeo, coinMat);
  m.position.set(x, 1, -(start + i * 3));
  scene.add(m);
  coins.push({ mesh: m, z: start + i * 3, x, pop: -1 });
}

// ---------- Planes ----------
const templates = {};
function loadModels() {
  const loader = new GLTFLoader();
  return Promise.all(ERAS.map(era => new Promise((ok, fail) => {
    const bin = Uint8Array.from(atob(A.models[era.model]), c => c.charCodeAt(0));
    loader.parse(bin.buffer, '', gltf => {
      const model = gltf.scene;
      model.rotation.set(...era.rot);
      const root = new THREE.Group();
      root.add(model);
      const box = new THREE.Box3().setFromObject(root);
      const size = box.getSize(new THREE.Vector3());
      const k = era.size / Math.max(size.x, size.y, size.z);
      model.scale.multiplyScalar(k);
      model.position.copy(box.getCenter(new THREE.Vector3()).multiplyScalar(-k));
      templates[era.model] = root;
      ok();
    }, fail);
  })));
}

// ---------- Sound (Web Audio, unlocked by the first touch) ----------
let audio;
const buffers = {};
function unlockAudio() {
  if (audio) return;
  audio = new (window.AudioContext || window.webkitAudioContext)();
  for (const [name, b64] of Object.entries(A.audio)) {
    const bin = Uint8Array.from(atob(b64), c => c.charCodeAt(0));
    audio.decodeAudioData(bin.buffer).then(b => { buffers[name] = b; }, () => {});
  }
}
function play(name, volume = 1, pitch = 1) {
  if (!audio || !buffers[name]) return;
  const src = audio.createBufferSource(), gain = audio.createGain();
  src.buffer = buffers[name];
  src.playbackRate.value = pitch;
  gain.gain.value = volume;
  src.connect(gain).connect(audio.destination);
  src.start();
}

// ---------- HUD ----------
const $ = id => document.getElementById(id);
const hud = { year: $('year'), popup: $('popup'), tutorial: $('tutorial'), end: $('end') };
function setText(el, text) {
  el.innerHTML = text.replace(/\n/g, '<br>');
  const longest = Math.max(...text.split('\n').map(l => l.length));
  el.style.fontSize = `calc(${el.dataset.size} * ${Math.min(1, 9.5 / Math.max(1, longest))})`; // long lines shrink to fit
}
let popupTimer = 0, timeScale = 1;
function popup(text, color, slowMo, seconds) {
  setText(hud.popup, text);
  hud.popup.style.color = color;
  hud.popup.classList.remove('pop'); void hud.popup.offsetWidth; hud.popup.classList.add('pop');
  popupTimer = seconds;
  timeScale = slowMo;
}

// ---------- Game state ----------
const player = new THREE.Group();
const squadron = new THREE.Group();
player.add(squadron);
scene.add(player);
let year = START_YEAR, planes = 1, eraIndex = -1, phase = 'wait';
let x = 0, y = HOVER, z = 0, targetX = 0, steerVel = 0, punch = 0, homes = [];
let flightTime = 0, flightDist = 0, flightHeight = 0, finished = false, takeoff = null, crashVel = null, tumble = [];
let coinPitch = 1, lastCoin = -10, clock = 0, doneAt = 0;
const era = () => ERAS[eraIndex];

const cam = { offset: new THREE.Vector3(0, 5, 6.5), runOffset: null, zoom: 1, curZoom: 1, look: 0, snap: false, freeze: false, shake: 0, base: new THREE.Vector3() };
const SPACE_OFFSET = new THREE.Vector3(0, -6, 6);

function buildSquadron() {
  squadron.clear();
  const count = Math.min(planes, MAX_PLANES), rows = Math.ceil(count / 2);
  homes = [];
  for (let i = 0; i < count; i++) {
    const row = Math.floor(i / 2), alone = count - row * 2 === 1;
    const plane = templates[era().model].clone();
    plane.scale.setScalar(planes > 1 ? SQUAD_SCALE : 1);
    const home = new THREE.Vector3(alone ? 0 : (i % 2 ? 0.5 : -0.5) * SPACING, 0, -((rows - 1) * 0.5 - row) * SPACING * 1.05);
    homes.push(home);
    plane.position.copy(home);
    squadron.add(plane);
  }
  cam.zoom = planes > 1 ? 1.2 + 0.15 * (rows - 1) : 1;
}

function refresh(announce, planesChanged) {
  setText(hud.year, year < 0 ? `${-year} BC` : `YEAR ${year}`);
  const index = eraFor(year), newEra = index !== eraIndex;
  eraIndex = index;
  if (newEra || planesChanged) buildSquadron();
  if (!announce) return;
  const msg = newEra ? era().name.toUpperCase() + '!' : planesChanged ? `x${planes} PLANES!\n+${Math.round(BONUS * (planes - 1) * 100)}% DISTANCE` : null;
  if (!msg) return;
  punch = 1;
  cam.shake = Math.max(cam.shake, 0.3);
  popup(msg, '#ffe14d', 0.4, 0.8);
}

function applyGate([op, v]) {
  const planesBefore = planes, eraBefore = eraIndex;
  if (op === '+') year += v;
  else if (op === '/') year = Math.trunc(year / Math.max(1, v));
  else planes = Math.max(1, planes * v);
  refresh(true, planes !== planesBefore);
  if (!isGood(op, v)) play('BadGate');
  else if (eraIndex > eraBefore) play('Evolve');
  else if (planes > planesBefore) play('PlanesUp');
  else play('Evolve', 0.5, 1.3);
}

// ---------- Input: drag anywhere to steer ----------
let dragX = null, dragTarget = 0;
addEventListener('pointerdown', e => {
  unlockAudio();
  if (phase === 'wait') { phase = 'run'; hud.tutorial.classList.add('hide'); }
  dragX = e.clientX; dragTarget = targetX;
});
addEventListener('pointermove', e => {
  if (dragX === null || phase !== 'run') return;
  targetX = clamp(dragTarget + (e.clientX - dragX) / innerWidth * LANE * 2.6, -LANE, LANE);
});
addEventListener('pointerup', () => { dragX = null; });
addEventListener('pointercancel', () => { dragX = null; });

// Unity's Mathf.SmoothDamp: eased like a finger dragging, not a constant-speed slide.
function smoothDamp(cur, target, maxSpeed, smoothTime, dt) {
  const omega = 2 / smoothTime, k = omega * dt, exp = 1 / (1 + k + 0.48 * k * k + 0.235 * k * k * k);
  const orig = target, maxChange = maxSpeed * smoothTime;
  const change = clamp(cur - target, -maxChange, maxChange);
  target = cur - change;
  const temp = (steerVel + omega * change) * dt;
  steerVel = (steerVel - omega * temp) * exp;
  let out = target + (change + temp) * exp;
  if (orig - cur > 0 === out > orig) { out = orig; steerVel = (out - orig) / dt; }
  return out;
}

// ---------- Phases (ported from Player.cs) ----------
function run(dt) {
  z += RUN_SPEED * dt;
  const ramp = invLerp(RAMP_Z, RAMP_Z + RAMP_LEN, z);
  if (ramp > 0) targetX = 0; // line up with the ramp
  else if (AUTO) { // ?auto (testing): steer for the good gate, or the bad one with ?auto=bad
    const next = gates.find(g => !g.used);
    if (next) targetX = isGood(...next.left) === (AUTO !== 'bad') ? -2.5 : 2.5;
  }
  x = smoothDamp(x, targetX, 18, 0.15, Math.max(dt, 1e-4));
  y = HOVER + ramp * RAMP_H;
  const pitch = ramp > 0 ? Math.atan2(RAMP_H, RAMP_LEN) : 0;
  const roll = THREE.MathUtils.degToRad(clamp(-steerVel * 3, -30, 30));
  squadron.rotation.x += clamp(pitch - squadron.rotation.x, -2.6 * dt, 2.6 * dt);
  squadron.rotation.z += clamp(roll - squadron.rotation.z, -2.6 * dt, 2.6 * dt);

  for (const g of gates) {
    if (g.used || z < g.z) continue;
    g.used = true;
    g.group.visible = false;
    applyGate(x < 0 ? g.left : g.right);
  }
  for (const c of coins) {
    if (c.pop >= 0 || Math.abs(c.z - z) >= 1.5 || Math.abs(c.x - x) >= 2.2) continue;
    c.pop = 0;
    coinPitch = clock - lastCoin > 1 ? 1 : Math.min(coinPitch + 0.06, 1.6);
    lastCoin = clock;
    play('Coin', 0.6, coinPitch);
  }
  wobble();
  if (ramp >= 1) takeOff();
}

function wobble() {
  const amount = era().crashes ? 1.6 : 1;
  squadron.children.forEach((p, i) => {
    const t = clock * 2.2 + i * 1.7;
    p.position.copy(homes[i]).add(new THREE.Vector3(Math.sin(t * 0.7) * 0.15, Math.sin(t) * 0.18, 0).multiplyScalar(amount));
    p.rotation.set(amount * Math.sin(t * 1.3) * 0.07, 0, amount * Math.sin(t * 0.9) * 0.16);
  });
}

function takeOff() {
  phase = 'flight';
  takeoff = new THREE.Vector3(x, y, z);
  flightTime = 0;
  flightDist = (era().dist || 0) * (1 + BONUS * (planes - 1));
  flightHeight = Math.min(flightDist * 0.15, 80);
  cam.snap = true;
  cam.runOffset = cam.offset.clone();
  squadron.children.forEach((p, i) => { p.position.copy(homes[i]); p.rotation.set(0, 0, 0); });
  squadron.rotation.set(0, 0, 0);
  popup('TAKE OFF!', '#ffe14d', 0.35, 0.7);
  play('TakeOff');
  if (era().space) play('Rocket', 0.35);
}

const prev = new THREE.Vector3();
function fly(dt) {
  const e = era();
  flightTime += dt;
  let s = flightTime / e.secs, dist, height;
  prev.set(x, y, z);
  if (e.space) {
    dist = s * 1500;
    height = s * s * 1200; // bends up into the sky, never comes down
    goToSpace(s);
  } else {
    s = Math.min(s, 1);
    dist = s * flightDist;
    height = e.crashes
      ? 1.2 * 4 * s * (1 - s) - s * s * (takeoff.y - GROUND_Y) // small hop, then a nose-dive
      : flightHeight * 4 * s * (1 - s) - s * (takeoff.y - GROUND_Y); // arc, lands on the grass
  }
  x = takeoff.x; y = takeoff.y + height; z = takeoff.z + dist;
  const vel = new THREE.Vector3(x, y, z).sub(prev);
  if (vel.lengthSq() > 1e-6) squadron.lookAt(squadron.getWorldPosition(new THREE.Vector3()).add(new THREE.Vector3(-vel.x, -vel.y, vel.z)));
  trail(dt);
  if (finished) return;
  setText(hud.year, `${Math.round(Math.min(s, 1) * (e.space ? 1500 : flightDist)).toLocaleString('en')} m`);
  if (s < 1) return;
  finished = true;
  if (e.crashes) return startCrash(vel.divideScalar(Math.max(dt, 1e-4)));
  if (!e.space) {
    impact('Bounce');
    end('CAN YOU DO\nBETTER?');
    play('Fail');
    return;
  }
  cam.shake = Math.max(cam.shake, 0.6);
  popup('TO SPACE!', '#ffe14d', 1, 999);
  play('Win');
}

function startCrash(v) {
  phase = 'crash';
  crashVel = v;
  tumble = squadron.children.map(() => new THREE.Vector3(rnd(-1, 1), rnd(-1, 1), rnd(-1, 1)).normalize());
  impact('Crash');
  popup('CRASH!', '#ff4d4d', 0.3, 0.6);
}

function crash(dt) {
  crashVel.y -= 25 * dt;
  x += crashVel.x * dt; y += crashVel.y * dt; z += crashVel.z * dt;
  if (y <= GROUND_Y) {
    y = GROUND_Y;
    if (crashVel.y < -2) { crashVel.y *= -0.45; crashVel.z *= 0.6; impact('Bounce'); } // bounce, losing energy
    else { crashVel.y = 0; crashVel.z = Math.max(0, crashVel.z - 10 * dt); } // slide to a stop
  }
  trail(dt, false); // the flight's trail fades out
  const speed = crashVel.length();
  squadron.children.forEach((p, i) => p.rotateOnWorldAxis(tumble[i], speed * 1.4 * dt));
  setText(hud.year, `${Math.round(z - takeoff.z)} m`);
  if (speed > 0.05 || y > GROUND_Y) return;
  end('CAN YOU DO\nBETTER?');
  play('Fail');
}

function impact(sound) {
  play(sound, 0.8);
  cam.shake = Math.max(cam.shake, 0.25);
}

// Rocket ending: the camera swings under the squadron to look up, the sky fades to black, stars appear.
function goToSpace(s) {
  const swing = THREE.MathUtils.smoothstep(s * 1.5, 0, 1);
  cam.offset.lerpVectors(cam.runOffset, SPACE_OFFSET, swing);
  cam.look = swing * 12;
  if (s >= 1.4) cam.freeze = true;
  if (s >= 3 && phase !== 'done') end(null);
  const dark = invLerp(0.3, 1.1, s);
  scene.background.lerpColors(SKY, SPACE, dark);
  scene.fog.far = dark < 0.3 ? 600 : 1e6;
  stars.visible = dark > 0.6;
  if (!cam.freeze) stars.position.set(x, y, -z); // the star field travels with the rockets
}

// Trail puffs behind each plane during the flight.
const TRAIL = 240;
const trailMesh = new THREE.InstancedMesh(new THREE.SphereGeometry(1, 6, 4), new THREE.MeshBasicMaterial({ color: 0xffffff, transparent: true, opacity: 0.7 }), TRAIL);
trailMesh.frustumCulled = false;
trailMesh.visible = false; // instances start as unit spheres at the origin: hidden until the flight
scene.add(trailMesh);
const puffs = [];
let puffNext = 0;
function trail(dt, emit = true) {
  const color = era().space ? 0xff9933 : 0xffffff;
  trailMesh.material.color.setHex(color);
  if (emit) for (const p of squadron.children) {
    puffs[puffNext] = { pos: p.getWorldPosition(new THREE.Vector3()), age: 0 };
    puffNext = (puffNext + 1) % TRAIL;
  }
  for (let i = 0; i < TRAIL; i++) {
    const p = puffs[i];
    if (p) p.age += dt;
    const k = p ? Math.max(0, 1 - p.age / 0.6) * 0.3 : 0;
    dummy.position.copy(p ? p.pos : new THREE.Vector3());
    dummy.scale.setScalar(k);
    dummy.rotation.set(0, 0, 0);
    dummy.updateMatrix();
    trailMesh.setMatrixAt(i, dummy.matrix);
  }
  trailMesh.instanceMatrix.needsUpdate = true;
  trailMesh.visible = true;
}

// ---------- End card ----------
function end(message) {
  phase = 'done';
  doneAt = clock;
  if (message) popup(message, '#ffe14d', 1, 999);
}
function showEndCard() {
  if (!hud.end.classList.contains('hide')) return;
  hud.end.classList.remove('hide');
  hud.year.classList.add('hide');
  hud.popup.classList.add('hide');
  $('title').innerHTML = era().space ? 'YOU REACHED<br>SPACE!' : 'CAN YOU DO<br>BETTER?';
  $('result').textContent = era().space ? `${planes} ROCKETS · 1,500 m` : `${hud.year.textContent} · ${era().name.toUpperCase()}`;
}
function cta() {
  if (window.mraid && mraid.open) mraid.open(STORE_URL);
  else if (window.FbPlayableAd) FbPlayableAd.onCTAClick();
  else window.open(STORE_URL, '_blank');
}
$('cta').addEventListener('click', e => { e.stopPropagation(); cta(); });
$('replay').addEventListener('click', e => { e.stopPropagation(); location.reload(); });

// ---------- Camera rig (CameraRig.cs) ----------
const lookAt = new THREE.Vector3();
function updateCamera(rdt) {
  if (cam.freeze) return;
  const ease = 1 - Math.exp(-6 * rdt);
  cam.curZoom = lerp(cam.curZoom, cam.zoom, ease * 0.5);
  const wanted = new THREE.Vector3(x, y - 1, -z).add(cam.offset.clone().multiplyScalar(cam.curZoom));
  cam.base.copy(cam.snap ? wanted : cam.base.lerp(wanted, ease));
  cam.shake = Math.max(0, cam.shake - rdt * 1.5);
  camera.position.copy(cam.base).add(new THREE.Vector3(rnd(-1, 1), rnd(-1, 1), rnd(-1, 1)).multiplyScalar(cam.shake));
  camera.lookAt(lookAt.set(x, y + cam.look, -(z + 6)));
}

// ---------- Main loop ----------
let last = performance.now();
function frame(now) {
  const rdt = Math.min(0.05, (now - last) / 1000);
  last = now;
  if (popupTimer > 0 && (popupTimer -= rdt) <= 0) { hud.popup.innerHTML = ''; timeScale = 1; }
  const dt = phase === 'wait' ? 0 : rdt * timeScale;
  clock += dt;
  punch = Math.max(0, punch - rdt * 3);
  squadron.scale.setScalar(1 + 0.4 * punch);
  if (phase === 'run') run(dt);
  else if (phase === 'flight') fly(dt);
  else if (phase === 'crash') crash(dt);
  else if (phase === 'done' && era().space && !cam.freeze) fly(dt);
  if (phase === 'wait') wobble();
  player.position.set(x, y, -z);
  for (const c of coins) {
    c.mesh.rotation.y += 3.1 * dt;
    if (c.pop < 0) continue;
    c.pop += dt;
    c.mesh.scale.setScalar(Math.max(0, 1 - c.pop * 4));
    c.mesh.position.y += 6 * dt;
  }
  updateCamera(rdt);
  if (phase === 'done' && clock - doneAt > 1.2) showEndCard();
  renderer.render(scene, camera);
  requestAnimationFrame(frame);
}

// ---------- Start ----------
const fontReady = document.fonts ? document.fonts.load('64px Lilita') : Promise.resolve();
Promise.all([loadModels(), fontReady]).then(() => {
  redraws.forEach(draw => draw());
  if (location.search.includes('test')) return testView();
  refresh(false, false);
  cam.base.set(0, HOVER - 1, 0).add(cam.offset);
  window.state = () => ({ phase, year, planes, era: era().name, z: Math.round(z), card: !hud.end.classList.contains('hide') });
  requestAnimationFrame(frame);
}).catch(e => console.error('Playable failed to start', e));

// ?test: every model side by side, seen from above, to check the noses all point up the screen (-Z).
function testView() {
  ERAS.forEach((e, i) => {
    const p = templates[e.model].clone();
    p.position.set((i - 2) * 4, 2, -10);
    scene.add(p);
  });
  camera.position.set(0, 25, -10);
  camera.lookAt(0, 0, -10.001);
  hud.tutorial.classList.add('hide');
  renderer.setAnimationLoop(() => renderer.render(scene, camera));
}
