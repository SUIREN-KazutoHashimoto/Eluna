# 開発引き継ぎ — 2026-10-19

別のWindowsマシンで開発を再開するための入口です。過去の計画書・検証台帳には、その時点の「未実装」「未公開」やローカルexeパスが残っています。現在の状態は本書、[0.1.0betaの変更記録](releases/0.1.0-beta.md)、[N01〜N20の台帳](social-autonomy-status.md)の順に確認してください。

## 最初に確認するGitの状態

2026-10-19の引き継ぎ確認時点の状態です。

| 対象 | 状態 |
| --- | --- |
| 最新の開発ソース | `origin/codex/social-autonomy`。引き継ぎ資料もこのブランチへ追加 |
| develop向けPR | [#2](https://github.com/SUIREN-KazutoHashimoto/Eluna/pull/2)、未マージ |
| 公開済みbeta | [Eluna 0.1.0beta](https://github.com/SUIREN-KazutoHashimoto/Eluna/releases/tag/v0.1.0-beta)、プレリリース |
| betaのソース | `05312a0ed34debe3b1bf80c3c69607f3cde88720`、タグ`v0.1.0-beta` |
| develop | `19f219e`。PR #2のアプリ改修はまだ含まない |
| main / 既定ブランチ | `e17b5fe`。最新の開発版を再開する入口にはしない |
| ローカルだけのブランチコミット | なし。全ローカルブランチのコミットはリモートまたは公開タグから到達可能 |
| submoduleの未コミット変更 | なし。両エンジンと再帰submoduleは固定コミットを使用 |

**通常のcloneだけではmainが開きます。PR #2が未マージの間は、必ず`codex/social-autonomy`を選んでください。** 今回の引き継ぎは資料とローカル補助ファイルの保存であり、公開済みbetaのタグ・バイナリの差し替えは行いません。

```powershell
git clone --branch codex/social-autonomy --recurse-submodules https://github.com/SUIREN-KazutoHashimoto/Eluna.git
cd Eluna
git status -sb
git submodule status --recursive
```

既存cloneなら、未コミット変更を確認してから`git fetch origin --prune --tags`、`git switch codex/social-autonomy`、`git pull --ff-only`、`git submodule update --init --recursive`を実行します。PRが統合済みになっていた場合は、更新したdevelopから`codex/<task>`を作る通常運用へ戻ります。main/developへの直接の開発変更や、公開済みbetaタグの付け替えはしません。

## 開発環境と再構築

- Windows x64、Git、.NET 10の安定版SDK。`global.json`は`10.0.100`を基準に`latestFeature`を許可し、旧PCの実測は`10.0.402`。
- ネイティブと単体exeの作成にはVisual StudioのMSVC x64、Windows SDK、C++用CMakeツールが必要。旧PCはVisual Studio 2026（`18.10.12306.391`）。WPFのソース確認・C#ビルドだけならC++ツールは不要。
- スクリプトはPowerShell 7での実行を推奨。旧PCの`.local`やPATH、インストール済みSDKはGitへ含めず、新PCで用意する。
- 外部C#パッケージは追加しない。NuGet取得元・依存チェックは維持する。Vulkan SDKとstable-diffusion.cppだけが既存の追加承認対象。詳細は[AGENTS.md](../AGENTS.md)、[依存方針](dependencies.md)。

| 依存 | 固定値・管理場所 |
| --- | --- |
| llama.cpp | `6868a52cb337450658006ad2f9281c2eb63f1666`、`third_party/llama.cpp` |
| stable-diffusion.cpp | `120e3e875c5501c1babb50c0966122cb569d12d4`、再帰submoduleも取得 |
| Vulkan SDK | `1.4.363.0`、URLとSHA-256は`scripts/vulkan-sdk.json` |
| カラー絵文字 | 自作の`native/Eluna.Text`、Windows DirectWrite / Direct2D。フォント再配布なし |

まずC#の確認を実行します。モデルは不要です。

```powershell
./scripts/Verify.ps1
```

新PCで配布物を作る場合は、キャッシュのない状態から以下を実行します。`Setup-VulkanSdk.ps1`は承認済み固定SDKをリポジトリの`.local`へ展開します。`Publish.ps1`はCPU/Vulkanのllama.cpp・stable-diffusion.cppと自作UI部品をビルドし、通知を含む自己完結exeを作ります。

```powershell
./scripts/Setup-VulkanSdk.ps1
./scripts/Publish.ps1 -Version 0.1.0-develop.0.4 -Train develop -OutputDirectory artifacts/release-handoff
./scripts/Test-ReleasePackage.ps1 -Directory artifacts/release-handoff
```

この番号は新PCでのローカル確認用の例であり、公開タグを追加する指示ではありません。初回から`-SkipNativeBuild`を指定すると4本の静的ネイティブがないため失敗します。配布用は各バックエンドの一体構成・静的CRTを維持します。

対応GPUでCPU参照と照合する場合は、演算テスト用バイナリも追加ビルドします。新PCのデバイス名は事前に確認してください。

```powershell
./scripts/Build-Llama.ps1 -Backend Vulkan -StaticRuntime -EnableBackendTests
./scripts/Test-LlamaGpu.ps1 -Device Vulkan0 -StaticRuntime
```

アプリの設定からGGUFを取得すると、そのPCで使う`Models/<id>/model.json`も生成されます。実モデル確認の`--model-meta`には新PCの実在するファイルを指定します。旧PCの絶対パス入りメタデータをそのまま使わないでください。画像認識には同系列projector、実画像生成には別の生成モデルが必要です。SD 1.5 Q4_0以外の全モデル構成を確認済みとはしていません。

## 残作業と次の優先順位

R01〜R10（R07を含む）、アカウント・チャンネル統合、自由形ディベート、N01〜N20は実装済みです。以下は品質確認・運用の残りであり、機能がすべて未実装という意味ではありません。

| ID | 優先 | 残作業 | 完了の判断・入口 |
| --- | --- | --- | --- |
| H01 | 高 | PR #2をレビューしてdevelopへ統合する | 今回はマージしない。beta元コミットのGitHub CIは全5ジョブ成功。統合後にrelease.ymlの自動公開を実確認する |
| H02 | 高 | 新PCのクリーンな構築と利用環境確認 | 上記のVerify・Publish・配布物検査。開発ツールなしの環境でも起動し、モデル取得・CPU/GPU切替・保存・再起動を確認 |
| H03 | 高 | Edgeの複雑なComputer Useを端から端まで再確認 | [既存の検証サイトと合格条件](computer-browser-verification.md)。ロック・前面化で止まった記録があり、成功扱いにしない。独立プロファイルで絞り込み→別タブ→転記→保存を照合 |
| H04 | 高 | 実GitHub配信からの更新と正式な導入・削除 | [導入と更新](installation-updates.md)。公開ファイルの取得・ハッシュは確認済み。別バージョンからの自動更新、Windowsのアプリ一覧・ショートカット、削除後のデータ保持を実環境で確認 |
| H05 | 中 | 多人数・長時間の会話と実リソースでの耐性 | 本人+49アカウント、途中の閲覧・招待・権限変更・予約・停止・再起動、発言頻度と推論枠の競合を確認。50人はサーバー定員であり、49モデルの同時常駐ではない |
| H06 | 中 | モデルごとの会話・検索・ツール利用の評価 | 指名先・横やり・短い返事・自発開始・描画委任、根拠に対応する引用と未確認返答を実モデルで評価。Web検索の技術エラーを本文へ戻さない |
| H07 | 保留 | 入力・表示・音声の物理操作の未確認条件 | 日本語IME、DPI・モニター切替、スクリーンリーダー、実マイク、長文や狭い画面での重なり。模擬IME・画面描画と区別する。以前ユーザーが追加実機検証を不要としたため、未確認の記録として残し、自動で必須タスクに戻さない |
| H08 | 中 | OS・GPU・モデルの対応条件を広げて明記する | RX 9070 XT以外、CPUのみ、少ないRAM/VRAM、画像認識projectorや生成モデルの組合せ。OOM・通信断・中断の回復も確認 |
| H09 | 運用 | 署名・正式版・リポジトリ保護 | betaはAuthenticode未署名。証明書は未指定、GitHub保護ルールは設定済みとしない。mainへの公開はdevelopからのリリースPRで行う |
| H10 | 継続 | UIと資料の完成度を上げる | 最新状態は本書を入口にする。過去の「最新exe」「未公開」は記録時点の記述。新しいモデル・UI変更で説明と検証台帳を更新 |

## 実装を読む入口

| 領域 | 主な場所 |
| --- | --- |
| Discord風シェル・入力・発言 | `MainWindow.xaml`、`MainWindow.*.cs`、`Resources/DiscordShell.xaml`、`Controls/MarkdownView.cs` |
| アカウント・権限・招待・記憶 | `Services/WorkspaceService.cs`、`ChannelAccess.cs`、`KnowledgeService.cs`、`ViewModels/ChatViewModel.Workspace.cs` / `Accounts.cs` / `Invitations.cs` |
| 自律会話・ディベート・描画 | `Services/SocialConversation.cs`、`DebateDirector.cs`、`ViewModels/ChatViewModel.Social.cs` / `Artists.cs` / `Debate.cs` |
| 推論・並行処理・保存 | `LlamaService.cs`、`InferenceQueue.cs`、`AppStore.cs`、`BackupArchive.cs`、`ViewModels/ChatViewModel.Queue.cs` |
| Computer Use・VC | `ComputerChatController.cs`、`ComputerUseRunner.cs`、`ComputerDesktop*.cs`、`WindowsVoiceAudio.cs`、`ChatViewModel.Voice.cs` |
| インストール・更新・ログ | `InstallerService.cs`、`UpdateService*.cs`、`UpdateWorker.cs`、`UpdateCoordinator.cs`、`AppTrace.cs`、`UiDiagnostics.cs` |
| 自動検証・WPF検証 | `tests/Eluna.Checks`、`Services/*Verification.cs`、`scripts/Verify.ps1`、`Test-DiscordExperience.ps1` |

ここでファイル名だけを記したサービスは`src/Eluna.App/Services`、ViewModelは`src/Eluna.App/ViewModels`配下です。UIを作り直す前に、保存形式5と旧版保持、チャンネル外の記憶の分離、予約時のモデル・参照の固定、未送信本文を検索・未読へ公開しない規則を維持してください。

## 検証結果の持ち越しと再実行

[Git管理下に保存した小さな検証記録](verification/0.1.0-beta/README.md)には、betaのWPF・更新・絵文字・ネイティブ・公開ダウンロード結果と、GitHub CIの最終状態を置きます。旧PCの`artifacts`全体がなくても確認済み範囲を読めます。ログや画面、実モデルの生成本文は含めません。

新PCでの例です。実行していない項目を引き継ぎだけで成功扱いにせず、出力先は毎回新しくします。次のGUI exeは1件ずつ終了を待ちます。

```powershell
$app = (Resolve-Path artifacts/release-handoff/Eluna.exe).Path
Start-Process -FilePath $app -ArgumentList '--data-dir artifacts/check-new-emoji/data --verify-emoji artifacts/check-new-emoji' -WindowStyle Hidden -Wait -PassThru
Start-Process -FilePath $app -ArgumentList '--data-dir artifacts/check-new-update/data --verify-updates artifacts/check-new-update' -WindowStyle Hidden -Wait -PassThru
Start-Process -FilePath $app -ArgumentList '--data-dir artifacts/check-new-logs/data --verify-diagnostics artifacts/check-new-logs' -WindowStyle Hidden -Wait -PassThru
./scripts/Test-DiscordExperience.ps1 -ExecutablePath $app -OutputDirectory artifacts/check-new-discord -Portable
```

各プロセスの終了コードと各`result.json` / `result.txt`を確認してください。更新検証は隔離先を使い、実環境のレジストリ・ショートカットは変更しません。

実モデル確認は`--verify-social-final <出力先> --data-dir <専用保存先> --model-meta <新PCのmodel.json>`、会話と描画の確認は`--verify-autonomy`を使います。正確な引数と必須条件は`App.xaml.cs`と該当`*Verification.cs`を確認してください。実Webは外部への検索語送信とネットワーク状態の影響があります。

## ローカル専用ファイルの扱い

| 分類 | 引き継ぎ方 |
| --- | --- |
| 独自の検証・編集・公開補助ファイル20点 | [履歴保存先](development-history/2026-10-local-tools/README.md)へ原文とSHA-256を保存。古いパス・ハッシュを使うため、誤実行防止の`.txt`形式 |
| 現行のアプリ・テスト・標準スクリプト | すべて追跡済み。submodule内にも未コミットのソースなし |
| 古いソースコピー・変更前スナップショット | 生成物を除く130点を照合。91点はGit履歴内と一致、履歴にない39点は[参照用ソース控え](development-history/baseline-sources/README.md)へ保存。現行ソースとして混ぜない |
| 配布exe・インストーラー | betaは上記GitHub Releaseから取得可能。再ビルドで再現する。Gitのソース履歴へ重複追加しない |
| `build`、`bin/obj`、`.local`、展開済みRuntime | ビルド・SDK・キャッシュ。新PCで再生成・再取得する。CMakeCacheの旧PC絶対パスを移さない |
| 会話・資料・メモリ・モデル・生成物・実ログ・ブラウザプロファイル | 公開リポジトリへ含めない。下記のアプリ全体バックアップまたは私的なファイル移行を使う |
| Git認証・署名鍵・秘密情報 | 含めない。新PC側で認証する。保管済み認証をGitへ書き出さない |

旧PCには`%LOCALAPPDATA%\Eluna`の利用データと、Git除外のモデル・検証生成物があります。**ソースのcloneでは会話やモデルは移りません。** 続けて使う場合は、旧PCで設定→管理→データ・バックアップから全体ZIPを作り、新PCへ私的に転送して復元します。モデルを含める設定も選べます。モデルを省略した場合は新PCで再取得します。[バックアップ仕様](backup.md)を参照し、旧PCを消す前に復元を確認してください。今回、利用データのZIP作成・公開アップロードは実行していません。

## 今回の引き継ぎで実施した確認

ソースを変更せず、資料と履歴を追加しました。59点の保存原文と7点の検証結果は、原本のSHA-256に加えGitのステージ内の実バイトも一致することを確認しました。PowerShell補助16点の構文、JSON12点の読み取り、Markdownの相対リンク、Git差分の空白検査と公開対象への秘密情報・モデル・実行ファイル混入を確認しました。新PCでのC#・ネイティブ再ビルドや、保存した全履歴スクリプトの再実行は今回行っていません。beta元コミットのCI成功と、新PCで必要な検証を区別してください。
