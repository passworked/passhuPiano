"""Play a local SMF through the Windows standard MIDI output to the loopback input.
Default route: MIDI Out `Codex Piano Input` -> MIDI In `Codex Piano Send` (first input).
VRChat can explicitly select it with --midi="Codex Piano Send".
"""
import argparse
import ctypes
from ctypes import wintypes
from pathlib import Path
import sys
import time

sys.path.insert(0, str(Path(__file__).parent / "python_packages"))
import mido

class Caps(ctypes.Structure):
    _fields_ = [("mid", wintypes.WORD), ("pid", wintypes.WORD),
                ("version", wintypes.DWORD), ("name", wintypes.WCHAR * 32),
                ("technology", wintypes.WORD), ("voices", wintypes.WORD),
                ("notes", wintypes.WORD), ("channel_mask", wintypes.WORD),
                ("support", wintypes.DWORD)]

winmm = ctypes.WinDLL("winmm")
winmm.midiOutOpen.argtypes = [ctypes.POINTER(ctypes.c_void_p), wintypes.UINT,
                              ctypes.c_size_t, ctypes.c_size_t, wintypes.DWORD]
winmm.midiOutShortMsg.argtypes = [ctypes.c_void_p, wintypes.DWORD]
winmm.midiOutReset.argtypes = [ctypes.c_void_p]
winmm.midiOutClose.argtypes = [ctypes.c_void_p]


def ports():
    result = []
    for index in range(winmm.midiOutGetNumDevs()):
        caps = Caps()
        error = winmm.midiOutGetDevCapsW(index, ctypes.byref(caps), ctypes.sizeof(caps))
        if error == 0:
            result.append((index, caps.name))
    return result


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("file", nargs="?", default=str(Path(__file__).resolve().parents[2] / "Assets/music/Classical100/004_致爱丽丝 WoO 59.mid"))
    parser.add_argument("--port", default="Codex Piano Input")
    parser.add_argument("--delay", type=float, default=5)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    available = ports()
    print("MIDI output ports:", available, flush=True)
    selected = next((index for index, name in available if name == args.port), None)
    if selected is None:
        raise RuntimeError("Loopback output missing: " + args.port)
    song = mido.MidiFile(args.file)
    print(f"Ready: {Path(args.file).name}; duration {song.length:.1f}s; output {args.port} -> input Codex Piano Send", flush=True)
    if args.check:
        return
    handle = ctypes.c_void_p()
    error = winmm.midiOutOpen(ctypes.byref(handle), selected, 0, 0, 0)
    if error:
        raise RuntimeError(f"midiOutOpen failed: {error}")
    def send(data):
        packed = sum(value << (8 * index) for index, value in enumerate(data))
        error = winmm.midiOutShortMsg(handle, packed)
        if error:
            raise RuntimeError(f"midiOutShortMsg failed: {error}")
    count = 0
    stop = Path(__file__).parent / "STOP"
    try:
        print(f"Starting in {args.delay:g}s. Ctrl+C or create Tools/LiveMidi/STOP to stop.", flush=True)
        start = time.perf_counter() + max(0, args.delay)
        due = start
        for message in song:
            due += message.time
            while time.perf_counter() < due:
                if stop.exists():
                    return
                time.sleep(min(0.02, max(0, due - time.perf_counter())))
            if stop.exists():
                return
            if message.is_meta or message.type not in ("note_on", "note_off", "control_change"):
                continue
            send(message.bytes())
            count += 1
            if count % 128 == 0:
                print(f"Playing {time.perf_counter()-start:.1f}s; {count} MIDI messages sent", flush=True)
        print(f"Finished: {count} MIDI messages sent.", flush=True)
    finally:
        for channel in range(16):
            send([0xB0 | channel, 64, 0])
            send([0xB0 | channel, 123, 0])
            send([0xB0 | channel, 120, 0])
        winmm.midiOutReset(handle)
        winmm.midiOutClose(handle)

if __name__ == "__main__":
    main()
