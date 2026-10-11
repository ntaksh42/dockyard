# AGENTS.md

Dockyard は、別々のアプリのウィンドウを 1 つの枠（グループ）にまとめ、タブと分割ペインで配置する Windows 常駐アプリ（C# / .NET 10 / WPF、Win32 は P/Invoke）。仕様は `docs/mock.html`、設計は `docs/architecture.md`、機能一覧は `README.md`。MSIX パッケージのはまりポイントは下の「MSIX パッケージ」節にある。

## コマンド

```
dotnet build                                   # Dockyard.slnx 全体
dotnet run --project src/Dockyard.App          # トレイに常駐し、空のグループ枠が 1 つ開く
dotnet test                                    # Domain.Tests と Controllers.Tests
dotnet test tests/Dockyard.Domain.Tests --filter "FullyQualifiedName~LayoutTests"   # 単一クラス・単一テスト
pwsh installer/build.ps1                       # 署名付き MSIX（artifacts/msix/）
pwsh installer/build.ps1 -Dev                  # 署名せず layout フォルダを開発登録（開発者モード）
```

- Dockyard は多重起動しない（2 つ目はすぐ終了する）。起動中の Dockyard.exe はビルド出力をロックするので、ビルド前に終了する。
- 実機 E2E の `tests/Dockyard.UiTests` は `dotnet test` の対象ではない。デスクトップを実マウスで 1 分ほど操作するので、ユーザーに確認してから `dotnet run --project tests/Dockyard.UiTests` で実行する。`src/Dockyard.App/bin/Debug/.../Dockyard.exe` を起動するので先に App を Debug でビルドしておく。`-- --features`（メニュー・レイアウト・設定・復元）、`-- --stress`（負荷）、`-- --perf` がある。`%LOCALAPPDATA%\Dockyard\session.json` が残っていると開始しない。
- ログは `%LOCALAPPDATA%\Dockyard\dockyard.log`（MSIX 版はパッケージ専用の場所へリダイレクトされる。下の「MSIX パッケージ」節参照）。`DOCKYARD_TRACE=1` で詳細トレース。

## アーキテクチャ

依存は `App → Infrastructure → Domain`、`App → Domain`。

- **`Dockyard.Domain`**（`net10.0`、Windows 非依存）: レイアウトの二分木（葉 = タブを持つペイン、節 = 縦横 2 分割、空ペインは親の分割を畳む）、枠サイズからのペイン矩形計算と最小サイズ制約、ドラッグの意味付け（移動か・どの辺のリサイズか）、配置結果の検証ルール。
- **`Dockyard.Infrastructure`**: `IWindowSystem`（ウィンドウ操作と物理ピクセル座標型）とその Win32 実装、WinEvent フック、キーボードフック、候補ウィンドウ列挙、設定・JSON 保存、ログ。
- **`Dockyard.App/Controllers`**（名前空間 `Dockyard.App.Controllers`）: グループ 1 つにつき 1 つの `GroupController` が「いつ・どのウィンドウを・どこへ・どの順に」動かすかを決める。WPF も Win32 も直接呼ばず、`Ports.cs` の `IGroupView`（枠）・`IScheduler`（後回し・タイマー）と `IWindowSystem` だけを使う。このため `tests/Dockyard.Controllers.Tests/Fakes.cs` の偽実装で単体テストできる。制御ロジックはここに置き、XAML のコードビハインドには描画と入力の受付だけを置く。
- **`Dockyard.App`** の残り: `GroupWindow`（`IGroupView` の WPF 実装）、ダイアログ類、トレイ常駐、`App.xaml.cs` での組み立て。`WpfScheduler` が `IScheduler` の実装。

### 設計上の前提（変更時に壊しやすい点）

