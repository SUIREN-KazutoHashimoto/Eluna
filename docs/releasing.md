# ブランチとリリース運用

| ブランチ | 役割 | 通常の統合先 |
| --- | --- | --- |
| main | リリース用 | リリース時のみ更新 |
| develop | リリース前の開発統合 | main |
| codex/*、feature/*、fix/*、chore/* | developから作る作業ブランチ | develop |

通常の変更は作業ブランチから`develop`へPRを出します。CIのBranch policyチェックは、`main`向けPRの元ブランチが同じリポジトリの`develop`であることを確認します。直接pushの制限にはGitHub側の保護ルールが必要です。

## GitHub側で適用する設定

このファイルとCIを追加しただけでは、保護ルールは有効になりません。オーナーがGitHubのRulesetsまたはBranch protectionで次を設定してください。

- `main` / `develop`: PR経由の変更、会話の解決、必須CI、force pushと削除の禁止。
- 必須チェック: `Branch policy`、`Managed build`、`Native CPU build`、`Native Vulkan build`。CIが一度実行された後に選択する。
- レビュー人数はプロジェクトの人数に合わせる。一人で開発する場合、自分では満たせない承認人数を必須にしない。
- 作業PRのデフォルト宛先を`develop`にするなら、GitHubのdefault branchも`develop`へ変更する。リリース用ブランチは引き続き`main`。

現時点でGitHub側の設定はこのリポジトリ整備に含めて適用していません。

## リリース手順

1. `develop`で対象変更を揃え、バージョンとリリースノートを準備する。
2. CIと実機確認を行う。会話復元、IME、生成停止、サーバー終了、対応DPI、クリーン環境での動作を確認する。
3. 固定したllama.cppと同梱コードのライセンス、モデルの扱い、配布ファイルを確認する。
4. 同じリポジトリの`develop`から`main`へリリースPRを作る。
5. 通常のマージコミットまたはfast-forwardで統合し、mainのリリースコミットに`vX.Y.Z`タグを付ける。リリースPRはsquash / rebaseで履歴を分岐させない。
6. 検証済みの成果物とリリースノートを公開する。リリース専用に変更した内容があれば`develop`にも反映する。

CIはビルド・検証と単体exeの作成を行います。リリースの自動公開は行いません。`0.1.0-dev`は開発用の仮バージョンです。配布確認は[プロトタイプガイド](prototype.md)を参照してください。

緊急修正で`main`起点が必要な場合はオーナーの指示で例外を決め、main向け元ブランチのCI条件と保護ルールを確認し、修正を`develop`にも反映します。
