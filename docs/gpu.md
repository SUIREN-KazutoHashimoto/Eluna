# GPUガイド

最初のGPUバックエンドはllama.cppのVulkanです。GPUドライバーが必要な機能を提供するAMD / NVIDIA / Intel GPUを対象にしますが、全製品の動作を保証するものではありません。まずAMD Radeon RX 9070 XTを実機確認の対象にします。CUDA、HIP、SYCLは今回追加しません。

WPFアプリから同梱サーバーへの通信とGPU / CPU設定を実装しています。配布用の一体構成は[プロトタイプの使い方](prototype.md)を参照してください。このガイドのコマンドは開発用の共有ライブラリ構成を扱います。

## SDKの用意とビルド

```powershell
./scripts/Setup-VulkanSdk.ps1
./scripts/Build-Llama.ps1 -Backend Vulkan -EnableBackendTests
```

セットアップは[scripts/vulkan-sdk.json](../scripts/vulkan-sdk.json)の公式URLとSHA-256を使い、SDKの必須コンポーネントを`.local/vulkan-sdk/<version>/`へcopy-onlyで配置します。SDL、GLMなどの任意コンポーネントやGPUドライバーは追加しません。SDK全体はGit管理対象外です。[LunarGの手順](https://vulkan.lunarg.com/doc/view/latest/windows/getting_started.html#unattended-installation)に基づく配置です。

既にSDKがある場合は取得せず、その場所を指定できます。

```powershell
./scripts/Build-Llama.ps1 -Backend Vulkan -VulkanSdkPath C:/VulkanSDK/1.4.363.0
```

探索順は`-VulkanSdkPath`、`VULKAN_SDK`環境変数、リポジトリ内の固定SDKです。glslc、Vulkanヘッダー、リンク用ライブラリがない場合は、取得を自動で始めずにエラーを返します。SDKの場所を変えたときはCMakeがその場所を再設定します。

CPU版は従来どおり`./scripts/Build-Llama.ps1`でビルドできます。CPU / Vulkanの出力先はそれぞれ`build/llama-cpu/`、`build/llama-vulkan/`で、CMakeキャッシュやDLLを混ぜません。

シェーダー生成ツールの作業ディレクトリは`cmake/NativeConfiguration.cmake`で`build/llama-vulkan/ep/`にまとめています。Windowsのネストしたビルドでパスが長くなりすぎることを避けるためで、llama.cppのソースは変更しません。

## 使用するGPUとモデル

```powershell
./scripts/Start-Llama.ps1 -ListDevices
./scripts/Start-Llama.ps1 -ModelPath ./models/model.gguf -Device Vulkan0
```

`Vulkan0`などの名前はサーバーが列挙したものを使います。複数GPUがある場合は一覧の製品名を確認してください。番号はハードウェア構成やドライバーによって変わり得ます。

生成用レイヤーのGPU配置は標準では`auto`を使い、VRAM容量とモデルに応じてllama.cppが調整します。配置を明示したい場合は次のように指定します。

```powershell
./scripts/Start-Llama.ps1 -ModelPath ./models/model.gguf -Device Vulkan0 -GpuLayers all
./scripts/Start-Llama.ps1 -ModelPath ./models/model.gguf -Device Vulkan0 -GpuLayers 20 -ContextSize 4096
./scripts/Start-Llama.ps1 -Backend Cpu -ModelPath ./models/model.gguf
```

`all`でVRAMに収まらない場合は`auto`に戻すか、レイヤー数・コンテキスト長を減らします。存在しないGPUは起動前に拒否します。GPUを指定したつもりでCPUだけの検証になることを避けるため、CPU版を使うときは明示的に切り替えます。

サーバーは`127.0.0.1`、ポート8080、Web UI無効で起動します。ポートは`-Port`で変更できます。モデルファイルは自動取得せず、指定したファイルを使います。Ctrl+Cで終了します。

## GPU演算の確認

```powershell
./scripts/Build-Llama.ps1 -Backend Vulkan -EnableBackendTests
./scripts/Test-LlamaGpu.ps1 -Device Vulkan0
```

llama.cpp付属の`test-backend-ops`で、小さな加算・乗算・行列積をGPUに実行させ、CPU参照と比較します。モデルや外部テストフレームワークは不要です。検査対象はfloat32 / float16と、行列積のQ4_0 / Q4_K / Q8_0です。GPUが存在しない、対象演算が実行されない、結果が許容誤差を超える場合は失敗にします。

この確認は実モデルの応答品質、全演算、速度、VRAM不足への回復を保証しません。チャット機能の実装時に、実モデルを使う生成・キャンセル・切り替えも確認します。

GitHubの通常のWindowsランナーではVulkan版と検証プログラムをビルドします。対応GPUの存在を前提にした演算テストは、実機で実行します。

### 実機確認結果

2026-10-14に、AMD Radeon RX 9070 XT、AMDドライバー26.8.1、Vulkan SDK 1.4.363.0、llama.cppコミット`6868a52`のReleaseビルドで確認しました。SDKの環境変数がない状態でも、ドライバーのVulkanローダーでGPUを列挙できています。

| 演算 | 対象の型 | CPU参照と一致した件数 |
| --- | --- | --- |
| ADD | float32 / float16 | 2 |
| MUL | float32 / float16 | 2 |
| MUL_MAT | float32 / float16 / Q4_0 / Q4_K / Q8_0 | 16 |

存在しないGPUの指定と、SDK不足・モデルファイル不足のエラー処理も確認しています。配布用の一体構成ではQwen3.5-0.8B Q4_K_Mの生成・停止・CPU切り替えも確認しました。他社GPUとSDKのない別PCでの動作は未確認です。

## 配布

利用者のPCには対応Vulkanドライバーと、ビルドに対応するMicrosoft Visual C++ランタイムが必要です。GPUベンダーのドライバーに含まれるVulkanローダーを使い、開発用SDK全体は配布しません。`llama-server.exe`、`ggml-vulkan.dll`、同じビルドの依存DLLを一緒に配置します。CPU版とVulkan版のDLLを混ぜないでください。ランタイムを含む具体的な配布方式は、リリース時に決めます。

リリース前には、SDKのないWindows環境でモデル生成を確認し、実際に含まれるコードのライセンスを配布物へ反映します。