- 外部ウィンドウは reparent（SetParent）しない。ペインの位置に合わせて移動・リサイズし続ける位置同期方式。Dockyard が落ちても各アプリは普通のウィンドウとして残る。
- 重なり順は「アクティブなタブ（各ペイン 1 つ）→ 枠 → 非アクティブなタブ」。非アクティブなタブは隠さず枠の裏に置くので Alt+Tab に出たまま。ダイアログ表示中はダイアログの直下に並べる（`IGroupView.ZAnchor`）。
- タブバー配置 B（タイトルバーに重ねる）は、透明でアクティブにならないオーバーレイウィンドウ（`IGroupView.TabOverlay`）をアクティブなタブの上に置いて実現している。
- 外部ウィンドウの見えないリサイズ枠が仕切りや外周にかぶさるため、「メンバーの端を引く」操作を仕切り移動・枠リサイズとして解釈する。
- Win11 のメモ帳などは他プロセスからの `SetWindowPos(HWND_TOP)` を無視するので、最前面へは topmost を経由し、2 つ目以降は「直下へ」の移動で並べる。
- WinEvent のコールバックで例外が漏れるとプロセスが即終了するので、インフラ層の受け口で受け止めて記録する。
- 強制終了に備え、グループとメンバー（ハンドルと追加前の位置）を `session.json` に書き続け、正常終了で消す。起動時に残っていれば組み直す。閉じたウィンドウは追加前の位置・サイズに戻す。
- 保存ファイルは `%LOCALAPPDATA%\Dockyard\` の `settings.json` / `layouts.json` / `session.json` / `last-groups.json`。

## 規約

- ドキュメント・コミットメッセージは日本語、コード内のコメントと識別子は英語。
- App とテストの TFM は `net10.0-windows10.0.19041.0`（MSIX の `StartupTask` のため）。Domain.Tests は `net10.0` で Windows に依存させない。
- C# のコーディングポリシー（可読性）は次に従う。

@docs/csharp-coding-policy.md

## MSIX パッケージ（installer/）

`installer/build.ps1` が `dotnet publish` → layout 組み立て → `makeappx pack` → `signtool sign` を行う。Visual Studio の Packaging Project は使っていない。

### はまりポイント

- **Publisher と証明書の Subject は完全一致させる。** 1 文字でも違うと `signtool` が失敗する。`AppxManifest.xml` の `Identity/@Publisher` は `$Publisher$` で、`build.ps1` が署名に使う証明書の Subject（既定は自己署名の `CN=Dockyard Dev`）を入れる。Publisher が変わるとパッケージファミリー名も変わり、別のアプリとして入る。
- **未署名の MSIX では Dockyard は入らない。** `Add-AppxPackage -AllowUnsigned` は、実行ファイルを起動するアプリを含むパッケージを拒否する（0x80073D2B）。インストールの確認（`installer/test.ps1`）は署名付きで行う。
- **未署名の .msix はインストールできない。** 自己署名証明書は `Cert:\CurrentUser\My` に作って署名に使うが、インストールする側では `Cert:\LocalMachine\TrustedPeople` に入れて信頼させる必要がある（管理者権限が要る）。
- **`runFullTrust` と `Windows.FullTrustApplication` は外せない。** Dockyard は他のアプリのウィンドウを `SetWindowPos` で動かし WinEvent をフックするので、サンドボックス（AppContainer）では動かない。
- **.NET ランタイムへの依存はマニフェストに書けない。** framework-dependent で publish しているので、.NET 10 Desktop Runtime がない PC では起動時に .NET 側のダイアログが出るだけになる。
- **AppData への書き込みはパッケージ専用の場所へリダイレクトされる。** パッケージから起動した Dockyard の `Log.cs` のログは `%LOCALAPPDATA%\Dockyard\` ではなく `%LOCALAPPDATA%\Packages\Dockyard_<hash>\LocalCache\Local\Dockyard\` に出る。
- **アーキテクチャは既定でビルドした PC に合わせる（`-Runtime` で指定可）。** `$env:PROCESSOR_ARCHITECTURE` は x64 エミュレーションのシェルでは AMD64 を返すので、`RuntimeInformation.OSArchitecture` で判定する。マニフェストの `ProcessorArchitecture` は `$Architecture$` を置き換える。
- **タスクバーなどのアイコンは `Square44x44Logo.targetsize-N(_altform-unplated).png` と `resources.pri` で決まる。** 修飾子付きのファイル名は `makepri` で resources.pri に索引しないと使われない。targetsize 版は ico の同じサイズのエントリーをそのまま使う（16〜24px は縮小ではなくピクセル単位で描いてある）。
- **ロゴは PNG が必須で、ico からは自前で切り出す。** `System.Drawing.Icon(path, 256, 256)` は ico 内の 256px（PNG 圧縮のエントリ）を返さず 128px になる。`build.ps1` は ico のディレクトリを読み、幅 0（= 256px）のエントリの PNG バイト列を直接取り出している。
- **`makeappx` / `signtool` は PATH に無い。** Windows SDK の `C:\Program Files (x86)\Windows Kits\10\bin\<ver>\x64\` にある。`build.ps1` は最新バージョンを探して使う。
- **自動起動は `windows.startupTask`（TaskId `DockyardStartup`）。** `Autostart.cs` の TaskId と一致させる。既定は無効で、設定画面かタスク マネージャーのスタートアップ アプリでオンにする。WinRT の `StartupTask` を使うため App の TFM は `net10.0-windows10.0.19041.0`。パッケージ外で起動したときは HKCU の Run キーを使う。
- **バージョンは 4 つ組で、更新するときは上げる。** 同じ Identity で中身だけ違うパッケージは上書きインストールできない。`-Version 1.0.1.0` のように指定する。

### 開発中の登録（`build.ps1 -Dev`）

- 開発者モードが有効なら、署名せずに layout フォルダをそのまま登録できる（`Add-AppxPackage -Register`）。
- 登録したパッケージは layout フォルダのファイルをその場で使う。フォルダを消すと壊れるので、`-Dev` は作り直す前に `Remove-AppxPackage` で登録を外している。パッケージ版の Dockyard を起動したままだとファイルがロックされて失敗するので、先に終了する。
- 開発登録と署名付き .msix は同じ Identity なので同時には入らない。切り替えるときは `Get-AppxPackage Dockyard | Remove-AppxPackage` で外してから入れる。
