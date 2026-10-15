# Use your Moto G45 to continue Wayfinder

Updated: 15 October 2026. **No verified APK exists yet.**

## The important distinction

Your phone can **control a build computer, download an APK and test the game**.
The supported desktop Unity Editor does **not** run directly on Android. Installing
Termux, an APK editor, Unity Remote, or a code editor on the Moto G45 does not turn
it into a supported Unity/IL2CPP/Android build machine. Unity Remote also requires
an editor running elsewhere; it does not compile a standalone game.

This sandbox still has no Unity editor or Android SDK and cannot access Unity's
download/package services. Those environmental blockers cannot be repaired by
changing C# files or pretending headless tests are Unity results. The source fixes
below are useful progress, but an installed, activated editor must validate them.

## What I continued in source

- Android starts with a 30 FPS target, with a 30/60 target toggle in the prototype
  HUD. These are targets, not measured frame-rate guarantees on the Moto G45.
- Added a pure, tested `MobileLifecycleGate`. Android pause and focus are handled
  independently: a focus-gained callback cannot resume a still-paused application,
  and an unpause callback cannot resume a still-unfocused application. Repeated
  callbacks do not emit duplicate suspension transitions. Gameplay still waits
  for an explicit Resume tap.
- Connected authored `WaterVolume` triggers to vehicle water-depth sampling and
  the existing one-incident engine-stall rule. Teleported/disabled volumes are
  ignored. The proving-ground water fixture now has a deeper test level.
- Added Unity regressions for overlapping lifecycle events and water stall/depth
  reset. These tests are written, **not executed in Unity**. Full water forces,
  mud depth, world streaming and campaign/save UI remain unfinished.
- Prepared an optional, owner-only manual GitHub Actions workflow that uses a
  **dedicated, already licensed Windows Unity machine** and uploads a development
  APK after setup/tests/build succeed. It is not free hosted Unity, does not
  install/activate an editor, and has not been run.

## Checks actually executed

- **59/59** headless C# tests passed, including four new lifecycle regressions;
  zero compile errors/warnings and .NET Standard 2.1 profile checks passed.
- **23/23** Python runner tests passed using mocked Unity processes.
- **21** Unity-dependent files passed syntax parsing only; they are not
  Unity API type-checked. The seven PlayMode tests have not run in Unity.
- Workflow YAML/manual-only/security contracts passed static validation;
  no runner or Actions job was started.
- Actual editor discovery still fails: Unity is not installed here.

Raw evidence: `Docs/evidence/mobile-continuation-2026-10-15/`. These results
prove neither a successful Android build nor performance on the Moto G45.

## Recommended route: phone controls a desktop

You need **one of these**, not necessarily a computer you own:

1. A trusted friend's/borrowed Windows, macOS or Linux desktop with Unity support.
   Ask its owner to help with the initial editor installation/activation and
   secure remote-desktop setup.
2. A suitable rented cloud Windows desktop that you control from the phone using
   the provider's browser console or Android remote-desktop client. **This may
   cost money.** Check desktop Unity graphics/OS requirements before paying;
   a basic terminal-only cloud shell or Android emulator is not equivalent.

A Windows x86-64 desktop with roughly 16 GB RAM, suitable graphics and ample SSD
space (allow many tens of GB for the editor, modules, project and build cache) is
an illustrative starting point, not a guarantee of compatibility or a benchmark.
Follow the actual Unity version's system requirements and your provider's terms.
Do not rent anything or buy a Unity subscription just on the basis of this guide.
Check current Unity Personal eligibility/activation terms; a paid Unity license
must not be assumed necessary or unnecessary without checking your eligibility.

### Step A — Open the build computer from your phone

- For a borrowed PC, its owner can configure a reputable remote-desktop tool,
  for example Chrome Remote Desktop: https://remotedesktop.google.com/
- For a cloud desktop, use its official client/console. Follow its security setup;
  do not expose an unauthenticated remote desktop to the internet.
