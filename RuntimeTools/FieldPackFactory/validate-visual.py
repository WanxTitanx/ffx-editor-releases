# FieldPack Visual Validator — SSIM/diff pipeline v2.176.0.0
# Compares rendered screenshots against golden references.
# Produces per-field score JSON + thumbnails.
#
# Usage:
#   python validate-visual.py --render-dir work/field_pack/validation --golden-dir work/field_pack/golden
#   python validate-visual.py --render-dir work/field_pack/validation --auto-golden  (use first render as golden)

import argparse
import json
import os
import sys
from pathlib import Path

try:
    from PIL import Image, ImageChops
    import struct
except ImportError:
    print("Pillow required. Install: pip install Pillow")
    sys.exit(1)


def ssim(img1, img2):
    """Simplified SSIM-like comparison. Returns 0..1 where 1 = identical."""
    if img1.size != img2.size:
        # Resize to match
        img2 = img2.resize(img1.size, Image.LANCZOS)

    # Convert to grayscale for luminance comparison
    gray1 = img1.convert("L")
    gray2 = img2.convert("L")

    # Mean, variance, covariance
    import math

    def _stats(im):
        pixels = list(im.getdata())
        n = len(pixels)
        mean = sum(pixels) / n
        var = sum((p - mean) ** 2 for p in pixels) / n
        return mean, var, pixels

    m1, v1, p1 = _stats(gray1)
    m2, v2, p2 = _stats(gray2)

    cov = sum((p1[i] - m1) * (p2[i] - m2) for i in range(len(p1))) / len(p1)

    c1 = (0.01 * 255) ** 2
    c2 = (0.03 * 255) ** 2

    numerator = (2 * m1 * m2 + c1) * (2 * cov + c2)
    denominator = (m1 ** 2 + m2 ** 2 + c1) * (v1 + v2 + c2)

    if denominator == 0:
        return 1.0

    return numerator / denominator


def mse(img1, img2):
    """Mean Squared Error between two images."""
    if img1.size != img2.size:
        img2 = img2.resize(img1.size, Image.LANCZOS)

    # Convert to RGB
    rgb1 = img1.convert("RGB")
    rgb2 = img2.convert("RGB")

    pixels1 = list(rgb1.getdata())
    pixels2 = list(rgb2.getdata())

    err = sum(
        (r1 - r2) ** 2 + (g1 - g2) ** 2 + (b1 - b2) ** 2
        for (r1, g1, b1), (r2, g2, b2) in zip(pixels1, pixels2)
    )

    return err / (len(pixels1) * 3)


def diff_image(img1, img2, output_path):
    """Generate a diff highlight image."""
    if img1.size != img2.size:
        img2 = img2.resize(img1.size, Image.LANCZOS)

    rgb1 = img1.convert("RGB")
    rgb2 = img2.convert("RGB")

    diff = ImageChops.difference(rgb1, rgb2)
    # Enhance contrast for visibility
    from PIL import ImageEnhance
    diff = ImageEnhance.Contrast(diff).enhance(3.0)
    diff.save(output_path)
    return output_path


