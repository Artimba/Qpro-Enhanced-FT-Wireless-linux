"""Stage lower-face training and publish complete model pairs without replacement."""

import argparse
import json
import os
import shutil
import tempfile
import uuid
from pathlib import Path

import torch

from prepare_cheek_stills import prepare
from tongue_image_processing import resolve_input_preprocessing
from train_cheek_model import main as train_head
from train_tongue_model import CHEEK_ARCHITECTURE_PREFIX, CHEEK_TARGET_NAMES


def tongue_parent(checkpoint):
    """Extract the original tongue branch; reject incomplete combined states."""
    if not str(checkpoint["architecture"]).startswith(CHEEK_ARCHITECTURE_PREFIX):
        return checkpoint
    architecture = checkpoint.get("baseArchitecture")
    names = checkpoint.get("targetNames", [])
    state = {key.removeprefix("parent."): value for key, value in checkpoint["modelState"].items()
             if key.startswith("parent.")}
    if not architecture or checkpoint["architecture"] != CHEEK_ARCHITECTURE_PREFIX + architecture or \
            len(names) <= 2 or tuple(names[-2:]) != CHEEK_TARGET_NAMES or \
            any(name in CHEEK_TARGET_NAMES for name in names[:-2]) or not state:
        raise ValueError("Unsupported combined lower-face target schema or missing tongue state")
    plain = {**checkpoint, "architecture": architecture, "targetNames": list(names[:-2]), "modelState": state}
    for key in ("baseArchitecture", "cheekTraining", "experimental"):
        plain.pop(key, None)
    return plain


def validate_combined(parent, candidate):
    """Check the persisted candidate, including exact preservation of tongue tensors."""
    if not str(candidate.get("architecture", "")).startswith(CHEEK_ARCHITECTURE_PREFIX):
        raise ValueError("Cheek training did not create a combined checkpoint")
    original = tongue_parent(candidate)
    if original["architecture"] != parent["architecture"] or \
            list(original["targetNames"]) != list(parent["targetNames"]) or \
            int(original["imageSize"]) != int(parent["imageSize"]) or \
            resolve_input_preprocessing(original) != resolve_input_preprocessing(parent) or \
            original["modelState"].keys() != parent["modelState"].keys() or any(
                not torch.equal(original["modelState"][key], value)
                for key, value in parent["modelState"].items()
            ) or not isinstance(candidate.get("cheekTraining"), dict):
        raise ValueError("The combined candidate changed its tongue parent or input contract")


def publish_model_files(files, models: Path, version: int):
    """Publish complete files exclusively; metadata is the final commit marker."""
    if version < 1:
        raise ValueError("A positive model version is required")
    models.mkdir(parents=True, exist_ok=True)
    stem = f"qpro-stereo-tongue-v{version}"
    if any(path.name.startswith((stem + "-", stem + ".")) for path in models.iterdir()):
        raise ValueError("This model version is already occupied; existing models will not be overwritten")
    temporary, published = [], []
    try:
        for source, ending in files:
            destination = models / (stem + ending)
            staged = models / (".qpro-publish-" + uuid.uuid4().hex + ".tmp")
            temporary.append(staged)
            with staged.open("xb") as output, Path(source).open("rb") as input_stream:
                shutil.copyfileobj(input_stream, output)
            # Windows rename refuses an existing destination. A hard link
            # provides the same exclusive behavior on other test hosts.
            if os.name == "nt":
                os.rename(staged, destination)
            else:
                os.link(staged, destination)
                staged.unlink()
            published.append(destination)
    except BaseException:
        for path in published:
            path.unlink(missing_ok=True)
        raise
    finally:
        for path in temporary:
            path.unlink(missing_ok=True)


def publish_pair(staged_root: Path, root: Path, version: int):
    """Validate and commit the pair; omit stale plain-direction scripts."""
    if staged_root.resolve() == root.resolve():
        raise ValueError("Train in a private staging directory before publishing")
    stem = f"qpro-stereo-tongue-v{version}"
    models = staged_root / "models"
    gate_path, direction_path = [models / (stem + ending) for ending in ("-gate.pt", "-direction.pt")]
    gate = tongue_parent(torch.load(gate_path, map_location="cpu", weights_only=True))
    direction_checkpoint = torch.load(direction_path, map_location="cpu", weights_only=True)
    direction = tongue_parent(direction_checkpoint)
    if list(gate["targetNames"]) != list(direction["targetNames"]):
        raise ValueError("The trained gate and direction model use different tongue targets")
    files = [(gate_path, "-gate.pt"), (direction_path, "-direction.pt")]
    for branch in ("gate", "direction"):
        script = models / f"{stem}-{branch}.torchscript.pt"
        if script.exists() and (branch != "direction" or not str(direction_checkpoint["architecture"]).startswith(CHEEK_ARCHITECTURE_PREFIX)):
            files.append((script, f"-{branch}.torchscript.pt"))
    metadata = models / (stem + ".metadata.json")
    if metadata.exists():
        data = json.loads(metadata.read_text(encoding="utf-8"))
        combined = str(direction_checkpoint["architecture"]).startswith(CHEEK_ARCHITECTURE_PREFIX)
        if data.get("version") != version or data.get("hasCameraCheeks", False) is not combined:
            raise ValueError("Model metadata does not describe the staged checkpoint")
        files.append((metadata, ".metadata.json"))
    elif str(direction_checkpoint["architecture"]).startswith(CHEEK_ARCHITECTURE_PREFIX):
        raise ValueError("A combined model requires its capability metadata")
    publish_model_files(files, root / "models", version)


