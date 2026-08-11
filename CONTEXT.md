# VxFiles

VxFiles is a personalized Windows file manager derived from Files Community and intended for controlled sharing with coworkers.

## Language

**VxFiles**
The public identity used by the installed application, executable, package, protocol, and distributions. Inherited `Files.*` namespaces, project names, libraries, extension names, task identifiers, COM identities, and persistence identifiers remain unchanged.

**Installed Distribution**
The only supported VxFiles distribution: a self-contained .NET 10 x64 unpackaged app installed per-user through Velopack and downloaded or updated from the VxFiles GitHub fork. V1 does not ship MSIX or portable ZIP assets.

**Automatic Update**
A newer release found on launch, or by the hourly re-check while the app runs, is downloaded in the background and staged. The Velopack updater installs it as the app exits, so the next launch runs it without anyone clicking. Taking the update immediately runs the same install through the same shutdown; only whether the app relaunches differs. Nothing waits for consent, and nothing can be skipped or deferred.

**Update Surface**
Where a staged Automatic Update becomes visible: a dot on the Settings icon in the sidebar footer, and the update card at the top of the About page carrying the pending version, its release notes, the last successful check, and a restart. It informs and never gates — ignoring it changes nothing except when the update lands. The card is absent unless Velopack installed the running copy.

**Downstream Layer**
The intentionally small, reviewable set of VxFiles-owned differences applied to a tagged Files Community baseline.

**Stable-Tag Intake**
Future upstream work starts from an accepted VxFiles line, merges a named stable Files release tag on a dedicated sync branch, and removes downstream hunks that upstream has made redundant. VxFiles does not continuously follow `upstream/main`.

Every stable-tag intake must follow `docs/VXFILES-UPSTREAM-MERGE-CHECKLIST.md` so the unpackaged compatibility layer, branding, and release path are retained.

**Automation Package**
A VxFiles automation install, update, validation, and trust unit. One package contains a `vxpackage.json` manifest and one or more Automation Actions, and appears as a root item in the Tools TreeView.

**Automation Action**
A named Python automation inside an Automation Package. Actions are independently runnable against an immutable folder-and-selection snapshot and appear as children of their package in the filterable Tools tab.

**Tools Tab**
The third Info Pane tab, after Details and Preview. It lists discovered Automation Packages as TreeView roots with their Automation Actions as children, filterable by name and description, and is where actions are run, watched, and cancelled. The headless session opens the first time the tab is shown, so an app that never opens Tools never discovers packages.

**Selection Policy**
What an Automation Action declares it accepts: how many items, of which kinds, with which extensions. One evaluator in `VxFiles.Automation.Abstractions` answers it for both the Tools tab's Run button and the session's own admission check, so a button is never enabled for a run the session would refuse.

**Action Setting**
A typed value an Automation Action declares in its manifest and a user configures per action, transported to the run alongside the selection. What it *is* and how it is *held* are separate: an enum, a file path and a folder path are all held as text, so a surface choosing between a dropdown, a picker and a text box needs the declared type rather than the stored kind. An action that has never been configured has the manifest's default, and a stored value that no longer satisfies its declaration refuses the run rather than being quietly replaced.

**External Tool**
A program an Automation Package declares it needs and the user points at, installed and updated by whoever installed it rather than by VxFiles. It is identified by the SHA-256 of what its path leads to and by nothing else, so the same executable named another way is the same tool, and a different build is a different one. The path is stored as the user spelled it and followed on every run, because the shims winget and scoop install are re-pointed by their own upgrades. A path known to be unusable is refused rather than stored: having no tool configured is a state the app can ask about, whereas a stored bad path reads as configured and broken.

**Readiness**
Whether an Automation Action can be run right now, decided per action from the External Tools that action names rather than per package, so configuring one tool of two leaves every action needing only the first perfectly runnable. Needing configuration is not a fault: it is the state every package declaring an External Tool is in on a clean install, and it is reported apart from a dependency that failed once a run had started. It is composed from what is stored each time the catalog is projected and never recorded, because a verdict written onto a snapshot is erased by the next catalog refresh. It costs a state lookup and a path check and nothing more — the SHA-256 and the declared version floor are the run's business alone, so an action reported ready can still be refused when it starts.

**Package Trust**
Consent granted to a whole Automation Package, recorded against a fingerprint of its content, its runner, and the external tools it resolves. It is requested before the package's first run and again whenever that fingerprint moves, and it covers every action the package contains rather than the one that triggered the prompt.

**Automation Payload**
The app-local files that make Automation work on a clean install: the hash-pinned CPython interpreter under `AutomationRuntime\Python`, the runner scripts beside it, and the bundled `vxfiles.tracer` package under `AutomationPackages`. It ships inside the ordinary Velopack release, so no user installs Python and no action ever runs on an interpreter found on PATH.

V1 does not import settings or data from Files or earlier VxFiles distributions.
