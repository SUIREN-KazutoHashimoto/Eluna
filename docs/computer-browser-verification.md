# EdgeでのComputer Use検証

6件の架空PCを扱う検証用サイトを、Windows標準のEdgeと新しい検証用プロファイルで開きます。Elunaの通常のチャット送信から、実際のローカルモデル・Computer Useコントローラー・Windowsのマウス／キーボード処理を通します。利用者のブラウザプロファイル、アカウント、外部サイトは使いません。

## 依頼と合格条件

1. カタログを「ミニPC」「最大120000円」「メモリ32GB以上」「在庫ありのみ」で絞り込み、価格が安い順にする。
2. 最安商品の詳細を開き、商品名・税込価格・メモリを読む。
3. 比較メモを別タブで開き、読み取った商品情報と、用途「ローカルAI用」、受け取り方法「店頭受取」を入力する。
4. 下書きを保存し、保存済み表示を確認して完了を報告する。

最安の商品は`North Mini 32`、価格`99800`円、メモリ`32`GBです。在庫切れ、メモリ不足、カテゴリ違いの安い商品を混ぜています。モデルには正解や要素IDを渡しません。保存するJavaScriptも商品情報を自動補完しません。

合格は、保存された各入力値、絞り込み・並び順、開いた詳細の商品ID、保存済みの画面表示、モデルの完了状態がすべて一致する場合だけです。確認待ちでは、この検証ページの「下書きを保存」だけを承認します。想定外の重要操作や、手動の操作引き継ぎが必要になった場合は不合格として停止します。

## 再実行

開発用の検証です。Windowsをロック解除してから、リポジトリのルートで実行します。モデルは取得済みの画像対応GGUFとprojectorの`model.json`を指定します。ブラウザは検証専用の新規プロファイルで起動し、終了時にそのブラウザプロセスを閉じます。

```powershell
dotnet run --project src/Eluna.App -c Release --no-build -- `
  --verify-computer-browser artifacts/browser-check `
  --data-dir artifacts/browser-check/data `
  --model-meta <画像対応モデルのmodel.json> `
  --fixture-page tests/fixtures/computer-browser/index.html
```

結果は`result.json`、操作記録は`progress.json`、保存の実データは`draft.json`、画面は`step-*.jpg`と`final.jpg`です。`Success: true`が合格、`BlockedByLock: true`はWindowsのロックによる未実施です。画像や検証用プロファイルを含む`artifacts`はGitへ追加しません。

## 2026-10-16の確認範囲

ChromeとEdgeの検証用プロファイルの起動・ページ読み込みまで進みました。CodexのComputer Useプラグインはアプリ承認待ちでタイムアウトしました。その後Eluna自身の実行経路で検証し、前面プロセスが`LockApp`であることを読み取り、Windowsがロックされていると確認しました。ロック画面への入力や、解除・OS設定変更は行っていません。

`artifacts/computer-browser-edge-run4/desktop-before.txt`と`desktop-ready.txt`に診断を、`result.json`に未達の結果を記録しています。Qwen3.5-4B Q4_K_M＋projector／Vulkan0で計画生成までは通りましたが、ブラウザへの操作は0回で停止しました。複数条件の絞り込み、別タブ、転記、保存の成功率は未確認です。

ロック中は、モデルの読み込み・アプリの観察・入力の前に理由を表示して停止する修正を追加しました。検証用HTTPサーバーの日本語JSON保存、別タブ用ページ、不明な経路の拒否は通常の自動チェックへ含めています。実ブラウザの成功とは区別します。

`scripts/Verify.ps1`は`artifacts/computer-browser-verify.log`で通過しました。このPCがロックされた実状態で、コントローラーがモデルの読み込み・アプリ列挙・入力の前に停止するチェックも実行しています。最終の検証用CLIは`artifacts/computer-browser-preflight/result.json`に`BlockedByLock: true`を記録し、ブラウザを起動せず終了しました。実ブラウザの合否判定はロック解除後に残しています。
