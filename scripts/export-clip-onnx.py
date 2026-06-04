#!/usr/bin/env python3
"""Export an OpenAI CLIP ViT-B/32 model to the ONNX bundle Find That Shot expects.

Find That Shot's AI tagging + natural-language search run two CPU ONNX sessions:
an *image* encoder (pixel_values -> image_embeds) and a *text* encoder
(input_ids/attention_mask -> text_embeds), plus the standard CLIP BPE merges for
tokenization. This script downloads the public `openai/clip-vit-base-patch32`
weights and emits exactly that layout so the bundle drops straight into the app
with zero code changes.

Output (default: <repo>/tools/models/clip-vit-b32):
    image_encoder.onnx                 CLIPVisionModelWithProjection -> image_embeds
    text_encoder.onnx                  CLIPTextModelWithProjection  -> text_embeds
    bpe_simple_vocab_16e6.txt.gz       gzip of the HF CLIP merges.txt (header + 48894 merges)
    manifest.json                      tensor names / image size / CLIP norm constants

Once present, AiModelProvider resolves tools/models/clip-vit-b32 automatically
(it's also copied into the build/publish output), so enabling "AI tagging" in
Settings just works -- no hosting, no download URL.

Usage:
    python -m pip install "torch" "transformers" "onnx" "onnxscript"
    python scripts/export-clip-onnx.py
    # options:
    python scripts/export-clip-onnx.py --model openai/clip-vit-base-patch32 --out tools/models/clip-vit-b32 --opset 14

Notes:
  * CPU-only; no GPU required. Roughly ~600 MB of weights are downloaded once by
    transformers into its cache. The two FP32 ONNX encoders total ~340 MB.
  * Everything here uses public, permissively/again-MIT-licensed assets
    (openai/clip-vit-base-patch32). Nothing is committed to the repo;
    tools/models/ is .gitignored.
"""
from __future__ import annotations

import argparse
import gzip
import json
import os
import shutil
import sys
from pathlib import Path


def _repo_root() -> Path:
    # scripts/ lives directly under the repo root.
    return Path(__file__).resolve().parent.parent


def _eprint(*args: object) -> None:
    print(*args, file=sys.stderr)


def _export_onnx(module, args_tuple, path, *, input_names, output_names, dynamic_axes, opset):
    """torch.onnx.export across torch versions.

    torch >= 2.x defaults to the new dynamo-based exporter (which needs the
    optional `onnxscript` package and reinterprets input/output names). We want
    the stable TorchScript exporter so the explicit names + dynamic axes below
    map straight through. Pass dynamo=False when supported, and fall back for
    older torch that lacks the kwarg.
    """
    import torch

    common = dict(
        input_names=input_names,
        output_names=output_names,
        dynamic_axes=dynamic_axes,
        opset_version=opset,
        do_constant_folding=True,
    )
    try:
        torch.onnx.export(module, args_tuple, str(path), dynamo=False, **common)
    except TypeError:
        # Older torch: no `dynamo` kwarg, already uses the TorchScript exporter.
        torch.onnx.export(module, args_tuple, str(path), **common)


def _locate_merges(model_id: str, tokenizer, out_dir: "Path"):
    """Return a path to the CLIP BPE merges.txt, or None.

    Prefer downloading the canonical merges.txt from the Hub (it's already in the
    local cache after from_pretrained). Fall back to save_vocabulary, which on
    both slow and fast CLIP tokenizers writes vocab.json + merges.txt.
    """
    try:
        from huggingface_hub import hf_hub_download

        return Path(hf_hub_download(model_id, "merges.txt"))
    except Exception as ex:  # noqa: BLE001 - best effort, fall back below
        _eprint(f"[export] Hub merges.txt fetch failed ({ex}); trying local save.")

    try:
        tmp_dir = out_dir / "_hf_tokenizer"
        tmp_dir.mkdir(exist_ok=True)
        # save_vocabulary returns the paths it wrote; the merges file is among them.
        written = tokenizer.save_vocabulary(str(tmp_dir))
        for p in written or ():
            if str(p).endswith("merges.txt"):
                return Path(p)
        candidate = tmp_dir / "merges.txt"
        return candidate if candidate.exists() else None
    except Exception as ex:  # noqa: BLE001
        _eprint(f"[export] Local tokenizer save failed: {ex}")
        return None


