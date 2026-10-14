// An explicit scene selection must never fall through to an unrelated catalog entry.
export function selectCatalogEntry(entries, requested) {
  if (requested) {
    const entry = entries.find((item) => item.area?.toLowerCase() === requested.toLowerCase());
    if (!entry) throw new Error(`Selected map '${requested}' is unavailable. Re-export this scene in Aurora and open it again.`);
    return entry;
  }
  const entry = entries.find((item) => item.status === "success" && item.gltf);
  if (!entry) throw new Error("No exported map is available. Export a scene in the editor first.");
  return entry;
}
