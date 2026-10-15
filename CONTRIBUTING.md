# 開発への参加

まず[README](README.md)、[AGENTS.md](AGENTS.md)、[依存関係ポリシー](docs/dependencies.md)を確認してください。

## 作業ブランチ

```powershell
git fetch origin
git switch develop
git pull --ff-only origin develop
git switch -c codex/short-task-name
```

人の作業では`feature/`、`fix/`、`chore/`も使用できます。通常の起点とPRの宛先は`develop`です。`main`はリリース用です。

## 変更の進め方

1. 一つの目的に絞り、利用者にとっての改善と完了条件を決める。
2. 外部パッケージが必要なら、導入前にオーナーへ相談する。承認された場合は依存関係台帳とチェックも更新する。
3. 実装と関連ドキュメントを変更し、`./scripts/Verify.ps1`を実行する。
4. llama.cppやネイティブビルドを変更したら`./scripts/Build-Llama.ps1`も実行する。
5. UIは実機で確認し、PRに変更の目的、確認結果、既知の制約を書く。

会話やモデルの実データをIssue、PR、ログへ貼らないでください。再現手順には匿名化した最小データを使います。llama.cppの変更は直接混ぜず、固定コミットの更新とEluna側の調整を区別してください。

コミットメッセージは`chore: ...`、`feat: ...`、`fix: ...`、`docs: ...`など、目的が分かる短い文にします。破壊的な変更やデータ形式変更はPRに明記します。

`tests/Eluna.Checks`は外部テストフレームワークを使わない検証プログラムです。Verifyで履歴復元・SSE・ダウンロードの確認を実行します。実モデルを使う検証は[プロトタイプの使い方](docs/prototype.md)を参照してください。