def main() -> int:
    parser = argparse.ArgumentParser(description="Export CLIP ViT-B/32 to the Find That Shot ONNX bundle.")
    parser.add_argument(
        "--model",
        default="openai/clip-vit-base-patch32",
        help="HuggingFace model id (default: openai/clip-vit-base-patch32).",
    )
    parser.add_argument(
        "--out",
        default=None,
        help="Output directory (default: <repo>/tools/models/clip-vit-b32).",
    )
    parser.add_argument("--opset", type=int, default=14, help="ONNX opset (default: 14).")
    parser.add_argument(
        "--force",
        action="store_true",
        help="Overwrite the output directory if it already contains a bundle.",
    )
    parser.add_argument(
        "--zip",
        action="store_true",
        help="Also pack the bundle into <out>.zip, ready to upload as a hosted "
        "download (e.g. a GitHub Release asset) for in-app download-on-demand.",
    )
    args = parser.parse_args()

    try:
        import torch
        from transformers import (
            CLIPTextModelWithProjection,
            CLIPTokenizer,
            CLIPVisionModelWithProjection,
        )
        # torch >= 2.x routes torch.onnx.export through an exporter module that
        # imports onnxscript at call time even for the legacy path, so require it
        # up front rather than failing mid-export.
        import onnxscript  # noqa: F401
    except ImportError as ex:  # pragma: no cover - guidance path
        _eprint("Missing dependency:", ex)
        _eprint('Install with:  python -m pip install "torch" "transformers" "onnx" "onnxscript"')
        return 2

    out_dir = Path(args.out) if args.out else _repo_root() / "tools" / "models" / "clip-vit-b32"
    out_dir.mkdir(parents=True, exist_ok=True)

    image_path = out_dir / "image_encoder.onnx"
    text_path = out_dir / "text_encoder.onnx"
    vocab_path = out_dir / "bpe_simple_vocab_16e6.txt.gz"
    manifest_path = out_dir / "manifest.json"

    if not args.force and image_path.exists() and text_path.exists() and vocab_path.exists():
        _eprint(f"Bundle already present in {out_dir} (use --force to re-export).")
        return 0

    image_size = 224
    context_length = 77
    embedding_dim = 512

    print(f"[export] Loading {args.model} (CPU)...", flush=True)
    torch.manual_seed(0)

    vision = CLIPVisionModelWithProjection.from_pretrained(args.model).eval()
    text = CLIPTextModelWithProjection.from_pretrained(args.model).eval()
    tokenizer = CLIPTokenizer.from_pretrained(args.model)

    embedding_dim = int(getattr(vision.config, "projection_dim", embedding_dim))
    image_size = int(getattr(vision.config, "image_size", image_size))
    context_length = int(getattr(tokenizer, "model_max_length", context_length) or context_length)
    # CLIP's text encoder is trained at a 77-token context; model_max_length is
    # sometimes reported as a very large sentinel, so clamp to the trained value.
    if context_length > 77:
        context_length = 77

    # --- Image encoder: pixel_values [N,3,H,W] -> image_embeds [N,D] ----------
    class VisionEmbed(torch.nn.Module):
        def __init__(self, m: torch.nn.Module) -> None:
            super().__init__()
            self.m = m

        def forward(self, pixel_values: "torch.Tensor") -> "torch.Tensor":
            return self.m(pixel_values=pixel_values).image_embeds

    print(f"[export] Writing {image_path.name} ...", flush=True)
    dummy_pixels = torch.randn(1, 3, image_size, image_size, dtype=torch.float32)
    _export_onnx(
        VisionEmbed(vision),
        (dummy_pixels,),
        image_path,
        input_names=["pixel_values"],
        output_names=["image_embeds"],
        dynamic_axes={"pixel_values": {0: "batch"}, "image_embeds": {0: "batch"}},
        opset=args.opset,
    )

    # --- Text encoder: input_ids/attention_mask [N,L] -> text_embeds [N,D] ----
    class TextEmbed(torch.nn.Module):
        def __init__(self, m: torch.nn.Module) -> None:
            super().__init__()
            self.m = m

        def forward(self, input_ids: "torch.Tensor", attention_mask: "torch.Tensor") -> "torch.Tensor":
            return self.m(input_ids=input_ids, attention_mask=attention_mask).text_embeds

    print(f"[export] Writing {text_path.name} ...", flush=True)
    dummy_ids = torch.ones(1, context_length, dtype=torch.int64)
    dummy_mask = torch.ones(1, context_length, dtype=torch.int64)
    _export_onnx(
        TextEmbed(text),
        (dummy_ids, dummy_mask),
        text_path,
        input_names=["input_ids", "attention_mask"],
        output_names=["text_embeds"],
        dynamic_axes={
            "input_ids": {0: "batch"},
            "attention_mask": {0: "batch"},
            "text_embeds": {0: "batch"},
        },
        opset=args.opset,
    )

    # --- Tokenizer merges: gzip HF's merges.txt under the default vocab name ---
    # The C# ClipTokenizer reads the standard CLIP merges format (skip the first
    # header line, take 48894 "a b" merge pairs). HuggingFace's CLIP merges.txt
    # is exactly that, so we just gzip it to the file name the manifest expects.
    # transformers 5.x returns the *fast* tokenizer, whose save_pretrained emits
    # only tokenizer.json (no merges.txt), so try a slow-tokenizer save first and
    # fall back to fetching merges.txt straight from the Hub.
    print(f"[export] Writing {vocab_path.name} ...", flush=True)
    merges_txt = _locate_merges(args.model, tokenizer, out_dir)
    if merges_txt is None:
        _eprint("Could not obtain merges.txt for the tokenizer.")
        return 3
    with open(merges_txt, "rb") as src, gzip.open(vocab_path, "wb") as dst:
        shutil.copyfileobj(src, dst)
    # Never let the fallback's scratch dir leak into the bundle / zip.
    shutil.rmtree(out_dir / "_hf_tokenizer", ignore_errors=True)

    # --- Manifest -------------------------------------------------------------
    manifest = {
        "modelId": "clip-vit-b32",
        "imageEncoderFile": "image_encoder.onnx",
        "textEncoderFile": "text_encoder.onnx",
        "vocabFile": "bpe_simple_vocab_16e6.txt.gz",
        "imageSize": image_size,
        "embeddingDim": embedding_dim,
        "contextLength": context_length,
        "imageInputName": "pixel_values",
        "imageOutputName": "image_embeds",
        "textInputIdsName": "input_ids",
        "textAttentionMaskName": "attention_mask",
        "textOutputName": "text_embeds",
        "imageMean": [0.48145466, 0.4578275, 0.40821073],
        "imageStd": [0.26862954, 0.26130258, 0.27577711],
    }
    manifest_path.write_text(json.dumps(manifest, indent=2), encoding="utf-8")

    total_mb = sum(p.stat().st_size for p in (image_path, text_path, vocab_path)) / (1024 * 1024)
    print(f"[export] Done -> {out_dir}  ({total_mb:.0f} MB)", flush=True)
    print("[export] Enable 'AI tagging' in Settings; the bundle resolves automatically.", flush=True)

    # Optionally produce a flat .zip (files at the archive root) for hosting. The
    # app's download-on-demand path extracts a .zip into its managed app-data
    # folder, so a self-hosted asset at this layout lets end users fetch the
    # model with one in-app click instead of running this script themselves.
    if args.zip:
        zip_base = str(out_dir)  # make_archive appends .zip
        archive = shutil.make_archive(zip_base, "zip", root_dir=str(out_dir))
        zip_mb = Path(archive).stat().st_size / (1024 * 1024)
        print(f"[export] Packed -> {archive}  ({zip_mb:.0f} MB)", flush=True)
        print(
            "[export] Upload it (e.g. a GitHub Release asset) and set that URL as "
            "'AI model download URL' in settings.",
            flush=True,
        )

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
