// T-pose preview for MonEditor Models tab. Supports Model1 + Model2 fusion (base + variant mesh).
import * as THREE from "three";
import { OrbitControls } from "three/addons/controls/OrbitControls.js";
import { GLTFLoader } from "three/addons/loaders/GLTFLoader.js";

const params = new URLSearchParams(location.search);
const catalogUrl = new URL("./modelviewer-catalog.json", location.href);
const id1 = params.get("id1") || params.get("id");
const id2 = params.get("id2") || params.get("id1") || params.get("id");
const label1 = params.get("label1") || id1 || "Model1";
const label2 = params.get("label2") || id2 || "Model2";
const fusionSame = params.get("fusion") === "same";

const el = {
  viewport: document.getElementById("viewport"),
  status: document.getElementById("status"),
  title: document.getElementById("title"),
};

const scene = new THREE.Scene();
scene.background = new THREE.Color(0x222831);
const camera = new THREE.PerspectiveCamera(45, 1, 0.01, 5000);
camera.position.set(6, 4, 8);
const renderer = new THREE.WebGLRenderer({ antialias: true });
renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
renderer.outputColorSpace = THREE.SRGBColorSpace;
el.viewport.appendChild(renderer.domElement);
const controls = new OrbitControls(camera, renderer.domElement);
controls.enableDamping = true;
controls.dampingFactor = 0.08;
scene.add(new THREE.AmbientLight(0xffffff, 0.9));
const key = new THREE.DirectionalLight(0xffffff, 2.0);
key.position.set(5, 9, 6);
scene.add(key);
const fill = new THREE.DirectionalLight(0xbcd0ff, 0.8);
fill.position.set(-6, 3, -4);
scene.add(fill);
const grid = new THREE.GridHelper(20, 40, 0x4a5560, 0x333b44);
grid.position.y = -0.001;
scene.add(grid);

const loader = new GLTFLoader();
const root = new THREE.Group();
root.name = "fusion-root";
scene.add(root);

function setStatus(text) {
  el.status.textContent = text;
}

function resize() {
  const r = el.viewport.getBoundingClientRect();
  const w = Math.max(1, Math.floor(r.width));
  const h = Math.max(1, Math.floor(r.height));
  renderer.setSize(w, h, false);
  camera.aspect = w / h;
  camera.updateProjectionMatrix();
}

function fitCamera() {
  const box = new THREE.Box3().setFromObject(root);
  if (box.isEmpty() || !isFinite(box.min.x)) {
    camera.position.set(6, 4, 8);
    controls.target.set(0, 0, 0);
    return;
  }
  const c = box.getCenter(new THREE.Vector3());
  const s = box.getSize(new THREE.Vector3());
  const r = Math.max(s.x, s.y, s.z, 0.5);
  const d = r / (2 * Math.tan((camera.fov * Math.PI) / 360));
  camera.position.copy(c).add(new THREE.Vector3(d * 0.9, d * 0.6, d * 1.1));
  camera.near = Math.max(0.01, d / 100);
  camera.far = Math.max(100, d * 30);
  camera.updateProjectionMatrix();
  controls.target.copy(c);
  controls.update();
}

function clearRoot() {
  while (root.children.length > 0) {
    const child = root.children[0];
    root.remove(child);
    child.traverse((node) => {
      if (node.geometry) node.geometry.dispose();
      if (node.material) {
        const mats = Array.isArray(node.material) ? node.material : [node.material];
        mats.forEach((m) => m.dispose());
      }
    });
  }
}

async function catalogEntry(catalogId) {
  const res = await fetch(catalogUrl.href, { cache: "no-store" });
  if (!res.ok) throw new Error(`catalog ${res.status}`);
  const catalog = await res.json();
  return (catalog.entries || []).find((item) => item.id === catalogId);
}

async function loadGltfInto(parent, catalogId, battleLabel, role, offsetX = 0) {
  const entry = await catalogEntry(catalogId);
  if (!entry?.gltf) throw new Error(`catalog entry missing for ${catalogId}`);

  const url = new URL(entry.gltf, location.origin).href;
  const gltf = await loader.loadAsync(url);
  const node = gltf.scene;
  node.name = `${role}:${catalogId}`;
  node.position.x = offsetX;
  parent.add(node);
  return { entry, node };
}

async function resolveAndLoad() {
  clearRoot();
  if (!id1 && !id2) {
    setStatus("No model ids specified.");
    return;
  }

  setStatus("Loading fusion preview…");

  try {
    const sameAsset = fusionSame || id1 === id2;
    if (sameAsset) {
      const target = id2 || id1;
      await loadGltfInto(root, target, label2, "fusion");
      el.title.textContent = `Fusion · ${label1} + ${label2}`;
      setStatus(`Fusion: ${label1} (base) + ${label2} (variant) · shared mesh ${target} · T-pose`);
      fitCamera();
      return;
    }

    // Side-by-side when we have two distinct glTF assets.
    const [{ entry: e1 }, { entry: e2 }] = await Promise.all([
      loadGltfInto(root, id1, label1, "model1", -1.2),
      loadGltfInto(root, id2, label2, "model2", 1.2),
    ]);

    el.title.textContent = `Fusion · ${label1} + ${label2}`;
    setStatus(
      `Fusion: ${label1} → ${e1.id} (left/base) + ${label2} → ${e2.id} (right/variant) · T-pose`
    );
    fitCamera();
  } catch (err) {
    setStatus(`Load failed: ${err.message}`);
  }
}

function animate() {
  requestAnimationFrame(animate);
  controls.update();
  renderer.render(scene, camera);
}

window.addEventListener("resize", resize);
resize();
animate();
resolveAndLoad();
