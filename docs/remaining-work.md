# 残作業とリポジトリ整理 — 2026-10-19

残作業の入口は[追跡Issue #10](https://github.com/SUIREN-KazutoHashimoto/Eluna/issues/10)、分類は[0.1.0beta フォローアップ](https://github.com/SUIREN-KazutoHashimoto/Eluna/milestone/1)です。R01〜R10、アカウント・チャンネル統合、自由形ディベート、N01〜N20は実装済みです。以下は主に品質確認と運用の残りであり、実装を最初からやり直す一覧ではありません。

## GitとIssueの整理結果

| 対象 | 現在の扱い |
| --- | --- |
| `develop` | 開発統合・既定ブランチ。[PR #2](https://github.com/SUIREN-KazutoHashimoto/Eluna/pull/2)を`2af29f2`で通常マージ。統合前の最新10チェックは全成功 |
| `main` | 正式版リリース用として保持。今回の開発版統合では更新しない |
| `codex/social-autonomy` | 全コミットがdevelopから到達できることを確認し、統合後にリモート・ローカルを削除 |
| 新しい作業 | 最新developから`codex/<task>`を作り、PRはdevelopへ。作業中のブランチや未統合コミットは削除しない |
| 公開済みbeta | `v0.1.0-beta`は`05312a0ed34debe3b1bf80c3c69607f3cde88720`のまま保持 |
| 自動公開 | developへの統合で開発版、mainへのリリースPR統合で正式版を作る。[PR #2統合後の実行](https://github.com/SUIREN-KazutoHashimoto/Eluna/actions/runs/39199128187)で結果を追跡する |
| Issue | 整理前は0件。未確認条件を7件へ分割し、親Issue #10へ集約。既存Issueの重複クローズは不要だった |
| ラベル | `priority: high`、`priority: medium`、`verification`、`release`、`tracking`を追加。既存の`documentation`等は保持 |
| マイルストーン | `0.1.0beta フォローアップ`。期限は未指定。H07は必須条件に含めない |

GitHubの公開APIではmain/developの`protected=true`を確認しました。必須CIや管理者例外など、個々の保護条件がすべて期待どおりであるという保証とは区別します。詳細監査はH09に残します。GitHubの「統合済みブランチの自動削除」は有効であることを確認しました。

## 今回の新PCで完了した範囲

検証対象ソースは`c8ec5e59ed5431f4ce7f4927c7f241dcd802c300`です。開発環境・ビルド生成物・ローカルの確認記録はGit除外の`.local`、`build`、`artifacts`に置き、実ログや生成された会話を本書へ転記しません。

| 項目 | 確認結果 |
| --- | --- |
| 固定依存 | llama.cpp `6868a52cb337450658006ad2f9281c2eb63f1666`、stable-diffusion.cpp `120e3e875c5501c1babb50c0966122cb569d12d4`、再帰submoduleを取得・照合 |
| .NET | 安定版SDK `10.0.402`。Microsoft公式SHA-512を照合しローカル配置 |
| C++ | Visual Studio Build Tools 2026 `18.11.12306.390`、MSVC `19.51.36260.0`、Windows SDK、CMakeを導入。再起動要求なし |
| Vulkan SDK | 固定版`1.4.363.0`を既存スクリプトのSHA-256で照合し、copy-onlyで配置 |
| `Verify.ps1` | Releaseビルドは警告0・エラー0。整形・全コンソール回帰・Git空白検査が成功 |
| `Publish.ps1` | CPU/Vulkanの推論・画像生成エンジン計4本と自作UI描画部品をビルドし、自己完結exeを作成 |
| 配布物検査 | `0.1.0-develop.0.4` / develop、形式5、容量、SHA-256、インストーラーとの一致を確認 |
| WPF | 単体exeと専用保存先で、カラー絵文字・Unicode保持・入力Undo・模擬IME・ピッカー・明暗表示を確認 |
| 同梱エンジン | 単体exeからの展開後、4本すべての`--version`が終了コード0。ビルド成果物とSHA-256が一致 |

ローカル確認用`Eluna.exe`は145,223,705 bytes、SHA-256は`d4e7f6ed59bbf83e1971b6bc906aca954319816f9d2bbc734ae7483c64770a13`でした。この番号・ハッシュはローカル生成物の記録であり、GitHubで自動生成される開発版の番号・ハッシュと同一とはしません。ネイティブ依存側にはCMake/MSVCの警告が残りますが、ビルドと上記の起動検査は成功しました。

モデル・会話データは移行していません。このPCのQXL表示環境で、実GPU演算や実モデル生成を確認したとは扱いません。開発ツールのない環境での実利用、正式なインストール・削除、実GitHub配信から別版への更新も未確認です。

## 残作業

| ID | 優先・状態 | Issue / 入口 | 残る完了条件 |
| --- | --- | --- | --- |
| H01 | 統合済み・公開結果を追跡 | [PR #2](https://github.com/SUIREN-KazutoHashimoto/Eluna/pull/2)、[公開実行](https://github.com/SUIREN-KazutoHashimoto/Eluna/actions/runs/39199128187) | 自動公開の成功と公開4ファイルの容量・ダイジェストを確認。利用者PCの更新成功はH04で別に扱う |
| H02 | 高・一部完了 | [#3](https://github.com/SUIREN-KazutoHashimoto/Eluna/issues/3) | 開発ツールのないWindowsで起動、モデル取得、CPU/対応GPU切替、保存・再起動。新PCの構築・基本検証は完了済み |
| H03 | 高・未確認 | [#4](https://github.com/SUIREN-KazutoHashimoto/Eluna/issues/4) | 専用Edgeプロファイルで絞り込み→別タブ参照→転記→保存を完走し、値を照合。ロック・前面化による停止を成功扱いにしない |
| H04 | 高・未確認 | [#5](https://github.com/SUIREN-KazutoHashimoto/Eluna/issues/5) | 旧版から実GitHub配信へ更新、Windowsへの登録、ショートカット、削除後のデータ保持・再導入と復旧 |
| H05 | 中・未確認 | [#6](https://github.com/SUIREN-KazutoHashimoto/Eluna/issues/6) | 本人＋49アカウントの長時間運転と、閲覧・招待・権限・予約・停止・再起動・推論枠競合の実測 |
| H06 | 中・評価を拡充 | [#7](https://github.com/SUIREN-KazutoHashimoto/Eluna/issues/7) | モデル別の指名・横やり・短文・自発開始・描画委任・引用対応・根拠不足時の返答を共通ケースで評価 |
| H07 | 保留・必須対象外 | [引き継ぎ](development-handoff.md) | 物理IME、実DPI/モニター切替、スクリーンリーダー、実マイク等は未確認記録として保持。追加実機検証は自動で必須に戻さない |
| H08 | 中・確認範囲を拡充 | [#8](https://github.com/SUIREN-KazutoHashimoto/Eluna/issues/8) | 他GPU、CPUのみの実生成、少RAM/VRAM、画像認識projector・生成モデルの組合せとOOM・通信断・中断回復 |
| H09 | 運用・判断が必要 | [#9](https://github.com/SUIREN-KazutoHashimoto/Eluna/issues/9) | 署名方針、保護ルールの詳細照合、正式版の品質条件。mainへはdevelopからリリースPRで統合 |
| H10 | 継続 | [#10](https://github.com/SUIREN-KazutoHashimoto/Eluna/issues/10)、本書 | UI・モデル・運用変更に合わせ、説明と確認済み/未確認の台帳を更新する |

50人はサーバー定員であり、49モデルを同時に常駐させる要件ではありません。旧PCで一部モデル・GPUを検証済みであることを、全モデル・全PCの動作保証へ広げません。

## 次に進める順序

1. H01の統合後CI・開発版公開の結果を確認する。
2. H02〜H04を優先し、H04では実際に公開された別バージョンを使用する。
3. H05・H06・H08を実測し、結果に合わせて設定・説明・対応条件を更新する。
4. H09のオーナー判断と正式版の条件が揃ってから、develop→mainのリリースPRへ進む。

保存形式5と旧形式の保持、権限外の記憶を参照しない規則、予約時のモデル・参照の固定、未送信本文を検索・未読へ公開しない規則を維持します。H07はこの順序の必須工程には追加しません。

過去の計画・検証記録は履歴として保持します。資料間で状態が食い違う場合は、本書・追跡Issue・[開発引き継ぎ](development-handoff.md)から現在の根拠を確認してください。
