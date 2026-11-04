# Music box source

Procedural editable source: `Assets/UdonMidiPlayer/Editor/Prefab/MidiMusicBoxBuilder.cs`, `BuildModel`, `ConfigureTabletop`, `ConfigureDrumNotes`, and `BuildCover`. Original project art, CC BY 4.0; code MIT. Authored with AI assistance.

Tabletop dimensions: approximately 50 × 22 × 13 cm for the box, or 53 cm wide including the external crank. The 0.14-scale model sits on a separate 77 cm round side table, with a clear bevelled dust cover and a notch for the crank shaft. The 88-tine physical comb is retained. The smooth cylinder has a 30.1 mm world-space radius and 64 radial sides. Note markers share that inner radius, with 1.4 mm outward thickness and about 8 mm maximum arc length. The cylinder is smooth, and crystal note markers travel from the back over its upper arc to the front, synchronized with its rotation. The drum and crank retain their transforms; the adjacent round stool preserves the desktop keyboard station. The procedural builder writes reusable meshes to `Assets/UdonMidiPlayer/Meshes/MusicBox` and retains the existing mesh GUIDs.

Reference: existing project marimba presentation, no external model. The device is an expanded artistic music box, not a claim that an 88-note commercial mechanical unit was recorded.
