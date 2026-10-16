# 完成版に向けた実装・検証の台帳

2026-10-17: `codex/social-chat-ux`でキャラクターの自動生成、本人のプロフィール、会話の対等な参加者表示、既存チャンネルへのUI／AI招待、入力候補・複数宛先・色付きメンション、プロフィールからDM、参加通知の表示密度を改善。今回のexeは`artifacts/release-social/Eluna.exe`。[仕様と確認範囲](social-chat-ux.md)を参照。

2026-10-16: `codex/agent-permissions-autonomy`でチャンネルACL、性格・ロールプレイ、つながりと招待許可、AIによるサーバー・チャンネル作成と担当別の実行・報告、入力中表示を追加。開発版exeは`artifacts/release-agents/Eluna.exe`。[仕様と確認範囲](agent-permissions-autonomy.md)を参照。

プロフィール編集、プロジェクト＝サーバー、ボイスチャンネル、ウィンドウ共有とComputer Useの入口、および重なり対策を追加しました。[現行の仕様・操作と検証範囲](voice-channels.md)を参照してください。

2026-10-16: codex/discord-shell で主画面をDiscordを参考にした構成へ変更し、AI設定をユーザー設定へ集約した。モデル選択はアカウントで管理する。今回の配布確認用exeは artifacts/release-discord/Eluna.exe。[画面・設定の移動先と検証](discord-ui.md)を参照。

2026-10-16: `codex/agent-channel-integration`でA01〜A16の統合改修を実装。アカウント、サーバー・チャンネル・DM・スレッド、参照許可付きメモリ、共通タイムライン、自由形ディベート、生成物の関連付け、schema 4と旧履歴保全を追加した。配布確認用exeは`artifacts/release-workspace/Eluna.exe`。使い方、確認結果、実機で未確認の範囲は[統合改修の記録](workspaces.md)を参照。以下のexeパスは各機能追加時点の記録。

Computer Useを会話へ統合した開発版は`artifacts/release-computer-chat/Eluna.exe`。専用ウィンドウを廃止し、チャットからの依頼、アプリ許可、通常のマウス・キー操作の自動実行、重要な操作の確認、追加指示、一時停止・再開・引き継ぎ、会話切り替え、履歴保存と再許可を実装した。使用モデルに左右される操作精度をChatGPTと同等と断定しない。現在の使い方・確認済み／未確認の範囲は[Computer Use](computer-use.md)を参照。以下は機能追加時点の記録として保持する。

Computer Useは`codex/computer-use`で追加した。開発版exeは`artifacts/release-computer-use/Eluna.exe`。対象の複数選択、ローカルモデルによる画面・UI要素の観察、各操作の確認と入力文字修正、入力・クリック・スクロール等、処理リスト、停止と補助プロセスの後始末を実装した。仕様と確認済み・未確認の範囲は[Computer Use](computer-use.md)に記録する。以下は各機能を追加した時点の記録として保持する。

全体バックアップ・復元と安定性の仕上げは`codex/backup-stability-release`で実装した。最新の配布候補は`artifacts/release-backup-stability/Eluna.exe`。全体ZIP、復元前の退避・中断回復・再起動、モデル本体がない取得の再開、960件のキュー負荷検証を追加した。操作は[バックアップ](backup.md)、実モデル・配布・Git統合の記録は[今回の作業記録](backup-stability-release.md)を参照。追加の物理操作・別PCの検証はオーナー指定により今回の対象外。以下は各機能を追加した時点の記録。

残機能R01〜R06・R08〜R10は`codex/remaining-features`で実装した。空きVRAM・予約の必要量予測、能力フィルター、画像モデルのギャラリー、スキル差分、メディア予約と読書位置の再起動復元、Markdownの背景・末尾解析、取得と画像／文書生成の並列化を追加した。配布先は`artifacts/release-remaining-features/Eluna.exe`。操作と検証・制約は[残機能の実装記録](remaining-features.md)。R07はその後`codex/multi-monitor-restore`で実装した。この時点の配布先は`artifacts/release-multi-monitor/Eluna.exe`。複数モニターの復元規則と確認範囲は[ウィンドウ復元](window-placement.md)を参照。

