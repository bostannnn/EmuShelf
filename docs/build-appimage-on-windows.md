# Building the SteamOS AppImage from a Windows workstation

CI builds `EmuShelf-linux-x64.AppImage` on Ubuntu (`.github/workflows/build.yml`). This document
covers reproducing that artifact locally from Windows using WSL, including the failure modes that
are easy to hit. Every command here has been run successfully on this workstation.

Output: `artifacts/EmuShelf-linux-x64.AppImage` plus `artifacts/EmuShelf-linux-x64.sha256`.

## The two scripts

- `packaging/appimage/build-appimage.sh` — the real packaging step. Takes a publish directory and
  an output path, assembles the AppDir, bundles required host libraries, and runs `appimagetool`.
  This is the script CI uses. **Do not modify it** to work around a local environment.
- `packaging/appimage/build-wsl-appimage.sh` — a convenience wrapper that copies the whole source
  tree into WSL, runs `dotnet test`, publishes, then calls the script above.

**Prefer the wrapper only on a small working tree.** It runs `cp -a "$source_root/."`, which copies
everything including `.git`, `artifacts/`, and every `bin/obj` (it prunes `bin`/`obj` *after*
copying). This tree has grown to ~11 GB, so that copy over the `/mnt/c` DrvFs mount takes far longer
than the build itself. The procedure below skips it by cross-publishing on Windows and invoking
`build-appimage.sh` directly. Same script, same artifact; it only skips the Linux-side `dotnet test`.

## Prerequisites

| Requirement | This workstation | Notes |
| --- | --- | --- |
| WSL distro | `Ubuntu` (26.04) | **The default distro is `docker-desktop`, which has no `bash`.** Always pass `-d Ubuntu`. |
| .NET SDK (Windows) | `C:\Users\Andre\.dotnet\dotnet` | No system-wide SDK; use the user-local path. |
| .NET SDK (WSL) | `~/.dotnet/dotnet` (10.0.302) | Only needed for the wrapper script, not for the procedure below. |
| `appimagetool` | `~/appimagetool.AppImage` | Must be on `PATH` as `appimagetool`; see below. |
| `libICE`, `libSM` | **not installed** | `build-appimage.sh` fails without them; see "Bundled libraries". |

Put `appimagetool` on `PATH` (no root required):

```bash
mkdir -p ~/.local/bin
cp -f ~/appimagetool.AppImage ~/.local/bin/appimagetool
chmod +x ~/.local/bin/appimagetool
```

If it is not present at all, fetch the same build CI uses:

```bash
curl -L -o ~/.local/bin/appimagetool \
  https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage
chmod +x ~/.local/bin/appimagetool
```

## Step 1 — cross-publish from Windows

Run in PowerShell. The flags must match CI exactly (`InvariantGlobalization=false` keeps ICU
enabled, which the AppImage relies on):

```powershell
& "$env:USERPROFILE\.dotnet\dotnet" publish `
  "C:\Users\Andre\Desktop\OpenEmu\src\EmuShelf.App\EmuShelf.App.csproj" `
  -c Release -r linux-x64 --self-contained true -p:InvariantGlobalization=false `
  -o "C:\Users\Andre\Desktop\OpenEmu\artifacts\publish-linux"
```

Confirm the publish root contains both `EmuShelf` and `libSDL2.so`. The SDL2 native is what makes
native controller input work on the Deck; if it is missing, the gamepad silently falls back to
Steam Input only.

## Step 2 — bundled libraries (libICE / libSM)

`build-appimage.sh` copies five host libraries into the AppDir — `libicudata`, `libicui18n`,
`libicuuc`, `libICE`, `libSM` — locating each via `ldconfig -p`. It exits with
`Required bundled library <name> was not found` if any is absent.

`libICE`/`libSM` are the X11 session-management libraries Avalonia dlopens during platform init.
Minimal SteamOS/gamescope images do not ship them, so without bundling the app aborts with
`System.DllNotFoundException: Unable to load shared library 'libICE.so.6'` **before any window
appears**. Never "fix" a local build by deleting them from the script's list — that produces an
AppImage that dies instantly in Gaming Mode.

### Path A — install them (preferred, needs sudo)

```bash
sudo apt-get install -y libice6 libsm6
```

Then skip to Step 3 and drop the `PATH="$shim:..."` prefix.

### Path B — no sudo available

Download the official `.debs` and extract them (both work as a normal user), then expose them to
the script's `ldconfig` lookup through a shim, leaving `build-appimage.sh` untouched:

```bash
mkdir -p ~/emushelf-libs && cd ~/emushelf-libs
apt-get download libice6 libsm6
for deb in *.deb; do dpkg -x "$deb" ~/emushelf-libs/root; done
```

This yields `~/emushelf-libs/root/usr/lib/x86_64-linux-gnu/{libICE.so.6,libSM.so.6}`.

> **Gotcha — exit code 141.** `build-appimage.sh` runs `ldconfig -p | awk '... { print $NF; exit }'`.
> `awk` exits at the first match and closes the pipe, so a naive shim is killed by `SIGPIPE`; with
> `set -o pipefail` the whole build aborts with status 141 and no error text. The shim below traps
> `PIPE` and always exits 0.

## Step 3 — build the AppImage

Save as `~/make-appimage.sh` and run with `wsl -d Ubuntu -e bash ~/make-appimage.sh`:

```bash
#!/usr/bin/env bash
set -euo pipefail

