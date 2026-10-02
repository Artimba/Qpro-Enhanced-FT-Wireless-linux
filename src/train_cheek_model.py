#!/usr/bin/env python3
"""Train an experimental cheek head while retaining a frozen tongue model.

Cheek targets come from deliberate camera capture cards. Meta's coupled cheek
channels are useful diagnostics, but are not ground truth for this camera head.
No public developer model is promoted automatically by this utility.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import random
from pathlib import Path

import numpy as np
import torch
from torch import nn
from torch.utils.data import DataLoader, TensorDataset, WeightedRandomSampler

from qpro_gpu import validated_torch_device_name
from tongue_image_processing import preprocess_stereo_images, resolve_input_preprocessing
from train_tongue_model import (
    CHEEK_ARCHITECTURE_PREFIX, CHEEK_TARGET_NAMES, FrozenTongueCheekModel,
    balanced_step_weights, blocked_train_validation_split,
    parent_checkpoint_metadata,
)


class CheekFrames:
    """Read private image caches without placing their paths in a checkpoint."""

    def __init__(self, caches: list[Path], image_size: int) -> None:
        self.images: list[np.ndarray] = []
        self.ends: list[int] = []
        self.capture_ids: list[str] = []
        self.cache_paths = list(caches)
        targets, steps, trainable = [], [], []
        total = 0
        try:
            for cache in caches:
                metadata = json.loads((cache / "metadata.json").read_text(encoding="utf-8"))
                if tuple(metadata.get("targetNames", ())) != CHEEK_TARGET_NAMES:
                    raise ValueError("Cheek cache targets must be cheekPuffLeft and cheekPuffRight")
                if metadata.get("targetSource") != "prompted-cheek-poses":
                    raise ValueError("Cheek training requires prompted cheek poses, not native face labels")
                target = np.load(cache / "targets.npy", allow_pickle=False)
                step = np.load(cache / "step_ids.npy", allow_pickle=False)
                count = len(target)
                if target.shape != (count, 2) or not np.isfinite(target).all() or \
                        np.any((target < 0) | (target > 1)):
                    raise ValueError("Cheek targets must be finite left/right strengths in 0..1")
                if step.shape != (count,) or not np.issubdtype(step.dtype, np.integer):
                    raise ValueError("Each cheek frame needs an integer capture card ID")
                permitted = np.load(cache / "trainable.npy", allow_pickle=False) \
                    if (cache / "trainable.npy").exists() else np.ones(count, dtype=np.bool_)
                if permitted.shape != (count,) or permitted.dtype != np.bool_:
                    raise ValueError("Cheek trainable mask must be a boolean value per frame")
                images = np.load(cache / "images.npy", mmap_mode="r", allow_pickle=False)
                if images.dtype != np.uint8 or images.shape != (count, 2, image_size, image_size):
                    images._mmap.close()
                    raise ValueError("Cheek images must be uint8 stereo frames at the parent's image size")
                self.images.append(images)
                total += count
                self.ends.append(total)
                self.capture_ids.append(str(metadata.get("captureId", cache.name)))
                targets.append(target.astype(np.float32))
                steps.append(step.astype(np.int64))
                trainable.append(permitted)
        except Exception:
            self.close()
            raise
        if not targets:
            raise ValueError("At least one cheek capture cache is required")
        self.targets = np.concatenate(targets)
        self.steps = np.concatenate(steps)
        self.trainable = np.concatenate(trainable)
        if not np.any(self.trainable):
            self.close()
            raise ValueError("The cheek caches contain no trainable samples")

    def pixels(self, index: int) -> np.ndarray:
        cache_index = int(np.searchsorted(self.ends, index, side="right"))
        offset = 0 if cache_index == 0 else self.ends[cache_index - 1]
        return self.images[cache_index][index - offset]

    def close(self) -> None:
        for images in self.images:
            if getattr(images, "_mmap", None) is not None:
                images._mmap.close()


def validate_pose_coverage(target: np.ndarray, description: str) -> None:
    for name, requested in (
        ("relaxed", (0, 0)), ("left cheek", (1, 0)),
        ("right cheek", (0, 1)), ("both cheeks", (1, 1)),
    ):
        if not np.any(np.all(np.isclose(target, requested, atol=.01), axis=1)):
            raise ValueError(f"{description} is missing a {name} capture card")


def validate_independent_captures(training: CheekFrames, validation: CheekFrames) -> None:
    if set(training.capture_ids) & set(validation.capture_ids):
        raise ValueError("Independent validation must use a separate capture session")

    def image_digest(cache: Path) -> str:
        digest = hashlib.sha256()
        with (cache / "images.npy").open("rb") as stream:
            for block in iter(lambda: stream.read(4 * 1024 * 1024), b""):
                digest.update(block)
        return digest.hexdigest()

    # Renaming a copy of the training cache cannot turn its images into an
    # independent validation capture, even when its capture ID was changed.
    if {image_digest(path) for path in training.cache_paths} & \
            {image_digest(path) for path in validation.cache_paths}:
        raise ValueError("Independent validation contains a copy of the training images")


def split_cheek_indices(data: CheekFrames) -> tuple[np.ndarray, np.ndarray]:
    # These are manual repetitions, not adjacent frames cut from a video.
    # Keep a repetition from every captured card out of gradient updates.
    training, validation = blocked_train_validation_split(
        data.steps, data.trainable, dataset_type="manual-stereo-stills",
    )
    if not len(training) or not len(validation):
        raise ValueError("Each cheek card needs at least four usable repetitions for a holdout")
    validate_pose_coverage(data.targets[training], "Training split")
    validate_pose_coverage(data.targets[validation], "Validation split")
    return training, validation


def create_combined_model(parent: dict, initial: dict | None = None) -> FrozenTongueCheekModel:
    architecture = str(parent.get("architecture", "legacy-late-fusion-v1"))
    if architecture.startswith(CHEEK_ARCHITECTURE_PREFIX):
        raise ValueError("Choose the original plain tongue model as the parent")
    names = list(parent["targetNames"]) + list(CHEEK_TARGET_NAMES)
    model = FrozenTongueCheekModel(architecture, names)
    model.parent.load_state_dict(parent["modelState"])
    if initial is not None:
        if initial.get("architecture") != CHEEK_ARCHITECTURE_PREFIX + architecture or \
                initial.get("targetNames") != names or \
                initial.get("imageSize") != parent.get("imageSize") or \
                resolve_input_preprocessing(initial) != resolve_input_preprocessing(parent):
            raise ValueError("Initial cheek model does not match the tongue parent's input contract")
        model.load_state_dict(initial["modelState"])
        assert_parent_unchanged(model, parent["modelState"])
    return model


def assert_parent_unchanged(model: FrozenTongueCheekModel, original: dict) -> None:
    current = model.parent.state_dict()
    if current.keys() != original.keys() or any(
        not torch.equal(current[name].detach().cpu(), value.detach().cpu())
        for name, value in original.items()
    ):
        raise RuntimeError("The frozen tongue state changed; no combined checkpoint was saved")


def extract_features(
    model: FrozenTongueCheekModel, data: CheekFrames, indices: np.ndarray,
    preprocessing: str, device: torch.device, batch_size: int, variants: int = 1,
) -> tuple[torch.Tensor, torch.Tensor]:
    """Run the frozen encoder once per observed/augmented stereo example.

    Subsequent epochs train only the small cheek head. This keeps a CPU
    experiment practical and avoids repeating frozen convolutions every epoch.
    Left and right share brightness changes; images are never mirrored.
    """
    if variants < 1:
        raise ValueError("Feature variants must be positive")
    features, targets = [], []
    model.parent.eval()
    with torch.inference_mode():
        for variant in range(variants):
            for start in range(0, len(indices), batch_size):
                selected = indices[start:start + batch_size]
                pixels = np.stack([
                    preprocess_stereo_images(data.pixels(int(index)), preprocessing)
                    for index in selected
                ])
                cameras = torch.from_numpy(pixels.copy()).float().div_(255)
                if variant:
                    # This is photometric augmentation of recorded images;
                    # it does not manufacture unseen cheek geometry.
                    contrast = torch.empty((len(cameras), 1, 1, 1)).uniform_(.92, 1.08)
                    brightness = torch.empty((len(cameras), 1, 1, 1)).uniform_(-.03, .03)
                    cameras = (cameras * contrast + brightness).clamp_(0, 1)
                features.append(model.parent.stereo_features(cameras.to(device)).cpu())
                targets.append(torch.from_numpy(data.targets[selected].copy()))
                print(f"TRAIN_FEATURES completed={variant*len(indices)+start+len(selected)} total={variants*len(indices)}", flush=True)
    # Clone outside inference_mode: the head must be able to save these input
    # tensors for its weight gradients without treating them as inference-only.
    return torch.cat(features).clone(), torch.cat(targets).clone()


def cheek_metrics(prediction: np.ndarray, target: np.ndarray) -> dict:
    if prediction.shape != target.shape or target.ndim != 2 or target.shape[1] != 2 or \
            not np.isfinite(prediction).all() or not np.isfinite(target).all():
        raise ValueError("Cheek metrics require matching finite N-by-2 arrays")
    error = np.abs(prediction - target)
    result: dict = {"mae": float(error.mean()), "perTarget": {}}
    for index, name in enumerate(CHEEK_TARGET_NAMES):
        inactive = target[:, index] <= .01
        active = target[:, index] >= .99
        result["perTarget"][name] = {
            "mae": float(error[:, index].mean()),
            "inactiveMean": float(prediction[inactive, index].mean()) if inactive.any() else None,
            "inactiveP95": float(np.quantile(prediction[inactive, index], .95)) if inactive.any() else None,
            "fullMean": float(prediction[active, index].mean()) if active.any() else None,
        }
    result["perPose"] = []
    for pose in np.unique(target, axis=0):
        mask = np.all(np.isclose(target, pose, atol=.001), axis=1)
        result["perPose"].append({
            "target": pose.tolist(), "samples": int(mask.sum()),
            "prediction": prediction[mask].mean(axis=0).tolist(),
            "mae": float(error[mask].mean()),
        })
    result["balancedPoseMae"] = float(np.mean([pose["mae"] for pose in result["perPose"]]))
    inactive = [side["inactiveP95"] for side in result["perTarget"].values()
                if side["inactiveP95"] is not None]
    result["checkpointScore"] = result["balancedPoseMae"] + .25 * float(np.mean(inactive) if inactive else 0)
    result["checkpointScoreFormula"] = "mean_pose_mae + 0.25*mean_inactive_cheek_p95"
    return result


def evaluate_head(model: FrozenTongueCheekModel, features: torch.Tensor,
                  target: torch.Tensor, device: torch.device, batch_size: int) -> dict:
    model.eval()
    prediction = []
    with torch.inference_mode():
        for batch in features.split(batch_size):
            prediction.append(model.cheeks_from_features(batch.to(device)).cpu().numpy())
    return cheek_metrics(np.concatenate(prediction), target.numpy())


def save_combined_checkpoint(
    output: Path, model: FrozenTongueCheekModel, parent: dict, parent_path: Path,
    metrics: dict, epoch: int, train_count: int, validation_count: int,
    independent_capture: bool,
) -> None:
    assert_parent_unchanged(model, parent["modelState"])
    parent_name, parent_sha = parent_checkpoint_metadata(parent_path)
    checkpoint = {
        "formatVersion": 1,
        "architecture": CHEEK_ARCHITECTURE_PREFIX + model.base_architecture,
        "baseArchitecture": model.base_architecture,
        "targetNames": model.target_names,
        "imageSize": int(parent["imageSize"]),
        "inputPreprocessing": resolve_input_preprocessing(parent),
        "modelState": {name: value.detach().cpu().clone() for name, value in model.state_dict().items()},
        "parentCheckpoint": parent_name,
        "parentCheckpointSha256": parent_sha,
        "visibilityGate": parent.get("visibilityGate", {}),
        "experimental": True,
        "cheekTraining": {
            "targetSource": "prompted-cheek-poses", "frozenTongueParent": True,
            "trainSamples": train_count, "validationSamples": validation_count,
            "checkpointEpoch": epoch, "validation": metrics,
            "featureNormalization": "fixed per-feature training mean/std; minimum scale 0.01",
            "loss": "binary-cross-entropy-with-logits using prompted strength fractions",
            "validationGrade": "independent-capture" if independent_capture else "same-session-card-repetitions",
            "independentWearerValidation": False,
            "developerPromotionApproved": False,
        },
    }
    output.parent.mkdir(parents=True, exist_ok=True)
    temporary = output.with_name(output.name + ".tmp")
    try:
        torch.save(checkpoint, temporary)
        temporary.replace(output)
    finally:
        temporary.unlink(missing_ok=True)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("cache", nargs="+")
    parser.add_argument("--parent-checkpoint", required=True)
    parser.add_argument("--initial-checkpoint")
    parser.add_argument("--validation-cache", nargs="+")
    parser.add_argument("--output", required=True)
    parser.add_argument("--epochs", type=int, default=80)
    parser.add_argument("--batch-size", type=int, default=64)
    parser.add_argument("--learning-rate", type=float, default=1e-3)
    parser.add_argument("--patience", type=int, default=15)
    parser.add_argument("--feature-variants", type=int, default=2)
    parser.add_argument("--device", default="auto")
    arguments = parser.parse_args(argv)
    if min(arguments.epochs, arguments.batch_size, arguments.patience, arguments.feature_variants) < 1 or \
            not np.isfinite(arguments.learning_rate) or arguments.learning_rate <= 0:
        parser.error("Epochs, batch size, patience, variants and learning rate must be positive")
    seed = 20261002
    random.seed(seed)
    np.random.seed(seed)
    torch.manual_seed(seed)
    torch.set_num_threads(min(4, max(1, torch.get_num_threads())))
    parent_path = Path(arguments.parent_checkpoint).resolve(strict=True)
    parent = torch.load(parent_path, map_location="cpu", weights_only=True)
    initial = torch.load(arguments.initial_checkpoint, map_location="cpu", weights_only=True) \
        if arguments.initial_checkpoint else None
    model = create_combined_model(parent, initial)
    device = torch.device(validated_torch_device_name(torch, arguments.device))
    model.to(device)
    data = CheekFrames([Path(path).resolve(strict=True) for path in arguments.cache], int(parent["imageSize"]))
    validation_data = None
    try:
        if arguments.validation_cache:
            validation_data = CheekFrames([
                Path(path).resolve(strict=True) for path in arguments.validation_cache
            ], int(parent["imageSize"]))
            validate_independent_captures(data, validation_data)
            train_indices = np.flatnonzero(data.trainable)
            validation_indices = np.flatnonzero(validation_data.trainable)
            validate_pose_coverage(data.targets[train_indices], "Training capture")
            validate_pose_coverage(validation_data.targets[validation_indices], "Validation capture")
        else:
            train_indices, validation_indices = split_cheek_indices(data)
        print(f"TRAIN_STAGE name=cheek-head device={device} frozen_tongue=True", flush=True)
        print("Preparing frozen stereo features once; subsequent epochs train only the cheek head.", flush=True)
        preprocessing = resolve_input_preprocessing(parent)
        train_features, train_targets = extract_features(
            model, data, train_indices, preprocessing, device, arguments.batch_size, arguments.feature_variants,
        )
        validation_features, validation_targets = extract_features(
            model, validation_data or data, validation_indices, preprocessing, device, arguments.batch_size,
        )
        if initial is None:
            # Fit on gradient-update samples only. Reusing an existing cheek
            # model preserves its normalization contract during refinement.
            model.fit_cheek_feature_normalization(train_features)
        weights = np.tile(balanced_step_weights(data.steps, train_indices), arguments.feature_variants)
        sampler = WeightedRandomSampler(torch.as_tensor(weights, dtype=torch.double), len(weights), replacement=True)
        loader = DataLoader(TensorDataset(train_features, train_targets), batch_size=arguments.batch_size, sampler=sampler)
        optimizer = torch.optim.AdamW(model.cheek_head.parameters(), lr=arguments.learning_rate, weight_decay=1e-4)
        best = float("inf")
        best_epoch = 0
        output = Path(arguments.output).resolve()
        if initial is not None:
            metrics = evaluate_head(model, validation_features, validation_targets, device, arguments.batch_size)
            best = float(metrics["checkpointScore"])
            save_combined_checkpoint(output, model, parent, parent_path, metrics, 0,
                                     len(train_indices), len(validation_indices), validation_data is not None)
        for epoch in range(1, arguments.epochs + 1):
            model.train()
            loss_total = 0.0
            sample_count = 0
            for features, targets in loader:
                features, targets = features.to(device), targets.to(device)
                optimizer.zero_grad(set_to_none=True)
                logits = model.cheek_logits_from_features(features)
                # Neutral and inactive cheeks matter as much as full puffs:
                # don't optimize active-only error and allow opposite leakage.
                # BCE on logits keeps gradients usable even when an early
                # epoch predicts a confident zero for a requested full puff.
                # Fractional pose targets remain continuous soft labels.
                loss = nn.functional.binary_cross_entropy_with_logits(logits, targets)
                loss.backward()
                optimizer.step()
                loss_total += float(loss.detach().cpu()) * len(targets)
                sample_count += len(targets)
            metrics = evaluate_head(model, validation_features, validation_targets, device, arguments.batch_size)
            score = float(metrics["checkpointScore"])
            print(f"TRAIN_EPOCH index={epoch} total={arguments.epochs} loss={loss_total/sample_count:.5f} validation_mae={metrics['mae']:.5f} validation_score={score:.5f}", flush=True)
            if score < best:
                best, best_epoch = score, epoch
                save_combined_checkpoint(output, model, parent, parent_path, metrics, epoch,
                                         len(train_indices), len(validation_indices), validation_data is not None)
            if epoch - best_epoch >= arguments.patience:
                print("No held-out cheek improvement; keeping the best checkpoint.", flush=True)
                break
        print(f"Experimental combined model saved: {output.name}; held-out cheek score={best:.5f}", flush=True)
        print("The original tongue weights are unchanged. Separate-capture and wearer validation are required before developer promotion.", flush=True)
    finally:
        data.close()
        if validation_data is not None:
            validation_data.close()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
