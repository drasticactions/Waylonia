---
name: waylonia-agent
description: Open, use, test, screenshot or record Linux desktop (GUI) applications in a private Waylonia agent session through the waylonia MCP tools. Use when the user asks to run a graphical program, check how an app looks, click or type into a window, fill a form, test a GTK, Qt, Firefox or X11 app, read what a window shows, check an animation or a flicker, or compare a window with a reference image, and whenever the waylonia MCP tools (waylonia_launch, windows_list, capture_window, waylonia_a11y-find) are present.
---

# Waylonia agent session

The `waylonia` MCP server runs `basin-mcp --launch -- waylonia --agent claude --headless`. That is a
private Wayland compositor with no window on the user's screen. The applications you start run
there, with their own home directory, session bus and clipboard. You see them only through
screenshots and accessibility trees, and you act on them only through these tools.

The tool names are the compositor's method names with `/` changed to `_`: `waylonia/launch` is
`waylonia_launch`, `input/text` is `input_text`. Claude Code prefixes them with the plugin and
server name (`mcp__plugin_waylonia-agent_waylonia__windows_list`). The full list with parameters is
in [references/methods.md](references/methods.md).

## The loop

1. **Orient.** Call `waylonia_describe` once. It tells you the output size, whether launches wait
   for approval, whether accessibility is up and which methods are missing. `waylonia_launchable`
   lists the installed applications you can start by `desktop_id`; any program on `PATH` starts by
   `command`. Right after the server starts, accessibility can say `starting`; it is `up` by the time
   the first window has drawn.
2. **Launch.** `waylonia_launch` with `command` (and `args`) or `desktop_id`. There is no shell, so
   no pipes, `&&`, globs or `~`. Keep the returned `launch_id`.
3. **Wait for the window.** `windows_wait` with the `app_id` (exact) or part of the `title`, then
   `windows_wait-idle` on its id before you read or capture it. Do not sleep and poll
   `windows_list`. The geometry in `windows_wait`'s reply is 0×0 until the window has drawn;
   call `windows_get` after the idle wait when you need its size. Windows map maximized to the
   whole output by default. To test how an app behaves at another size or position, restore it
   with `windows_set-state` (`maximized: false`), then use `windows_move` and `windows_resize`;
   a maximized window refuses both.
4. **Read the window.** Prefer the accessibility tree (see *Acting*). Take a screenshot with
   `capture_window` when there is no tree, when layout or rendering is the question, or when the
   user wants to see the result.
5. **Act**, then **verify**: `waylonia_a11y-wait-text` for text you expect, or `windows_wait-idle`
   and a new read. Never report success from the action's reply alone.
6. **Finish.** Close what you opened with `windows_close` or `process_kill` when the task is done,
   unless the user wants it left open. Everything you started ends when the MCP server stops.

## Acting: accessibility first

When `waylonia_describe` says accessibility is up, use the tree before coordinates. It does not
need a screenshot, it works on covered or scrolled-away widgets, and it does not break when the
layout moves.

- `waylonia_a11y-find` with `window` plus `role`, `name` (substring, any case) and `states` finds
  nodes. `waylonia_a11y-tree` shows the whole tree when you do not know what to look for. Keep
  `depth` and `max_nodes` small at first; large trees cost context.
- `waylonia_a11y-action` presses buttons, menu items, check boxes and links. Without `action` it
  runs the node's first action. A wrong name lists the node's actions in the error.
- `waylonia_a11y-set-text` replaces the whole text of an entry or document.
  `waylonia_a11y-set-value` sets sliders and spin buttons. `waylonia_a11y-focus` moves the keyboard
  focus to a node before `input_text` or `input_key`.
- `waylonia_a11y-click` clicks a node's center through the compositor, for a node with no action
  (Firefox links, for example). The node must be visible and not covered.
- `waylonia_a11y-wait-text` waits for text to appear under a window. Use it to verify a result and
  to wait for a page or dialog to load.

Node ids (`busname:objectpath`) are valid only while that widget exists. Find the node again after
the window changes.

Fall back to coordinates when a widget has no node, or its node has no usable action:

1. `capture_window` with the window id. The image is scaled so its longer side is at most 1024
   pixels. With `path` the full-size PNG goes to a file and you get text back.
2. Convert what you see to the window's client-area coordinates. If the image was scaled, scale
   the point back up by the window's `client_geometry` size from `windows_get`.
3. `input_pointer-button` with `window`, `x`, `y` and `button`. Coordinates with `window` are
   relative to its client area; the call is refused when another window covers the point.
4. Type with `input_text`. It types the whole string, through the client's text input when it has
   one. Use `input_key` (evdev codes: 28 Enter, 1 Escape, 15 Tab, 14 Backspace) or `input_chord`
   (`Control+s`, `Alt+F4`) for keys and shortcuts.

Toolkits differ in how complete their trees are. GTK 3 and 4, Qt 6 and Firefox work. Chromium and
Electron need `--force-renderer-accessibility`; pass it in `args` when you launch them. An X11
app has a tree only if its toolkit exports one.

