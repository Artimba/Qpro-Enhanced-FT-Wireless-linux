"""Create a new portable gate/direction pair with a trained cheek branch."""

import argparse
import json
import re
import tempfile
from datetime import datetime, timezone
from pathlib import Path

import torch

from prepare_cheek_stills import prepare
from train_cheek_model import main as train_head
from train_tongue_model import CHEEK_ARCHITECTURE_PREFIX, parent_checkpoint_metadata
from lower_face_training import publish_model_files, tongue_parent, validate_combined


def train_pair(session_path: Path, parent_path: Path, gate_path: Path, version: int,
               root: Path, epochs=80, device="auto", source="VirtualDesktop"):
    if version < 1:
        raise ValueError("A positive model version is required")
    session_path, parent_path, gate_path = [path.resolve(strict=True) for path in (session_path, parent_path, gate_path)]
    capture_path = session_path.with_name(session_path.name.removesuffix(".qpsession.json") + ".qpcap")
    parent = torch.load(parent_path, map_location="cpu", weights_only=True)
    gate = torch.load(gate_path, map_location="cpu", weights_only=True)
    models = root / "models"
    models.mkdir(parents=True, exist_ok=True)
    stem = f"qpro-stereo-tongue-v{version}"
    outputs = [models / (stem + ending) for ending in ("-gate.pt", "-direction.pt", ".metadata.json")]
    if any(path.name.startswith((stem + "-", stem + ".")) for path in models.iterdir()):
        raise ValueError("This model version is already occupied; existing models will not be overwritten")
    training = root / "training"
    training.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="cheek-head-", dir=training) as temporary:
        work = Path(temporary)
        base = parent
        initial = None
        if str(parent["architecture"]).startswith(CHEEK_ARCHITECTURE_PREFIX):
            base = tongue_parent(parent)
            initial = parent_path
        if list(tongue_parent(gate)["targetNames"]) != list(base["targetNames"]):
            raise ValueError("The tongue gate and direction model use different target schemas")
        base_path = work / "tongue-parent.pt"
        torch.save(base, base_path)
        cache = work / "cache"
        prepare(capture_path, session_path, cache, int(base["imageSize"]))
        direction = work / "direction.pt"
        arguments = [str(cache), "--parent-checkpoint", str(base_path), "--output", str(direction),
                     "--epochs", str(epochs), "--device", device]
        if initial is not None:
            arguments += ["--initial-checkpoint", str(initial)]
        if train_head(arguments) != 0:
            raise RuntimeError("Cheek model training failed")
        trained = torch.load(direction, map_location="cpu", weights_only=True)
        validate_combined(base, trained)
        metadata = {
            "format": "qpro-tongue-model-metadata-v1", "version": version,
            "displayName": "Tongue + camera cheeks", "modelKind": "camera-cheeks-experimental",
            "isExperimental": True, "hasCameraCheeks": True, "origin": "prompted cheek camera training",
            "createdUtc": datetime.now(timezone.utc).isoformat(),
            "datasetSession": session_path.name, "trackingSource": source,
            "tongueParent": parent_path.name, "cheekTraining": trained["cheekTraining"],
        }
        metadata["tongueParentSha256"] = parent_checkpoint_metadata(parent_path)[1]
        parent_version = re.fullmatch(r"qpro-stereo-tongue-v(\d+)-direction\.pt", parent_path.name)
        if parent_version:
            metadata["parentVersion"] = int(parent_version[1])
            parent_metadata_path = parent_path.with_name(f"qpro-stereo-tongue-v{parent_version[1]}.metadata.json")
            if parent_metadata_path.exists():
                try:
                    previous = json.loads(parent_metadata_path.read_text(encoding="utf-8"))
                    if not isinstance(previous, dict):
                        raise ValueError("Parent friendly metadata must be an object")
                    kind = previous.get("modelKind")
                    if previous.get("parentModelKind") == "mustachio-experimental":
                        kind = "mustachio-experimental"
                    if isinstance(kind, str):
                        metadata["parentModelKind"] = kind
                    if isinstance(previous.get("displayName"), str):
                        metadata["parentDisplayName"] = previous["displayName"]
                except (OSError, ValueError, TypeError):
                    # Optional friendly metadata cannot invalidate a safely
                    # trained copy. Its capability remains experimental.
                    pass
        staged_metadata = work / "metadata.json"
        staged_metadata.write_text(json.dumps(metadata, indent=2), encoding="utf-8")
        publish_model_files([(gate_path, "-gate.pt"), (direction, "-direction.pt"),
                             (staged_metadata, ".metadata.json")], models, version)
    print(f"MODEL_READY version={version} camera_cheeks=experimental frozen_tongue=True", flush=True)
    return metadata


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--session", type=Path, required=True)
    parser.add_argument("--parent-checkpoint", type=Path, required=True)
    parser.add_argument("--gate-checkpoint", type=Path, required=True)
    parser.add_argument("--version", type=int, required=True)
    parser.add_argument("--root", type=Path, default=Path.cwd())
    parser.add_argument("--epochs", type=int, default=80)
    parser.add_argument("--device", default="auto")
    parser.add_argument("--source", choices=("VirtualDesktop", "SteamLink"), default="VirtualDesktop")
    args = parser.parse_args()
    train_pair(args.session, args.parent_checkpoint, args.gate_checkpoint, args.version, args.root,
               args.epochs, args.device, args.source)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