- Use a strong login, two-factor authentication where supported and a stable
  connection. A Bluetooth keyboard/mouse helps but is not required.
- Keep account passwords, activation information and private signing keys out of
  chat. The agent does not need these credentials.

### Step B — Get this exact updated source onto the remote computer

The current work is local to the Arena session and is **not automatically pushed
onto GitHub**. Cloning `main` alone will not include it.

Use the supplied **Wayfinder-source-for-desktop.zip** handoff:

1. On the remote desktop, open the same Arena conversation in its browser.
2. Download the source ZIP linked in the agent's reply, and extract it into a
   normal writable folder, for example `C:\Projects\Wayfinder`.
3. The folder containing `Assets`, `Packages`, `ProjectSettings` and `Tools` is
   the Unity project root. The ZIP has no `.git` history, license files, build
   caches, keys or passwords. It contains the current source and instructions.

Alternatively, after the owner authorizes pushing/merging the current session
branch, clone/download that up-to-date branch. Do not use an old snapshot and
expect the new setup code to be present.

### Step C — Install and activate the build tools on that computer

1. Install Unity Hub from Unity's official site. Sign in/activate the editor using
   your own eligible license; do not send credentials to the agent.
2. Install a real stable **Unity 6.3 LTS / 6000.3.x** editor.
3. Add **Android Build Support**, **Android SDK & NDK Tools** and **OpenJDK** via Hub.
4. Install Python 3.9+; on Windows enable its PATH option. Open a new terminal
   afterwards so it sees the updated PATH.
5. Open the extracted project in that editor once, allow Package Manager access
   and resolve first-import problems. Pins in `Packages/manifest.json` are still
   provisional until resolved by Unity. If Unity reports a nonexistent/incompatible
   package version, select an actual compatible version offered by Package Manager
   and retain its generated lockfile; don't guess versions in chat.
6. If needed, select **Input System Package (New)** under Player Settings → Active
   Input Handling and allow Unity to restart. Then close the project/editor before
   running batch commands, so the same project isn't opened twice.

### Step D — Run the smoke pipeline while controlling the PC from the Moto G45

Windows PowerShell, inside the extracted project folder:

```powershell
python Tools/Build/unity_pipeline.py smoke --unity "C:\Program Files\Unity\Hub\Editor\YOUR_INSTALLED_VERSION\Editor\Unity.exe"
```

Replace `YOUR_INSTALLED_VERSION` with the real folder name shown by Hub; do not
copy the placeholder literally. If Python was installed with the Windows launcher,
`py -3` can replace `python`.

For macOS/Linux executable paths see [UNITY_HANDOFF.md](UNITY_HANDOFF.md).

The pipeline reads/pins the actual editor version, runs setup, Unity EditMode and
PlayMode tests, then attempts a development APK. It stops on failed or stale
results. The editor requires a license, internet/package access and the Android
modules; the script does not supply these or bypass activation.

Expected output **only if all stages succeed**:

```text
Builds/Android/Wayfinder-dev.apk
```

If it fails, inspect `Logs/unity-pipeline/` on the remote computer. Share the first
relevant compiler/package/test error after reviewing logs for private information.
Do not repeatedly rerun a timed-out Unity/Gradle build without checking whether
child processes remain. No successful build is promised by the source changes.

### Step E — Install the APK on the Moto G45

1. Transfer **your own newly built** `Wayfinder-dev.apk` from the remote desktop
   to your phone using an owner-controlled file-transfer method.
2. On the phone, open the APK from Downloads/Files.
3. If Android asks, temporarily allow **Install unknown apps** for that specific
   browser/file manager. Menu wording varies with Android/Motorola versions.
4. Install and open Wayfinder; revoke the temporary install permission afterwards.
   Keep Play Protect enabled. Do not install a random purported Wayfinder APK.
5. No root access or USB debugging is required merely to tap-install an APK.
   An AAB cannot be installed by tapping it; it is for Play distribution.