2026-10-15のUI改善は`codex/ui-ux-polish`で実装した。管理操作を設定へ集約、進捗と予約を「処理リスト」へ統合し、表示密度・編集保護・読書位置・生成中の選択・モデル準備・スキル編集を改善した。開発版exeは`artifacts/release-ui-ux-polish/Eluna.exe`。監査U01〜U35への対応、実モデルと画面検証、残る制約は[UI / UXの改善記録](ui-ux-polish.md)を参照。以下は各機能を追加した時点の記録として保持する。

2026-10-15にSKILL.md形式のスキルを追加した。作業ブランチは`codex/agent-skills`、開発版exeは`artifacts/release-skills/Eluna.exe`。取り込み・作成・編集・書き出し、会話ごとの自動／指定／オフ、段階的な資料読み込み、PowerShellの確認付き実行、プロジェクト分離と予約した版の保持を実装した。必須検証、単体exeでの4Bモデルによる適用・CSV作成・文書生成・並行実行とWPF操作を確認した。Claude専用環境やPython/Bashの完全互換は対象外。範囲と確認記録は[スキルガイド](skills.md)を参照。

2026-10-15に会話を切り替えながらの推論予約、同時推論1〜4件、個別停止、予約の保存・再開を追加した。作業ブランチは`codex/concurrent-work`、開発版exeは`artifacts/release-concurrent/Eluna.exe`。必須検証、単体exe、RX 9070 XTでの4Bモデル2並列と会話ごとの順序、プロジェクトの参照分離を確認した。範囲・操作・確認記録と未確認条件は[並行作業ガイド](concurrent-work.md)を参照。

2026-10-15に画像/PDF・Office文書等の認識、画像生成、文書生成を追加した。作業ブランチは`codex/media-documents`、開発版exeは`artifacts/release-media/Eluna.exe`。形式・上限、承認された依存、実モデルの確認結果と未確認範囲は[画像とドキュメント](media-documents.md)を参照。画像生成のCPU/Vulkanエンジンも単体exeへ含める。

2026-10-15に通常チャットの自動Web検索、明示依頼・強制検索モード、検索しないモードを追加した。作業ブランチは`codex/automatic-web-search`、開発版exeは`artifacts/release-auto-search/Eluna.exe`。実装と検証・残る品質上の制約は[通常チャットの自動Web検索](automatic-web-search.md)を参照。

2026-10-15の追加UI変更で、参加者のドラッグ＆ドロップとDiscord風の会話表示を追加した。UI変更を含む開発版は`artifacts/release-debate-discord/Eluna.exe`。変更内容と確認範囲は[参加者・会話表示の変更](ui-debate-chat.md)を参照。以下の01〜29の初期追加時の検証記録も保持する。

作業ブランチ: `codex/complete-app`。対象は[機能一覧の01〜29](product-backlog.md)全体です。2026-10-15に全項目の機能を開発版へ追加しています。以下は実装と検証の到達点であり、製品としての完成・正式リリースを示す文書ではありません。操作は[使い方](using-eluna.md)を参照してください。

