#!/usr/bin/env python3
"""Check the packaged artifact, including every native ELF (no third-party Python modules)."""
import argparse
import hashlib
from pathlib import Path
import re
import struct
import subprocess
import zipfile


def verify_elf(data, name):
    if data[:6] != b"\x7fELF\x02\x01":
        raise ValueError(f"{name}: expected a little-endian 64-bit ELF")
    phoff = struct.unpack_from("<Q", data, 32)[0]
    size, count = struct.unpack_from("<HH", data, 54)
    loads = 0
    for i in range(count):
        header = phoff + i * size
        kind, _, offset, address, _, _, _, alignment = struct.unpack_from("<IIQQQQQQ", data, header)
        if kind == 1:  # PT_LOAD
            loads += 1
            if alignment < 16384 or (address - offset) % 16384:
                raise ValueError(f"{name}: LOAD segment does not support 16 KB pages")
    if not loads:
        raise ValueError(f"{name}: no LOAD segments")


def verify(apk, build_tools, expected_certificate=None):
    def run(tool, *args):
        return subprocess.check_output([str(build_tools / tool), *map(str, args)], text=True)

    signatures = run("apksigner", "verify", "--verbose", "--print-certs", apk)
    if expected_certificate:
        expected = hashlib.sha256(expected_certificate.read_bytes()).hexdigest()
        actual = re.findall(r"Signer #\d+ certificate SHA-256 digest: ([0-9a-f]+)", signatures)
        if actual != [expected] or "CN=Android Debug" in signatures:
            raise ValueError("APK does not have the expected non-debug release certificate")
    run("zipalign", "-c", "-P", "16", "-v", "4", apk)
    badging = run("aapt", "dump", "badging", apk)
    if "package: name='com.emushelf.app'" not in badging or "application-debuggable" in badging:
        raise ValueError("Unexpected package identity or debuggable release")
    if "native-code: 'arm64-v8a'" not in badging:
        raise ValueError("Expected the arm64 release ABI")
    if not re.search(r"application:.*icon='[^']+'", badging):
        raise ValueError("Missing launcher icon")
    with zipfile.ZipFile(apk) as archive:
        libraries = [n for n in archive.namelist() if n.startswith("lib/") and n.endswith(".so")]
        if not libraries or any(not n.startswith("lib/arm64-v8a/") for n in libraries):
            raise ValueError("Expected only arm64 native libraries")
        for name in libraries:
            verify_elf(archive.read(name), name)
    print(f"Verified signature, release manifest, icon, ZIP alignment and {len(libraries)} native libraries.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("apk", type=Path)
    parser.add_argument("--build-tools", type=Path, required=True)
    parser.add_argument("--expected-certificate", type=Path)
    args = parser.parse_args()
    try:
        verify(args.apk, args.build_tools, args.expected_certificate)
    except (ValueError, struct.error, subprocess.CalledProcessError, OSError, zipfile.BadZipFile) as error:
        parser.exit(1, f"Android artifact verification failed: {error}\n")
