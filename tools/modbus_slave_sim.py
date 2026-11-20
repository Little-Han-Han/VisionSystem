#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Minimal Modbus TCP slave simulator (zero dependencies, stdlib only).

Why this exists
---------------
VisionSystem's Modbus TCP endpoint is a *client* (master): it needs a slave
(PLC) to connect to. On a bench without a PLC, "connection timed out" tells you
nothing about whether our protocol code is correct. This script gives you a
local slave so you can verify the whole chain end to end:
connect -> trigger -> one cycle -> heartbeat / result / coordinates written back.

Supported function codes
------------------------
  0x01 Read Coils               0x05 Write Single Coil
  0x03 Read Holding Registers   0x06 Write Single Register
  0x10 Write Multiple Registers

Quick start
-----------
  python modbus_slave_sim.py                       # listen on 127.0.0.1:502
  python modbus_slave_sim.py --only-result         # quiet: only [RESULT] blocks
  python modbus_slave_sim.py --port 15020          # if 502 is taken / reserved
  python modbus_slave_sim.py --pulse 0 --pulse-sec 3   # auto trigger every 3s

Then in VisionSystem: 通讯 -> PLC -> 类型 Modbus TCP -> 主机 127.0.0.1 / 端口 502 /
SlaveId 1, and click 运行.

Interactive console (type commands while it runs)
-------------------------------------------------
  r 0 5       读 HR[0..4]
  w 0 1       写 HR[0]=1
  p 0         脉冲 HR[0]：0 -> 1 -> 0，等一轮跑完自动回显主站写了什么
  p 0 300 3000 同上，保持 300ms，等 3000ms 再回显
  dump        列出所有非零寄存器
  clear       全部清零
  help / quit

Automatic pulse (hands-free)
----------------------------
  python modbus_slave_sim.py --pulse 0 --pulse-ms 500 --pulse-sec 3 --capture-ms 2500
  -> every 3s: HR[0]=1 for 500ms, then report what the master wrote.

Notes
-----
* Per-frame traces go through log_frame(); --only-result hides them so the
  100ms heartbeat writes do not scroll the result away.
* The [RESULT] block reports write EVENTS, not value changes: in production
  every cycle writes the same coordinates, so a diff would always say
  "nothing changed" while the master really wrote 6 registers.
* Windows may reserve low ports (Hyper-V). If binding 502 fails, use
  --port 15020 and set the endpoint port to the same value.
