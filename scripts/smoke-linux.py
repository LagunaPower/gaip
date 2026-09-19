"""Start the real published GUI on X11/WSLg, then close its own native window."""
import ctypes as c
import ctypes.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import time

binary = Path(sys.argv[1]).resolve()
output = Path(sys.argv[2]).resolve()
output.mkdir(parents=True, exist_ok=True)
x = c.CDLL(ctypes.util.find_library('X11'))
x.XOpenDisplay.argtypes = [c.c_char_p]; x.XOpenDisplay.restype = c.c_void_p
x.XDefaultRootWindow.argtypes = [c.c_void_p]; x.XDefaultRootWindow.restype = c.c_ulong
x.XInternAtom.argtypes = [c.c_void_p, c.c_char_p, c.c_int]; x.XInternAtom.restype = c.c_ulong
x.XQueryTree.argtypes = [c.c_void_p, c.c_ulong, c.POINTER(c.c_ulong), c.POINTER(c.c_ulong), c.POINTER(c.POINTER(c.c_ulong)), c.POINTER(c.c_uint)]
x.XGetWindowProperty.argtypes = [c.c_void_p, c.c_ulong, c.c_ulong, c.c_long, c.c_long, c.c_int, c.c_ulong, c.POINTER(c.c_ulong), c.POINTER(c.c_int), c.POINTER(c.c_ulong), c.POINTER(c.c_ulong), c.POINTER(c.POINTER(c.c_ubyte))]
x.XFree.argtypes = [c.c_void_p]
x.XCloseDisplay.argtypes = [c.c_void_p]
x.XFlush.argtypes = [c.c_void_p]
display = x.XOpenDisplay(None)
if not display:
    raise RuntimeError('No X11/WSLg display available')
root = x.XDefaultRootWindow(display)

def prop(window, name):
    actual, fmt, count, rest, data = c.c_ulong(), c.c_int(), c.c_ulong(), c.c_ulong(), c.POINTER(c.c_ubyte)()
    result = x.XGetWindowProperty(display, window, x.XInternAtom(display, name, 0), 0, 1024, 0, 0, c.byref(actual), c.byref(fmt), c.byref(count), c.byref(rest), c.byref(data))
    if result or not data:
        return None
    try:
        if fmt.value == 32:
            return list(c.cast(data, c.POINTER(c.c_ulong))[:count.value])
        return c.string_at(data, count.value).decode('utf-8', errors='replace')
    finally:
        x.XFree(data)

def own_window(window, pid, depth=0):
    if prop(window, b'_NET_WM_PID') == [pid]:
        title = prop(window, b'_NET_WM_NAME') or prop(window, b'WM_NAME') or ''
        if isinstance(title, str) and title.startswith('G@IP'):
            return window, title
    if depth > 3:
        return None
    tree_root, parent, children, count = c.c_ulong(), c.c_ulong(), c.POINTER(c.c_ulong)(), c.c_uint()
    if not x.XQueryTree(display, window, c.byref(tree_root), c.byref(parent), c.byref(children), c.byref(count)):
        return None
    try:
        for child in children[:count.value]:
            found = own_window(child, pid, depth + 1)
            if found:
                return found
    finally:
        if children:
            x.XFree(children)

class MessageData(c.Union):
    _fields_ = [('l', c.c_long * 5), ('b', c.c_char * 20)]

class ClientMessage(c.Structure):
    _fields_ = [('type', c.c_int), ('serial', c.c_ulong), ('send_event', c.c_int), ('display', c.c_void_p), ('window', c.c_ulong), ('message_type', c.c_ulong), ('format', c.c_int), ('data', MessageData)]

class XEvent(c.Union):
    _fields_ = [('client', ClientMessage), ('pad', c.c_long * 24)]

x.XSendEvent.argtypes = [c.c_void_p, c.c_ulong, c.c_int, c.c_long, c.POINTER(XEvent)]
try:
    with tempfile.TemporaryDirectory(prefix='gaip-native-smoke-') as workspace:
        standalone = Path(workspace) / 'GAIP'
        shutil.copy2(binary, standalone); standalone.chmod(0o700)
        env = os.environ.copy()
        env.update(XDG_DATA_HOME=workspace + '/data', XDG_CONFIG_HOME=workspace + '/config', DOTNET_BUNDLE_EXTRACT_BASE_DIR=workspace + '/bundle')
        env.pop('GAIP_USE_X11', None)  # Exercise the normal WSLg backend decision.
        with (output / 'stdout.log').open('wb') as stdout, (output / 'stderr.log').open('wb') as stderr:
            process = subprocess.Popen([str(standalone)], env=env, stdout=stdout, stderr=stderr)
            try:
                deadline, found = time.monotonic() + 40, None
                while process.poll() is None and time.monotonic() < deadline:
                    found = own_window(root, process.pid)
                    if found and (Path(workspace) / 'data/GAIP/local/gaip-data.json').exists():
                        break
                    time.sleep(0.2)
                if not found or process.poll() is not None:
                    raise RuntimeError('G@IP did not open its real native window; check stderr.log')
                time.sleep(2)
                event = XEvent()
                event.client.type = 33; event.client.display = display; event.client.window = found[0]
                event.client.message_type = x.XInternAtom(display, b'WM_PROTOCOLS', 0); event.client.format = 32
                event.client.data.l[0] = x.XInternAtom(display, b'WM_DELETE_WINDOW', 0)
                x.XSendEvent(display, found[0], 0, 0, c.byref(event)); x.XFlush(display)
                code = process.wait(timeout=10)
                if code != 0:
                    raise RuntimeError(f'Unexpected exit code: {code}')
                if not (Path(workspace) / 'data/GAIP/local/gaip-data.json').exists():
                    raise RuntimeError('Native UI opened but data initialization failed')
                result = dict(platform='Linux WSLg', binary=str(binary), windowTitle=found[1], exitCode=code, display=env.get('DISPLAY'), waylandDisplay=env.get('WAYLAND_DISPLAY'), sessionType=env.get('XDG_SESSION_TYPE'), isolatedExecutable=True)
                (output / 'result.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
                print(json.dumps(result, indent=2))
            finally:
                if process.poll() is None:
                    process.terminate()
                    try:
                        process.wait(timeout=5)
                    except subprocess.TimeoutExpired:
                        process.kill(); process.wait()
finally:
    x.XCloseDisplay(display)
