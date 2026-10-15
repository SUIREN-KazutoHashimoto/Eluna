# 依存関係ポリシー

llama.cpp以外の外部パッケージは原則使いません。必要な場合は、導入・ダウンロード・ビルド組み込みの前にオーナーへ相談します。この条件はアプリ本体だけでなく、テスト、UI、MVVM、DI、ログ、Markdown、データベース、ネイティブ拡張にも適用します。

## 現在の台帳

| 項目 | 扱い | バージョン管理 |
| --- | --- | --- |
| .NET / WPF / Windows API | 標準基盤 | global.jsonと対象フレームワーク |
| Computer UseのUI Automation / User32 / GDI | WindowsとWPFの標準API。外部パッケージ・別ランタイム・操作SDKの追加なし | Windows / WPF基盤。既存llama.cppで操作を提案 |
| 標準.NETランタイムパック / SDKのILLinkタスク | 単体exe配布のための標準プラットフォーム資材。アプリの外部ライブラリ追加とは区別 | SDKが選択するバージョン、NuGet.Configで取得可能なパッケージ名を限定 |
| llama.cpp | 許可済みの推論基盤 | third_party/llama.cppのGit submoduleコミット |
| stable-diffusion.cpp | 2026-10-15に追加・ビルド・同梱をオーナー承認。CPU/Vulkanの静的CRTエンジン。C#ライブラリは追加しない | third_party/stable-diffusion.cpp、120e3e875c5501c1babb50c0966122cb569d12d4 |
| llama.cppに同梱されたvendorコード | llama.cppの一部として扱う。別途取得・追加しない | 同じsubmoduleコミット |
| C#の外部PackageReference | 現在なし | ビルド時にELUNA001で検出 |
| Vulkan SDK | オーナー承認済みのGPUビルド用依存。C#や配布物にSDK全体を同梱しない | scripts/vulkan-sdk.jsonでバージョンとSHA-256を固定 |
| Git、PowerShell、MSVC、CMake | 開発用ツール。自動インストールしない | 必要環境をdocs/development.mdに記載 |
| GitHub公式Actions | CIの実行用。アプリへ同梱しない | ci.ymlでコミットSHAを固定 |

SDK 10.0.402ではWPFの配布用ランタイムを基底の`Microsoft.WindowsDesktop.App`にまとめて解決するため、self-contained時だけ標準FrameworkReferenceを明示する。`DisableTransitiveFrameworkReferenceDownloads`、NuGet.Configの取得先とパッケージ名の制限、ELUNA001のチェックは維持する。

llama.cppにはggmlやcpp-httplibなどが同梱されています。「外部依存なし」とは説明せず、固定したllama.cppの構成とライセンスを確認します。Vulkan SDK以外のGPU SDKやOpenSSLなどは承認済みとはみなしません。

Vulkan SDKは2026-10-14にオーナーが導入とビルドを承認しました。`Setup-VulkanSdk.ps1`を明示的に実行した場合にのみ、公式インストーラーをSHA-256で検証し、copy-onlyで`.local/vulkan-sdk/`へ配置します。SDL、GLMなどの任意コンポーネントは追加せず、システムのPATH、レジストリ、GPUドライバーは変更しません。通常のC#ビルドとCPUビルドではSDKを取得しません。

利用者のPCにはGPUベンダーの対応Vulkanドライバーが必要です。SDKは開発用です。配布時にはllama.cppのDLLとVulkanバックエンドを含め、SDKのツールや開発用レイヤーを配布しない構成を確認します。SDKを更新するときは固定バージョンと公式ハッシュを同時に更新し、Vulkan版ビルドと実機テストを行います。

## 追加が必要になった場合

相談には目的、標準機能では不足する理由、代替案、必要なバージョン、直接・推移依存、ライセンス、配布サイズ、更新・保守の影響を含めます。承認後にこの台帳、NuGet.Config、Directory.Build.targets、配布用のライセンス表示を合わせて更新します。

NuGetの取得元は単体配布に必要なMicrosoft.NETCore.App / Microsoft.WindowsDesktop.Appのwin-x64ランタイム・ホストと、SDK付属のILLinkタスクに限定しています。SDKの暗黙的な取得だけを許し、明示的なPackageReferenceを検出するMSBuildターゲットも維持しています。バイナリの手動コピーや別のパッケージ管理手段で、このポリシーを迂回しないでください。

Hugging Faceはモデル検索・取得のデータ配布元として公開HTTP APIを利用します。Hugging FaceのSDKやCLIは追加しません。モデルは実行ライブラリと別に扱い、ユーザーが容量と配布元を選んで取得します。

2026-10-15にSearXNG内蔵のWeb検索が追加要件となり、その後、自前で同等の検索機能を用意する代替方式が承認されました。開発版は.NET標準のHttpClient、XML / JSON、文字列処理でBing RSS・DuckDuckGo Lite・Wikipedia APIへ問い合わせます。SDKや外部PackageReferenceは追加しません。ページ本文の抽出、キャッシュ、出典管理、調査・議論もC#で実装します。検索先の利用条件やアクセス制限はそれぞれに従い、アクセス確認を回避しません。

SearXNG・Python・関連依存は配布物へ追加していません。将来SearXNGそのものを追加する場合は、具体的なWindows同梱構成、直接・推移依存、ライセンス、サイズ、更新への影響を提示して導入前に相談します。自前検索の承認をC#のPackageReference制限や取得元設定の変更へ広げません。

## 承認済みの画像生成とWindowsの標準機能

画像生成ではstable-diffusion.cpp本体とその固定版ggml、同梱のoniguruma、utf8proc、darts、JSON、stb、ZIP/miniz、および移植された乱数処理の通知を保持する。本体はMIT、同梱コードにはMIT・BSD・Unicodeの通知・パブリックドメイン等が含まれる。WebP/WebMとサーバーUIはビルド・配布に含めない。乱数処理が参照するPyTorchの固定版ライセンスは`docs/notices/`へ保存し、PyTorchの実行環境は導入しない。追加のCUDA/HIP SDKや外部C#パッケージは承認範囲に含めない。

PDF読み取りにはWindows標準のWindows.Data.PdfとWindows PowerShellのWinRT投影を使う。追加モジュール・Office・PDFライブラリ・NuGetパッケージは不要。Windows PowerShellが管理ポリシー等で利用できない環境ではPDF読み取りを利用できず、理由を表示する。実行ポリシーを回避する設定変更は行わない。

## llama.cppの更新

submoduleの参照コミットは親リポジトリに記録します。通常のセットアップには`git submodule update --init --recursive`を使い、`--remote`で追従しません。

更新は`develop`から作業ブランチを作り、選んだコミットを明示して行います。更新前後のSHA、モデル / APIの互換性、ビルドオプション、ライセンス差分をPRに記録してください。CPUビルドと、接続実装後は生成・キャンセル・モデル切り替え・終了も確認します。