"""

import argparse
import socket
import struct
import sys
import threading
import time

LOCK = threading.Lock()
REGS = [0] * 65536          # holding registers
COILS = [0] * 65536         # coils (FC01 / FC05)
WRITES = []                 # (addr, value) write events, for the per-trigger report
VERBOSE = True
SHOW_FRAMES = True          # per-frame traces (disable with --only-result)
CAPTURE_MS = 2500           # after a trigger, wait this long before reporting
TRIGGER_BASE = 0            # 第 0 条流程的触发位地址
STRIDE = 100                # 每条流程占用的寄存器区间（与软件「多流程步长」保持一致）
RUNNING = True


def log(msg):
    if VERBOSE:
        print("[%s] %s" % (time.strftime("%H:%M:%S"), msg), flush=True)


def log_frame(msg):
    """Per-request frame trace, separated from log() so --only-result can hide it."""
    if VERBOSE and SHOW_FRAMES:
        print("[%s] %s" % (time.strftime("%H:%M:%S"), msg), flush=True)


# --------------------------------------------------------------------------
#  protocol
# --------------------------------------------------------------------------
def handle_pdu(pdu):
    """Return response PDU bytes, or an exception PDU (FC | 0x80, code)."""
    fc = pdu[0]

    if fc == 0x03:                                   # read holding registers
        addr, count = struct.unpack(">HH", pdu[1:5])
        if count == 0 or count > 125:
            return bytes([fc | 0x80, 0x03])
        with LOCK:
            values = REGS[addr:addr + count]
        if len(values) != count:
            return bytes([fc | 0x80, 0x02])
        body = b"".join(struct.pack(">H", v) for v in values)
        #log_frame("FC3  read  HR[%d..%d] -> %s" % (addr, addr + count - 1, fmt_vals(values)))
        return bytes([fc, len(body)]) + body

    if fc == 0x06:                                   # write single register
        addr, value = struct.unpack(">HH", pdu[1:5])
        with LOCK:
            REGS[addr] = value
            WRITES.append((addr, value))
        log_frame("FC6  write HR[%d] = %d" % (addr, value))
        return bytes([fc]) + pdu[1:5]

    if fc == 0x10:                                   # write multiple registers
        addr, count, byte_count = struct.unpack(">HHB", pdu[1:6])
        if byte_count != count * 2 or len(pdu) < 6 + byte_count:
            return bytes([fc | 0x80, 0x03])
        with LOCK:
            for i in range(count):
                REGS[addr + i] = struct.unpack(">H", pdu[6 + i * 2:8 + i * 2])[0]
                WRITES.append((addr + i, REGS[addr + i]))
            values = REGS[addr:addr + count]
        log_frame("FC16 write HR[%d..%d] = %s" % (addr, addr + count - 1, fmt_vals(values)))
        return bytes([fc]) + pdu[1:5]

    if fc == 0x01:                                   # read coils
        addr, count = struct.unpack(">HH", pdu[1:5])
        if count == 0 or count > 2000:
            return bytes([fc | 0x80, 0x03])
        with LOCK:
            bits = COILS[addr:addr + count]
        if len(bits) != count:
            return bytes([fc | 0x80, 0x02])
        buf = bytearray((count + 7) // 8)
        for i, b in enumerate(bits):
            if b:
                buf[i // 8] |= 1 << (i % 8)
        log_frame("FC1  read  Coil[%d..%d] -> %s" % (addr, addr + count - 1, bits[:8]))
        return bytes([fc, len(buf)]) + bytes(buf)

    if fc == 0x05:                                   # write single coil
        addr, value = struct.unpack(">HH", pdu[1:5])
        with LOCK:
            COILS[addr] = 1 if value == 0xFF00 else 0
        log_frame("FC5  write Coil[%d] = %d" % (addr, COILS[addr]))
        return bytes([fc]) + pdu[1:5]

    log_frame("unsupported FC 0x%02X" % fc)
    return bytes([fc | 0x80, 0x01])


def fmt_vals(values):
    return ",".join(str(v) for v in values)


def recv_exact(conn, n):
    buf = b""
    while len(buf) < n:
        chunk = conn.recv(n - len(buf))
        if not chunk:
            return None
        buf += chunk
    return buf


def serve_client(conn, addr):
    log("client connected: %s:%d" % addr[:2])
    conn.settimeout(300)
    try:
        while RUNNING:
            head = recv_exact(conn, 7)
            if head is None:
                break
            tid, pid, length, unit = struct.unpack(">HHHB", head)
            if length < 2:
                break
            pdu = recv_exact(conn, length - 1)
            if pdu is None:
                break
            resp_pdu = handle_pdu(pdu)
            resp = struct.pack(">HHHB", tid, pid, len(resp_pdu) + 1, unit) + resp_pdu
            conn.sendall(resp)
    except socket.timeout:
        log("client idle timeout: %s:%d" % addr[:2])
    except Exception as ex:
        log("client error %s:%d -> %s: %s" % (addr[0], addr[1], type(ex).__name__, ex))
    finally:
        try:
            conn.close()
        except Exception:
            pass
        log("client closed: %s:%d" % addr[:2])


# --------------------------------------------------------------------------
#  trigger helpers
# --------------------------------------------------------------------------
def report_writes(items, trigger_addr):
    """Report the registers the master WROTE during one trigger.

    Report write *events*, not value *changes*: production cycles write the same
    coordinates every time, so a diff would print "nothing changed" while the
    master actually wrote 6 registers - which looks exactly like "the result
    never comes back". Same address is reported once, last value wins.
    """
    last = {}
    order = []
    for addr, value in items:
        if addr == trigger_addr:
            continue
        if addr not in last:
            order.append(addr)
        last[addr] = value

    if not order:
        log("[RESULT] the master wrote nothing in this cycle "
            "(flow triggered? check Trigger point address / edge task, and 'r 0 5')")
        return

    # 按 flow 分区归类（zone = addr // stride）：多流程时一眼看出哪条流程写了什么
    if STRIDE > 0:
        zones = {}
        for addr in order:
            zones.setdefault(addr // STRIDE, []).append(addr)

        log("[RESULT] %d register(s) written back, grouped by flow zone (stride=%d):"
            % (len(order), STRIDE))
        for zone in sorted(zones.keys()):
            regs = ", ".join("HR[%d]=%d" % (a, last[a]) for a in zones[zone][:32])
            log("         flow %d -> %s" % (zone, regs))
        return

    log("[RESULT] master wrote %d register(s) in this cycle:" % len(order))
    for addr in order[:64]:
        log("         HR[%d] = %d" % (addr, last[addr]))
    if len(order) > 64:
        log("         ... (%d more, use 'dump' to see all)" % (len(order) - 64))


def pulse(addr, hold_ms, capture_ms=0):
    """0 -> 1 (hold) -> 0, then report what the master wrote during the cycle."""
    def worker():
        with LOCK:
            mark = len(WRITES)

        with LOCK:
            REGS[addr] = 1
        log("PULSE HR[%d] = 1  (%d ms)" % (addr, hold_ms))
        time.sleep(hold_ms / 1000.0)

        with LOCK:
            REGS[addr] = 0
        log_frame("PULSE HR[%d] = 0" % addr)

        if capture_ms > 0:
            time.sleep(capture_ms / 1000.0)     # let the flow finish this cycle
            with LOCK:
                items = list(WRITES[mark:])
            report_writes(items, addr)

    t = threading.Thread(target=worker)
    t.daemon = True
    t.start()


def auto_pulse_loop(addr, period_s, hold_ms, capture_ms):
    while RUNNING:
        time.sleep(period_s)
        if not RUNNING:
            break
        pulse(addr, hold_ms, capture_ms)


def print_help():
    print("", flush=True)
    print("commands:", flush=True)
    print("  r <addr> [count]   read holding registers (default count=1)", flush=True)
    print("  w <addr> <value>   write one holding register", flush=True)
    print("  p <flow> [hold_ms] [capture_ms]  trigger flow N (addr = base + N*stride), auto-report", flush=True)
    print("  pa <addr> [hold_ms] [capture_ms]  pulse a raw register address instead", flush=True)
    print("  dump               list non-zero registers", flush=True)
    print("  clear              zero all registers", flush=True)
    print("  help               this text", flush=True)
    print("  quit               exit", flush=True)
    print("", flush=True)


def run_console():
    """Read commands from stdin.

    Returns True only when the user asked to quit. On EOF (detached process, or
    stdin redirected to /dev/null) it returns False so the caller keeps serving.
    """
    try:
        while RUNNING:
            line = sys.stdin.readline()
            if not line:                 # EOF: no interactive console available
                return False
            parts = line.strip().split()
            if not parts:
                continue
            cmd = parts[0].lower()
            try:
                if cmd in ("q", "quit", "exit"):
                    print("bye", flush=True)
                    return True
                if cmd in ("h", "help", "?"):
                    print_help()
                elif cmd in ("r", "read"):
                    addr = int(parts[1], 0)
                    count = int(parts[2], 0) if len(parts) > 2 else 1
                    with LOCK:
                        values = REGS[addr:addr + count]
                    print("HR[%d..%d] = %s" % (addr, addr + count - 1, fmt_vals(values)), flush=True)
                elif cmd in ("w", "write"):
                    addr = int(parts[1], 0)
                    value = int(parts[2], 0) & 0xFFFF
                    with LOCK:
                        REGS[addr] = value
                    print("HR[%d] = %d" % (addr, value), flush=True)
                elif cmd in ("p", "pulse"):
                    # p <flow>：第 flow 条流程的触发位 = TRIGGER_BASE + flow * STRIDE
                    flow = int(parts[1], 0)
                    addr = TRIGGER_BASE + flow * STRIDE
                    hold = int(parts[2], 0) if len(parts) > 2 else 300
                    cap = int(parts[3], 0) if len(parts) > 3 else CAPTURE_MS
                    if flow != 0:
                        log("trigger flow %d -> HR[%d]" % (flow, addr))
                    pulse(addr, hold, cap)
                elif cmd == "pa":
                    addr = int(parts[1], 0)
                    hold = int(parts[2], 0) if len(parts) > 2 else 300
                    cap = int(parts[3], 0) if len(parts) > 3 else CAPTURE_MS
                    pulse(addr, hold, cap)
                elif cmd == "dump":
                    with LOCK:
                        items = [(i, v) for i, v in enumerate(REGS[:4096]) if v]
                    if items:
                        print("non-zero: " + ", ".join("HR[%d]=%d" % it for it in items[:64]), flush=True)
                    else:
                        print("all registers are 0", flush=True)
                elif cmd == "clear":
                    with LOCK:
                        for i in range(len(REGS)):
                            REGS[i] = 0
                    print("cleared", flush=True)
                else:
                    print("unknown command: %s (type help)" % cmd, flush=True)
            except IndexError:
                print("missing arguments (type help)", flush=True)
            except ValueError:
                print("bad number (use decimal or 0x hex)", flush=True)
    except Exception:
        return False


# --------------------------------------------------------------------------
#  main
# --------------------------------------------------------------------------
def main():
    global VERBOSE, RUNNING, SHOW_FRAMES, CAPTURE_MS, TRIGGER_BASE, STRIDE

    ap = argparse.ArgumentParser(add_help=True)
    ap.add_argument("--host", default="127.0.0.1", help="bind address (default 127.0.0.1)")
    ap.add_argument("--port", type=int, default=502, help="bind port (default 502)")
    ap.add_argument("--quiet", action="store_true", help="suppress all logs")
    ap.add_argument("--trigger-base", type=int, default=0,
                    help="第 0 条流程的触发位地址（默认 0）")
    ap.add_argument("--stride", type=int, default=100,
                    help="每条流程占用的寄存器区间，与软件「多流程步长」一致（默认 100）")
    ap.add_argument("--only-result", action="store_true",
                    help="hide per-frame traces, keep [RESULT] blocks and connect/disconnect")
    ap.add_argument("--seed", action="store_true", help="preset HR[0..4] demo values")
    ap.add_argument("--pulse", type=int, default=-1, metavar="ADDR",
                    help="auto-pulse this register periodically")
    ap.add_argument("--pulse-sec", type=float, default=3.0, help="auto-pulse period (default 3s)")
    ap.add_argument("--pulse-ms", type=int, default=500, help="auto-pulse hold time (default 500ms)")
    ap.add_argument("--capture-ms", type=int, default=2500,
                    help="after a trigger, wait this long before reporting what was written (default 2500ms)")
    args = ap.parse_args()

    VERBOSE = not args.quiet
    SHOW_FRAMES = not args.only_result
    CAPTURE_MS = args.capture_ms
    TRIGGER_BASE = args.trigger_base
    STRIDE = max(0, args.stride)

    if args.seed:
        with LOCK:
            REGS[0] = 100      # e.g. Trigger / start signal
            REGS[1] = 1        # e.g. Heartbeat
            REGS[2] = 0        # e.g. Result OK
            REGS[3] = 1234     # e.g. X
            REGS[4] = 5678     # e.g. Y

    srv = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    srv.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    try:
        srv.bind((args.host, args.port))
    except OSError as ex:
        print("BIND FAILED %s:%d -> %s" % (args.host, args.port, ex))
        print("Hint: use --port 15020 (Windows may reserve low ports), then set the")
        print("      endpoint port in VisionSystem to the same value.")
        return 1

    srv.listen(8)
    print("Modbus TCP slave listening on %s:%d   (Ctrl+C / 'quit' to stop)"
          % (args.host, args.port), flush=True)
    print("VisionSystem endpoint -> kind=Modbus TCP, host=%s, port=%d, slaveId=1"
          % (args.host, args.port), flush=True)
    print("multi-flow zones: flow N trigger = HR[%d + N*%d]  (p 0 / p 1 / p 2 ...)"
          % (TRIGGER_BASE, STRIDE), flush=True)

    if args.pulse >= 0:
        auto_addr = TRIGGER_BASE + args.pulse * STRIDE
        print("auto-pulse: flow %d (HR[%d]) = 1 for %dms every %.1fs, result reported %dms later"
              % (args.pulse, auto_addr, args.pulse_ms, args.pulse_sec, args.capture_ms), flush=True)
        t = threading.Thread(target=auto_pulse_loop,
                             args=(auto_addr, args.pulse_sec, args.pulse_ms, args.capture_ms))
        t.daemon = True
        t.start()
    else:
        print("type 'help' for console commands  |  'p 0' = one trigger + auto result report",
              flush=True)

    def accept_loop():
        while RUNNING:
            try:
                conn, addr = srv.accept()
            except Exception:
                return
            t = threading.Thread(target=serve_client, args=(conn, addr))
            t.daemon = True
            t.start()

    at = threading.Thread(target=accept_loop)
    at.daemon = True
    at.start()

    try:
        quit_requested = run_console()
        if not quit_requested:
            # No interactive console (background / stdin redirected): keep serving.
            while RUNNING:
                time.sleep(0.5)
    except KeyboardInterrupt:
        pass
    finally:
        RUNNING = False
        try:
            srv.close()
        except Exception:
            pass
        print("stopped", flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
