#!/usr/bin/env python3
"""Send portal-selected PipeWire screen frames to the desktop over stdout.

The stream is an eight-byte little-endian width/height header followed by
fixed-size RGBA frames. stderr is reserved for diagnostics.
"""

import os
import struct
import sys
import traceback
import uuid

import gi

gi.require_version("Gst", "1.0")
from gi.repository import Gio, GLib, Gst


PORTAL = "org.freedesktop.portal.Desktop"
OBJECT = "/org/freedesktop/portal/desktop"
INTERFACE = "org.freedesktop.portal.ScreenCast"
WIDTH = 1920
HEIGHT = 1080
FPS = 30


def options(**values):
    return values


def request(connection, method, arguments):
    token = "vento_" + uuid.uuid4().hex
    result = {}
    loop = GLib.MainLoop()

    def response(_connection, _sender, path, _interface, _signal, payload):
        if not path.endswith("/" + token):
            return
        result["value"] = payload.unpack()
        loop.quit()

    subscription = connection.signal_subscribe(
        PORTAL, "org.freedesktop.portal.Request", "Response", None, None,
        Gio.DBusSignalFlags.NONE, response)
    try:
        connection.call_sync(PORTAL, OBJECT, INTERFACE, method, arguments(token),
                             GLib.VariantType("(o)"), Gio.DBusCallFlags.NONE, 15000, None)

        def timeout():
            result["timeout"] = True
            loop.quit()
            return False

        timer = GLib.timeout_add_seconds(90, timeout)
        try:
            if "value" not in result:
                loop.run()
        finally:
            if not result.get("timeout"):
                GLib.source_remove(timer)
        if result.get("timeout"):
            raise RuntimeError("A seleção da tela demorou demais.")
        status, data = result["value"]
        if status != 0:
            raise RuntimeError("A seleção da tela foi cancelada ou recusada.")
        return {key: value.unpack() if isinstance(value, GLib.Variant) else value
                for key, value in data.items()}
    finally:
        connection.signal_unsubscribe(subscription)


def main():
    connection = Gio.bus_get_sync(Gio.BusType.SESSION, None)
    created = request(connection, "CreateSession", lambda token: GLib.Variant("(a{sv})", (
        options(handle_token=GLib.Variant("s", token),
                session_handle_token=GLib.Variant("s", "vento_" + uuid.uuid4().hex)),)))
    session = created["session_handle"]
    request(connection, "SelectSources", lambda token: GLib.Variant("(oa{sv})", (
        session, options(handle_token=GLib.Variant("s", token),
                         types=GLib.Variant("u", 3),
                         multiple=GLib.Variant("b", False),
                         cursor_mode=GLib.Variant("u", 2)))))
    started = request(connection, "Start", lambda token: GLib.Variant("(osa{sv})", (
        session, "", options(handle_token=GLib.Variant("s", token)))))
    streams = started["streams"]
    if not streams:
        raise RuntimeError("Nenhuma tela foi selecionada.")
    node_id = streams[0][0]
    reply, fd_list = connection.call_with_unix_fd_list_sync(
        PORTAL, OBJECT, INTERFACE, "OpenPipeWireRemote",
        GLib.Variant("(oa{sv})", (session, options())), GLib.VariantType("(h)"),
        Gio.DBusCallFlags.NONE, 15000, None, None)
    pipewire_fd = fd_list.get(reply.unpack()[0])

    Gst.init(None)
    pipeline = Gst.parse_launch(
        f"pipewiresrc fd={pipewire_fd} path={node_id} do-timestamp=true ! "
        "queue max-size-buffers=1 leaky=downstream ! videoconvert ! videoscale add-borders=true ! videorate ! "
        f"video/x-raw,format=RGBA,width={WIDTH},height={HEIGHT},framerate={FPS}/1 ! "
        "appsink name=frames sync=false max-buffers=1 drop=true")
    sink = pipeline.get_by_name("frames")
    if pipeline.set_state(Gst.State.PLAYING) == Gst.StateChangeReturn.FAILURE:
        raise RuntimeError("Não foi possível iniciar o PipeWire.")
    output = sys.stdout.buffer
    output.write(struct.pack("<II", WIDTH, HEIGHT))
    output.flush()
    try:
        while True:
            sample = sink.emit("try-pull-sample", Gst.SECOND * 3)
            if sample is None:
                message = pipeline.get_bus().pop_filtered(Gst.MessageType.ERROR | Gst.MessageType.EOS)
                if message is not None:
                    if message.type == Gst.MessageType.ERROR:
                        _, detail = message.parse_error()
                        raise RuntimeError(detail)
                    break
                continue
            buffer = sample.get_buffer()
            mapped, info = buffer.map(Gst.MapFlags.READ)
            if not mapped:
                continue
            try:
                if info.size == WIDTH * HEIGHT * 4:
                    output.write(info.data)
                    output.flush()
            finally:
                buffer.unmap(info)
    finally:
        pipeline.set_state(Gst.State.NULL)
        os.close(pipewire_fd)
        connection.call_sync(PORTAL, session, "org.freedesktop.portal.Session", "Close",
                             None, None, Gio.DBusCallFlags.NONE, 5000, None)


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        traceback.print_exc(file=sys.stderr)
        sys.exit(1)
