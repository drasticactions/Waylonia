# Tools

Each tool is a compositor method with `/` changed to `_`. The tool's own schema is the authority;
this page tells you which one to pick. Window ids come from `windows_list` or `windows_wait`, and
a string of digits is accepted wherever an id is. `process_spawn` and the `seat_*` methods are
left out of an agent session; `waylonia_launch` and `input_*` replace them.

## Session

| Tool | Use |
| --- | --- |
| `waylonia_describe` | Profile, output size and scale, whether launches wait for approval, missing methods, accessibility state. Call first. |
| `waylonia_launchable` | The installed applications `waylonia_launch` can start by `desktop_id`. |
| `session_describe`, `outputs_list` | The compositor and its one output. |

## Programs

| Tool | Use |
| --- | --- |
| `waylonia_launch` | `command` + `args`, or `desktop_id`. No shell. Starts at once unless the profile holds launches. Returns `launch_id` and `pid`. |
| `process_list` | Every launch: argv, running or not, exit code or signal. |
| `process_log` | The last `lines` of a launch's stdout and stderr. |
| `process_kill` | SIGTERM to the launch's process group, SIGKILL after `grace_ms`. May need approval. |

## Windows

| Tool | Use |
| --- | --- |
| `windows_wait` | Wait for a window by exact `app_id` or `title` substring. Default `timeout_ms` 5000. |
| `windows_wait-idle` | Wait until a window (or every window) has drawn nothing for `quiet_ms` (default 300). Call before a capture or a read. |
| `windows_list`, `windows_get` | Ids, app ids, titles, `client_geometry`, state. |
| `waylonia_window-info` | The `launch_id` that owns a window, X11 or not, dialog and parent, and whether its tree was found. |
| `windows_activate` | Focus and raise. |
| `windows_set-state` | `maximized`, `minimized`, `fullscreen`. |
| `windows_close` | Ask the client to close. May need approval. |

A dialog is its own window. After an action that opens one, call `windows_wait` by its title or
`windows_list` again.

## Accessibility

Present only when `waylonia_describe` says accessibility is up. Node ids are `busname:objectpath`.
Boxes are in the window's client-area coordinates, the same ones `input_*` takes with `window`.

| Tool | Use |
| --- | --- |
| `waylonia_a11y-tree` | The tree as indented text: `[node] role "name" {states} @x,y wxh`. `depth` default 12, `max_nodes` default 400. |
| `waylonia_a11y-find` | Nodes by `role` (`push-button`, `text`, `entry`, `menu-item`, `link`, `check-box`, …), `name` substring and `states` (`showing`, `enabled`, `focusable`, …). |
| `waylonia_a11y-action` | Run a node's action by name (`click`, `activate`, `press`, `toggle`), or its first one. |
| `waylonia_a11y-set-text` | Replace the whole text of an editable node. |
| `waylonia_a11y-set-value` | Set a slider or spin button. |
| `waylonia_a11y-focus` | Give a node the keyboard focus. |
| `waylonia_a11y-click` | Click a node's center through the compositor, for a node with no action. |
| `waylonia_a11y-wait-text` | Wait until some node under a window has a name or text containing `text`. Default `timeout_ms` 5000. |

## Screenshots

| Tool | Use |
| --- | --- |
| `capture_window` | One window. `client_only` leaves out the frame. |
| `capture_output` | The whole output. |
| `capture_region` | A rectangle in layout coordinates. |

Without `path` the image comes back inline, scaled until its longer side is at most 1024 pixels
(`max_dimension` changes that). With an absolute `path` the full-size PNG is written there and the
reply is text; read the file when you need it.

## Input

These go in where a physical device's input goes, so compositor shortcuts see them. With `window`,
`x` and `y` are relative to that window's client area and a covered point is refused; `raise`
activates the window first.

| Tool | Use |
| --- | --- |
| `input_pointer-button` | Click (`button` `left`, `right`, `middle`) at `x`, `y`. With `pressed`, only press or release, for a drag. |
| `input_pointer-move` | Move the pointer, for hover or a drag. |
| `input_axis` | Scroll: positive `value` is down or right. |
| `input_text` | Type a string. Through text input when the client has it, else through the keymap. |
| `input_key` | Tap or press an evdev keycode: 1 Esc, 14 Backspace, 15 Tab, 28 Enter, 57 Space, 103 Up, 108 Down, 105 Left, 106 Right, 102 Home, 107 End, 104 PageUp, 109 PageDown, 111 Delete. |
| `input_chord` | A shortcut with xkb names: `Control+s`, `Control+Shift+t`, `Alt+F4`. |
| `input_touch` | Touch `down`, `motion`, `up`, then `frame`. |

## Clipboard

`clipboard_read` and `clipboard_write` use the session's own clipboard, not the user's. Either may
need approval.

## The bridge

| Tool | Use |
| --- | --- |
| `bridge_status` | Whether `basin-mcp` is connected to the compositor, and why the last attempt failed. |
| `wait_event` | Wait for a compositor event, with `then` to run the method that causes it once the wait is live. `windows_wait` and `waylonia_a11y-wait-text` cover the usual cases. Never name `approval/requested`. |

## Not for you

`approval_answer` answers the approvals that are the user's to answer. `session_quit` ends the
session and every application in it. `outputs_*`, `workspaces_*`, `idle_*`, `lock_status` and
`keyboard_keymap` are published but an agent task does not need them.
