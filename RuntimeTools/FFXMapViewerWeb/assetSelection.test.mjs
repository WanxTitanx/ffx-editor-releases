import test from "node:test";
import assert from "node:assert/strict";
import { selectCatalogEntry } from "./assetSelection.js";

const scene = { area: "map/azit/azit03", status: "success", gltf: "/selected.gltf" };
test("uses the explicitly selected scene", () => {
  assert.equal(selectCatalogEntry([scene], "MAP/AZIT/AZIT03"), scene);
});
test("does not substitute azit00 for a missing scene", () => {
  assert.throws(() => selectCatalogEntry([{ ...scene, area: "map/azit/azit00" }], "aurora_empty/azit/azit03"), /Selected map.*unavailable/);
});
test("an empty catalog is an actionable error", () => {
  assert.throws(() => selectCatalogEntry([], null), /Export a scene/);
});