## X11 apps

An X11 window is an ordinary window: `windows_list` shows it once, with `x11_display`, and every
`windows_*`, `input_*` and capture tool works on it. Most toolkits prefer Wayland when both are
there. To test an app as an X11 client, launch it through `env` with a Wayland display that does
not exist: `command` `env`, `args` `["WAYLAND_DISPLAY=no-wayland", "APP", ...]`. Removing the
variable with `env -u` is not enough: libwayland then tries `wayland-0` in the session's runtime
directory, which is the session's own socket. Add `GDK_BACKEND=x11` for GTK or
`QT_QPA_PLATFORM=xcb` for Qt if the app still opens on Wayland.

## Motion and regressions: recordings

A screenshot shows one moment. When the question is about change over time (an animation, a
transition, a flicker, a spinner that never stops, a redraw after an action), record the window.

1. Wait as for a screenshot: `windows_wait`, then `windows_wait-idle`. A window that has not drawn
   its first frame is refused with "not in the scene".
2. `diag_record-window` with `window` (an id or an `app_id`) and `frames` (1 to 600). It follows
   the window if it moves. `diag_record` takes a rectangle of the output instead (`x`, `y`,
   `width`, `height`). The call returns only when its frames are in, so act first and record
   right after it: the frames are what is drawn after the call starts. An animation shorter than
   the gap between the two calls is over before the recording starts.
3. Read the numbers before the image. Each frame has `luma` (mean brightness, 0 to 255), `peak`
   (the brightest 2%) and `delta` (pixels changed since the frame before), and the reply has their
   ranges. `delta_max` 0 means nothing changed. A wide `peak_low`..`peak_high` range with a steady
   `luma` is a flicker. `timed_out` means fewer frames arrived than you asked for in `timeout_ms`
   (default 10000).
4. Look at the image when the numbers are not enough. It is one PNG with one band per frame, top to
   bottom. Inline, each band is scaled to at most 1024 pixels on its longer side, so keep `frames`
   to about 8 inline. For more, pass an absolute `path` and read the file, or a smaller
   `max_dimension`.

Frames are not evenly spaced. A window that is drawing gives up to one frame every 16 ms; an idle
one gives about one every 100 ms. Do not compute durations from the frame count.

To check that a window still looks as it did, write a reference once with `diag_record-window`,
`frames` 1 and an absolute `path`, then call `diag_compare` with `window` and `reference` set to
that file. It passes when at most `pixels` (default 0) pixels differ by more than `channel`
(default 2) in any channel. `diff_path` writes the differences in red; `inline` returns them as an
image. A window that changed size fails the comparison and does not raise an error.

Only one recording runs at a time. A new one ends the one before it with the frames it has.

## Guard rails

- **A denied launch.** Launches start at once unless the user put `launch` in `[approve] actions`
  in `~/.local/state/waylonia/agents/claude/agent.toml`; then each one waits for the user's
  approval (Claude Code shows a prompt). When a launch is denied, stop and ask the user what to do
  next. Do not edit `agent.toml` yourself and do not try another name for the same program. The
  format is in [references/profile.md](references/profile.md).
- **Approvals are the user's.** Some calls (`windows_close`, `process_kill`, clipboard access,
  launches, as the profile says) wait for a person. Never call `approval_answer`, never
  name `approval/requested` in `wait_event`, and never connect to the control socket yourself to
  answer your own approval. A denial is the answer; report it and ask what to do next.
- **Paused.** `paused: someone is using the window` means a person took over a visible session.
  Stop acting, tell the user, and wait for them to say it is resumed. Do not retry in a loop.
- **Not a sandbox.** The applications run as the user, on the user's files and network. The
  profile keeps them off the user's desktop, clipboard and windows, nothing more. Do not use the
  session to run something you would not run in the user's shell, and do not open files outside
  what the task needs.
- **Everything is recorded.** Each call goes to
  `~/.local/state/waylonia/agents/claude/audit/*.jsonl`, with before and after screenshots of calls
  that change something. Say so if the user asks what you did.

## When something goes wrong

- **An app starts but no window appears.** Read its output with `process_log` and the
  `launch_id`, and check `process_list` for an exit code. The logs are also in
  `~/.local/state/waylonia/agents/claude/logs/`.
- **`windows_wait` times out.** The `app_id` must match exactly. Call `windows_list` to see the
  real one, or wait by `title`.
- **The a11y methods are missing.** `waylonia_describe` says why: accessibility is off in the
  profile or at-spi2-core is not installed. Use screenshots and input instead, and tell the user.
- **The capture is black or empty.** Wait with `windows_wait-idle` first; the client may not have
  drawn yet.
- **The `diag_*` tools are missing.** The installed `waylonia` is older than agent-mode
  recordings. Tell the user to reinstall it (`scripts/install-waylonia.sh` in the Waylonia
  repository) and restart the MCP server.
- **The tools are gone.** The server stopped or is not running. Ask the user to check `/mcp`. If
  it says another run holds the profile, another Claude Code session is using the `claude`
  profile; only one run may hold it.
