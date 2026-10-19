# ブランチとリリース運用

2026-10-19にPR #2をdevelopへ統合し、既定ブランチをdevelopへ変更しました。現在の残作業は[一覧](remaining-work.md)と[追跡Issue #10](https://github.com/SUIREN-KazutoHashimoto/Eluna/issues/10)で管理します。

| ブランチ | 役割 | 通常の統合先 |
| --- | --- | --- |
| main | リリース用 | リリース時のみ更新 |
| develop | リリース前の開発統合・既定ブランチ | main |
| codex/*、feature/*、fix/*、chore/* | developから作る作業ブランチ | develop |

通常の変更は作業ブランチから`develop`へPRを出します。CIのBranch policyチェックは、`main`向けPRの元ブランチが同じリポジトリの`develop`であることを確認します。直接pushの制限にはGitHub側の保護ルールが必要です。

## GitHub側で適用する設定

このファイルとCIを追加しただけでは、保護ルールは有効になりません。オーナーがGitHubのRulesetsまたはBranch protectionで次を設定してください。

- `main` / `develop`: PR経由の変更、会話の解決、必須CI、force pushと削除の禁止。
- 必須チェック: `Branch policy`、`Managed build`、`Native CPU build`、`Native Vulkan build`。CIが一度実行された後に選択する。
- レビュー人数はプロジェクトの人数に合わせる。一人で開発する場合、自分では満たせない承認人数を必須にしない。
- 作業PRのデフォルト宛先を`develop`にするなら、GitHubのdefault branchも`develop`へ変更する。リリース用ブランチは引き続き`main`。

2026-10-19の公開APIでmain/developの`protected=true`を確認し、設定画面で統合済みブランチの自動削除が有効であることを確認しました。必須CI・レビュー・管理者例外などの詳細条件は未監査で、上記の全項目が設定済みであるとは報告しません。詳細照合は[H09 / #9](https://github.com/SUIREN-KazutoHashimoto/Eluna/issues/9)で扱います。保護条件を緩めるための変更は行っていません。

## リリース手順

1. `develop`で対象変更を揃え、バージョンとリリースノートを準備する。
2. CIと実利用の確認を行い、会話復元、生成停止、サーバー終了、クリーン環境での動作などを記録する。物理IME・実DPI等のH07はオーナー判断で保留しており、自動的に必須条件へ戻さない。
3. 固定したllama.cppと同梱コードのライセンス、モデルの扱い、配布ファイルを確認する。
4. 同じリポジトリの`develop`から`main`へリリースPRを作る。
5. 通常のマージコミットまたはfast-forwardで統合し、mainのリリースコミットに`vX.Y.Z`タグを付ける。リリースPRはsquash / rebaseで履歴を分岐させない。
6. 検証済みの成果物とリリースノートを公開する。リリース専用に変更した内容があれば`develop`にも反映する。

## 自動公開

`.github/workflows/ci.yml`はビルド・検証・単体exe作成を行います。これとは別の`release.yml`は、develop/mainへのpushと明示的なworkflow_dispatchで配布物を作成し、GitHub Releasesへ公開します。PRの統合にもこのpush条件が適用されます。

- developは`v<基底版>-develop.<実行番号>.<試行番号>`のプレリリース。正式版のlatestへは指定しません。
- mainは基底バージョンの正式版。公開済みの同じタグがあれば失敗するため、次回の正式公開前にバージョンを更新します。
- 固定依存と標準のVerify・Publish・配布物検査を実行し、exe・インストーラー・更新マニフェスト・チェックサムの4点を下書きへアップロードします。
- GitHub上の容量とSHA-256ダイジェストがローカル成果物と一致してから公開します。失敗時は成功扱いにせず、下書き・ログを確認します。
- 公開済みのタグや配布物を付け替え/上書きしません。`v0.1.0-beta`は既存の固定コミットと配布物を保持します。

[PR #2統合後の初回開発版公開](https://github.com/SUIREN-KazutoHashimoto/Eluna/actions/runs/39199128187)の結果はActionsで追跡します。ワークフローの公開成功と、利用者PCでの実配信からの自動更新成功は別の確認です。後者は[H04 / #5](https://github.com/SUIREN-KazutoHashimoto/Eluna/issues/5)に残します。

新PCの`0.1.0-develop.0.4`はローカル確認用であり、自動公開される開発版の番号とは区別します。配布確認は[プロトタイプガイド](prototype.md)も参照してください。

緊急修正で`main`起点が必要な場合はオーナーの指示で例外を決め、main向け元ブランチのCI条件と保護ルールを確認し、修正を`develop`にも反映します。