def attach_cheeks(session: Path, direction: Path, root: Path, version: int, device="auto", epochs=80):
    root = root.resolve()
    if root.parent.name != "training" or not root.name.startswith("lower-face-run-"):
        raise ValueError("Attach cheeks only in this run's private training directory")
    model_root = root / "models"
    if direction.resolve() != model_root / f"qpro-stereo-tongue-v{version}-direction.pt":
        raise ValueError("The cheek output must belong to the just-trained model version")
    parent = torch.load(direction, map_location="cpu", weights_only=True)
    if str(parent["architecture"]).startswith(CHEEK_ARCHITECTURE_PREFIX):
        raise ValueError("Train the tongue branch first, then rebuild its camera cheek head")
    training = root / "training"
    training.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="lower-face-cheeks-", dir=training) as temporary:
        work = Path(temporary)
        capture = session.with_name(session.name.removesuffix(".qpsession.json") + ".qpcap")
        prepare(capture, session, work / "cache", int(parent["imageSize"]))
        output = work / "combined.pt"
        if train_head([str(work / "cache"), "--parent-checkpoint", str(direction), "--output", str(output),
                       "--device", device, "--epochs", str(epochs)]) != 0:
            raise RuntimeError("Camera cheek training failed; staged tongue model kept")
        candidate = torch.load(output, map_location="cpu", weights_only=True)
        validate_combined(parent, candidate)
        metadata = {"format": "qpro-tongue-model-metadata-v1", "version": version,
                    "displayName": "Personal lower-face model", "origin": "lower-face calibration",
                    "isExperimental": True, "hasCameraCheeks": True,
                    "modelKind": "camera-cheeks-experimental", "datasetSession": session.name,
                    "cheekTraining": candidate["cheekTraining"]}
        metadata["tongueParent"] = candidate.get("parentCheckpoint", direction.name)
        metadata["tongueParentSha256"] = candidate.get("parentCheckpointSha256")
        metadata_path = model_root / f"qpro-stereo-tongue-v{version}.metadata.json"
        temporary_metadata = work / "metadata.json"
        temporary_metadata.write_text(json.dumps(metadata, indent=2), encoding="utf-8")
        backup = work / "original.pt"
        shutil.copyfile(direction, backup)
        previous_metadata = metadata_path.read_bytes() if metadata_path.exists() else None
        try:
            output.replace(direction)
            temporary_metadata.replace(metadata_path)
        except BaseException:
            backup.replace(direction)
            if previous_metadata is None:
                metadata_path.unlink(missing_ok=True)
            else:
                metadata_path.write_bytes(previous_metadata)
            raise
        # A plain tongue script cannot describe the new combined outputs.
        direction.with_suffix(".torchscript.pt").unlink(missing_ok=True)
    print(f"TRAIN_CHEEKS_READY version={version} camera_cheeks=experimental", flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    unwrap = commands.add_parser("unwrap")
    unwrap.add_argument("checkpoint", type=Path)
    unwrap.add_argument("--output", type=Path, required=True)
    attach = commands.add_parser("attach")
    attach.add_argument("--session", type=Path, required=True)
    attach.add_argument("--direction", type=Path, required=True)
    attach.add_argument("--root", type=Path, required=True)
    attach.add_argument("--version", type=int, required=True)
    attach.add_argument("--device", default="auto")
    attach.add_argument("--epochs", type=int, default=80)
    publish = commands.add_parser("publish")
    publish.add_argument("--staged-root", type=Path, required=True)
    publish.add_argument("--root", type=Path, required=True)
    publish.add_argument("--version", type=int, required=True)
    args = parser.parse_args()
    if args.command == "unwrap":
        if args.output.resolve() == args.checkpoint.resolve():
            raise ValueError("Use a new temporary tongue-parent path")
        args.output.parent.mkdir(parents=True, exist_ok=True)
        with args.output.open("xb") as stream:
            torch.save(tongue_parent(torch.load(args.checkpoint, map_location="cpu", weights_only=True)), stream)
    elif args.command == "attach":
        attach_cheeks(args.session, args.direction, args.root, args.version, args.device, args.epochs)
    else:
        publish_pair(args.staged_root, args.root, args.version)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
