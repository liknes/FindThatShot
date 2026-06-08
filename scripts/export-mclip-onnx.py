#!/usr/bin/env python3
"""Export the MULTILINGUAL CLIP bundle Find That Shot expects (ONNX, CPU).

Find That Shot's natural-language search compares a text embedding against the
stored per-clip CLIP image embeddings. To make that search work in 50+ languages
(Norwegian, Brazilian Portuguese, German, ...) we keep the *image* encoder of
OpenAI CLIP ViT-B/32 (so existing image embeddings stay valid) and swap the
*text* encoder for `sentence-transformers/clip-ViT-B-32-multilingual-v1` -- a
multilingual DistilBERT distilled to map text into the *same* CLIP ViT-B/32
vector space. Only the text side and its tokenizer change.

This script downloads the public weights and emits the exact bundle layout the
app loads (selected via manifest.json "tokenizerType": "bert-wordpiece"):

Output (default: <repo>/tools/models/clip-multilingual-v1):
    image_encoder.onnx     CLIPVisionModelWithProjection -> image_embeds  (unchanged ViT-B/32)
    text_encoder.onnx      DistilBERT -> mean-pool -> Dense(768->512) -> text_embeds
    vocab.txt              bert-base-multilingual-cased WordPiece vocab (119547 tokens)
    manifest.json          tensor names / image size / tokenizer type

Once present, AiModelProvider resolves tools/models/clip-multilingual-v1
automatically (it's also copied into the build/publish output), so enabling
"AI tagging" in Settings just works -- no hosting, no download URL.

Usage:
    python -m pip install "torch" "transformers" "sentence-transformers" "onnx" "onnxscript"
    python scripts/export-mclip-onnx.py
    # also pack a hosted-download .zip:
    python scripts/export-mclip-onnx.py --zip

Notes:
  * CPU-only; no GPU required. The two FP32 ONNX encoders total ~450 MB
    (image ~350 MB + multilingual text ~135 MB).
  * Public, permissively-licensed assets (OpenAI CLIP ViT-B/32 image encoder,
    sentence-transformers multilingual text encoder, multilingual BERT vocab).
    Nothing is committed to the repo; tools/models/ is .gitignored.
"""
from __future__ import annotations

import argparse
import json
import shutil
import sys
from pathlib import Path

IMAGE_MODEL = "openai/clip-vit-base-patch32"
TEXT_MODEL = "sentence-transformers/clip-ViT-B-32-multilingual-v1"
CONTEXT_LENGTH = 64  # padded query/caption length; captions are short


def _repo_root() -> Path:
    return Path(__file__).resolve().parent.parent


def _eprint(*args: object) -> None:
    print(*args, file=sys.stderr)


def _export_onnx(module, args_tuple, path, *, input_names, output_names, dynamic_axes, opset):
    """torch.onnx.export across torch versions (prefer the stable exporter)."""
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
        torch.onnx.export(module, args_tuple, str(path), **common)


def _write_vocab(tokenizer, vocab_path: Path) -> None:
    """Write a one-token-per-line vocab.txt ordered by id (BertTokenizer format)."""
    # Fast/slow BERT tokenizers both expose get_vocab() -> {token: id}.
    vocab = tokenizer.get_vocab()
    ordered = sorted(vocab.items(), key=lambda kv: kv[1])
    with open(vocab_path, "w", encoding="utf-8") as f:
        for token, _id in ordered:
            f.write(token)
            f.write("\n")


