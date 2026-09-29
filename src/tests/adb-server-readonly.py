"""Run a read-only headset command through an already running local ADB server."""

from __future__ import annotations

import argparse
import socket


def send_request(stream: socket.socket, request: str) -> None:
    payload = request.encode("utf-8")
    stream.sendall(f"{len(payload):04x}".encode("ascii") + payload)
    response = stream.recv(4)
    if response != b"OKAY":
        raise RuntimeError(f"ADB server rejected {request.split(':', 1)[0]}: {response!r}")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("serial")
    parser.add_argument("command", choices=("build", "root"))
    arguments = parser.parse_args()
    command = {
        "build": "getprop ro.build.version.incremental",
        "root": "su -c id",
    }[arguments.command]
    with socket.create_connection(("127.0.0.1", 5037), timeout=3) as stream:
        send_request(stream, f"host:transport:{arguments.serial}")
        send_request(stream, f"shell:{command}")
        stream.settimeout(5)
        chunks = []
        while True:
            try:
                chunk = stream.recv(4096)
            except socket.timeout:
                break
            if not chunk:
                break
            chunks.append(chunk)
    print(b"".join(chunks).decode("utf-8", errors="replace").strip())


if __name__ == "__main__":
    main()
