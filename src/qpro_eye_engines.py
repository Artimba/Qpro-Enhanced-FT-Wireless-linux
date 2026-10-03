"""Exact tracking-engine identities and their reviewed native gaze layouts.

Firmware build approval is separate from this catalog. A profile is selected
only when both the complete engine hash and its size match. No engine binaries
are distributed with Qpro; preparation reads the connected headset's identity.
"""

from __future__ import annotations

from dataclasses import dataclass
import re


@dataclass(frozen=True)
class EngineProbeProfile:
    size: int
    offset: int
    arguments: str
    sha256: str
    profile: str
    entry_offset: int | None = None
    validation: str = "live-reference"
    builds: tuple[str, ...] = ()


STACK_ARGUMENTS = (
    "x=+0x30(%sp):x32 y=+0x34(%sp):x32 z=+0x38(%sp):x32 "
    "tag=+0x0(%x19):x32"
)
EYEDATA_ARGUMENTS = (
    "x=+0x300(%x19):x32 y=+0x304(%x19):x32 z=+0x308(%x19):x32 "
    "tag=+0x0(%x19):x32 pointer=%x19:x64 ready=+0x35c(%x19):u8"
)
# The detector checks one byte with ldrb; adjacent flags are not readiness.
ENTRY_ARGUMENTS = "tag=+0x0(%x1):x32 ready=+0x35c(%x1):u8 pointer=%x1:x64"

