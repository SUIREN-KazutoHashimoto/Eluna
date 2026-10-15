# R07 — 複数モニターのウィンドウ復元

作業ブランチは`codex/multi-monitor-restore`。主画面を閉じたときの画面・位置・通常サイズ・最大化状態を保存し、次回起動時に復元する。設定操作は不要。Windows標準APIと.NET / WPFのみを使い、外部依存や両推論エンジンは変更していない。

## 復元の規則

- WindowsのモニターデバイスインターフェイスIDを保存し、接続済みなら同じ画面へ戻す。DISPLAY番号が変わってもIDで照合する。IDを取得できない場合だけDISPLAY名を使う。接続ポート変更などでIDが変わった場合は次の位置による選択へ進む。
- 保存先がない場合は、以前のウィンドウと重なる面積が最大の画面、次に距離が近い画面を選ぶ。同条件ならプライマリーを優先する。プライマリーへ一律に移す方式を置き換えた。
- 画面内の位置は作業領域の左上からの距離、サイズはDIP相当で保持する。画面の配置や拡大率が変わっても同じ画面内へ戻し、タスクバーを除いた現在の作業領域に収める。左や上にある画面の負座標も許容する。
- 通常サイズは最大化・最小化中も保持する。最大化から最小化して終了した場合も次回は最大化し、起動時に最小化したままにはしない。
- 作業領域が標準の最小サイズ820×600 DIPより狭い場合は、画面内へ収めるため最小サイズを緩める。広い画面へ移ると元の最小サイズへ戻す。非常に狭い画面での全機能の操作性を保証するものではない。
- 保存先はデータフォルダーの`window.json`。通常位置の物理ピクセル、保存時のDPI・作業領域・画面IDを版2として記録する。一時ファイルへ書いて置換し、書き込み失敗時は既存ファイルを維持する。不正・過大なファイルは無視して通常の初期表示へ戻る。
- 以前の版1（Left / Top / Width / Height / Maximizedのみ）も読み取り、次の終了時に版2へ更新する。版1には画面・DPIの情報がないため、位置は現在のプライマリーDPIで換算する最善努力の移行となる。通常サイズのDIPは復元先DPIへ換算する。

アプリのマニフェストにPer-Monitor V2を明示した。座標を扱う箇所ではWPFのDIPとWin32の物理ピクセルを混ぜず、WINDOWPLACEMENTの作業領域座標と画面座標も相互変換する。DPIは対象画面に置く非表示の小さなネイティブウィンドウから取得し、すぐ破棄する。

対象は主画面の終了・起動時の復元。ダイアログは従来どおり所有画面を基準に開く。起動中の画面取り外しによる移動はWindowsの通常動作に任せる。

## 検証

`WindowPlacementChecks`を標準の`./scripts/Verify.ps1`へ追加した。固定ケースで画面IDとDISPLAY番号変更、負座標、配置・DPI変更、画面取り外しの代替、上下左右の作業領域補正、狭い画面、旧形式、不正値を検査する。100 / 125 / 150 / 175 / 200 / 250 / 300%の配置を模擬した700組で、画面内への収まりと保存・復元を繰り返しても位置・サイズがずれないことを確認する。

実ウィンドウの検証は、隔離した保存先を指定して実行する。

```powershell
./artifacts/release-multi-monitor/Eluna.exe --data-dir ./artifacts/multi-monitor/portable/data --verify-window-placement ./artifacts/multi-monitor/portable/window-check
```

Windowsが認識する3画面（3440×1440、右2560×1440、右上1920×1080、すべて96 DPI）それぞれで、通常・最大化・最小化・最大化後に最小化の4状態を保存して開き直す。Win32の実ウィンドウ矩形と保存値、WPFとWin32のDPI、通常サイズへ戻した矩形を検査する。主画面の実際の起動・終了経路、旧形式の更新、壊れた5種類の保存、保存失敗時の後始末、画面外の検証用ウィンドウによる保存の上書き防止も確認する。

異なる拡大率の実画面間での移動・再起動、実際のケーブルの抜き差し・画面再配置、リモートデスクトップの接続条件変更は未確認。模擬配置の確認と実機の確認を区別する。実IME・スクリーンリーダー等の既存の未確認事項も残る。

APIの確認にはMicrosoftの[WINDOWPLACEMENTの座標規則](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-windowplacement)、[モニター情報](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-monitorinfo)、[Per-Monitor DPI](https://learn.microsoft.com/en-us/windows/win32/hidpi/high-dpi-desktop-application-development-on-windows)、[DPI取得APIの注意点](https://learn.microsoft.com/en-us/windows/win32/api/shellscalingapi/nf-shellscalingapi-getdpiformonitor)を参照した。

## 確定した確認結果 — 2026-10-15

- `artifacts/multi-monitor-verify.log`: Releaseビルド0警告・0エラー、書式、R07の追加検証と既存チェック一式、Gitの空白検査が成功。
- `artifacts/multi-monitor-publish.log`: `scripts/Publish.ps1 -SkipNativeBuild`が成功。配布先は`artifacts/release-multi-monitor/Eluna.exe`、132,854,637 bytes、SHA-256は`B59DE5EAC3F1F4E6693056E382CDFA8AE5AE7216D0BA805E712616D5787C4BA0`。
- exeだけを`artifacts/multi-monitor/portable/`へコピーし、インストール済み.NETを参照しない設定で起動した。`window-check/window-verification.json`に3画面×4状態の12件、主画面の実起動・終了、旧形式の更新、不正ファイル、保存失敗の後始末と検証用保存先の分離の成功を記録した。`main-restored.png`と各画面の通常状態の描画も確認した。
- 同じ配布exeの`ui-check/result.txt`と`verification.json`で、管理導線・進捗と予約順・取消・未保存保護・独立下書き・モデルフィルター・読書位置・1,000／10,000会話の操作を確認。最小820×600で会話欄334 DIP、履歴257 DIPを維持し、明暗テーマ・文字130%・設定のスクロールと下部操作を描画で確認した。`regression/result.txt`の既存画面回帰も成功。
- `portable/portable-verification.json`: 同梱したCPU／Vulkanのllama-server・sd-cliの4本を展開して`--version`で起動成功。各SHA-256は前のビルドと一致。エンジンフォルダーの共有DLLは0、ライセンス通知ファイルは23。ネイティブ構成を変えていないため、ネイティブの再ビルド・GPU演算のCPU照合は再実施していない。

作業用の配布検証スクリプトは初回、結果文を`success`と誤って想定して判定に失敗した。各アプリ検証の終了コードは0で、実際の結果文の`OK.`へ判定を修正し、生成済みの結果を`Confirm-Portable.ps1`で確認した。初回のチェック全体では既存スキル検証のマーカーファイルをPowerShellが書き込んでいる瞬間に読んで共有違反となったが、その後の全体チェックと最終の`Verify.ps1`は成功した。この既存テストの待機方法は今回変更していない。

ユーザーの既存差分を保持し、コミット・公開は行っていない。
