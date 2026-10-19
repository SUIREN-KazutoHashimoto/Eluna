# 0.1.0betaの持ち運べる検証記録

ローカルの`artifacts`にしかなかった小さな結果を、会話・モデル・画面・秘密情報・旧PCの絶対パスを含めずに保存しています。`sources.json`に元ファイルとSHA-256を記録しました。原文コピーのJSONは成功項目と確認範囲の記録であり、新PCで再実行した結果ではありません。

| ファイル | 対象 |
| --- | --- |
| `beta-emoji.json` | 公開betaと同じexe。カラー画素、肌色・ZWJ、Unicode選択、Undo、模擬IME、明暗 |
| `beta-update.json` | 公開betaと同じexe。隔離先への導入、旧プロセス待ち、本体置換、新WPFの起動確認、旧exe・モデル保持。システム登録は未変更 |
| `beta-native.json` | betaが展開したCPU/Vulkanのllama-server・sd-cliの4本、起動終了コードとハッシュ |
| `beta-public.json` | 2026-10-17、公開後の認証なしダウンロード・マニフェスト・SHA-256照合、ソースコミット |
| `development-autonomy.json` | beta直前の開発版。実モデルの指名と別アカウントの返信、実画像生成、本人名義の画像投稿、別チャンネル閲覧中の配送先保持 |
| `development-diagnostics.json` | beta直前の開発版。入力値を残さない操作ログ、本文から分離したメッセージ別ログ、保存・ZIP・明暗130% |
| `development-voice.json` | beta前の開発版。論理VC参加者、Windows TTSのWAVと日本語音声認識、画面共有UI。物理マイクの試験ではない |
| `github-ci.json` | 2026-10-19に取得したbeta元コミット`05312a0`のGitHub Check結果。push / PR双方のBranch policy、Managed、CPU、Vulkan、Portableがすべて成功 |
| `local-checks.json` | beta作成時のVerify成功記録とログのハッシュ、RX 9070 XTでのCPU参照一致の確認範囲 |

`development-*`はbetaのパッケージそのものの検証と区別します。実Webでは資料に対応する引用を生成した実行と、根拠を確認できず短い未確認返答になった実行があり、毎回の正確な回答を保証しません。実モデルの本文や元ログをGitへ含めていません。

各JSONのハッシュは検証資料の同一性を照合するためのもので、将来の再ビルドのexeが同じハッシュになる保証ではありません。ビルド環境・SDK・バージョン・ソースのメタデータが変わればバイナリも変わります。