| 対象 | 実装状況 | 検証・残り |
| --- | --- | --- |
| 01〜02 | WPFでMarkdownの見出し、箇条書き、表、引用、リンク。コード色分け、コピー、折り返し | パーサー検証と描画確認。CommonMarkの全構文互換性は対象外 |
| 03〜05 | コピー・再生成・続き・質問編集・元の会話を保つ分岐 | Qwen実モデルで生成と独立保存を確認 |
| 06〜07 | 全分岐の全文検索、名前、ピン、保管、下書き保存 | 保存・会話切替と、一致する分岐の選択を確認。本文内の一致箇所へのスクロール・強調は対象外 |
| 08 | token数に応じた古い履歴の要約、元の履歴を保持、引き継ぎメモの編集 | 長い会話の実モデル検証を通過。メモを空にした後の元履歴復帰も検証。要約品質の評価は継続 |
| 09〜12 | 共通・会話別指示、文字・行間、明暗テーマ、サイドバー、最新へ移動、仮想化、ショートカット | 描画・設定・スクロール確認。実IME、複数DPI・モニターは未確認 |
| 13〜14 | Windowsの空きRAM・GPU報告値、容量の案内、GGUFの規模・量子化・思考形式、モデルカード | ヘッダーと容量判定を自動検証。実GGUFの0.77B・Q4_K_M・qwen35を読み取り。能力は配布元の説明と分けて表示し、日本語の未評価を明示 |
| 15 | 順次バックグラウンド取得、キュー、一時停止・再開、速度・残り時間、再起動復元 | 中断後に次の項目を処理、再起動後のRange再開、通信待機タイムアウトと再試行を自動検証。完了モデル選択と管理中の無効化、通常・最小サイズの画面を確認 |
| 16〜17 | 保存先移動、SHA-256照合、削除、名前、お気に入り、管理フォルダの使用量 | 中断時に旧保存先と元ファイルを保持。外部参照の削除拒否、保存先が利用できない時の起動を検証。実モデルの移動後GPU推論を確認 |
| 18 | 分割GGUFの一括取得、全ファイル検証、中断再開、手元からの一括追加 | 自動検証を通過。実際の4分割モデルを取り込み、GPU推論で5を回答することを確認 |
| 19 | モデル別の記憶長、温度、Top P/K、繰り返し、回答上限、既定思考量、プリセット | 永続化とエンジン要求への反映を自動検証。8192 token設定の実モデル生成と移動後の保持を確認 |
| 20〜22 | 読み込み・解放、エラー説明・再試行、1秒間隔の途中保存と復元表示 | 実モデル・取消・保存復元を確認。確認専用アプリを生成中に強制終了し、352文字の復元と子エンジン終了、新しい起動の準備完了を確認。実際のメモリ不足の再現は未実施 |
| 23〜25 | 世代バックアップ、復元、JSON入出力、処理時間・速度・入力量・RAM、本文を含めない診断 | 自動検証・画面確認。5,000メッセージ・37.2 MBの履歴で保存/読み込み0.40秒、書き出し/読み込み0.62秒。入出力の解析はUIスレッドから分離。測定値はこのPCでの結果 |
| 26 | バージョン、公開リリースの確認、更新内容の表示 | 開発版・正式版・プレビュー版の比較、未公開・通信失敗・形式不正を自動検証。画面を描画確認。実際のリリース公開・置き換えは未実施 |
| 27 | 自前の複数検索先、本文取得、参照元リンクの発見、重複除去、キャッシュ、出典、活動表示、全体予算・停止 | 実通信・本文取得・保存・出典・失敗処理を確認。参照元→リポジトリ→文書を共通予算内でたどることを自動検証。実検索で公式リポジトリとリリースページに到達。検索先の状態・問いによって結果は変わる |
| 28 | 計画編集、追加調査、照合、レポート、保存再開、Markdown書き出し | 制御フロー・再開を自動検証。4B実モデルでレポート生成と引用の本文一致を確認。根拠を確認できない事実の節は除去し、活動に記録。事実正確性と網羅性の評価は継続 |
| 29 | 役割・人数・モデル・ラウンドの設定、共有資料、順次発言、司会、停止再開 | 自動検証と4B実モデルの議論・司会、4B/2Bを役割別に切り替える実生成を確認。予算で予定発言を締め切った場合は警告を表示。同時生成は対象外。モデルの読解ミス・根拠のない推測は残る |

## 検証方法

