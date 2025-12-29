# Changelog

## [3.0.0] - 29-12-2025

- Fixes occasional startup crashes
- Big rewrite of the backend - adds on-demand type registering, so you don't need to register types in advance anymore. Also changes library initialization to happen immediately (no delay in sending messages on startup)
- Adds standalone project and allows using a custom queue name to connect to other processes, not just Unity
- Adds a way to send and receive Type
- Adds ways to send generic collections and arrays
- Performance optimizations and memory optimizations (Now 0.1 millisecond message latency on average)

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