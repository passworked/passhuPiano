<p align="center">
  <img src="Docs/Brand/passhuPiano-logo.png" width="160" alt="passhuPiano logo">
</p>
<h1 align="center">passhuPiano</h1>
<p align="center">Bring MIDI to your VRChat world.</p>
<p align="center">
  <strong>English</strong> ·
  <a href="README.zh-CN.md">简体中文</a> ·
  <a href="README.ja.md">日本語</a>
</p>
<p align="center">
  <a href="Docs/GETTING_STARTED.md">Get started</a> ·
  <a href="Docs/Developer/AI_GUIDE.md">Developer guide</a> ·
  <a href="Docs/Contributing/README.md">Contribute instruments</a>
</p>

**MIDI playback, multiplayer synchronization, and live performance for VRChat Worlds.**

The main scene includes piano, marimba, music box, strings, and drums, with editable artwork and authoring tools. The two standalone piano Prefabs are maintained separately.

File-playback channels use golden-angle colors. Manage MIDI in a folder/song editor with drag-and-drop files, folders and ZIPs, editable metadata, ordering, source maintenance, and one Save and apply action. See the [Unity MIDI import tutorial](Docs/MIDI_IMPORT.md) (Chinese).

> Development candidate `1.0.0-rc.3`, with PC as the priority. See [current implementation](Docs/Developer/CURRENT_STATE.md) for features and validation status.

## Quick start

Install Git LFS and obtain access to this private repository, then:

```sh
git clone https://github.com/passworked/passhuPiano.git
cd passhuPiano
git lfs pull
```

Restore the Worlds SDK through VCC / ALCOM and open the project with **Unity 2022.3.22f1**. The recorded SDK baseline is **3.10.5**. Open the main scene, or drag a piano Prefab into your own world.

See [setup and usage](Docs/GETTING_STARTED.md) for details. [Technical documentation](Docs/UdonMidiPlayer/README.md) · [AI workflow guide](Docs/Developer/AI_GUIDE.md). Detailed guides are currently in Chinese.

## Contributing

Instrument, sound, art, code, and documentation contributions are welcome. You may submit a PR directly or contribute in stages.

Official instrument inclusion requires the model, sound source, integration, documentation, and PC VRChat single-client test evidence, followed by **passhu**'s review. See the [contribution guide](Docs/Contributing/README.md) for source licenses, resource limits, and AI disclosure requirements.

## License

Project-owned code is licensed under [MIT](Licenses/MIT.txt), and project-owned artwork under [CC BY 4.0](Licenses/CC-BY-4.0.txt). Third-party content retains its original licenses and credits; see the [license scope](LICENSE.md) and [attributions](THIRD_PARTY_NOTICES.md).