- `./scripts/Verify.ps1`: Releaseビルド、SDK付属のformat、外部テストフレームワークを使わない動作確認。
- `dotnet run --project tests/Eluna.Checks -c Release --no-build -- --web-integration artifacts/web-checks`: 自前検索の実通信。
- `Eluna.exe --capture-dir <確認専用ディレクトリ>`: 表示サンプルをWPFで描画。実応答の内容や人による入力確認とは区別する。
- `Eluna.exe --data-dir <確認専用ディレクトリ> --verify-model <model.json> --verify-workflows`: CPU/GPU、会話と調査・議論の実モデル確認。利用者データを使わない。
- `./scripts/Test-AppRecovery.ps1 -ExePath <exe> -ModelMetadata <model.json>`: 新しい確認専用ディレクトリで、生成中のアプリだけを強制終了し、子エンジン終了・本文復元・新しい起動を確認する。
- `dotnet run --project tests/Eluna.Checks -c Release --no-build -- --history-scale artifacts/completion-audit-scale`: 5,000メッセージの履歴入出力。

検索・調査・議論までを含む確認済み開発版は`artifacts/release-research-checked/Eluna.exe`です。`Publish.ps1`で単体exeを作り、同梱CPU/Vulkanエンジンの展開・起動を実モデルで確認しました。exeだけを別フォルダへコピーし、.NETの探索先を無効にした状態でも起動・描画・検索・調査・議論が通りました。このexeには、その後ソースへ追加した分割GGUF対応はまだ含まれません。

最新の開発版は`artifacts/release-complete-dev/Eluna.exe`です。モデル管理と分割GGUFを含め、01〜29の機能を含めています。操作は[使い方](using-eluna.md)、[モデルガイド](models.md)、[調査ガイド](research.md)に記載しています。ビルドと自動検証は`artifacts/completion-audit-verify.log`、単体配布は`artifacts/completion-audit-publish.log`、履歴の実測は`artifacts/completion-audit-scale.log`に保存しています。

exeのみのコピーで.NET探索先を無効にし、独立した展開先を指定してGUIの描画・操作、生成中の強制終了と復元を確認しています。最終exeの描画と操作結果は`artifacts/complete-portable/preview-checked/result.txt`、同梱エンジンのGPU/CPU・思考量・取消・会話復元は`artifacts/complete-portable/native-checked/verification.json`です。復元結果は`artifacts/complete-portable/recovery-66aaf5f141f249bfa97ef32192751656/result.json`、検索・調査・議論の実生成と役割別モデルは`artifacts/complete-portable/workflows-final/verification.json`、`artifacts/complete-portable/debate-final/verification.json`です。品質の評価は[調査の評価メモ](research-evaluation.md)に記載しています。

最終exeのSHA-256は`AA6F41CB82AD215AB1E25F1879288C3DAB964F9C3E071D04BDC16268FF76A243`です。Windows x64のself-contained single-fileとして作り、CPU/Vulkanの配布用ネイティブ構成・固定コミットは変更していません。

確認結果は`artifacts/research-portable/preview/result.txt`、`artifacts/research-bundled-engine-*/verification.json`、`artifacts/research-bundled-work-*/verification.json`、`artifacts/split-model-verify.log`、`artifacts/split-model-real.log`に保存しています。利用者データと別の確認用ディレクトリで実行しています。

開発環境がない別PC、実IME、高DPI・複数モニター、スクリーンリーダー、各社GPU、コード署名、実際のリリース公開・更新は未確認です。これらは正式リリース前の確認として残しています。

2026-10-16: `codex/computer-use`で[Computer Useの画面効果](computer-use.md)を追加。対象枠、操作位置・入力先・ドラッグ経路、停止バーとチャットの状態連動を実装しています。自動チェック、描画、単体exeと同梱エンジンの起動確認は通過。実画面の前面化で停止しているため、効果と実入力の併用・取得画像への除外の実機検証は未完了です。最新の当該開発版は`artifacts/release-computer-effects/Eluna.exe`です。

2026-10-16: [Edgeの複雑なブラウザ操作の検証](computer-browser-verification.md)を追加。検証用の独立したプロファイル・ローカルサイト・結果照合を用意しました。前面化失敗の原因はWindowsのロック画面と確認し、ロック中の開始・観察・入力を止める案内を追加しました。自動チェックは通過、実ブラウザの操作成功はロック解除待ちで未確認です。この後の修正と検証用CLIはソースの開発ビルドに含まれ、上記の配布exeにはまだ含みません。
