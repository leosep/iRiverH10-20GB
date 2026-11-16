# Legal notice

## Independence

H10 Firmware Manager is an independent recovery and preservation tool. It is **not affiliated
with, endorsed by, sponsored by or supported by** iRiver, Reigncom, the Rockbox project, or any
other manufacturer or project mentioned in this repository. All trademarks belong to their
respective owners.

## Firmware ownership

The firmware images in `firmware/` and `recovery/burnkey/` are the property of iRiver/Reigncom and
their respective rights holders. They are included **only** to allow owners of iRiver H10 20GB
players to recover and preserve hardware they already own, after the official update channels
stopped being usable.

- No licence to the firmware is granted or implied by this repository.
- If you are a rights holder and want an image removed, open an issue and it will be removed.
- Do not redistribute the firmware for commercial purposes or as part of another product.

## Rockbox

Rockbox is a separate open-source project distributed under the GNU General Public License.
No Rockbox code is bundled in this repository or in the installer. The application only detects
Rockbox files on the player and can replace them with the original firmware at the user's request.
See <https://www.rockbox.org/> for the Rockbox licence and documentation.

## Risk

Flashing firmware and formatting drives are destructive operations.

- Formatting **erases all data** on the player, including music and any backup stored on it.
- Writing firmware can leave a player **unbootable** if interrupted, if the cable is faulty, or if
  the wrong image is used.
- Installing the firmware of a **different H10 model** can permanently damage the player.
- Opening the player to replace the hard drive voids any warranty and risks further damage.

The software is provided **"as is"**, without warranty of any kind, express or implied, including
but not limited to the warranties of merchantability, fitness for a particular purpose and
non-infringement. In no event shall the authors or copyright holders be liable for any claim,
damages or other liability, whether in an action of contract, tort or otherwise, arising from, out
of or in connection with the software or the use or other dealings in the software.

**You use this tool at your own risk.** Keep backups on your PC, not on the player.

## Privacy

The application does not collect telemetry and does not contact the network. It reads and writes
local drives, and stores settings, logs and backups under `%LOCALAPPDATA%\H10FirmwareManager`.

## Licence of this repository

The application source code, documentation and build scripts in this repository are provided for
personal recovery and preservation use. Unless a file states otherwise, no separate open-source
licence is granted for the source; the bundled firmware remains the property of its rights holders
as described above.