repo=/mnt/c/Users/Andre/Desktop/OpenEmu
libs="$HOME/emushelf-libs/root/usr/lib/x86_64-linux-gnu"   # Path B only
out="$repo/artifacts/EmuShelf-linux-x64.AppImage"

# --- Path B only: SIGPIPE-safe ldconfig shim (see gotcha above) ---
real_ldconfig=$(command -v ldconfig || echo /sbin/ldconfig)
shim=$(mktemp -d)
cat > "$shim/ldconfig" <<EOF
#!/usr/bin/env bash
trap '' PIPE
if [ "\${1:-}" = "-p" ]; then
  {
    "$real_ldconfig" -p
    printf '\tlibICE.so.6 (libc6,x86-64) => %s\n' "$libs/libICE.so.6"
    printf '\tlibSM.so.6 (libc6,x86-64) => %s\n' "$libs/libSM.so.6"
  } 2>/dev/null
  exit 0
fi
exec "$real_ldconfig" "\$@"
EOF
chmod +x "$shim/ldconfig"

# Stage the Windows publish on a native filesystem so real Unix permissions apply. A publish
# produced on Windows carries no executable bit, and AppRun execs usr/bin/EmuShelf directly.
stage=$(mktemp -d)
trap 'rm -rf "$shim" "$stage"' EXIT
mkdir -p "$stage/EmuShelf"
cp -a "$repo/artifacts/publish-linux/." "$stage/EmuShelf/"
chmod +x "$stage/EmuShelf/EmuShelf"
find "$stage/EmuShelf" -name '*.so' -exec chmod +x {} +

# Keep the previous artifact until the new one validates.
if [ -f "$out" ]; then mv -f "$out" "$out.prev"; fi

cd "$repo"                                   # build-appimage.sh reads packaging/appimage/* from CWD
export PATH="$shim:$HOME/.local/bin:$PATH"   # drop "$shim:" when using Path A
bash packaging/appimage/build-appimage.sh "$stage/EmuShelf" "$out"
chmod +x "$out"

"$out" --appimage-extract-and-run --version
cd "$repo/artifacts"
sha256sum EmuShelf-linux-x64.AppImage > EmuShelf-linux-x64.sha256
sha256sum -c EmuShelf-linux-x64.sha256
```

## Step 4 — verify before shipping it to the Deck

A file being produced is not sufficient. Check all four:

1. **It executes.** `./EmuShelf-linux-x64.AppImage --appimage-extract-and-run --version` prints
   `EmuShelf`. This proves the exec bits survived and the .NET runtime loads.
2. **SDL2 is in the payload** — required for native controller input:
   ```bash
   ./EmuShelf-linux-x64.AppImage --appimage-extract >/dev/null
   ls squashfs-root/usr/bin/libSDL2.so
   ```
3. **The five libraries are bundled:**
   ```bash
   ls squashfs-root/usr/lib
   # libICE.so.6  libSM.so.6  libicudata.so.*  libicui18n.so.*  libicuuc.so.*
   ```
4. **Checksum recorded** — `sha256sum -c EmuShelf-linux-x64.sha256` reports `OK`.

Expected size is roughly 59 MB. Older builds were ~101 MB; current `appimagetool` compresses with
zstd instead of gzip, so a smaller file is not a truncated payload — confirm via the uncompressed
filesystem size in the `mksquashfs` output (~153 MB).

## Deploying to the Steam Deck

EmuShelf is portable: `Data/`, `Covers/`, `Cache/`, `Logs/`, and `Settings/` live beside the
`.AppImage` (`AppPaths` uses the writable parent of `$APPIMAGE`, never the read-only `$APPDIR`).
Copy the new AppImage into the same directory as the existing install to keep the library; place it
elsewhere and it starts empty.

```bash
chmod +x EmuShelf-linux-x64.AppImage
./EmuShelf-linux-x64.AppImage --gamepad-ui
```

If FUSE is unavailable: `./EmuShelf-linux-x64.AppImage --appimage-extract-and-run --gamepad-ui`.

## Failure reference

| Symptom | Cause |
| --- | --- |
| `execvpe(bash) failed` | Ran against the default `docker-desktop` distro. Use `wsl -d Ubuntu`. |
| Exit 141, no message | `SIGPIPE` in the `ldconfig` shim. Use the `trap '' PIPE` version. |
| `Required bundled library libICE was not found` | Step 2 not done. |
| Copy step runs for many minutes | Used `build-wsl-appimage.sh` against the ~11 GB tree. |
| App exits instantly on the Deck, no window | `libICE`/`libSM` missing from the AppImage. |
| Library empty on the Deck | AppImage not placed beside the existing `Data/`. |