def main() -> int:
    parser = argparse.ArgumentParser(description="Export the multilingual CLIP ONNX bundle.")
    parser.add_argument("--out", default=None, help="Output dir (default: <repo>/tools/models/clip-multilingual-v1).")
    parser.add_argument("--opset", type=int, default=14, help="ONNX opset (default: 14).")
    parser.add_argument("--force", action="store_true", help="Overwrite an existing bundle.")
    parser.add_argument("--zip", action="store_true", help="Also pack <out>.zip for hosted download-on-demand.")
    args = parser.parse_args()

    try:
        import torch
        from transformers import CLIPVisionModelWithProjection
        from sentence_transformers import SentenceTransformer
        import onnxscript  # noqa: F401  (torch>=2 routes export through it)
    except ImportError as ex:  # pragma: no cover - guidance path
        _eprint("Missing dependency:", ex)
        _eprint('Install:  python -m pip install "torch" "transformers" "sentence-transformers" "onnx" "onnxscript"')
        return 2

    out_dir = Path(args.out) if args.out else _repo_root() / "tools" / "models" / "clip-multilingual-v1"
    out_dir.mkdir(parents=True, exist_ok=True)

    image_path = out_dir / "image_encoder.onnx"
    text_path = out_dir / "text_encoder.onnx"
    vocab_path = out_dir / "vocab.txt"
    manifest_path = out_dir / "manifest.json"

    if not args.force and image_path.exists() and text_path.exists() and vocab_path.exists():
        _eprint(f"Bundle already present in {out_dir} (use --force to re-export).")
        return 0

    image_size = 224
    embedding_dim = 512

    torch.manual_seed(0)

    # --- Image encoder: pixel_values [N,3,H,W] -> image_embeds [N,512] --------
    # Identical to the English bundle's image encoder, so per-clip image
    # embeddings already in the catalog remain valid.
    print(f"[export] Loading image encoder {IMAGE_MODEL} (CPU)...", flush=True)
    vision = CLIPVisionModelWithProjection.from_pretrained(IMAGE_MODEL).eval()
    embedding_dim = int(getattr(vision.config, "projection_dim", embedding_dim))
    image_size = int(getattr(vision.config, "image_size", image_size))

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

    # --- Text encoder: input_ids/attention_mask [N,L] -> text_embeds [N,512] --
    # The sentence-transformers model is DistilBERT -> mean pooling -> Dense
    # (768->512, no bias). We fold the pooling + dense into the exported graph
    # so the app just feeds tokens and reads the final 512-d embedding.
    print(f"[export] Loading text encoder {TEXT_MODEL} (CPU)...", flush=True)
    st = SentenceTransformer(TEXT_MODEL, device="cpu")
    transformer = st[0].auto_model.eval()   # DistilBertModel
    tokenizer = st[0].tokenizer
    dense = st[2]                           # models.Dense(768->512, bias=False, identity)

    class TextEmbed(torch.nn.Module):
        def __init__(self, transformer: torch.nn.Module, dense: torch.nn.Module) -> None:
            super().__init__()
            self.transformer = transformer
            self.dense = dense

        def forward(self, input_ids: "torch.Tensor", attention_mask: "torch.Tensor") -> "torch.Tensor":
            token_embeddings = self.transformer(input_ids=input_ids, attention_mask=attention_mask)[0]
            mask = attention_mask.unsqueeze(-1).to(token_embeddings.dtype)
            summed = (token_embeddings * mask).sum(dim=1)
            counts = mask.sum(dim=1).clamp(min=1e-9)
            mean_pooled = summed / counts
            return self.dense.linear(mean_pooled)

    print(f"[export] Writing {text_path.name} ...", flush=True)
    dummy_ids = torch.ones(1, CONTEXT_LENGTH, dtype=torch.int64)
    dummy_mask = torch.ones(1, CONTEXT_LENGTH, dtype=torch.int64)
    _export_onnx(
        TextEmbed(transformer, dense),
        (dummy_ids, dummy_mask),
        text_path,
        input_names=["input_ids", "attention_mask"],
        output_names=["text_embeds"],
        dynamic_axes={
            "input_ids": {0: "batch", 1: "sequence"},
            "attention_mask": {0: "batch", 1: "sequence"},
            "text_embeds": {0: "batch"},
        },
        opset=args.opset,
    )

    # --- Tokenizer vocab ------------------------------------------------------
    print(f"[export] Writing {vocab_path.name} ...", flush=True)
    _write_vocab(tokenizer, vocab_path)

    # --- Manifest -------------------------------------------------------------
    lower_case = bool(getattr(tokenizer, "do_lower_case", False))
    manifest = {
        "modelId": "clip-vit-b32-multilingual-v1",
        "imageEncoderFile": "image_encoder.onnx",
        "textEncoderFile": "text_encoder.onnx",
        "vocabFile": "vocab.txt",
        "tokenizerType": "bert-wordpiece",
        "tokenizerLowerCase": lower_case,
        "imageSize": image_size,
        "embeddingDim": embedding_dim,
        "contextLength": CONTEXT_LENGTH,
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
    print("[export] Enable 'AI tagging' in Settings; the multilingual bundle resolves automatically.", flush=True)

    if args.zip:
        zip_base = str(out_dir)  # make_archive appends .zip
        archive = shutil.make_archive(zip_base, "zip", root_dir=str(out_dir))
        zip_mb = Path(archive).stat().st_size / (1024 * 1024)
        print(f"[export] Packed -> {archive}  ({zip_mb:.0f} MB)", flush=True)
        print(
            "[export] Upload it (e.g. a GitHub Release asset under the models-v2 tag) and set that "
            "URL as 'AI model download URL' in settings.",
            flush=True,
        )

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
