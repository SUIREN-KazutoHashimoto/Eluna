# 第三者コードの扱い

Eluna本体には[Apache-2.0](LICENSE)を適用します。このライセンスをllama.cppやモデルに適用するものではありません。

llama.cppは[ggml-org/llama.cpp](https://github.com/ggml-org/llama.cpp)をGit submoduleとして利用し、参照コミットを親リポジトリに固定しています。llama.cpp本体のライセンスはMITです。正確な原文は[固定コミットのLICENSE](https://github.com/ggml-org/llama.cpp/blob/6868a52cb337450658006ad2f9281c2eb63f1666/LICENSE)を参照してください。

llama.cppにはggml、cpp-httplib、JSON処理などの同梱コードがあります。それぞれの著作権表示・ライセンスはsubmodule内の原文を保持します。外部から別の依存を追加取得する構成は、現在のビルドで無効にしています。

Vulkan SDKは承認済みのGPUビルド用依存として、公式の固定バージョンを別途セットアップします。SDKには複数のライセンスがあります。SDK内の`Licenses/`と各コンポーネントの原文を保持し、Vulkanバックエンドに含まれるヘッダーやコードについても配布前に必要な表示を確認します。SDK全体や開発用ツールをアプリへ同梱するものではありません。

リリース時には、実際に配布するネイティブバイナリに含まれるコードを確認し、必要なライセンス原文と著作権表示を配布物へ含めてください。この説明ファイルだけで配布時の表示を代用しません。

GGUFモデルはリポジトリに含めません。モデルを同梱・取得する機能を追加する場合は、モデルごとの利用・再配布条件を確認して別に記録します。

画像生成にはオーナー承認済みの[stable-diffusion.cpp](https://github.com/leejet/stable-diffusion.cpp/tree/120e3e875c5501c1babb50c0966122cb569d12d4)を固定して利用します。本体はMITです。ggml、oniguruma、utf8proc、darts、JSON、stb、ZIP/minizと移植元の乱数処理の著作権・ライセンス通知も配布用資材へ含めます。原文がヘッダー内にあるものは該当ファイルを通知として保持します。WebP/WebM、Web UIは同梱しません。PyTorchをライブラリとして導入するものではなく、移植された処理が参照する原文を`docs/notices/stable-diffusion-pytorch-rng-LICENSE.txt`に保持します。

プロトタイプのexeには、標準.NET / WPFランタイムとCPU / Vulkanの一体構成エンジンを含めます。ライセンス・第三者表示はネイティブ資材と一緒にexeへ埋め込み、初回起動時の`Runtime/<hash>/Licenses/`に展開します。モデルはexeへ同梱せず、GUIで配布元と利用条件を表示してユーザーが取得します。

設定の「ライセンス」タブから、同梱したライセンス原文と通知をオフラインで閲覧できます。本体・両エンジンの原文とこの説明もアプリへ埋め込み、エンジン展開前から確認できます。配布版では埋め込まれたネイティブ資材から関連通知も直接読み込みます。
