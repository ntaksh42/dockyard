# AGENTS.md

Dockyard は、別々のアプリのウィンドウを 1 つの枠（グループ）にまとめ、タブと分割ペインで配置する Windows 常駐アプリ（C# / .NET 10 / WPF、Win32 は P/Invoke）。仕様は `docs/mock.html`、設計は `docs/architecture.md`、機能一覧は `README.md`。MSIX パッケージのはまりポイントは下の「MSIX パッケージ」節にある。

## コマンド

```
dotnet build                                   # Dockyard.slnx 全体
dotnet run --project src/Dockyard.Exe            # トレイに常駐し、空のグループ枠が 1 つ開く
dotnet test                                    # Domain.Tests・Application.Tests・Architecture.Tests
dotnet test tests/Dockyard.Domain.Tests --filter "FullyQualifiedName~LayoutTests"   # 単一クラス・単一テスト
pwsh installer/build.ps1                       # 署名付き MSIX（artifacts/msix/）
pwsh installer/build.ps1 -Dev                  # 署名せず layout フォルダを開発登録（開発者モード）
```

- Dockyard は多重起動しない（2 つ目はすぐ終了する。`--layout "名前"` 付きなら、終了する前に起動中の Dockyard へそのレイアウトを開かせる）。起動中の Dockyard.exe はビルド出力をロックするので、ビルド前に終了する。
- 実機 E2E の `tests/Dockyard.UiTests` は `dotnet test` の対象ではない。デスクトップを実マウスで 1 分ほど操作するので、ユーザーに確認してから `dotnet run --project tests/Dockyard.UiTests` で実行する。`src/Dockyard.Exe/bin/Debug/.../Dockyard.exe` を起動するので先に Dockyard.Exe を Debug でビルドしておく。`-- --features`（メニュー・レイアウト・設定・復元）、`-- --stress`（負荷）、`-- --perf` がある。`%LOCALAPPDATA%\Dockyard\session.json` が残っていると開始しない。
- ログは `%LOCALAPPDATA%\Dockyard\dockyard.log`（MSIX 版はパッケージ専用の場所へリダイレクトされる。下の「MSIX パッケージ」節参照）。詳細トレースは `dockyard.trace.log` に出る。Debug ビルドは常に出し、Release は `DOCKYARD_TRACE=1` のときだけ。

## アーキテクチャ

4 つの層と入口。依存は内側（Domain）へ向く。公開する型は各アセンブリのルート名前空間（`Dockyard.<層>`）に置き、実装は `Internal/` フォルダ・`Dockyard.<層>.Internal` 名前空間に入れて `internal` にする（ASP.NET Core の Kestrel と同じ流儀）。Domain は全型が語彙なのでそのまま公開する。

- **フォルダと名前空間。** 公開型はフォルダで分けても名前空間はルートのまま（Application の `Ports/Machine/`・`Ports/Screen/`・`Model/` はすべて `Dockyard.Application`）。公開ポートは 1 型 1 ファイル。`Internal/` 配下のサブフォルダは名前空間もフォルダに合わせる（Infrastructure の `Internal/Win32`・`Storage`・`Logging`・`Launching`、Presentation の `Internal/Views`）。

| プロジェクト | 役割 | 参照してよい先 | 窓口（public） | TFM |
|---|---|---|---|---|
| `Dockyard.Domain` | レイアウトの二分木、ペイン矩形の計算と最小サイズ制約、ドラッグの意味付け、配置結果の検証ルール | なし | レイアウトの語彙（`Pane`・`LayoutEngine` など） | `net10.0` |
| `Dockyard.Application` | 「いつ・どのウィンドウを・どこへ・どの順に」。グループ単位の `GroupController`、全グループを束ねる `Workspace`、機械側と画面側のポート | Domain | `Workspace` とポート（`IPlatform`・`IShell` ほか）と、ポートを越える値 | `net10.0` |
| `Dockyard.Infrastructure` | ポートの Win32・ファイル実装（ウィンドウ操作、WinEvent・キーボードフック、候補列挙、JSON 保存、自動起動、ログ） | Application、Domain | `PlatformServices`（`IPlatform` の実装） | `net10.0-windows10.0.19041.0` |
| `Dockyard.Presentation` | WPF の枠・ダイアログ・タブ・トレイ・スタイル | Application、Domain | `WpfShell`（`IShell` の実装） | `net10.0-windows` |
| `Dockyard.Exe` | 入口。多重起動の判定、例外フック、3 層の組み立て（`App.xaml.cs` は 60 行ほど） | Application、Infrastructure、Presentation | なし | `net10.0-windows10.0.19041.0` |

