# 旧PCにだけあった補助ファイル — 2026-10-19保存

開発再開に際し、`artifacts`内にだけ存在した独自スクリプト・公開文面20点を原文のまま保存しました。ファイル名の末尾に`.txt`を付け、元のサブフォルダーは`--`で表しています。元のパス・容量・SHA-256は[manifest.json](manifest.json)にあります。実行バイナリ、モデル、認証情報、会話、ブラウザプロファイルは含みません。

これらは**過去の作業手順の参照用**です。移動後の場所から実行する設計ではありません。旧リリースの`artifacts`、特定のモデルID、当時のnativeハッシュ、旧UIの結果形式を前提にしたスクリプトがあります。最新の再実行は[開発引き継ぎ](../../development-handoff.md)と現行の`scripts`・`*Verification.cs`を使ってください。

| 元のファイル | 内容・注意 |
| --- | --- |
| `Verify-*Portable.ps1`、`Verify-*PortableFinal.ps1`等 | 各改修時点の単体exe・実モデル・画面の検証。現在の統合回帰は`scripts/Test-DiscordExperience.ps1` |
| `Verify-AgentRegression.ps1`、`Verify-SocialRegression.ps1`、`Verify-MessageUi.ps1` | 過去のUI回帰ラッパー。旧exeや旧出力フォルダーを参照 |
| `backup-stability/Run-Portable.ps1` | 10分反復、再起動、強制終了復元の当時の検証。nativeハッシュや通知件数の前提は現在と異なる |
| `multi-monitor/Verify-Portable.ps1`、`Confirm-Portable.ps1` | R07の検証と結果照合。旧版のnativeハッシュと通知件数を使用 |
| `Update-MessageLayout.ps1`、`remaining-edits.py`、`remaining-media-integration.py` | すでにソースへ反映された一度きりの編集。現行ソースへ再適用しない |
| `Beta-GitHub.ps1` | 0.1.0betaを作成した一度きりのGitHub REST補助。既存Git認証をメモリ内で利用する処理はあるが、認証の値は保存していない。ブランチ・タグ・リリースIDを固定しており再実行しない |
| `beta-pr-body.md`、`beta-release-notes.md` | PR #2 / 0.1.0betaの公開時点の文面。最新の状態はGitHubと引き継ぎ資料を参照 |

今回の検証は原本とのハッシュ一致とPowerShellの構文確認です。これら全履歴スクリプトを最新アプリで再実行した記録ではありません。新しい依存・Python環境を導入する指示でもありません。