6. If an existing installation has a different signing key, Android may reject
   the update. Keep the same development key for subsequent tests where practical;
   uninstalling an existing app deletes its local data unless separately preserved.

The current intended prototype flow is **title → Open proving ground → drive**.
It is not the final campaign and has no connected campaign save/Continue yet.

### Step F — Test on the actual phone

- Hold GAS and LEFT/RIGHT at the same time; check multi-touch and release safety.
- Brake to a stop; continue holding brake to request reverse. Throttle brakes in R.
- Open notification shade, switch apps and return. The game must stay paused until
  you tap Resume, without latched throttle or unwanted vehicle movement.
- Try the water channel; check stall and Reset vehicle behavior. This engine
  integration still needs real physics validation.
- Rotate between allowed landscape orientations; check cutouts/safe areas.
- Play offline and sustain driving for 15–20 minutes to observe frame pacing and
  heat. This is a proposed test, not a claim about Moto G45 performance.
- Record Android version, RAM variant, build version, symptoms and screenshots if
  something fails. Don't assume the phone runs smoothly because it is ARM64.

## Optional: start later APK builds from the Moto G45's GitHub browser

Prepared file: `.github/workflows/android-phone-build.yml`.
This optional path saves remote-desktop clicking **after a real Unity PC exists**.
It does not remove the need for that machine or provide a hosted Unity license.

**Security first: this repository is public.** Use a disposable/dedicated machine
or VM without personal files, production keys or unrelated cloud credentials. Do
not use a personal everyday PC as an unrestricted public-repository runner. Never
run code from an untrusted pull request/fork on the machine.

One-time owner setup on the desktop (a helper can assist):

1. Push the reviewed source/workflow from the current session branch and merge
   the workflow into the repository's default branch with owner approval. GitHub's
   manual Run workflow UI requires the workflow to exist on the default branch.
   None of those git/PR operations has been performed by this batch.
2. In GitHub Settings → Environments, configure `unity-android-build` with branch
   restrictions and approval protection **where your GitHub plan supports them**.
   A named environment alone does not create approval protection. If that protection
   is unavailable, prefer the remote-desktop route rather than assuming it exists.
3. Settings → Actions → Runners → New self-hosted runner → Windows x64. Follow
   GitHub's displayed commands privately and add label `wayfinder-unity-android`.
   Never share runner registration tokens in chat.
4. Run it as the same dedicated Windows user whose Unity editor is activated, with
   Python/Git/Android modules installed and outbound access to Unity services.
   A service running as another account may not have that license or environment.
5. Keep that runner online. The workflow only accepts manual runs by the personal
   repository owner, has read-only repository permissions and no PR trigger.
   These safeguards do not make untrusted branch contents safe to execute.

From the Moto G45:

1. Open the repository in Chrome and enable **Desktop site** if controls are hidden.
2. Actions → **Android APK (phone-controlled, dedicated Unity PC)** → Run workflow.
3. Select the reviewed source branch and optionally enter the real Unity executable
   path; otherwise the runner uses `UNITY_BIN`/installed-editor discovery.
4. Approve the protected environment if required and wait. A job stuck in **Queued**
   commonly means no matching online runner; it is not building on your phone.
5. On success open the run → Artifacts → **Wayfinder-dev-APK**. Download its ZIP,
   extract the APK using your phone's Files app, and install as above. GitHub may
   require sign-in to download artifacts. Retention is seven days.
6. Unity logs are not automatically uploaded; inspect/redact them on the dedicated
   machine before sharing. No production AAB or Play upload runs in this workflow.

## If you have only the phone and cannot access any desktop/cloud machine

You can review/edit source and manage the project from your phone, but **there is
no supported phone-only build route for this Unity project**. A trusted helper's
computer, suitable cloud desktop, or properly licensed managed build service is
still required. Hosting availability, free quotas and activation/licensing rules
must be checked with the provider; no free service or working APK is guaranteed.
Switching to a different Android-native/browser game engine would be a separate
rewrite, not a way to compile this existing Unity project on the Moto G45.
