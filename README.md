# Eluna

**別マシンで開発を再開する場合は[開発引き継ぎ（2026-10-19）](docs/development-handoff.md)を最初に確認してください。** 現在の最新開発ブランチ、未マージPR、残作業、環境構築とローカル補助ファイルの保存先をまとめています。

**0.1.0beta（Windows x64の開発プレリリース）**の配布先は[GitHub Releases](https://github.com/SUIREN-KazutoHashimoto/Eluna/releases/tag/v0.1.0-beta)です。導入には`Eluna-Setup.exe`、インストールせず使う場合は`Eluna.exe`を選びます。モデルの重みは含まず、初回にアプリから取得します。[変更点と確認範囲](docs/releases/0.1.0-beta.md)も確認してください。以下のローカルexeパスは各機能追加時点の記録です。

N01〜N20の機能を実装しました。アカウント同士の会話・自発的な開始、初期12アカウントとサーバー最大50人、絵描きスキル、カラー絵文字、個別の生成設定、VC参加者、開発者ログに対応しています。インストーラーと正式版／開発版を選ぶ自動更新も追加しました。[実装・検証と未確認範囲](docs/social-autonomy-status.md)、[導入と更新の仕様](docs/installation-updates.md)を参照してください。

提示されたDiscordの画面を基準に、サイドバー・ヘッダー・発言・引用・入力欄・本人プロフィールの配置と寸法を調整しました。最大化時の入力欄の隠れとPlaceholderの重なりも修正しています。最新の開発版exeは`artifacts/release-discord-reference/Eluna.exe`。[比較内容・変更・確認範囲](docs/discord-reference-ui.md)を参照してください。

日常操作とDiscordを参考にした表示・動きを改善しました。Ctrl+Kの移動、未読とメンションの受信トレイ、発言検索・ピン・リアクション、検索できる絵文字、入力の書式補助、通知と取り消し、下書き・メンバーの状態表示を追加しています。最新の開発版exeは`artifacts/release-experience/Eluna.exe`。[操作と検証範囲](docs/discord-experience.md)を参照してください。以下のexeパスは各機能追加時点の記録です。

キャラクターの自動生成、本人のプロフィール、後からのメンバー招待、AIへの招待依頼、入力途中の候補・色付きメンションを追加しました。会話は全員を名前のある参加者として表示します。[使い方と確認範囲](docs/social-chat-ux.md)、配布確認用exeは`artifacts/release-social/Eluna.exe`です。

チャンネルごとのAI閲覧・発言・招待・作成権限、プロフィールの性格・ロールプレイ・つながり、依頼からのチーム自動編成と担当別の入力中表示を追加しました。操作と制限は[アカウントと自動編成](docs/agent-permissions-autonomy.md)を参照してください。今回の配布確認用exeは`artifacts/release-agents/Eluna.exe`です。以下のexeパスは各機能追加時点の記録です。

主画面をサーバー・チャンネル・メンバー・メッセージの配置へ変更しました。モデル選択、思考量、検索・スキル・推論の設定は左下の歯車へ集約し、モデルはアカウントに割り当てます。[新しい画面と設定の移動先](docs/discord-ui.md)を参照してください。更新版の配布確認用exeは artifacts/release-voice/Eluna.exe です。アカウントはプロフィールから編集でき、サーバーに資料・指示・メモリをまとめ、ボイスチャンネルで音声・テキスト・ウィンドウ共有を使えます。[操作と確認範囲](docs/voice-channels.md)を参照してください。

Windows向けのローカルAIチャットツール。llama.cppを推論基盤とし、C# / WPFで、実用性とデザインを重視した日常使いのアプリを目指します。

**現在は機能追加を進めた開発版です。** ChatGPTを参考にした全体構成とDiscord風の会話表示、Markdownとコード、会話の編集・分岐・検索、モデル管理、GPU / CPU推論、バックアップに加え、自前のWeb検索・深掘り調査・役割別モデルのディベートを実装しています。参加者はドラッグ＆ドロップで追加・モデル変更・並べ替えできます。C#の外部ライブラリは使用していません。確認範囲は[実装・検証の台帳](docs/implementation-status.md)、操作は[使い方](docs/using-eluna.md)を参照してください。

## 利用する

エージェントをアカウントとして作成し、サーバー・チャンネル・DM・スレッドで会話できます。許可した記憶だけを別の場所へ持ち越し、通常会話・ラウンド制・自由形ディベートの各発言を中央へ表示します。途中発言・指名・割り込み・参加者変更・停止と再開にも対応します。[統合改修の使い方と確認範囲](docs/workspaces.md)を参照してください。新しい配布確認用exeは`artifacts/release-workspace/Eluna.exe`です。

配布用の`Eluna.exe`を起動し、設定 → モデル・データ →「モデルを探す」からGGUFをダウンロードして会話を始めます。未設定時の案内からも開けます。.NET、Visual C++ランタイム、SDKなどの環境構築は不要です。初回のモデル取得にはインターネットが必要です。GPUが使えないPCでは同梱CPU版を使用します。

通常のチャットでも必要に応じてWeb検索を使います。「検索して」と依頼するか「Web検索 · 必ず」を選ぶと検索を試みます。質問や会話の文脈から作る検索語は外部の検索サービスへ送信されます。「検索しない」では検索・ページ取得を行いません。仕組みと外部通信の範囲は[検索ガイド](docs/research.md)を参照してください。

入力欄の「＋」から画像・文書の添付、画像認識モデルの設定、画像生成、文書生成を開けます。画像/PDFは画像対応GGUFと同系列のprojectorが必要です。画像生成には承認済みのstable-diffusion.cppを同梱し、生成モデルをGUIから取得します。DOCX/PDF/Markdown/TXTの作成、DOCX/XLSX/PPTX等の読み取りにも対応します。形式・上限・確認範囲は[画像とドキュメント](docs/media-documents.md)を参照してください。

サイドバーでプロジェクトを作成し、会話・専用の指示・資料・保存メモリをまとめられます。初期設定はプロジェクト限定メモリで、共通指示・共通メモリ・プロジェクト外の会話や資料を参照しません。回答に渡した抜粋も画面から確認できます。参照範囲、資料の読み取り、移動・削除・取り込みの扱いは[ナレッジの仕様](docs/knowledge.md)に記載しています。

推論・ダウンロード中も別の会話を開き、次の依頼を予約できます。「処理リスト」で進捗・予約順・停止・メモリの見込みを確認します。設定でチャット推論、ダウンロード、画像・文書生成それぞれの同時実行数を1〜4に変更できます。同じ会話のチャットは順番に処理します。画像・文書の依頼も保存し、再起動後は明示的に再開できます。詳細は[並行作業ガイド](docs/concurrent-work.md)と[残機能の実装記録](docs/remaining-features.md)を参照してください。

設定 → 管理 →「データ・バックアップ」で、会話・プロジェクト・資料・メモリ・スキル本体・生成物・予約・設定を全体ZIPへ保存・復元できます。モデル本体の同梱を選べ、復元前の退避と中断からの回復にも対応します。従来の履歴JSONは別タブに残しています。範囲と操作は[バックアップの仕様](docs/backup.md)を参照してください。

主画面の画面・位置・通常サイズ・最大化状態を終了時に保存し、次回起動時に復元します。保存先の画面がない場合は接続中の画面へ補正します。仕様・確認範囲は[ウィンドウ復元](docs/window-placement.md)を参照してください。

入力欄の「スキル」で、再利用する手順・参考資料・補助コードを依頼へ適用できます。SKILL.md／ZIPの取り込み、GUI作成、会話ごとの自動・指定・オフ、プロジェクト分離と使用記録に対応します。標準スキルは管理画面から追加できます。PowerShellの実行条件、Claude形式との互換範囲と保存方法は[スキルガイド](docs/skills.md)を参照してください。

チャットで`@Computer`と依頼するか、作業モードの「Computer Use」を選ぶと、ローカルモデルがWindowsアプリのマウスとキーボードを自動で操作します。アプリの許可、計画、プレビュー、重要な操作の確認、履歴を会話内に表示します。追加指示、一時停止・再開、利用者への操作引き継ぎ、別チャットへの移動と保存にも対応します。Ctrl+Alt+Escで停止します。画像対応モデルとprojectorを使うと座標操作もできます。対応範囲・確認済みの条件と制限は[Computer Useガイド](docs/computer-use.md)を参照してください。

Computer Useの実行中は、操作先に青紫の枠、クリック位置の波紋、入力欄やドラッグ経路の強調を表示します。画面上の小さな操作バーから一時停止・停止できます。表示効果はWindowsの画面取得除外機能を利用し、判断用の画像には含めません。

ソースから配布用exeを作る場合は、開発ツールを用意して次を実行します。

```powershell
./scripts/Setup-VulkanSdk.ps1
./scripts/Publish.ps1
```

出力先は`artifacts/release/Eluna.exe`です。使い方と確認範囲は[プロトタイプガイド](docs/prototype.md)を参照してください。

## 方針

- 対応OSはWindowsのみ。初期のビルド対象はx64。
- UIはC# / WPF、基盤は.NET 10。OS / .NET標準機能を優先する。
- llama.cpp以外の外部パッケージは原則追加しない。必要な場合は導入前にオーナーへ相談する。
- 応答速度だけでなく、日本語入力、キャンセル、履歴の復元、エラーからの回復、読みやすさを品質の基準にする。

## 開発を始める

Windowsと.NET 10 SDKが必要です。Visual Studioを使う場合は.NET 10に対応するバージョンと「.NET デスクトップ開発」ワークロードを用意してください。SDKの選択範囲は[global.json](global.json)に定義しています。

```powershell
git clone --recurse-submodules https://github.com/SUIREN-KazutoHashimoto/Eluna.git
cd Eluna
git switch develop
git switch -c codex/your-task
./scripts/Build.ps1
dotnet run --project src/Eluna.App --no-restore
```

WPFのコンパイルにC++ツールやモデルは不要です。開発ビルドで実際にチャットする場合は、CPU / Vulkanの推論エンジンもビルドしてください。配布用exeにはエンジンを含めます。初回だけスクリプト実行ポリシーにより実行できない場合は、内容を確認したうえで、そのコマンドに限って実行してください。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ./scripts/Build.ps1
```

Visual Studioでは[Eluna.slnx](Eluna.slnx)を開き、`Eluna.App`を起動プロジェクトにします。

### llama.cppのCPUビルド

追加でVisual Studioの「C++によるデスクトップ開発」、Windows SDK、CMakeが必要です。スクリプトはPATH上のCMake、またはVisual Studio付属のCMakeを使用します。

```powershell
git submodule update --init --recursive
./scripts/Build-Llama.ps1
```

出力先は`build/llama-cpu/bin/Release/`です。実行には`llama-server.exe`と同じディレクトリのDLL一式が必要です。モデルを使う手動確認は[開発ガイド](docs/development.md)を参照してください。

### GPUビルドと起動

GPUバックエンドはVulkanです。開発用のVulkan SDK導入は承認済みで、バージョンとSHA-256を固定しています。次のセットアップは公式SDKを`.local/`に配置し、システムの環境変数やドライバーを変更しません。

```powershell
./scripts/Setup-VulkanSdk.ps1
./scripts/Build-Llama.ps1 -Backend Vulkan -EnableBackendTests
./scripts/Start-Llama.ps1 -ListDevices
./scripts/Test-LlamaGpu.ps1 -Device Vulkan0
./scripts/Start-Llama.ps1 -ModelPath ./models/model.gguf -Device Vulkan0
```

出力先は`build/llama-vulkan/bin/Release/`です。モデルは別途用意します。CPU版への切り替えは`Start-Llama.ps1 -Backend Cpu`で明示します。SDKのない利用者環境での条件やGPUの指定は[GPUガイド](docs/gpu.md)を参照してください。

### 変更の確認

```powershell
./scripts/Verify.ps1
```

Releaseビルド、SDK付属の`dotnet format`、履歴・SSE・ダウンロードの動作チェック、Gitの空白チェックを実行します。CIはWindows上で同じ確認とllama.cppのCPU / Vulkanビルドを行います。GPU演算と実モデル生成は対応GPUのある実機で確認します。

## 構成

```text
src/Eluna.App/          WPFアプリとUIリソース
tests/Eluna.Checks/     外部テストライブラリを使わない動作確認
third_party/llama.cpp/  コミットを固定したGit submodule
scripts/               Windows用のビルド・検証スクリプト
docs/                  設計・品質・依存関係・リリース運用
.github/               CIとIssue / PRテンプレート
```

モデル、会話データ、ローカル設定、ビルド成果物はGit管理対象に含めません。

## ブランチ運用

`main`はリリース用、`develop`はリリース前の開発統合用です。通常の作業ブランチは`develop`から作り、PRの宛先も`develop`にします。リリース時に`develop`から`main`へPRを出します。詳細は[CONTRIBUTING.md](CONTRIBUTING.md)と[リリース運用](docs/releasing.md)を参照してください。

## ドキュメント

- [開発ガイド](docs/development.md)
- [プロトタイプの使い方](docs/prototype.md)
- [機能追加後の開発版の使い方](docs/using-eluna.md)
- [GPUガイド](docs/gpu.md)
- [アーキテクチャ](docs/architecture.md)
- [依存関係ポリシー](docs/dependencies.md)
- [UI / UXの基準](docs/design.md)
- [開発ロードマップ](docs/roadmap.md)
- [完成版に向けた機能一覧](docs/product-backlog.md)
- [実装・検証の台帳](docs/implementation-status.md)
- [検索・調査・ディベートの開発版ガイド](docs/research.md)
- [モデルの取得・整理・設定](docs/models.md)
- [Computer Use](docs/computer-use.md)
- [リリース運用](docs/releasing.md)

Elunaのライセンスは[Apache-2.0](LICENSE)です。llama.cppとその同梱コード、モデルのライセンスは別に扱います。[第三者コードの扱い](THIRD_PARTY_NOTICES.md)を参照してください。