- **書く場所の目安。** 制御ロジックは Application に置く（偽実装で単体テストできる）。XAML のコードビハインドには描画と入力の受付だけを置く。Win32 やファイルに触るものは Infrastructure に実装し、インターフェースを Application に置く。
- **ポートは Application が定義する。** `IWindowSystem`（他アプリのウィンドウ操作）・`IWindowCatalog`・`IInputState`・`IWindowEventSource`・`IKeyboardSource`・各ストア・`IAppLauncher`・`IAutostart`・`ILog` は機械側、`IShell`・`IGroupFrame`・`IDockGuide`・`IScheduler` は画面側。`Workspace` は `IPlatform` と `IShell` だけを受け取る。
- **外へ出す型を増やすとき。** ルート名前空間に置く。`tests/Dockyard.Architecture.Tests/PublicApi.cs` が各アセンブリの公開一覧で、`ArchitectureTests` が「公開型はルート名前空間にだけある」ことと、一覧との一致、参照関係（Domain は何も参照しない、Application は Domain だけ、Presentation は Infrastructure を参照しない）を検査する。型を public にしたらそこも直す。
- `tests/Dockyard.Application.Tests/Fakes.cs`・`WorkspaceFakes.cs` が偽の機械・画面。内部の `GroupController` は `InternalsVisibleTo` でテストから使う。

### 設計上の前提（変更時に壊しやすい点）

