# 開発ガイド

## 必要な環境

| 用途 | 必要なもの |
| --- | --- |
| WPFのビルド・実行 | Windows x64、.NET 10 SDK |
| Visual Studioで編集 | .NET 10対応のVisual Studio、.NET デスクトップ開発ワークロード |
| llama.cppのCPUビルド | Visual Studio 2022以降のMSVC x64、Windows SDK、対応するCMake |
| llama.cppのVulkanビルド | CPUビルドのツールに加え、承認済みのVulkan SDK |
| スクリプト | Windows PowerShell 5.1またはPowerShell 7 |

ビルドツールは自動インストールしません。Visual Studio付属のCMakeを使う場合はC++用CMakeツールを有効にしてください。Visual Studio 2026を使う場合は同バージョンに対応するCMakeが必要です。

`global.json`は.NET 10 SDKを選択し、安定版の新しい機能バンド・パッチを許可します。preview版と次のメジャーバージョンは使いません。CIもこのファイルを参照します。

## 日常のコマンド

リポジトリのルートから実行します。

```powershell
./scripts/Build.ps1 -Configuration Debug
dotnet run --project src/Eluna.App --no-restore
./scripts/Verify.ps1
./scripts/Build-Llama.ps1 -Configuration Release -Parallel 4
./scripts/Setup-VulkanSdk.ps1
./scripts/Build-Llama.ps1 -Backend Vulkan -EnableBackendTests
./scripts/Test-LlamaGpu.ps1 -Device Vulkan0
```

Buildはrestoreとbuild、VerifyはReleaseビルド・フォーマット・空白チェックを実行します。コマンドの失敗はスクリプトの失敗として返します。

NuGetの取得元は標準.NETランタイムとSDKの単体配布用ツールに限定しています。SDKの参照パックが見つからない場合は、必要な.NET 10 SDKを用意してください。明示的な外部パッケージ参照を加えると`ELUNA001`で失敗します。

llama.cppはsubmoduleの固定コミットを使い、ビルド時の外部ファイル取得を無効化しています。CPUバックエンドを複数ビルドし、CPUに応じて選択できる構成です。Vulkanは`-Backend Vulkan`で有効にし、CPU版と出力先を分けます。OpenMP、TLS、Web UI、外部コマンド起動、Vulkan以外のGPUバックエンドは無効です。

SDKの取得は`Setup-VulkanSdk.ps1`を明示的に実行した場合のみ行います。通常のC# / CPUビルドでは取得しません。既にSDKを持っている場合は`-VulkanSdkPath C:/VulkanSDK/<version>`か`VULKAN_SDK`環境変数を使えます。詳細は[GPUガイド](gpu.md)を参照してください。

## モデルを使う手動確認

モデルは自分で利用・配布条件を確認して用意します。モデルの自動ダウンロードやアプリへの同梱はまだ行いません。

```powershell
New-Item -ItemType Directory -Force models
# models/model.ggufに、利用条件を確認したモデルを配置する
./scripts/Start-Llama.ps1 -Backend Cpu -ModelPath ./models/model.gguf
# Vulkan版をビルドした場合
./scripts/Start-Llama.ps1 -ModelPath ./models/model.gguf -Device Vulkan0
```

別のPowerShellで`Invoke-RestMethod http://127.0.0.1:8080/health`を使って準備完了を確認できます。ポートが使用中なら変更してください。手動で起動したサーバーはCtrl+Cで終了します。通常のアプリは自身で同梱サーバーの起動・認証・終了を管理します。[プロトタイプの使い方](prototype.md)を参照してください。

## 検証の方針

- 現段階ではビルド、依存関係チェック、フォーマット、WPF起動、llama-serverのバージョン表示、実機でのVulkan GPU認識とCPU参照との演算一致を確認する。
- 機能実装時にはキャンセル、ストリームの分割受信、ファイル破損、履歴復元、サーバー終了など、壊れると困る振る舞いを自動検証する。
- 最初は.NET標準機能を使う小さな検証プログラムを選択肢にする。テストフレームワークを使う場合も導入前に相談する。
- 自動確認が難しいIME、DPI、アクセシビリティ、見た目は手動でも確認する。
- 速度やメモリの評価にはCPU、RAM、モデル、量子化、コンテキスト長を記録する。根拠のない性能目標を合否基準にしない。

`build/`、`artifacts/`、`models/`、`data/`、`runtime/`、`.local/`はGit管理対象外です。会話本文やユーザー固有のパスを含むログを公開しないでください。
