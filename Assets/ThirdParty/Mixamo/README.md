# Mixamo files (not in git)

This folder holds characters and animations downloaded from [Mixamo](https://www.mixamo.com).
**The files themselves are never committed.** This repo is public, and Mixamo's terms
forbid sharing raw character and animation files, so a public repo would count as
distributing them. `.gitignore` keeps everything in this folder out of git except:

- this README
- the Unity `.meta` files. They only hold Unity's ID for each file plus our import
  settings (Humanoid rig, clip names). Keeping them means a re-downloaded file with the
  **same file name** reconnects to every scene, prefab and Animator Controller that uses it.

The same rule applies to Unity Asset Store assets: never commit them.

## After cloning the repo

Scenes and Animator Controllers will show these files as missing until you download them.
For each row in the table below, download the file from Mixamo with the listed settings,
save it here under the exact file name, and Unity will pick it up.

## Downloaded files

Add a row every time you add a file here.

| File name | Mixamo character or clip | Download settings (format, skin, FPS, in place) | Date |
|---|---|---|---|
| *(none yet)* | | | |

Typical settings: Format **FBX for Unity (.fbx)**. Characters: **With Skin**. Animations
for an existing character: **Without Skin**, **30 FPS**, and tick **In Place** for moves
that shouldn't drift (dribbling, jump shots). In Unity, set Rig > Animation Type to
**Humanoid**.
