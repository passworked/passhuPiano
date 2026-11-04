<p align="center">
  <img src="Docs/Brand/passhuPiano-logo.png" width="160" alt="passhuPiano ロゴ">
</p>
<h1 align="center">passhuPiano</h1>
<p align="center">あなたの VRChat ワールドに MIDI を。</p>
<p align="center">
  <a href="README.md">English</a> ·
  <a href="README.zh-CN.md">简体中文</a> ·
  <strong>日本語</strong>
</p>
<p align="center">
  <a href="Docs/GETTING_STARTED.md">はじめに</a> ·
  <a href="Docs/Developer/AI_GUIDE.md">開発ガイド</a> ·
  <a href="Docs/Contributing/README.md">楽器をコントリビュート</a>
</p>

VRChat Worlds 向けの **MIDI 再生・マルチプレイヤー同期・リアルタイム演奏**。

メインシーンにはピアノ、マリンバ、オルゴール、弦楽器、ドラムがあり、編集可能なアート素材と制作ツールも含まれています。2 種類の単体ピアノ Prefab は個別に管理されています。

ファイル再生のチャンネル色は黄金角で配置します。フォルダーと曲のエディターでは、MIDI・フォルダー・ZIP のドロップ、曲名と作者の編集、並べ替え、ソースの管理を行い、「保存して適用」で反映できます。[Unity MIDI インポートガイド](Docs/MIDI_IMPORT.md)（中国語）を参照してください。

> 現在は PC を優先した開発候補版 `1.0.0-rc.3` です。機能と検証状況は[現在の実装](Docs/Developer/CURRENT_STATE.md)を参照してください。

## クイックスタート

Git LFS をインストールし、この非公開リポジトリへのアクセス権を取得してから実行します。

```sh
git clone https://github.com/passworked/passhuPiano.git
cd passhuPiano
git lfs pull
```

VCC / ALCOM で Worlds SDK を復元し、**Unity 2022.3.22f1** でプロジェクトを開きます。記録上の SDK 基準バージョンは **3.10.5** です。メインシーンを開くか、ピアノ Prefab を自分のワールドに配置してください。

詳しい手順は[セットアップと使い方](Docs/GETTING_STARTED.md)を参照してください。[技術ドキュメント](Docs/UdonMidiPlayer/README.md) · [AI 作業ガイド](Docs/Developer/AI_GUIDE.md)。詳細ガイドは現在、中国語で提供されています。

## コントリビュート

楽器、音源、アート、コード、ドキュメントへの貢献を歓迎します。直接 PR を提出でき、段階的なコントリビュートも可能です。

楽器の正式採用には、モデル・音源・組み込み・説明資料をそろえ、PC 版 VRChat の単一クライアントでの実機テスト結果を提出し、**passhu** のレビューを受ける必要があります。出典とライセンス、リソースの上限、AI 利用の申告については[貢献ガイド](Docs/Contributing/README.md)を参照してください。

## ライセンス

プロジェクト独自のコードは [MIT](Licenses/MIT.txt)、独自のアート素材は [CC BY 4.0](Licenses/CC-BY-4.0.txt) です。第三者のコンテンツには元のライセンスとクレジットが適用されます。[ライセンスの範囲](LICENSE.md)と[出典・クレジット](THIRD_PARTY_NOTICES.md)を確認してください。