def validate_field(field_dir, golden_dir, out_dir, auto_golden=False):
    """Validate all screenshots in a field directory against golden references."""
    field_name = Path(field_dir).name
    screenshots = sorted(Path(field_dir).glob("*.png"))

    if not screenshots:
        return {"field": field_name, "status": "no_screenshots", "score": 0}

    results = []
    scores = []

    for shot in screenshots:
        shot_name = shot.name

        # Golden path
        golden_path = Path(golden_dir) / field_name / shot_name if golden_dir else None

        if golden_path and golden_path.exists():
            # Compare against golden
            img_render = Image.open(shot)
            img_golden = Image.open(golden_path)

            s = ssim(img_render, img_golden)
            err = mse(img_render, img_golden)

            # Generate diff
            diff_dir = Path(out_dir) / "diffs" / field_name
            diff_dir.mkdir(parents=True, exist_ok=True)
            diff_path = diff_dir / shot_name
            diff_image(img_render, img_golden, diff_path)

            results.append({
                "camera": shot_name.replace(".png", ""),
                "ssim": round(s, 4),
                "mse": round(err, 2),
                "diff": str(diff_path),
            })
            scores.append(s)
        elif auto_golden:
            # First render becomes golden
            golden_field_dir = Path(golden_dir) / field_name
            golden_field_dir.mkdir(parents=True, exist_ok=True)
            import shutil
            shutil.copy2(shot, golden_field_dir / shot_name)
            results.append({
                "camera": shot_name.replace(".png", ""),
                "ssim": 1.0,
                "mse": 0,
                "note": "auto-golden (first render)",
            })
            scores.append(1.0)
        else:
            results.append({
                "camera": shot_name.replace(".png", ""),
                "ssim": None,
                "mse": None,
                "note": "no golden reference",
            })

    avg_score = sum(scores) / len(scores) if scores else 0

    return {
        "field": field_name,
        "status": "validated" if scores else "no_reference",
        "screenshots": len(screenshots),
        "compared": len(scores),
        "avgSsim": round(avg_score, 4),
        "cameras": results,
    }


def main():
    parser = argparse.ArgumentParser(description="FieldPack Visual Validator")
    parser.add_argument("--render-dir", default="work/field_pack/validation",
                        help="Directory with rendered screenshots")
    parser.add_argument("--golden-dir", default="work/field_pack/golden",
                        help="Directory with golden reference screenshots")
    parser.add_argument("--out", default="work/field_pack/validation",
                        help="Output directory for validation results")
    parser.add_argument("--auto-golden", action="store_true",
                        help="Use first render as golden reference")
    parser.add_argument("--field", default="",
                        help="Validate a single field (e.g. map_maca_maca03)")

    args = parser.parse_args()

    render_dir = Path(args.render_dir)
    golden_dir = Path(args.golden_dir)
    out_dir = Path(args.out)

    if not render_dir.exists():
        print(f"Render directory not found: {render_dir}")
        print("Run render-gate.mjs first to generate screenshots.")
        sys.exit(1)

    # Discover field directories
    if args.field:
        field_dirs = [render_dir / args.field]
    else:
        field_dirs = sorted([d for d in render_dir.iterdir() if d.is_dir()])

    if not field_dirs:
        print("No field directories found in render dir.")
        sys.exit(1)

    print(f"Validating {len(field_dirs)} fields...")
    print(f"  Golden: {golden_dir}")
    print(f"  Output: {out_dir}")

    all_results = []
    for field_dir in field_dirs:
        result = validate_field(
            field_dir, golden_dir, out_dir,
            auto_golden=args.auto_golden
        )
        all_results.append(result)
        status_icon = "OK" if result["status"] == "validated" else ".."
        print(f"  [{status_icon}] {result['field']}: SSIM={result['avgSsim']} ({result['compared']}/{result['screenshots']} shots)")

    # Write score.json
    score = {
        "generatedAt": datetime_now(),
        "totalFields": len(all_results),
        "validated": sum(1 for r in all_results if r["status"] == "validated"),
        "noReference": sum(1 for r in all_results if r["status"] == "no_reference"),
        "noScreenshots": sum(1 for r in all_results if r["status"] == "no_screenshots"),
        "fields": all_results,
    }

    out_dir.mkdir(parents=True, exist_ok=True)
    score_path = out_dir / "score.json"
    with open(score_path, "w") as f:
        json.dump(score, f, indent=2)

    print(f"\nScore: {score_path}")
    print(f"  Validated: {score['validated']}/{score['totalFields']}")
    avg_all = sum(r["avgSsim"] for r in all_results if r["status"] == "validated")
    n_valid = score["validated"]
    if n_valid > 0:
        print(f"  Avg SSIM: {round(avg_all / n_valid, 4)}")


def datetime_now():
    from datetime import datetime, timezone
    return datetime.now(timezone.utc).isoformat()


if __name__ == "__main__":
    main()
