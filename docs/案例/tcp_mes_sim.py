#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
TCP MES/PLC simulator for docs/案例/通讯配置完整案例.md

It connects to the VisionPlatform TCP server endpoint "SIM-PLC" (127.0.0.1:9000),
sends TRIG every 5 seconds, prints every line received, and replies ACK after
receiving the JSON result.

Usage:
    python tcp_mes_sim.py [host] [port]
"""

import socket
import sys
import time

HOST = sys.argv[1] if len(sys.argv) > 1 else "127.0.0.1"
PORT = int(sys.argv[2]) if len(sys.argv) > 2 else 9000
TRIG_PERIOD = 5.0


def run_once():
    sock = socket.create_connection((HOST, PORT), timeout=3)
    sock.settimeout(0.2)
    print("[sim] connected to %s:%d" % (HOST, PORT))

    buf = b""
    last_trig = 0.0
    last_ack = 0.0

    while True:
        now = time.time()
        if now - last_trig >= TRIG_PERIOD:
            sock.sendall(b"TRIG\n")
            last_trig = now
            print("[sim] TX TRIG")

        try:
            data = sock.recv(4096)
        except socket.timeout:
            continue

        if not data:
            print("[sim] connection closed by peer")
            return

        buf += data
        while b"\n" in buf:
            line, buf = buf.split(b"\n", 1)
            text = line.rstrip(b"\r").decode("utf-8", "replace")
            if not text:
                continue
            print("[sim] RX " + text)

            # JSON result -> ACK (rate limited to avoid flooding)
            if text.startswith("{") and now - last_ack > 1.0:
                sock.sendall(b"ACK\n")
                last_ack = time.time()
                print("[sim] TX ACK")


def main():
    while True:
        try:
            run_once()
        except KeyboardInterrupt:
            print("[sim] stopped")
            return
        except OSError as ex:
            print("[sim] %s; retry in 2s" % ex)
            time.sleep(2.0)


if __name__ == "__main__":
    main()