ENGINE_PROFILES = (
    EngineProbeProfile(
        47_724_232, 0xB63FE4, STACK_ARGUMENTS,
        "56a5bab3478e686f3da513889b77ad5f732bda69fc5f61f9d79dd184480e699c",
        "51483620027600340",
        builds=("51483620027600340",),
    ),
    EngineProbeProfile(
        47_418_280, 0xB1F3E8, EYEDATA_ARGUMENTS,
        "0fb6f54a3e190bec791d757ea18d32a8ecc1af4a861992d04b1703c93293cd03",
        "51503870024400340", entry_offset=0xB1F000,
        builds=("51503870024400340",),
    ),
    EngineProbeProfile(
        47_418_280, 0xB1F3E8, EYEDATA_ARGUMENTS,
        "dddaa5a6427e069a982d6de4326a4a7fedae31248ec5d86dd623268cee0ffa22",
        "51503870023600340", entry_offset=0xB1F000,
        validation="firmware-analysis",
        builds=("51503870023600340",),
    ),
    EngineProbeProfile(
        47_418_280, 0xB1F3E8, EYEDATA_ARGUMENTS,
        "503a850c80860b5aa5fd5a8211b3718a7d87c4cdaddad05588a8baf6c1233548",
        "51503870021300340", entry_offset=0xB1F000,
        validation="firmware-analysis", builds=("51503870021300340",),
    ),
    EngineProbeProfile(
        47_418_280, 0xB1F3E8, EYEDATA_ARGUMENTS,
        "2f36a083e6509b8b9971c6bc8ce0acacdcc184ccc01f060dd1f6e2cc24af4b60",
        "51503870020100340", entry_offset=0xB1F000,
        validation="firmware-analysis", builds=("51503870020100340",),
    ),
    EngineProbeProfile(
        47_418_280, 0xB1F3E8, EYEDATA_ARGUMENTS,
        "b7aa923f2eef1705d0af789caa1d09cdfaad8374378de0a582d668d96bdc912c",
        "51503870019200340", entry_offset=0xB1F000,
        validation="firmware-analysis", builds=("51503870019200340", "51503870018800340"),
    ),
    EngineProbeProfile(
        47_409_576, 0xB1F0E8, EYEDATA_ARGUMENTS,
        "09ada5903855ac96d3a7d3ca4a646cf1f60bca13f9d519a72b54b9c8f6d66d12",
        "51503870018000340", entry_offset=0xB1ED00,
        validation="firmware-analysis", builds=("51503870018000340",),
    ),
    EngineProbeProfile(
        47_724_232, 0xB63FE4, STACK_ARGUMENTS,
        "1f9aefd11475d971009a55eb95ec0bbc325ade74f4d081d706763627b68ae739",
        "51483620032400340",
        validation="firmware-analysis", builds=("51483620032400340",),
    ),
    EngineProbeProfile(
        47_724_232, 0xB63FE4, STACK_ARGUMENTS,
        "d0bd93c140ad7e930c24802b2d650539b0d496244e62e58ca0bc14c3d5594967",
        "51483620026500340",
        validation="firmware-analysis", builds=("51483620026500340",),
    ),
    EngineProbeProfile(
        47_724_232, 0xB63FE4, STACK_ARGUMENTS,
        "87abdbcc11f18408844975792c0567f09d2cac6bbb3a17ed826e8f2d3cf4dfc7",
        "51483620024400340",
        validation="firmware-analysis", builds=("51483620024400340", "51483620023800340"),
    ),
    EngineProbeProfile(
        47_724_232, 0xB63FE4, STACK_ARGUMENTS,
        "57094796f37659a51de9d3d9c86a2a13ca245056225e2ab9eb284a257e4114d2",
        "51483620020700340",
        validation="firmware-analysis", builds=("51483620020700340",),
    ),
    EngineProbeProfile(
        47_724_232, 0xB63FE4, STACK_ARGUMENTS,
        "1e6b663331b1e83991f002ef66ce7e55d180c4b75888a08e19b947fbe33a67a3",
        "51483620019200340",
        validation="firmware-analysis", builds=("51483620019200340",),
    ),
    EngineProbeProfile(
        47_647_432, 0xB62B94, STACK_ARGUMENTS,
        "5b2f45dec8e7d72d4b711594a28f5d43af5b38d86cefdd87020ec5e0c473c655",
        "51463340027700340",
        validation="firmware-analysis", builds=("51463340027700340",),
    ),
    EngineProbeProfile(
        47_018_856, 0xB566A4, STACK_ARGUMENTS,
        "c2e799a91f2fb3d58d85f1aef313729034d8b7404c6fc6c56a98a388e45ebd20",
        "51436340040000340",
        validation="firmware-analysis", builds=("51436340040000340",),
    ),
    EngineProbeProfile(
        47_018_856, 0xB566A4, STACK_ARGUMENTS,
        "fa211e424a157a4b54ac3c9e17c004d63191bc8b35225fe3fb00b6d4bff15392",
        "51436340031400340",
        validation="firmware-analysis", builds=("51436340031400340",),
    ),
    EngineProbeProfile(
        47_018_856, 0xB566A4, STACK_ARGUMENTS,
        "a01a755782068bbbe50593572fa2b61f11d62a9f165b9ba411f8d12f7bfde7bd",
        "51436340028700340",
        validation="firmware-analysis", builds=("51436340028700340",),
    ),
    EngineProbeProfile(
        44_198_016, 0xB37604, STACK_ARGUMENTS,
        "96fdebc377b475df55d59f7added04c5014c4069aa1d636cf3d6f15d8fe27f1e",
        "51412760034600340",
        validation="firmware-analysis", builds=("51412760034600340",),
    ),
    EngineProbeProfile(
        44_198_016, 0xB37604, STACK_ARGUMENTS,
        "12482222a3bd7e434023e01e1e4aa396bc23465ad28c21edbe8e98ce07f732a3",
        "51412760030200340",
        validation="firmware-analysis", builds=("51412760030200340",),
    ),
    EngineProbeProfile(
        44_198_016, 0xB37604, STACK_ARGUMENTS,
        "88005187a9132ec01474ef7c5f00ed7d1648e44a4bedbc47861ccaeaa089c286",
        "51412760027100340",
        validation="firmware-analysis", builds=("51412760027100340",),
    ),
    EngineProbeProfile(
        44_198_016, 0xB37604, STACK_ARGUMENTS,
        "1a59d4ce78b54d2c1e1f09ed52cec2fd62adfbd294efa4f864febf5b492f292a",
        "51412760025900340",
        validation="firmware-analysis", builds=("51412760025900340",),
    ),
    EngineProbeProfile(
        44_197_984, 0xB37604, STACK_ARGUMENTS,
        "561c5656b41220ec624316de4fa1cc41de030c7c98c9f31ee934f761debcf164",
        "51412760020900340",
        validation="firmware-analysis", builds=("51412760020900340",),
    ),
    EngineProbeProfile(
        44_197_840, 0xB37604, STACK_ARGUMENTS,
        "ea9e53ebc17e6c77ccd209a13e41e6567f5330f0ff9d71a542709d206e737d0e",
        "51412760014500340",
        validation="firmware-analysis", builds=("51412760014500340",),
    ),
    EngineProbeProfile(
        43_847_720, 0xA9F444, STACK_ARGUMENTS,
        "c46c8d8b0086d4c3ca7a45b63493662ac99e63bf0726c10a6e218b1bc9a3dd06",
        "51360500051900340",
        validation="firmware-analysis", builds=("51360500051900340",),
    ),
    EngineProbeProfile(
        43_847_720, 0xA9F444, STACK_ARGUMENTS,
        "6f45f080049726227306fdfe520437a9e8418d90680e1e7963c2741c4905daf6",
        "51360500040800340",
        validation="firmware-analysis", builds=("51360500040800340",),
    ),
    EngineProbeProfile(
        43_847_720, 0xA9F444, STACK_ARGUMENTS,
        "51b4c57d4d29a93bb484b3afddca4569e7a6c9710891aec92d0d6fafda4d65c1",
        "51360500037600340",
        validation="firmware-analysis", builds=("51360500037600340",),
    ),
    EngineProbeProfile(
        43_847_720, 0xA9F444, STACK_ARGUMENTS,
        "652190494111aea81f2e7599021a01448e49c0e80162d3024a98b0acc4a710a9",
        "51360500035000340",
        validation="firmware-analysis", builds=("51360500035000340",),
    ),
    EngineProbeProfile(
        43_847_704, 0xA9F444, STACK_ARGUMENTS,
        "4c2f0289df5b60d544faed133f6d45d3a550fbc728d7b384a154d2ce82792dbf",
        "51360500032000340",
        validation="firmware-analysis", builds=("51360500032000340",),
    ),
    EngineProbeProfile(
        43_847_720, 0xA9F444, STACK_ARGUMENTS,
        "3d7d2ac757d143688448e0f0d562b93885306c3498f5d667b0abc4626a69c59c",
        "51360500027200340",
        validation="firmware-analysis", builds=("51360500027200340",),
    ),

)


class EngineCompatibilityError(ValueError):
    """The connected engine has no matching reviewed extraction layout."""


def select_engine_profile(size: int, digest: str) -> EngineProbeProfile:
    """Select by complete identity, including engines that share a file size."""
    if not isinstance(size, int) or isinstance(size, bool) or size <= 0:
        raise EngineCompatibilityError("Could not read a valid tracking-engine size.")
    if not isinstance(digest, str) or re.fullmatch(r"[0-9a-f]{64}", digest) is None:
        raise EngineCompatibilityError("Could not read a valid tracking-engine SHA-256.")
    matches = [item for item in ENGINE_PROFILES if item.size == size and item.sha256 == digest]
    if len(matches) == 1:
        return matches[0]
    if matches:
        raise EngineCompatibilityError("Tracking-engine identity has ambiguous native profiles.")
    if any(item.size == size for item in ENGINE_PROFILES):
        raise EngineCompatibilityError(
            f"Tracking-engine SHA-256 {digest} differs from supported profiles for size {size}."
        )
    raise EngineCompatibilityError(
        f"Unsupported tracking-engine size {size} and SHA-256 {digest}. "
        "This firmware needs its own reviewed native eye profile."
    )