- 外部ウィンドウは reparent（SetParent）しない。ペインの位置に合わせて移動・リサイズし続ける位置同期方式。Dockyard が落ちても各アプリは普通のウィンドウとして残る。
- 重なり順は「アクティブなタブ（各ペイン 1 つ）→ 枠 → 非アクティブなタブ」。非アクティブなタブは隠さず枠の裏に置くので Alt+Tab に出たまま。ダイアログ表示中はダイアログの直下に並べる（`IGroupView.ZAnchor`）。
- タブバー配置 B（タイトルバーに重ねる）は、透明でアクティブにならないオーバーレイウィンドウ（`IGroupView.TabOverlay`）をアクティブなタブの上に置いて実現している。
- 外部ウィンドウの見えないリサイズ枠が仕切りや外周にかぶさるため、「メンバーの端を引く」操作を仕切り移動・枠リサイズとして解釈する。
- Win11 のメモ帳などは他プロセスからの `SetWindowPos(HWND_TOP)` を無視するので、最前面へは topmost を経由し、2 つ目以降は「直下へ」の移動で並べる。
- 重なり順の `SetWindowPos` に `SWP_NOOWNERZORDER` を付けない。付けるとオーナーだけが動いてオーナー付きのダイアログの上に出て、モーダルダイアログが本体の背後に隠れる。
- UWP アプリの枠（`ApplicationFrameWindow`）は全部 ApplicationFrameHost.exe のウィンドウなので、アプリの識別（exe 名・パス）には `Native.GetAppProcessId` で求めた実プロセスを使う。
- WinEvent のコールバックで例外が漏れるとプロセスが即終了するので、インフラ層の受け口で受け止めて記録する。
- 強制終了に備え、グループとメンバー（ハンドルと追加前の位置）を `session.json` に書き続け、正常終了で消す。起動時に残っていれば組み直す。閉じたウィンドウは追加前の位置・サイズに戻す。
- 保存ファイルは `%LOCALAPPDATA%\Dockyard\` の `settings.json` / `layouts.json` / `session.json` / `last-groups.json`。

## 規約

- ドキュメント・コミットメッセージは日本語、コード内のコメントと識別子は英語。
- ID・名前を `nint` や `string` のまま持ち回らない。ウィンドウハンドルは `WindowHandle`（Domain。「窓なし」は `WindowHandle.None`）、グループ・レイアウトの名前は `LayoutName`、プロセス名は `ProcessName`（大文字小文字を区別しない）で持つ。生の `nint` を使うのは P/Invoke の境界（Infrastructure の `Win32`、Presentation の `Native`）だけで、保存ファイルの形式は `Infrastructure/Internal/Storage/IdJsonConverters.cs` が従来の数値・文字列のまま保つ。
- Domain と Application は Windows・WPF を参照させず `net10.0` のままにし（Application.Tests・Domain.Tests も `net10.0`）、Infrastructure・Exe とそのテストの TFM は `net10.0-windows10.0.19041.0`（MSIX の `StartupTask` のため）にする。
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
- **レイアウトのデスクトップショートカットは実行エイリアス `dockyard.exe` を指す。** MSIX 内の exe（`WindowsApps`）はショートカットから直接起動できないので、マニフェストの `uap5:AppExecutionAlias` を `%LOCALAPPDATA%\Microsoft\WindowsApps\dockyard.exe` として使う（`Infrastructure/Internal/Launching/LayoutShortcuts.cs` の `PackageAlias` と一致させる。エイリアスの要素名は `uap5:ExecutionAlias` で、`desktop:` ではスキーマ検証に通らない）。
- **自動起動は `windows.startupTask`（TaskId `DockyardStartup`）。** `src/Dockyard.Infrastructure/Internal/Launching/Autostart.cs` の TaskId と一致させる。既定は無効で、設定画面かタスク マネージャーのスタートアップ アプリでオンにする。WinRT の `StartupTask` を使うため Infrastructure と Exe の TFM は `net10.0-windows10.0.19041.0`。パッケージ外で起動したときは HKCU の Run キーを使う。
- **バージョンは 4 つ組で、更新するときは上げる。** 同じ Identity で中身だけ違うパッケージは上書きインストールできない。`-Version 1.0.1.0` のように指定する。

### 開発中の登録（`build.ps1 -Dev`）

- 開発者モードが有効なら、署名せずに layout フォルダをそのまま登録できる（`Add-AppxPackage -Register`）。
- 登録したパッケージは layout フォルダのファイルをその場で使う。フォルダを消すと壊れるので、`-Dev` は作り直す前に `Remove-AppxPackage` で登録を外している。パッケージ版の Dockyard を起動したままだとファイルがロックされて失敗するので、先に終了する。
- 開発登録と署名付き .msix は同じ Identity なので同時には入らない。切り替えるときは `Get-AppxPackage Dockyard | Remove-AppxPackage` で外してから入れる。

## リリース（CI）

`.github/workflows/release.yml` が、毎朝 6:00（JST。cron は UTC の `0 21 * * *`）に前回のリリースと比べ、署名付き MSIX（arm64・x64）を GitHub リリースに公開する。

- **出す条件。** 最新リリースのタグから `src/`・`installer/`・`Dockyard.slnx` に変更があるとき。ドキュメントやテストだけの変更では出さない（`PRODUCT_PATHS`）。手動実行（Actions タブの Run workflow）では、`force`（変更が無くても出す）と `dry_run`（ビルドまでで止め、タグもリリースも作らない。MSIX は成果物としてだけ残る）を選べる。
- **バージョン。** 最新のタグ `vX.Y.Z` の Z を 1 つ上げる（パッケージは `X.Y.(Z+1).0`）。minor・major を上げるときは、先に手で `vX.Y.0` のリリースを作る。リリースがまだ無ければ `v1.0.0`。
- **公開前にテストを通す。** `dotnet test Dockyard.slnx`。落ちたら公開しない。
- **署名。** リポジトリの Secrets `MSIX_PFX_BASE64`（PFX を Base64 にしたもの）と `MSIX_PFX_PASSWORD` があればその証明書で署名する。無ければ実行ごとに自己署名証明書を作るので、リリースごとに `Dockyard.cer` を信頼させ直す必要がある（リリースノートにも書く）。Publisher は証明書の Subject になるので、証明書を替えるときは Subject を `CN=Dockyard Dev` のままにするか、パッケージが別のアプリとして入ることを承知しておく。署名にはタイムスタンプ（DigiCert）を付け、証明書の期限後も署名が有効なままにする。
- **スケジュールの実行はデフォルトブランチ（`main`）のワークフローが使われ、数分〜数十分遅れることがある。**
