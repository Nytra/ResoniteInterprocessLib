# Changelog

## [2.0.1] - 28-10-2025

- Fixed folder structure of the Thunderstore package

## [2.0.0] - 02-10-2025

- Changed the library initialization to no longer need dedicated bootstrapper mods, now it can instantiate on its own whenever it's called
- Added RML projects
- Now builds to InterprocessLib.FrooxEngine.dll and InterprocessLib.Unity.dll (No dependency on mod loaders or Harmony)
- Added extra extension libraries for each mod loader
- The new project setup allows for multiple 'instances' of the library per-process

## [1.0.1] - 02-10-2025

- Fixed an error that occured when multiple messenger instances registered the same extra types
- Fixed crashes on startup due to not initializing types correctly (now using a patch for this which is more reliable)

## [1.0.0] - 02-10-2025

- Initial release